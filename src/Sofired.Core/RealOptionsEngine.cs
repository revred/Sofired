using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sofired.Core
{
    /// <summary>
    /// Real options pricing engine using ThetaData for authentic market conditions
    /// Replaces synthetic Black-Scholes approximations with real bid/ask data
    /// </summary>
    public class RealOptionsEngine
    {
        private readonly ThetaDataClient _thetaClient;
        private readonly Dictionary<string, OptionsChain> _optionsCache;
        private DateTime _lastCacheUpdate;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(1);

        public RealOptionsEngine(ThetaDataClient thetaClient)
        {
            _thetaClient = thetaClient;
            _optionsCache = new Dictionary<string, OptionsChain>();
            _lastCacheUpdate = DateTime.MinValue;
        }

        /// <summary>
        /// Get real options pricing for put credit spreads
        /// </summary>
        public async Task<RealOptionsPricing> GetPutSpreadPricing(
            string symbol, 
            decimal stockPrice, 
            DateTime expirationDate, 
            decimal shortStrike, 
            decimal longStrike, 
            DateTime tradingDate)
        {
            try
            {
                // Attempt to get real options chain data
                var optionsChain = await GetOptionsChain(symbol, tradingDate, expirationDate);
                
                if (optionsChain?.PutOptions != null && optionsChain.PutOptions.Count > 0)
                {
                    return CalculateRealPutSpreadPricing(optionsChain, shortStrike, longStrike, stockPrice);
                }
                else
                {
                    Console.WriteLine($"⚠️  No real options data available for {symbol}, using enhanced synthetic pricing");
                    return CalculateEnhancedSyntheticPricing(symbol, stockPrice, expirationDate, shortStrike, longStrike, tradingDate);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error fetching real options data: {ex.Message}");
                return CalculateEnhancedSyntheticPricing(symbol, stockPrice, expirationDate, shortStrike, longStrike, tradingDate);
            }
        }

        /// <summary>
        /// Calculate real put spread pricing from actual options chain data
        /// </summary>
        private RealOptionsPricing CalculateRealPutSpreadPricing(
            OptionsChain optionsChain, 
            decimal shortStrike, 
            decimal longStrike, 
            decimal stockPrice)
        {
            var shortPut = optionsChain.PutOptions.FirstOrDefault(p => Math.Abs(p.Strike - shortStrike) < 0.01m);
            var longPut = optionsChain.PutOptions.FirstOrDefault(p => Math.Abs(p.Strike - longStrike) < 0.01m);

            if (shortPut != null && longPut != null)
            {
                // Real market pricing
                var shortPutCredit = (shortPut.Bid + shortPut.Ask) / 2m;
                var longPutDebit = (longPut.Bid + longPut.Ask) / 2m;
                var netCredit = shortPutCredit - longPutDebit;
                
                // Real bid-ask spread cost
                var shortSpreadCost = (shortPut.Ask - shortPut.Bid) / 2m;
                var longSpreadCost = (longPut.Ask - longPut.Bid) / 2m;
                var totalSpreadCost = shortSpreadCost + longSpreadCost;
                
                // Adjust for real market execution
                var realNetCredit = Math.Max(0.05m, netCredit - totalSpreadCost);
                
                return new RealOptionsPricing
                {
                    NetCreditReceived = realNetCredit,
                    MaxRisk = (shortStrike - longStrike) - realNetCredit,
                    MaxProfit = realNetCredit,
                    BidAskSpreadCost = totalSpreadCost,
                    ImpliedVolatility = shortPut.ImpliedVolatility,
                    Delta = shortPut.Delta - longPut.Delta,
                    Theta = shortPut.Theta - longPut.Theta,
                    Gamma = shortPut.Gamma - longPut.Gamma,
                    IsRealData = true,
                    Liquidity = Math.Min(shortPut.Volume, longPut.Volume),
                    OpenInterest = Math.Min(shortPut.OpenInterest, longPut.OpenInterest)
                };
            }

            // Fallback if specific strikes not available
            return CalculateFallbackPricing(optionsChain, shortStrike, longStrike, stockPrice);
        }

        /// <summary>
        /// Enhanced synthetic pricing with realistic market friction
        /// </summary>
        private RealOptionsPricing CalculateEnhancedSyntheticPricing(
            string symbol, 
            decimal stockPrice, 
            DateTime expirationDate, 
            decimal shortStrike, 
            decimal longStrike, 
            DateTime tradingDate)
        {
            var daysToExpiration = (expirationDate - tradingDate).Days;
            var timeValue = (decimal)Math.Sqrt(daysToExpiration / 365.0);
            
            // Enhanced volatility estimation based on symbol sector
            var sectorVol = GetSectorVolatility(symbol);
            var moneyness = (stockPrice - shortStrike) / stockPrice;
            
            // More realistic premium calculation
            var basePremium = stockPrice * timeValue * Math.Abs(moneyness) * sectorVol;
            
            // Realistic minimum premiums based on stock price
            var minPremium = stockPrice switch
            {
                <= 10m => 0.50m,
                <= 15m => 0.75m,
                <= 25m => 1.00m,
                <= 50m => 1.50m,
                <= 100m => 2.00m,
                _ => 3.00m
            };
            
            var shortPutPremium = Math.Max(minPremium, basePremium * 1.2m);
            var longPutPremium = Math.Max(minPremium * 0.6m, basePremium * 0.8m);
            var netCredit = shortPutPremium - longPutPremium;
            
            // Add realistic bid-ask spread friction (1-3% depending on liquidity)
            var spreadCost = netCredit * GetSpreadCostMultiplier(symbol, stockPrice);
            var adjustedCredit = Math.Max(0.10m, netCredit - spreadCost);
            
            // Add execution slippage (0.5-2%)
            var slippage = adjustedCredit * GetSlippageMultiplier(symbol);
            var finalCredit = Math.Max(0.05m, adjustedCredit - slippage);
            
            return new RealOptionsPricing
            {
                NetCreditReceived = finalCredit,
                MaxRisk = (shortStrike - longStrike) - finalCredit,
                MaxProfit = finalCredit,
                BidAskSpreadCost = spreadCost,
                ImpliedVolatility = sectorVol,
                Delta = CalculateSyntheticDelta(stockPrice, shortStrike, daysToExpiration),
                Theta = CalculateSyntheticTheta(finalCredit, daysToExpiration),
                Gamma = CalculateSyntheticGamma(stockPrice, shortStrike),
                IsRealData = false,
                Liquidity = EstimateLiquidity(symbol, stockPrice),
                OpenInterest = EstimateOpenInterest(symbol, stockPrice),
                ExecutionSlippage = slippage
            };
        }

        private decimal GetSectorVolatility(string symbol)
        {
            // Sector-based volatility estimates
            return symbol.ToUpper() switch
            {
                "SOFI" => 0.45m, // Fintech - high volatility
                "APP" => 0.55m,  // AdTech - very high volatility
                "AAPL" => 0.25m, // Large cap tech - moderate volatility
                "TSLA" => 0.60m, // EV - extreme volatility
                "NVDA" => 0.50m, // AI/GPU - high volatility
                _ => 0.35m       // Default moderate volatility
            };
        }

        private decimal GetSpreadCostMultiplier(string symbol, decimal stockPrice)
        {
            // Spread costs based on symbol liquidity and price
            var baseCost = symbol.ToUpper() switch
            {
                "SOFI" => 0.025m, // 2.5% spread for mid-cap fintech
                "APP" => 0.035m,  // 3.5% spread for smaller adtech
                "AAPL" => 0.010m, // 1.0% spread for liquid large cap
                "TSLA" => 0.020m, // 2.0% spread for volatile large cap
                "NVDA" => 0.015m, // 1.5% spread for tech mega cap
                _ => 0.030m       // 3.0% default for mid-cap
            };

            // Adjust for stock price (lower prices = higher spreads)
            if (stockPrice < 15m) baseCost *= 1.5m;
            else if (stockPrice < 30m) baseCost *= 1.2m;
            else if (stockPrice > 200m) baseCost *= 0.8m;

            return baseCost;
        }

        private decimal GetSlippageMultiplier(string symbol)
        {
            return symbol.ToUpper() switch
            {
                "SOFI" => 0.015m, // 1.5% slippage
                "APP" => 0.025m,  // 2.5% slippage  
                "AAPL" => 0.005m, // 0.5% slippage
                "TSLA" => 0.010m, // 1.0% slippage
                "NVDA" => 0.008m, // 0.8% slippage
                _ => 0.020m       // 2.0% default
            };
        }

        private decimal CalculateSyntheticDelta(decimal stockPrice, decimal strike, int dte)
        {
            var moneyness = stockPrice / strike;
            var timeAdjustment = Math.Max(0.1, dte / 30.0);
            return (decimal)(-0.5 * Math.Exp(-Math.Abs(1 - (double)moneyness)) * timeAdjustment);
        }

        private decimal CalculateSyntheticTheta(decimal premium, int dte)
        {
            return dte > 0 ? -premium / dte * 0.7m : 0m;
        }

        private decimal CalculateSyntheticGamma(decimal stockPrice, decimal strike)
        {
            var distance = Math.Abs(stockPrice - strike);
            return distance < 5m ? 0.05m : 0.02m;
        }

        private int EstimateLiquidity(string symbol, decimal stockPrice)
        {
            return symbol.ToUpper() switch
            {
                "SOFI" => 150,  // Moderate liquidity
                "APP" => 75,   // Lower liquidity
                "AAPL" => 2000, // Very high liquidity
                "TSLA" => 800,  // High liquidity
                "NVDA" => 1200, // Very high liquidity
                _ => 100       // Default moderate
            };
        }

        private int EstimateOpenInterest(string symbol, decimal stockPrice)
        {
            return EstimateLiquidity(symbol, stockPrice) * 5; // Rough 5:1 ratio
        }

        private async Task<OptionsChain?> GetOptionsChain(string symbol, DateTime tradingDate, DateTime expirationDate)
        {
            var cacheKey = $"{symbol}_{tradingDate:yyyyMMdd}_{expirationDate:yyyyMMdd}";
            
            // Check cache first
            if (_optionsCache.ContainsKey(cacheKey) && DateTime.Now - _lastCacheUpdate < _cacheExpiry)
            {
                return _optionsCache[cacheKey];
            }

            try
            {
                // Attempt to fetch from ThetaData
                var optionsData = await _thetaClient.GetOptionsChain(symbol, tradingDate, expirationDate);
                
                if (optionsData != null)
                {
                    _optionsCache[cacheKey] = optionsData;
                    _lastCacheUpdate = DateTime.Now;
                    
                    Console.WriteLine($"✅ Loaded real options data for {symbol} expiring {expirationDate:yyyy-MM-dd}");
                    return optionsData;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  ThetaData options request failed: {ex.Message}");
            }

            return null;
        }

        private RealOptionsPricing CalculateFallbackPricing(OptionsChain optionsChain, decimal shortStrike, decimal longStrike, decimal stockPrice)
        {
            // Use nearest available strikes
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
                Liquidity = Math.Min(shortPut.Volume, longPut.Volume),
                OpenInterest = Math.Min(shortPut.OpenInterest, longPut.OpenInterest)
            };
        }
    }

    /// <summary>
    /// Real options pricing result with market microstructure data
    /// </summary>
    public class RealOptionsPricing
    {
        public decimal NetCreditReceived { get; set; }
        public decimal MaxRisk { get; set; }
        public decimal MaxProfit { get; set; }
        public decimal BidAskSpreadCost { get; set; }
        public decimal ImpliedVolatility { get; set; }
        public decimal Delta { get; set; }
        public decimal Theta { get; set; }
        public decimal Gamma { get; set; }
        public bool IsRealData { get; set; }
        public int Liquidity { get; set; }
        public int OpenInterest { get; set; }
        public decimal ExecutionSlippage { get; set; }
        
        /// <summary>
        /// Realistic profit/loss calculation considering all market friction
        /// </summary>
        public decimal CalculateRealisticPnL(decimal currentStockPrice, decimal shortStrike, decimal longStrike, int daysRemaining)
        {
            // Time decay benefit (theta)
            var timeDecayBenefit = Theta * (21 - daysRemaining) / 21m; // Assume 21 DTE entry
            
            // Intrinsic value risk
            var intrinsicRisk = 0m;
            if (currentStockPrice < shortStrike)
            {
                intrinsicRisk = Math.Min(shortStrike - currentStockPrice, shortStrike - longStrike);
            }
            
            // Net P&L considering all factors
            var unrealizedPnL = NetCreditReceived + timeDecayBenefit - intrinsicRisk;
            
            // Subtract exit costs (spread + slippage)
            var exitCosts = BidAskSpreadCost + ExecutionSlippage;
            
            return Math.Max(-MaxRisk, unrealizedPnL - exitCosts);
        }
        
        /// <summary>
        /// Probability of profit based on real market conditions
        /// </summary>
        public decimal ProbabilityOfProfit => IsRealData 
            ? Math.Max(0.45m, Math.Min(0.85m, 0.65m + (Delta * 100m) / 100m))
            : 0.60m; // Conservative synthetic estimate
    }

    /// <summary>
    /// Options chain data structure
    /// </summary>
    public class OptionsChain
    {
        public List<OptionContract> CallOptions { get; set; } = new();
        public List<OptionContract> PutOptions { get; set; } = new();
        public DateTime ExpirationDate { get; set; }
        public decimal UnderlyingPrice { get; set; }
        public DateTime TradingDate { get; set; }
    }

    /// <summary>
    /// Individual option contract data
    /// </summary>
    public class OptionContract
    {
        public decimal Strike { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public decimal LastPrice { get; set; }
        public int Volume { get; set; }
        public int OpenInterest { get; set; }
        public decimal ImpliedVolatility { get; set; }
        public decimal Delta { get; set; }
        public decimal Gamma { get; set; }
        public decimal Theta { get; set; }
        public decimal Vega { get; set; }
        public bool IsPut { get; set; }
    }
}