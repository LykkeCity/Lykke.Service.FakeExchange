﻿using System;
using System.Collections.Generic;
using System.Linq;
using Lykke.Service.FakeExchange.Core.Domain.Exceptions;
using Lykke.Service.FakeExchange.Core.Services;

namespace Lykke.Service.FakeExchange.Core.Domain
{
    public class OrderBook
    {
        private readonly IBalancesService _balancesService;

        private readonly object _sync = new object();
        
        private readonly List<Order> _buySide = new List<Order>();

        private readonly List<Order> _sellSide = new List<Order>();

        public string Pair { get; }

        public IReadOnlyList<Order> Asks
        {
            get
            {
                lock (_sync)
                {
                    return _sellSide.ToList();
                }
            }
        }

        public IReadOnlyList<Order> Bids
        {
            get
            {
                lock (_sync)
                {
                    return _buySide.ToList();
                }
            }
        }

        public IReadOnlyList<Order> AllOrders
        {
            get
            {
                lock (_sync)
                {
                    return _buySide.Union(_sellSide).ToList();
                }
            }
        }
        
        public event Action<OrderBook> OrderBookChanged;

        public OrderBook(
            string pair,
            IBalancesService balancesService)
        {
            Pair = pair;
            _balancesService = balancesService;
        }
        
        public void Add(Order order)
        {
            lock (_sync)
            {
                Validate(order);

                TryExecute(order);

                if (order.HasRemainingVolume)
                {
                    AddToOrderBook(order);
                }

                RemoveExecutedOrders();
            }
            
            OrderBookChanged?.Invoke(this);
        }

        private void RemoveExecutedOrders()
        {
            _sellSide.Where(x => !x.HasRemainingVolume).ToList().ForEach(x => _sellSide.Remove(x));
            _buySide.Where(x => !x.HasRemainingVolume).ToList().ForEach(x => _buySide.Remove(x));
        }

        private void AddToOrderBook(Order order)
        {
            if (order.TradeType == TradeType.Buy)
            {
                _buySide.Add(order);
            }

            if (order.TradeType == TradeType.Sell)
            {
                _sellSide.Add(order);
            }
        }

        private void Validate(Order order)
        {
            if (!string.Equals(order.Pair, Pair, StringComparison.InvariantCultureIgnoreCase))
            {
                throw new InvalidInstrumentException($"OrderBook for {Pair} can't accept orders for {order.Pair}");
            }
            
            if (!_balancesService.UserHasEnoughBalanceForOrder(order))
            {
                throw new InsufficientBalanceException($"User {order.ClientId} can't place order {order}");
            }
        }

        private bool TryExecute(Order order)
        {
            if (order.OrderType == OrderType.Limit)
            {
                return TryExecuteLimit(order);
            }

            if (order.OrderType == OrderType.Market)
            {
                return TryExecuteMarket(order);
            }

            return false;
        }

        private bool TryExecuteLimit(Order order)
        {
            var ordersForMatching =
                order.TradeType == TradeType.Buy
                    ? _sellSide.Where(x => x.Price <= order.Price).OrderBy(x => x.Price)
                    : _buySide.Where(x => x.Price >= order.Price).OrderByDescending(x => x.Price);
                    
            foreach (var orderForMatching in ordersForMatching)
            {
                var volumeForExecution = Math.Min(orderForMatching.RemainingVolume, order.RemainingVolume);

                if (volumeForExecution > 0)
                {
                    orderForMatching.Execute(volumeForExecution, orderForMatching.Price);
                    order.Execute(volumeForExecution, orderForMatching.Price);
                }

                if (!order.HasRemainingVolume)
                {
                    break;
                }
            }

            return order.HasExecutions;
        }

        private bool TryExecuteMarket(Order order)
        {
            throw new NotImplementedException();
        }

        public void Cancel(Order order)
        {
            lock (_sync)
            {
                if (order.TradeType == TradeType.Sell && _sellSide.Remove(order))
                {
                    order.Cancel();
                }
                else if (order.TradeType == TradeType.Buy && _buySide.Remove(order))
                {
                    order.Cancel();
                }
            }
        }
    }
}
