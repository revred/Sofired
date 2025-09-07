using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sofired.Core
{
    /// <summary>
    /// Multi-symbol portfolio management engine for sector-diversified trading
    /// Manages risk allocation across different symbols with sector-specific configurations
    /// </summary>
    public class MultiSymbolPortfolioEngine
    {
        private readonly Dictionary<string, TradingEngine> _symbolEngines;
        private readonly Dictionary<string, SymbolConfig> _symbolConfigs;
        private readonly ConfigurationManager _configManager;
        private readonly RealOptionsEngine _realOptionsEngine;
        private readonly decimal _totalPortfolioCapital;
        
        public MultiSymbolPortfolioEngine(decimal totalCapital, RealOptionsEngine realOptionsEngine)
        {
            _symbolEngines = new Dictionary<string, TradingEngine>();
            _symbolConfigs = new Dictionary<string, SymbolConfig>();
            _configManager = new ConfigurationManager();
            _realOptionsEngine = realOptionsEngine;
            _totalPortfolioCapital = totalCapital;
        }
        
        /// <summary>
        /// Initialize trading engines for multiple symbols with their configurations
        /// </summary>
        public async Task InitializeSymbols(List<string> symbols)
        {
            Console.WriteLine($"\n🚀 PHASE 2: MULTI-SYMBOL PORTFOLIO INITIALIZATION");
            Console.WriteLine($"Symbols: {string.Join(", ", symbols)}");
            Console.WriteLine($"Total Portfolio Capital: ${_totalPortfolioCapital:N0}");
            Console.WriteLine("=".PadRight(60, '='));
            
            foreach (var symbol in symbols)
            {
                try
                {
                    // Load symbol-specific configuration
                    var config = _configManager.LoadSymbolConfig(symbol);
                    _symbolConfigs[symbol] = config;
                    
                    // Calculate capital allocation for this symbol
                    var symbolCapital = CalculateSymbolCapitalAllocation(symbol, config);
                    
                    // Create symbol-specific strategy config
                    var strategyConfig = CreateSymbolStrategyConfig(config, symbolCapital);
                    
                    // Initialize trading engine for this symbol
                    var engine = new TradingEngine(strategyConfig, null, _realOptionsEngine);
                    _symbolEngines[symbol] = engine;
                    
                    Console.WriteLine($"✅ {symbol} ({config.Company.Sector}): ${symbolCapital:N0} allocated");
                    Console.WriteLine($"   Strategy Mix: {config.Strategy.PutCreditSpreadWeight:P0} PCS / {config.Strategy.CoveredCallWeight:P0} CC");
                    Console.WriteLine($"   Risk Limits: {config.Risk.MaxPositionSize:P0} max position, {config.Risk.MaxLossPerTrade:P1} max loss");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Failed to initialize {symbol}: {ex.Message}");
                }
            }
            
            Console.WriteLine($"\n📊 Portfolio Summary: {_symbolEngines.Count} symbols initialized");
        }
        
        /// <summary>
        /// Run multi-symbol backtest across all initialized symbols
        /// </summary>
        public async Task<MultiSymbolPortfolioResults> RunPortfolioBacktest(
            DateTime startDate, 
            DateTime endDate,
            Dictionary<string, List<DailyBar>> symbolPriceData,
            Dictionary<DateTime, decimal> vixData)
        {
            Console.WriteLine($"\n📈 MULTI-SYMBOL BACKTEST: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
            
            var portfolioResults = new MultiSymbolPortfolioResults
            {
                StartDate = startDate,
                EndDate = endDate,
                InitialCapital = _totalPortfolioCapital,
                SymbolResults = new Dictionary<string, SymbolPerformance>()
            };
            
            // Get all trading dates
            var allTradingDates = symbolPriceData.Values
                .SelectMany(bars => bars.Select(b => b.Date))
                .Distinct()
                .OrderBy(d => d)
                .Where(d => d >= startDate && d <= endDate)
                .ToList();
            
            Console.WriteLine($"Processing {allTradingDates.Count} trading days across {_symbolEngines.Count} symbols...");
            
            // Process each trading day across all symbols
            foreach (var date in allTradingDates)
            {
                var dailyVix = vixData.ContainsKey(date) ? vixData[date] : 20m; // Default VIX
                
                // Process each symbol for this date
                foreach (var symbolKvp in _symbolEngines)
                {
                    var symbol = symbolKvp.Key;
                    var engine = symbolKvp.Value;
                    
                    if (symbolPriceData.ContainsKey(symbol))
                    {
                        var symbolBars = symbolPriceData[symbol];
                        var dayBar = symbolBars.FirstOrDefault(b => b.Date.Date == date.Date);
                        
                        if (dayBar != null)
                        {
                            // Process trading day for this symbol
                            var session = engine.ProcessTradingDay(date, dayBar, dailyVix);
                            
                            // Track symbol performance
                            if (!portfolioResults.SymbolResults.ContainsKey(symbol))
                            {
                                portfolioResults.SymbolResults[symbol] = new SymbolPerformance
                                {
                                    Symbol = symbol,
                                    Sector = _symbolConfigs[symbol].Company.Sector,
                                    Sessions = new List<TradingSession>()
                                };
                            }
                            
                            portfolioResults.SymbolResults[symbol].Sessions.Add(session);
                        }
                    }
                }
                
                // Log progress periodically
                if (allTradingDates.IndexOf(date) % 50 == 0)
                {
                    var progress = (allTradingDates.IndexOf(date) + 1) / (decimal)allTradingDates.Count;
                    Console.WriteLine($"Progress: {progress:P1} - {date:yyyy-MM-dd}");
                }
            }
            
            // Calculate portfolio-level metrics
            CalculatePortfolioMetrics(portfolioResults);
            
            return portfolioResults;
        }
        
        /// <summary>
        /// Calculate capital allocation for each symbol based on risk and sector
        /// </summary>
        private decimal CalculateSymbolCapitalAllocation(string symbol, SymbolConfig config)
        {
            // Base allocation based on symbol characteristics
            var baseAllocation = symbol.ToUpper() switch
            {
                "AAPL" => 0.35m,  // 35% - Large stable tech
                "NVDA" => 0.25m,  // 25% - High growth AI  
                "SOFI" => 0.20m,  // 20% - Fintech growth
                "APP"  => 0.10m,  // 10% - Smaller adtech
                "TSLA" => 0.10m,  // 10% - High volatility EV
                _ => 0.15m        // 15% - Default
            };
            
            // Adjust for risk parameters
            var riskAdjustment = 1.0m;
            if (config.Risk.MaxLossPerTrade > 0.06m) // High risk tolerance
                riskAdjustment *= 0.8m;
            else if (config.Risk.MaxLossPerTrade < 0.04m) // Low risk tolerance  
                riskAdjustment *= 1.2m;
            
            // Sector diversification adjustment
            var sectorAllocation = baseAllocation * riskAdjustment;
            var symbolCapital = _totalPortfolioCapital * sectorAllocation;
            
            return Math.Max(5000m, symbolCapital); // Minimum $5k per symbol
        }
        
        /// <summary>
        /// Create symbol-specific strategy configuration
        /// </summary>
        private StrategyConfig CreateSymbolStrategyConfig(SymbolConfig symbolConfig, decimal symbolCapital)
        {
            return new StrategyConfig
            {
                InitialCapital = symbolCapital,
                AggressivenessMultiplier = 1.5m, // Moderate aggression for multi-symbol
                CapitalAllocationPerTrade = symbolConfig.Risk.CapitalAllocation,
                EarlyCloseThreshold = symbolConfig.Strategy.ProfitTarget,
                PreferredDTE = symbolConfig.Trading.Options.DteOptimal,
                MinDTE = symbolConfig.Trading.Options.DteMin,
                MaxDTE = symbolConfig.Trading.Options.DteMax,
                MinContractSize = 1,
                MaxContractSize = 20  // Reduced max for multi-symbol
            };
        }
        
        /// <summary>
        /// Calculate portfolio-level performance metrics
        /// </summary>
        private void CalculatePortfolioMetrics(MultiSymbolPortfolioResults results)
        {
            decimal totalPnL = 0m;
            decimal totalCapital = 0m;
            int totalTrades = 0;
            
            foreach (var symbolResult in results.SymbolResults.Values)
            {
                // Calculate symbol-level metrics
                var symbolPnL = symbolResult.Sessions.Sum(s => s.TotalPnL);
                var symbolTrades = symbolResult.Sessions.Sum(s => s.PositionsClosed);
                
                symbolResult.TotalPnL = symbolPnL;
                symbolResult.TotalTrades = symbolTrades;
                symbolResult.ROI = GetSymbolInitialCapital(symbolResult.Symbol) > 0 
                    ? symbolPnL / GetSymbolInitialCapital(symbolResult.Symbol) 
                    : 0m;
                
                totalPnL += symbolPnL;
                totalCapital += GetSymbolInitialCapital(symbolResult.Symbol);
                totalTrades += symbolTrades;
            }
            
            results.TotalPnL = totalPnL;
            results.PortfolioROI = totalCapital > 0 ? totalPnL / totalCapital : 0m;
            results.TotalTrades = totalTrades;
            results.FinalCapital = _totalPortfolioCapital + totalPnL;
        }
        
        private decimal GetSymbolInitialCapital(string symbol)
        {
            return _symbolEngines.ContainsKey(symbol) 
                ? _symbolEngines[symbol].GetCurrentCapital() - _symbolEngines[symbol].GetTotalPnL()
                : 0m;
        }
        
        /// <summary>
        /// Generate comprehensive portfolio performance report
        /// </summary>
        public void GeneratePortfolioReport(MultiSymbolPortfolioResults results)
        {
            Console.WriteLine($"\n📊 MULTI-SYMBOL PORTFOLIO PERFORMANCE REPORT");
            Console.WriteLine("=".PadRight(60, '='));
            Console.WriteLine($"Period: {results.StartDate:yyyy-MM-dd} to {results.EndDate:yyyy-MM-dd}");
            Console.WriteLine($"Initial Capital: ${results.InitialCapital:N0}");
            Console.WriteLine($"Final Capital: ${results.FinalCapital:N0}");
            Console.WriteLine($"Total P&L: ${results.TotalPnL:N0} ({results.PortfolioROI:P1} ROI)");
            Console.WriteLine($"Total Trades: {results.TotalTrades}");
            
            Console.WriteLine($"\n📈 SYMBOL BREAKDOWN:");
            Console.WriteLine($"{"Symbol",-6} {"Sector",-15} {"P&L",-10} {"ROI",-8} {"Trades",-6} {"Performance",-12}");
            Console.WriteLine("-".PadRight(60, '-'));
            
            foreach (var symbolResult in results.SymbolResults.Values.OrderByDescending(s => s.ROI))
            {
                var performance = symbolResult.ROI >= 0.15m ? "🟢 Strong" :
                                 symbolResult.ROI >= 0.05m ? "🟡 Moderate" :
                                 symbolResult.ROI >= 0.0m ? "🟠 Weak" : "🔴 Loss";
                                 
                Console.WriteLine($"{symbolResult.Symbol,-6} {symbolResult.Sector,-15} ${symbolResult.TotalPnL,-9:N0} {symbolResult.ROI,-7:P1} {symbolResult.TotalTrades,-6} {performance,-12}");
            }
            
            // Sector analysis
            Console.WriteLine($"\n🏭 SECTOR ANALYSIS:");
            var sectorGroups = results.SymbolResults.Values.GroupBy(s => s.Sector);
            foreach (var sector in sectorGroups)
            {
                var sectorPnL = sector.Sum(s => s.TotalPnL);
                var sectorTrades = sector.Sum(s => s.TotalTrades);
                Console.WriteLine($"{sector.Key}: ${sectorPnL:N0} P&L, {sectorTrades} trades, {sector.Count()} symbols");
            }
        }
    }
    
    /// <summary>
    /// Multi-symbol portfolio backtest results
    /// </summary>
    public class MultiSymbolPortfolioResults
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal InitialCapital { get; set; }
        public decimal FinalCapital { get; set; }
        public decimal TotalPnL { get; set; }
        public decimal PortfolioROI { get; set; }
        public int TotalTrades { get; set; }
        public Dictionary<string, SymbolPerformance> SymbolResults { get; set; } = new();
    }
    
    /// <summary>
    /// Performance results for individual symbol
    /// </summary>
    public class SymbolPerformance
    {
        public string Symbol { get; set; } = "";
        public string Sector { get; set; } = "";
        public decimal TotalPnL { get; set; }
        public decimal ROI { get; set; }
        public int TotalTrades { get; set; }
        public List<TradingSession> Sessions { get; set; } = new();
    }
}