using System.Diagnostics;

namespace Crypto.Websocket.Extensions.Core.Wallets.Models
{
    /// <summary>
    /// Wallet info - single currency balance
    /// </summary>
    [DebuggerDisplay("Wallet: {Currency} - {Balance}")]
    public class CryptoWallet
    {
        /// <summary>
        /// Wallet type (supported only by few exchanges)
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Target currency
        /// </summary>
        public string Currency { get; set; }

        /// <summary>
        /// Balance in target currency
        /// </summary>
        public double Balance { get; set; }

        /// <summary>
        /// Available balance (supported only by few exchanges)
        /// </summary>
        public double? BalanceAvailable { get; set; }

        /// <summary>
        /// Current leverage (supported only by few exchanges)
        /// </summary>
        public double? Leverage { get; set; }

        /// <summary>
        /// Realized profit (supported only by few exchanges)
        /// </summary>
        public double? RealizedPnl { get; set; }

        /// <summary>
        /// Unrealized profit (supported only by few exchanges)
        /// </summary>
        public double? UnrealizedPnl { get; set; }
    }
}
