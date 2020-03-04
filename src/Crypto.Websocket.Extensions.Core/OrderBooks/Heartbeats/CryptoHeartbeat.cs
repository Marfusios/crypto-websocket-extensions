using System;
using Crypto.Websocket.Extensions.Core.OrderBooks.HeartBeats;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Heartbeats
{
    public class CryptoHeartbeat : ICryptoHeartbeat
    {
        public int Cid { get; set; }
        /// <summary>
        /// Target product id
        /// </summary>
        public string ProductId { get; set; }

        /// <summary>
        /// Last executed trade id
        /// </summary>
        public long? LastTradeId { get; set; }
        
        public DateTime Timestamp { get; set; }
    }
}