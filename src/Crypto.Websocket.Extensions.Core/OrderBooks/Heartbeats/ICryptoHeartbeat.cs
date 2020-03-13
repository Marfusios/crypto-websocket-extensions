using System;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Heartbeats
{
    public interface ICryptoHeartbeat
    {
        int Cid { get; set; }

        /// <summary>
        /// Target product id
        /// </summary>
        string ProductId { get; set; }

        /// <summary>
        /// Last executed trade id
        /// </summary>
        long? LastTradeId { get; set; }

        DateTime Timestamp { get; set; }
    }
}