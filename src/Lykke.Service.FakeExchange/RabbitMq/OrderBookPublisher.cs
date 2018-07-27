﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Lykke.Common.ExchangeAdapter.Contracts;
using Lykke.Common.ExchangeAdapter.Server;
using Lykke.Common.ExchangeAdapter.Server.Settings;
using Lykke.Common.Log;
using Lykke.Service.FakeExchange.Core.Services;
using Microsoft.Extensions.Hosting;
using ContractOrderBook = Lykke.Common.ExchangeAdapter.Contracts.OrderBook;
using OrderBook = Lykke.Service.FakeExchange.Core.Domain.OrderBook;

namespace Lykke.Service.FakeExchange.RabbitMq
{
    [UsedImplicitly]
    public class OrderBookPublisher : IHostedService
    {
        private readonly ILogFactory _logFactory;
        private readonly IFakeExchange _fakeExchange;
        private readonly OrderBookProcessingSettings _rabbitMqSettings;

        private IDisposable _subscription;
        
        public OrderBooksSession Session { get; private set; }
        
        public OrderBookPublisher(ILogFactory logFactory, 
            IFakeExchange fakeExchange,
            OrderBookProcessingSettings rabbitMqSettings)
        {
            _logFactory = logFactory;
            _fakeExchange = fakeExchange;
            _rabbitMqSettings = rabbitMqSettings;
        }
        
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var orderBooksObservable = Observable.Create<ContractOrderBook>(async (obs, ct) =>
            {
                _fakeExchange.OrderBookChanged += book =>
                {
                    obs.OnNext(book.ToContractBook());
                };
                
                var tcs = new TaskCompletionSource<Unit>();
                ct.Register(r => ((TaskCompletionSource<Unit>) r).SetResult(Unit.Default), tcs);
                await tcs.Task;
            });

            Session = orderBooksObservable.FromRawOrderBooks(
                new List<string>(),
                _rabbitMqSettings,
                _logFactory);
            
            _subscription = new CompositeDisposable(
                Session,
                Session.Worker.Subscribe());
            
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _subscription?.Dispose();
            
            return Task.CompletedTask;
        }
    }

    public static class OrderBookConverter
    {
        public static ContractOrderBook ToContractBook(this OrderBook orderBook)
        {
            return new ContractOrderBook(Core.Domain.Exchange.FakeExchange.Name, 
                orderBook.Pair, 
                DateTime.UtcNow,
                orderBook.Asks.Select(x => new OrderBookItem(x.Price, x.Volume)),
                orderBook.Bids.Select(x => new OrderBookItem(x.Price, x.Volume)));
        }
    }
}
