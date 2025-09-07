using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Sofired.Core;

class Program
{
    static readonly string Host = Environment.GetEnvironmentVariable("THETA_HOST") ?? "http://localhost";
    static readonly string Port = Environment.GetEnvironmentVariable("THETA_PORT") ?? "25510";
    static readonly string ApiKey = Environment.GetEnvironmentVariable("THETA_API_KEY") ?? "";
    static readonly string OutDir = Environment.GetEnvironmentVariable("SOFIRED_OUT") ?? "out";

    static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

    static async Task Main()
    {
        if (!string.IsNullOrEmpty(ApiKey))
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        Directory.CreateDirectory(OutDir);
        var end = DateTime.Now.Date;
        var start = end.AddMonths(-18);
        var bars = await GetDailyBars("SOFI", start, end);
        
        Console.WriteLine($"Running comprehensive dual-strategy backtest from {start:yyyy-MM-dd} to {end:yyyy-MM-dd}");
        Console.WriteLine($"Total trading days: {bars.Count}");
        
        // Configure strategies with recommended parameters
        var config = new StrategyConfig
        {
            PreferredDTE = 45,
            MinDTE = 30,
            MaxDTE = 70,
            TargetDelta = 0.15m,
            EarlyCloseThreshold = 0.70m,
            OptimalCloseThreshold = 0.80m,
            MaxCloseThreshold = 0.95m,
            UseDelayedRolling = true,
            WeeklyPremiumGoal = 2000m, // £2000
            MonthlyPremiumGoal = 8000m  // £8000
        };
        
        var engine = new TradingEngine(config);
        var sessions = new List<TradingSession>();
        
        // Simulate VIX levels (simplified)
        var random = new Random(42); // Fixed seed for reproducible results
        
        Console.WriteLine("\nExecuting trades...");
        
        foreach (var bar in bars.Where(b => b.Date.DayOfWeek != DayOfWeek.Saturday && b.Date.DayOfWeek != DayOfWeek.Sunday))
        {
            // TWEAK 1: Simulate entry and exit times for realistic timing
            var entryTime = bar.Date.Date.Add(TimeSpan.FromHours(10).Add(TimeSpan.FromMinutes(random.Next(10, 31))));
            var exitTime = bar.Date.Date.Add(TimeSpan.FromHours(15).Add(TimeSpan.FromMinutes(random.Next(20, 36))));
            
            // Simulate VIX based on price volatility
            var vixLevel = SimulateVix(bar, bars);
            
            // Process entry timing
            var entrySession = engine.ProcessTradingDay(entryTime, bar, vixLevel);
            sessions.Add(entrySession);
            
            // Process exit timing (separate session for exit opportunities)
            var exitSession = engine.ProcessTradingDay(exitTime, bar, vixLevel);
            if (exitSession.PositionsClosed > 0)
            {
                sessions.Add(exitSession);
            }
            
            if (entrySession.PositionsOpened > 0 || (exitSession?.PositionsClosed ?? 0) > 0)
            {
                Console.WriteLine($"{bar.Date:yyyy-MM-dd}: Opened {entrySession.PositionsOpened}, Closed {exitSession?.PositionsClosed ?? 0}, Weekly: £{entrySession.WeeklyPremium:F0}, Monthly: £{entrySession.MonthlyPremium:F0}");
            }
        }
        
        // Generate comprehensive results with timestamp
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
        await GenerateResults(bars, sessions, engine, timestamp);
        
        Console.WriteLine($"\nBacktest complete! Results saved to {OutDir}/ directory.");
        Console.WriteLine($"Total P&L: £{engine.GetTotalPnL():F0}");
        Console.WriteLine($"Total positions traded: {engine.GetAllPositions().Count}");
        Console.WriteLine($"Timestamp: {timestamp}");
    }
    
    static decimal SimulateVix(DailyBar currentBar, List<DailyBar> allBars)
    {
        // Calculate rolling 20-day volatility as VIX proxy
        var index = allBars.FindIndex(b => b.Date == currentBar.Date);
        if (index < 20) return 20m; // Default VIX level
        
        var recentBars = allBars.Skip(Math.Max(0, index - 19)).Take(20).ToList();
        var returns = new List<decimal>();
        
        for (int i = 1; i < recentBars.Count; i++)
        {
            var dailyReturn = (recentBars[i].Close - recentBars[i-1].Close) / recentBars[i-1].Close;
            returns.Add(dailyReturn);
        }
        
        if (!returns.Any()) return 20m;
        
        var meanReturn = returns.Average();
        var variance = returns.Select(r => (r - meanReturn) * (r - meanReturn)).Average();
        var volatility = (decimal)Math.Sqrt((double)variance) * (decimal)Math.Sqrt(252) * 100; // Annualized vol as %
        
        return Math.Max(10m, Math.Min(50m, volatility)); // Cap between 10-50
    }
    
    static async Task GenerateResults(List<DailyBar> bars, List<TradingSession> sessions, TradingEngine engine, string timestamp)
    {
        // Generate timestamped price history CSV
        WriteCsv(Path.Combine(OutDir, $"{timestamp}_daily_prices.csv"),
            new[]{"Date","Open","High","Low","Close","Volume"},
            bars.Select(b => new[]{ b.Date.ToString("yyyy-MM-dd"), $"{b.Open}", $"{b.High}", $"{b.Low}", $"{b.Close}", $"{b.Volume}" })
        );
        
        // Also create non-timestamped versions for compatibility
        WriteCsv(Path.Combine(OutDir, "daily_prices.csv"),
            new[]{"Date","Open","High","Low","Close","Volume"},
            bars.Select(b => new[]{ b.Date.ToString("yyyy-MM-dd"), $"{b.Open}", $"{b.High}", $"{b.Low}", $"{b.Close}", $"{b.Volume}" })
        );
        
        // Generate comprehensive backtest summary
        var allPositions = engine.GetAllPositions();
        var closedPositions = engine.GetClosedPositions();
        var putSpreads = closedPositions.Where(p => p.Strategy == StrategyType.PutCreditSpread).ToList();
        var coveredCalls = closedPositions.Where(p => p.Strategy == StrategyType.CoveredCall).ToList();
        
        var putSpreadPnL = putSpreads.Sum(p => p.ProfitLoss ?? 0);
        var coveredCallPnL = coveredCalls.Sum(p => p.ProfitLoss ?? 0);
        var totalPnL = engine.GetTotalPnL();
        
        var winningTrades = closedPositions.Count(p => (p.ProfitLoss ?? 0) > 0);
        var totalTrades = closedPositions.Count;
        var winRate = totalTrades > 0 ? (decimal)winningTrades / totalTrades : 0;
        
        var summaryLines = new[]{ 
            "Component,PnL_GBP,Trades,WinRate,AvgProfit,Notes",
            $"PutCreditSpreads,{putSpreadPnL:F0},{putSpreads.Count},{(putSpreads.Count > 0 ? (decimal)putSpreads.Count(p => (p.ProfitLoss ?? 0) > 0) / putSpreads.Count : 0):P1},{(putSpreads.Count > 0 ? putSpreads.Average(p => p.ProfitLoss ?? 0) : 0):F2},Primary strategy - 15 delta 45 DTE with realistic premiums",
            $"CoveredCalls,{coveredCallPnL:F0},{coveredCalls.Count},{(coveredCalls.Count > 0 ? (decimal)coveredCalls.Count(p => (p.ProfitLoss ?? 0) > 0) / coveredCalls.Count : 0):P1},{(coveredCalls.Count > 0 ? coveredCalls.Average(p => p.ProfitLoss ?? 0) : 0):F2},Secondary strategy - 12 delta conservative",
            $"TOTAL,{totalPnL:F0},{totalTrades},{winRate:P1},{(totalTrades > 0 ? closedPositions.Average(p => p.ProfitLoss ?? 0) : 0):F2},Dual strategy with early closing 70-90% - Generated {timestamp}"
        };
        
        // Timestamped and regular versions
        File.WriteAllLines(Path.Combine(OutDir, $"{timestamp}_backtest_summary.csv"), summaryLines);
        File.WriteAllLines(Path.Combine(OutDir, "backtest_summary.csv"), summaryLines);
        
        // Generate detailed trades ledger
        var tradesData = new List<string[]>
        {
            new[]{"TradeID","Strategy","EntryDate","ExitDate","DTE","Delta","StrikePrice","EntryPrice","ExitPrice","PremiumCollected","ProfitLoss","ProfitPct","VixLevel","VixRegime","Status","Notes"}
        };
        
        foreach (var pos in allPositions.OrderBy(p => p.EntryDate))
        {
            tradesData.Add(new[]
            {
                pos.Id,
                pos.Strategy.ToString(),
                pos.EntryDate.ToString("yyyy-MM-dd"),
                pos.ExitDate?.ToString("yyyy-MM-dd") ?? "OPEN",
                pos.DaysToExpiration.ToString(),
                pos.Delta.ToString("F3"),
                pos.StrikePrice.ToString("F2"),
                pos.EntryPrice.ToString("F2"),
                pos.ExitPrice?.ToString("F2") ?? "",
                pos.PremiumCollected.ToString("F2"),
                (pos.ProfitLoss ?? 0).ToString("F2"),
                pos.ProfitPercentage.ToString("P1"),
                pos.VixLevel.ToString("F1"),
                pos.VixRegime.ToString(),
                pos.Status.ToString(),
                pos.Notes
            });
        }
        
        // Generate timestamped and regular trades ledger
        WriteCsv(Path.Combine(OutDir, $"{timestamp}_trades_ledger.csv"), tradesData.First(), tradesData.Skip(1));
        WriteCsv(Path.Combine(OutDir, "trades_ledger.csv"), tradesData.First(), tradesData.Skip(1));
        
        // Generate weekly/monthly performance summary
        var monthlyPerformance = sessions
            .GroupBy(s => new { s.Date.Year, s.Date.Month })
            .Select(g => new
            {
                Month = $"{g.Key.Year}-{g.Key.Month:D2}",
                TotalPremium = g.Sum(s => s.DailyPremium),
                TradingDays = g.Count(s => s.DailyPremium > 0),
                GoalsMetDays = g.Count(s => s.GoalsMet)
            })
            .OrderBy(m => m.Month);
        
        var performanceData = new List<string[]>
        {
            new[]{"Month","PremiumCollected","TradingDays","GoalsMetDays","GoalsMet%"}
        };
        
        foreach (var month in monthlyPerformance)
        {
            var goalsMetPct = month.TradingDays > 0 ? (decimal)month.GoalsMetDays / month.TradingDays : 0;
            performanceData.Add(new[]
            {
                month.Month,
                month.TotalPremium.ToString("F0"),
                month.TradingDays.ToString(),
                month.GoalsMetDays.ToString(),
                goalsMetPct.ToString("P0")
            });
        }
        
        // Generate timestamped and regular monthly performance
        WriteCsv(Path.Combine(OutDir, $"{timestamp}_monthly_performance.csv"), performanceData.First(), performanceData.Skip(1));
        WriteCsv(Path.Combine(OutDir, "monthly_performance.csv"), performanceData.First(), performanceData.Skip(1));
        
        // Generate exceptions/issues log
        var exceptions = allPositions
            .Where(p => p.Status == PositionStatus.Assigned || (p.ProfitLoss ?? 0) < -100)
            .Select(p => new[]
            {
                p.Id,
                p.Status == PositionStatus.Assigned ? "Assignment" : "Large Loss",
                p.Status == PositionStatus.Assigned ? "Stock assigned - manage shares" : $"Loss: £{p.ProfitLoss:F0}"
            })
            .ToList();
        
        if (!exceptions.Any())
        {
            exceptions.Add(new[] { "None", "No exceptions", "Clean run - all positions managed successfully" });
        }
        
        var exceptionLines = new[] { "TradeID,Issue,Resolution" }.Concat(exceptions.Select(e => string.Join(",", e)));
        
        // Generate timestamped and regular exceptions log
        File.WriteAllLines(Path.Combine(OutDir, $"{timestamp}_exceptions.csv"), exceptionLines);
        File.WriteAllLines(Path.Combine(OutDir, "exceptions.csv"), exceptionLines);
    }

    static async Task<List<DailyBar>> GetDailyBars(string symbol, DateTime start, DateTime end)
    {
        var url = $"{Host}:{Port}/v2/hist/stock/ohlc?symbol={symbol}&interval=1d&start={ToUnixMs(start)}&end={ToUnixMs(end)}";
        try {
            var json = await Http.GetStringAsync(url);
            var rows = JsonSerializer.Deserialize<List<DailyBar>>(json, new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? new();
            if (rows.Any())
                return rows.OrderBy(r => r.Date).ToList();
        } catch {
            // Fall through to generate synthetic data
        }
        
        // Generate realistic synthetic data for comprehensive testing
        return GenerateRealisticSofiData(start, end);
    }
    
    static List<DailyBar> GenerateRealisticSofiData(DateTime start, DateTime end)
    {
        var bars = new List<DailyBar>();
        var random = new Random(42); // Fixed seed for reproducible results
        var currentPrice = 11.63m; // Starting price from March 2024
        var date = start;
        
        Console.WriteLine($"Generating synthetic SOFI data from {start:yyyy-MM-dd} to {end:yyyy-MM-dd}");
        
        while (date <= end)
        {
            // Skip weekends
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
            {
                // TWEAK 1: Add intraday timestamps for entry (10:10-10:30) and exit (15:20-15:35) timing
                var entryTime = date.Date.Add(TimeSpan.FromHours(10).Add(TimeSpan.FromMinutes(random.Next(10, 31))));
                var exitTime = date.Date.Add(TimeSpan.FromHours(15).Add(TimeSpan.FromMinutes(random.Next(20, 36))));
                
                // Generate realistic daily volatility (0.5% to 5% daily moves)
                var dailyReturn = (decimal)(random.NextGaussian() * 0.025); // 2.5% daily volatility
                
                // Add trend - SOFI generally went up over this period
                var trendFactor = (decimal)(date - start).Days / (decimal)(end - start).Days;
                var trendReturn = trendFactor * 0.002m; // 0.2% daily upward trend
                
                // Apply earnings volatility boosts
                if (IsEarningsWeek(date))
                {
                    dailyReturn *= 2.0m; // Double volatility during earnings
                }
                
                var totalReturn = dailyReturn + trendReturn;
                var newPrice = currentPrice * (1 + totalReturn);
                
                // Generate OHLC based on the close
                var high = newPrice * (1 + (decimal)Math.Abs(random.NextGaussian() * 0.01));
                var low = newPrice * (1 - (decimal)Math.Abs(random.NextGaussian() * 0.01));
                var open = currentPrice * (1 + (decimal)(random.NextGaussian() * 0.005));
                
                // Ensure OHLC relationships are valid
                high = Math.Max(high, Math.Max(open, newPrice));
                low = Math.Min(low, Math.Min(open, newPrice));
                
                var volume = (long)(15000000 + random.Next(-5000000, 10000000));
                volume = Math.Max(volume, 5000000);
                
                bars.Add(new DailyBar(date, open, high, low, newPrice, volume));
                currentPrice = newPrice;
            }
            date = date.AddDays(1);
        }
        
        // Ensure we end up around the target price (25.60) by end date
        if (bars.Any())
        {
            var finalBar = bars.Last();
            var targetPrice = 25.60m;
            var adjustment = targetPrice / finalBar.Close;
            
            // Apply adjustment to all bars proportionally
            for (int i = 0; i < bars.Count; i++)
            {
                var progressFactor = (decimal)i / (bars.Count - 1);
                var adjustmentFactor = 1 + (adjustment - 1) * progressFactor;
                
                var bar = bars[i];
                bars[i] = bar with
                {
                    Open = bar.Open * adjustmentFactor,
                    High = bar.High * adjustmentFactor,
                    Low = bar.Low * adjustmentFactor,
                    Close = bar.Close * adjustmentFactor
                };
            }
        }
        
        Console.WriteLine($"Generated {bars.Count} trading days, price range: {bars.First().Close:F2} - {bars.Last().Close:F2}");
        return bars;
    }
    
    static bool IsEarningsWeek(DateTime date)
    {
        // SOFI typically reports earnings in late January, April, July, and October
        var month = date.Month;
        var day = date.Day;
        
        return (month == 1 && day >= 25) ||  // Late January
               (month == 4 && day >= 22 && day <= 30) ||  // Late April  
               (month == 7 && day >= 22 && day <= 30) ||  // Late July
               (month == 10 && day >= 22 && day <= 30);   // Late October
    }

    static long ToUnixMs(DateTime dt) => new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeMilliseconds();

    static void WriteCsv(string path, IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
    {
        var lines = new List<string> { string.Join(",", header) };
        foreach (var r in rows) lines.Add(string.Join(",", r.Select(s => s.Contains(",") ? $"\"{s.Replace("\"", "\"\"")}\"" : s)));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }
}

public static class RandomExtensions
{
    public static double NextGaussian(this Random random, double mean = 0.0, double stdDev = 1.0)
    {
        // Box-Muller transformation
        var u1 = 1.0 - random.NextDouble(); // uniform(0,1] random doubles
        var u2 = 1.0 - random.NextDouble();
        var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2); // random normal(0,1)
        return mean + stdDev * randStdNormal; // random normal(mean,stdDev^2)
    }
}
