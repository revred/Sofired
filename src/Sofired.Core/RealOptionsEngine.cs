using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sofired.Core
{
    /// <summary>
    /// Real options pricing engine using MCP market data service
    /// </summary>
    public class RealOptionsEngine
    {
        private readonly IMarketDataService _marketDataService;

        public RealOptionsEngine(IMarketDataService marketDataService)
        {
            _marketDataService = marketDataService;
        }

        /// <summary>
        /// Get real put spread pricing from MCP market data service
        /// </summary>
        public async Task<RealOptionsPricing> GetPutSpreadPricing(string symbol, decimal stockPrice, DateTime expirationDate, decimal shortStrike, decimal longStrike, DateTime tradingDate)
        {
            try
            {
                Console.WriteLine($"🔥 Fetching real options data for {symbol} expiring {expirationDate:yyyy-MM-dd} (trading day {tradingDate:yyyy-MM-dd})...");
                
                var optionsChain = await _marketDataService.GetOptionsChain(symbol, tradingDate, expirationDate);
                
                if (optionsChain == null || optionsChain.PutOptions.Count == 0)
                {
                    Console.WriteLine($"❌ CRITICAL: No real options data available for {symbol}. System requires real market data.");
                    throw new InvalidOperationException($"Unable to fetch real options data for {symbol}. System requires real market data.");
                }

                // Find the exact strikes we need
                var shortPut = optionsChain.PutOptions.FirstOrDefault(p => Math.Abs(p.Strike - shortStrike) < 0.01m);
                var longPut = optionsChain.PutOptions.FirstOrDefault(p => Math.Abs(p.Strike - longStrike) < 0.01m);

                if (shortPut == null || longPut == null)
                {
                    Console.WriteLine($"⚠️ Exact strikes not found, using nearest available strikes");
                    return CalculateFallbackPricing(optionsChain, shortStrike, longStrike, stockPrice);
                }

                // Calculate the put credit spread pricing
                var netCredit = shortPut.Bid - longPut.Ask; // What we receive for selling the spread
                var maxRisk = (shortStrike - longStrike) - netCredit;
                var bidAskSpread = (shortPut.Ask - shortPut.Bid) + (longPut.Ask - longPut.Bid);

                Console.WriteLine($"✅ Real pricing: Short Put {shortStrike} @ ${shortPut.Bid}/{shortPut.Ask}, Long Put {longStrike} @ ${longPut.Bid}/{longPut.Ask}");
                Console.WriteLine($"   Net Credit: ${netCredit:F2}, Max Risk: ${maxRisk:F2}");

                return new RealOptionsPricing
                {
                    NetCreditReceived = Math.Max(0.05m, netCredit),
                    MaxRisk = maxRisk,
                    MaxProfit = netCredit,
                    BidAskSpreadCost = bidAskSpread / 4m,
                    ImpliedVolatility = shortPut.ImpliedVolatility,
                    Delta = shortPut.Delta - longPut.Delta,
                    Theta = shortPut.Theta - longPut.Theta,
                    IsRealData = true,
                    ShortStrike = shortStrike,
                    LongStrike = longStrike,
                    DaysToExpiration = (expirationDate - tradingDate).Days
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ CRITICAL: Error fetching real options data: {ex.Message}");
                Console.WriteLine($"❌ BACKTEST FAILED: Cannot proceed without real options data from ThetaData API.");
                throw new InvalidOperationException($"Unable to fetch real options data for {symbol}. Synthetic data is prohibited. Backtest terminated.");
            }
        }


        private RealOptionsPricing CalculateFallbackPricing(OptionsChain optionsChain, decimal shortStrike, decimal longStrike, decimal stockPrice)
        {
            var availableStrikes = optionsChain.PutOptions.Select(p => p.Strike).OrderBy(s => s).ToList();
            var nearestShort = availableStrikes.OrderBy(s => Math.Abs(s - shortStrike)).First();
            var nearestLong = availableStrikes.OrderBy(s => Math.Abs(s - longStrike)).First();

            var shortPut = optionsChain.PutOptions.First(p => p.Strike == nearestShort);
            var longPut = optionsChain.PutOptions.First(p => p.Strike == nearestLong);

            var netCredit = ((shortPut.Bid + shortPut.Ask) / 2m) - ((longPut.Bid + longPut.Ask) / 2m);
            var spreadCost = ((shortPut.Ask - shortPut.Bid) + (longPut.Ask - longPut.Bid)) / 4m;

            return new RealOptionsPricing
            {
                NetCreditReceived = Math.Max(0.05m, netCredit - spreadCost),
                MaxRisk = (nearestShort - nearestLong) - netCredit,
                MaxProfit = netCredit,
                BidAskSpreadCost = spreadCost,
                ImpliedVolatility = shortPut.ImpliedVolatility,
                Delta = shortPut.Delta - longPut.Delta,
                Theta = shortPut.Theta - longPut.Theta,
                IsRealData = true,
                ShortStrike = nearestShort,
                LongStrike = nearestLong,
                DaysToExpiration = 45 // Default
            };
        }
    }

    public class OptionsChain
    {
        public DateTime ExpirationDate { get; set; }
        public DateTime TradingDate { get; set; }
        public List<OptionContract> PutOptions { get; set; } = new();
        public List<OptionContract> CallOptions { get; set; } = new();
        public decimal UnderlyingPrice { get; set; }
    }

    public class OptionContract
    {
        public decimal Strike { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public int Volume { get; set; }
        public int OpenInterest { get; set; }
        public decimal ImpliedVolatility { get; set; }
        public decimal Delta { get; set; }
        public decimal Theta { get; set; }
        public decimal LastPrice { get; set; }
        public decimal Gamma { get; set; }
        public decimal Vega { get; set; }
        public bool IsPut { get; set; }
    }

    public class RealOptionsPricing
    {
        public decimal NetCreditReceived { get; set; }
        public decimal MaxRisk { get; set; }
        public decimal MaxProfit { get; set; }
        public decimal BidAskSpreadCost { get; set; }
        public decimal ImpliedVolatility { get; set; }
        public decimal Delta { get; set; }
        public decimal Theta { get; set; }
        public bool IsRealData { get; set; }
        public decimal ShortStrike { get; set; }
        public decimal LongStrike { get; set; }
        public int DaysToExpiration { get; set; }
    }
}