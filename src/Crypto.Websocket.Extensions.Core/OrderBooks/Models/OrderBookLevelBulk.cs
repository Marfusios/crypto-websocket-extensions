﻿using System;
using System.Diagnostics;
using Crypto.Websocket.Extensions.Core.Models;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models
{
    /// <summary>
    /// Groups together order book levels that are coming from server
    /// </summary>
    [DebuggerDisplay("OrderBookLevelBulk [{Action}] type: {OrderBookType} count: {Levels.Length}")]
    public class OrderBookLevelBulk : CryptoChangeInfo
    {
        /// <inheritdoc />
        public OrderBookLevelBulk(OrderBookAction action, OrderBookLevel[] levels, CryptoOrderBookType orderBookType)
        {
            Action = action;
            OrderBookType = orderBookType;
            Levels = levels ?? Array.Empty<OrderBookLevel>();
        }

        /// <summary>
        /// Action of this bulk
        /// </summary>
        public OrderBookAction Action { get; }

        /// <summary>
        /// Order book levels for this bulk
        /// </summary>
        public OrderBookLevel[] Levels { get; }

        /// <summary>
        /// Type of the order book levels
        /// </summary>
        public CryptoOrderBookType OrderBookType { get; }
    }
}
