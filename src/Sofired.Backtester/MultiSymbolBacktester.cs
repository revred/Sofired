using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Sofired.Core;

namespace Sofired.Backtester
{
    /// <summary>
    /// Multi-symbol backtesting system for portfolio-level strategy validation
    /// Tests different symbol combinations with sector-specific configurations
    /// </summary>
    public class MultiSymbolBacktester
    {
        private readonly string _host;
        private readonly string _port;
        private readonly string _outDir;
        
        public MultiSymbolBacktester(string host = "http://localhost", string port = "25510", string outDir = "out")
        {
            _host = host;
            _port = port;
            _outDir = outDir;
        }
        
        /// <summary>
        /// Run comprehensive multi-symbol backtest
        /// </summary>
        public async Task RunMultiSymbolBacktest(List<string> symbols, decimal portfolioCapital = 50000m)
        {
            Console.WriteLine($"\n🚀 PHASE 2: MULTI-SYMBOL PORTFOLIO BACKTEST");
            Console.WriteLine($"Symbols: {string.Join(", ", symbols)}");
            Console.WriteLine($"Portfolio Capital: ${portfolioCapital:N0}");
            Console.WriteLine("=".PadRight(60, '='));
            
            // Initialize MCP-based market data service
            IMarketDataService marketDataService = new StrollThetaMarketService();
            var realOptionsEngine = new RealOptionsEngine(marketDataService);
            
            // Initialize multi-symbol portfolio engine
            var portfolioEngine = new MultiSymbolPortfolioEngine(portfolioCapital, realOptionsEngine);
            await portfolioEngine.InitializeSymbols(symbols);
            
            // Get date range from first symbol configuration
            var configManager = new ConfigurationManager();
            var firstConfig = configManager.LoadSymbolConfig(symbols.First());
            var startDate = DateTime.Parse(firstConfig.Backtest.StartDate);
            var endDate = DateTime.Parse(firstConfig.Backtest.EndDate);
            
            // Load price data for all symbols
            var symbolPriceData = await LoadMultiSymbolPriceData(symbols, startDate, endDate);
            
            // Load VIX data
            var vixData = await LoadVixData(marketDataService, startDate, endDate);
            
            // Run portfolio backtest
            var results = await portfolioEngine.RunPortfolioBacktest(startDate, endDate, symbolPriceData, vixData);
            
            // Generate comprehensive report
            portfolioEngine.GeneratePortfolioReport(results);
            
            // Save detailed results
            await SavePortfolioResults(results, symbols);
        }
        
        /// <summary>
        /// Load price data for multiple symbols
        /// </summary>
        private async Task<Dictionary<string, List<DailyBar>>> LoadMultiSymbolPriceData(
            List<string> symbols, DateTime startDate, DateTime endDate)
        {
            Console.WriteLine($"\n📊 Loading price data for {symbols.Count} symbols...");
            var symbolPriceData = new Dictionary<string, List<DailyBar>>();
            
            foreach (var symbol in symbols)
            {
                try
                {
                    Console.WriteLine($"Loading {symbol} data from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}...");
                    
                    // Try to get real data from ThetaData
                    var bars = await GetDailyBars(symbol, startDate, endDate);
                    
                    if (bars.Count > 0)
                    {
                        Console.WriteLine($"✅ {symbol}: {bars.Count} real trading days loaded");
                    }
                    else
                    {
                        Console.WriteLine($"⚠️  {symbol}: No real data available, generating synthetic");
                        bars = GenerateSyntheticData(symbol, startDate, endDate);
                    }
                    
                    symbolPriceData[symbol] = bars;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error loading {symbol} data: {ex.Message}");
                    // Generate synthetic data as fallback
                    symbolPriceData[symbol] = GenerateSyntheticData(symbol, startDate, endDate);
                }
            }
            
            return symbolPriceData;
        }
        
        /// <summary>
        /// Get daily bars for a symbol (implementation from Program.cs)
        /// </summary>
        private async Task<List<DailyBar>> GetDailyBars(string symbol, DateTime start, DateTime end)
        {
            try
            {
                var startDateStr = start.ToString("yyyyMMdd");
                var endDateStr = end.ToString("yyyyMMdd");
                var url = $"{_host}:{_port}/v2/hist/stock/ohlc?root={symbol}&start_date={startDateStr}&end_date={endDateStr}&ivl=86400000&rth=true";
                
                Console.WriteLine($"ThetaData request: {url}");
                
                using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var response = await httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return ParseDailyBars(responseContent);
                }
                else
                {
                    Console.WriteLine($"❌ ThetaData API error: {response.StatusCode}");
                    return new List<DailyBar>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error fetching {symbol} data: {ex.Message}");
                return new List<DailyBar>();
            }
        }
        
        /// <summary>
        /// Parse daily bars from ThetaData response
        /// </summary>
        private List<DailyBar> ParseDailyBars(string jsonResponse)
        {
            var bars = new List<DailyBar>();
            
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("response", out var response) && response.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var dataPoint in response.EnumerateArray())
                    {
                        if (dataPoint.ValueKind == System.Text.Json.JsonValueKind.Array && dataPoint.GetArrayLength() >= 8)
                        {
                            var dataArray = dataPoint.EnumerateArray().ToArray();
                            
                            var open = dataArray[1].GetDecimal();
                            var high = dataArray[2].GetDecimal();
                            var low = dataArray[3].GetDecimal();
                            var close = dataArray[4].GetDecimal();
                            var volume = dataArray[5].GetInt64();
                            var dateInt = dataArray[7].GetInt32();
                            
                            // Convert YYYYMMDD integer to DateTime
                            var dateString = dateInt.ToString();
                            if (dateString.Length == 8)
                            {
                                var year = int.Parse(dateString.Substring(0, 4));
                                var month = int.Parse(dateString.Substring(4, 2));
                                var day = int.Parse(dateString.Substring(6, 2));
                                var date = new DateTime(year, month, day);
                                
                                bars.Add(new DailyBar(date, open, high, low, close, volume));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing daily bars: {ex.Message}");
            }
            
            return bars.OrderBy(b => b.Date).ToList();
        }
        
        /// <summary>
        /// Generate synthetic price data when real data unavailable
        /// </summary>
        private List<DailyBar> GenerateSyntheticData(string symbol, DateTime startDate, DateTime endDate)
        {
            var bars = new List<DailyBar>();
            var random = new Random(symbol.GetHashCode()); // Consistent seed per symbol
            
            // Symbol-specific starting prices and characteristics
            var (startPrice, volatility, drift) = symbol.ToUpper() switch
            {
                "AAPL" => (150m, 0.20m, 0.08m),   // Apple: $150, 20% vol, 8% drift
                "NVDA" => (400m, 0.35m, 0.25m),   // NVIDIA: $400, 35% vol, 25% drift  
                "TSLA" => (200m, 0.50m, 0.15m),   // Tesla: $200, 50% vol, 15% drift
                "SOFI" => (12m, 0.45m, 0.20m),    // SoFi: $12, 45% vol, 20% drift
                "APP" => (25m, 0.55m, 0.10m),     // App: $25, 55% vol, 10% drift
                _ => (100m, 0.30m, 0.10m)          // Default
            };
            
            var currentPrice = startPrice;
            var currentDate = startDate;
            
            while (currentDate <= endDate)
            {
                // Skip weekends
                if (currentDate.DayOfWeek != DayOfWeek.Saturday && currentDate.DayOfWeek != DayOfWeek.Sunday)
                {
                    // Generate daily price movement
                    var dailyReturn = (decimal)(random.NextGaussian(0, (double)volatility / Math.Sqrt(252)));
                    var driftReturn = drift / 252m;
                    var totalReturn = dailyReturn + driftReturn;
                    
                    var newPrice = currentPrice * (1 + totalReturn);
                    
                    // Generate OHLC based on close price
                    var high = newPrice * (1 + (decimal)Math.Abs(random.NextGaussian(0, 0.01)));
                    var low = newPrice * (1 - (decimal)Math.Abs(random.NextGaussian(0, 0.01)));
                    var open = currentPrice; // Use previous close as open
                    var volume = (long)(random.Next(1000000, 10000000));
                    
                    bars.Add(new DailyBar(
                        currentDate,
                        open,
                        Math.Max(high, Math.Max(open, newPrice)),
                        Math.Min(low, Math.Min(open, newPrice)),
                        newPrice,
                        volume
                    ));
                    
                    currentPrice = newPrice;
                }
                
                currentDate = currentDate.AddDays(1);
            }
            
            Console.WriteLine($"Generated {bars.Count} synthetic trading days for {symbol}, price range: ${bars.Min(b => b.Close):F2} - ${bars.Max(b => b.Close):F2}");
            return bars;
        }
        
        /// <summary>
        /// Load VIX data for volatility analysis
        /// </summary>
        private async Task<Dictionary<DateTime, decimal>> LoadVixData(IMarketDataService marketDataService, DateTime startDate, DateTime endDate)
        {
            Console.WriteLine("Loading VIX data for volatility analysis...");
            
            try
            {
                // Use IMarketDataService for VIX data fetching via ThetaData bridge
                var vixBars = await marketDataService.GetDailyBarsAsync("VIX", startDate, endDate);
                
                if (vixBars.Count > 0)
                {
                    Console.WriteLine($"✅ Loaded {vixBars.Count} real VIX data points");
                    return vixBars.ToDictionary(v => v.Date, v => v.Close);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ VIX data error: {ex.Message}");
            }
            
            Console.WriteLine("❌ CRITICAL: No real VIX data available. System requires real market data.");
            return new Dictionary<DateTime, decimal>();
        }
        
        /// <summary>
        /// Generate synthetic VIX data when real data unavailable
        /// </summary>
        private Dictionary<DateTime, decimal> GenerateSyntheticVixData(DateTime startDate, DateTime endDate)
        {
            var vixData = new Dictionary<DateTime, decimal>();
            var random = new Random(42);
            var currentVix = 20m;
            var currentDate = startDate;
            
            while (currentDate <= endDate)
            {
                if (currentDate.DayOfWeek != DayOfWeek.Saturday && currentDate.DayOfWeek != DayOfWeek.Sunday)
                {
                    // VIX mean reversion model
                    var meanReversion = (20m - currentVix) * 0.1m;
                    var randomShock = (decimal)random.NextGaussian(0, 2);
                    currentVix = Math.Max(10m, Math.Min(50m, currentVix + meanReversion + randomShock));
                    
                    vixData[currentDate] = currentVix;
                }
                currentDate = currentDate.AddDays(1);
            }
            
            return vixData;
        }
        
        /// <summary>
        /// Save portfolio results to files
        /// </summary>
        private async Task SavePortfolioResults(MultiSymbolPortfolioResults results, List<string> symbols)
        {
            var timestamp = DateTime.Now.ToString("HHmm");
            var dateRange = $"{results.StartDate:yyyyMMdd}_{results.EndDate:yyyyMMdd}";
            var symbolsString = string.Join("-", symbols.Take(3)); // First 3 symbols
            
            var resultsPath = System.IO.Path.Combine(_outDir, "20250908", $"{timestamp}_MULTI_{symbolsString}_{dateRange}.txt");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(resultsPath));
            
            var report = GenerateDetailedPortfolioReport(results);
            await System.IO.File.WriteAllTextAsync(resultsPath, report);
            
            Console.WriteLine($"\n💾 Portfolio results saved: {resultsPath}");
        }
        
        /// <summary>
        /// Generate detailed portfolio report
        /// </summary>
        private string GenerateDetailedPortfolioReport(MultiSymbolPortfolioResults results)
        {
            var report = new System.Text.StringBuilder();
            
            report.AppendLine("MULTI-SYMBOL PORTFOLIO BACKTEST RESULTS");
            report.AppendLine("=".PadRight(60, '='));
            report.AppendLine($"Period: {results.StartDate:yyyy-MM-dd} to {results.EndDate:yyyy-MM-dd}");
            report.AppendLine($"Initial Capital: ${results.InitialCapital:N2}");
            report.AppendLine($"Final Capital: ${results.FinalCapital:N2}");
            report.AppendLine($"Total P&L: ${results.TotalPnL:N2}");
            report.AppendLine($"Portfolio ROI: {results.PortfolioROI:P2}");
            report.AppendLine($"Total Trades: {results.TotalTrades}");
            report.AppendLine();
            
            report.AppendLine("SYMBOL PERFORMANCE BREAKDOWN:");
            report.AppendLine("-".PadRight(60, '-'));
            
            foreach (var symbolResult in results.SymbolResults.Values.OrderByDescending(s => s.ROI))
            {
                report.AppendLine($"{symbolResult.Symbol} ({symbolResult.Sector}):");
                report.AppendLine($"  P&L: ${symbolResult.TotalPnL:N2} ({symbolResult.ROI:P2} ROI)");
                report.AppendLine($"  Trades: {symbolResult.TotalTrades}");
                report.AppendLine($"  Sessions: {symbolResult.Sessions.Count}");
                report.AppendLine();
            }
            
            return report.ToString();
        }
    }
    
    /// <summary>
    /// Extension methods for random number generation
    /// </summary>
    public static class RandomExtensions
    {
        public static double NextGaussian(this Random random, double mean = 0.0, double stdDev = 1.0)
        {
            var u1 = 1.0 - random.NextDouble();
            var u2 = 1.0 - random.NextDouble();
            var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + stdDev * randStdNormal;
        }
    }
}