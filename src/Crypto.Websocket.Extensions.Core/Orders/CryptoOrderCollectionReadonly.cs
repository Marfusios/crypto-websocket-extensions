using System.Collections.Generic;
using System.Collections.ObjectModel;
using Crypto.Websocket.Extensions.Core.Orders.Models;

namespace Crypto.Websocket.Extensions.Core.Orders
{
    /// <summary>
    /// Orders collection (readonly)
    /// </summary>
    public class CryptoOrderCollectionReadonly : ReadOnlyDictionary<string, CryptoOrder>
    {
        /// <inheritdoc />
        public CryptoOrderCollectionReadonly(IDictionary<string, CryptoOrder> dictionary) : base(dictionary)
        {
        }
    }
}
