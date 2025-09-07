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
        var end = new DateTime(2025, 8, 31);
        var start = new DateTime(2024, 1, 1);
        var bars = await GetDailyBars("SOFI", start, end);
        
        Console.WriteLine($"Running comprehensive dual-strategy backtest from {start:yyyy-MM-dd} to {end:yyyy-MM-dd}");
        Console.WriteLine($"Total trading days: {bars.Count}");
        
        // High-ROI Configuration - Aggressive capital deployment strategy
        var config = new StrategyConfig
        {
            PreferredDTE = 45,              // Optimal 45-60 DTE range
            MinDTE = 30,
            MaxDTE = 60,                    // Shorter max for faster turnover
            TargetDelta = 0.15m,            // 15 delta target for optimal risk/reward
            EarlyCloseThreshold = 0.70m,    // Early closing strategy (70-90%)
            OptimalCloseThreshold = 0.80m,
            MaxCloseThreshold = 0.90m,      // Close at 90% for reinvestment
            UseDelayedRolling = true,
            WeeklyPremiumGoal = 2000m,      
            MonthlyPremiumGoal = 8000m,
            
            // High-ROI Parameters
            InitialCapital = 10000m,        // £10k starting capital
            MaxPortfolioRisk = 0.05m,       // 5% risk per trade
            EnableCompounding = true,       // CRITICAL: Reinvest profits
            AggressivenessMultiplier = 5.0m,// 5x more aggressive than conservative
            MinContractSize = 5,            // Minimum 5 contracts (vs 1)
            MaxContractSize = 50,           // Maximum 50 contracts
            CapitalAllocationPerTrade = 0.15m // 15% capital per trade (aggressive)
        };
        
        var engine = new TradingEngine(config);
        var sessions = new List<TradingSession>();
        
        // Simulate VIX levels (simplified)
        var random = new Random(42); // Fixed seed for reproducible results
        
        // REGRESSION TESTING - Validate against known performance benchmarks
        var regressionTester = new RegressionTesting();
        var testSuite = regressionTester.CreateTestSuite(engine);
        
        Console.WriteLine("\nExecuting trades...");
        
        foreach (var bar in bars.Where(b => b.Date.DayOfWeek != DayOfWeek.Saturday && b.Date.DayOfWeek != DayOfWeek.Sunday))
        {
            // Simulate realistic entry and exit timing windows
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
        
        // PERFORMANCE REGRESSION VALIDATION - Ensure no degradation from known benchmarks
        try 
        {
            PerformanceRegression.ValidatePerformanceMetrics(
                engine,
                expectedROI: 4.89m,           // 489% ROI from 20-month backtest
                expectedTotalPnL: 48892m,     // £48,892 total P&L 
                expectedTotalTrades: 870,     // Expected total positions
                tolerance: 0.02m              // 2% tolerance for minor variations
            );
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"⚠️  REGRESSION ALERT: {ex.Message}");
            Console.WriteLine("Please review recent changes for potential performance impacts.");
        }
        
        // RUN COMPREHENSIVE REGRESSION TEST SUITE
        testSuite.RunFullSuite(bars.Take(10).ToList(), captureBaseline: false);
        
        // Generate comprehensive results with timestamp
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
        await GenerateResults(bars, sessions, engine, timestamp);
        
        Console.WriteLine($"\nBacktest complete! Results saved to {OutDir}/ directory.");
        Console.WriteLine($"Total P&L: £{engine.GetTotalPnL():F0}");
        Console.WriteLine($"Final Capital: £{engine.GetCurrentCapital():F0}");  
        Console.WriteLine($"Total ROI: {(engine.GetTotalPnL() / config.InitialCapital):P1}");
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
    
    static Task GenerateResults(List<DailyBar> bars, List<TradingSession> sessions, TradingEngine engine, string timestamp)
    {
        // Create comprehensive Excel workbook with multiple sheets
        var excelPath = Path.Combine(OutDir, "20250907", $"{DateTime.Now:HHmm}_sofired_backtest.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(excelPath));
        
        CreateExcelWorkbook(excelPath, bars, sessions, engine, timestamp);
        
        Console.WriteLine($"\nExcel workbook generated: {excelPath}");
        return Task.CompletedTask;
    }
    
    static void CreateExcelWorkbook(string filePath, List<DailyBar> bars, List<TradingSession> sessions, TradingEngine engine, string timestamp)
    {
        using var document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        
        var sheets = new Sheets();
        workbookPart.Workbook.AppendChild(sheets);
        
        // Get data
        var allPositions = engine.GetAllPositions();
        var closedPositions = engine.GetClosedPositions();
        var putSpreads = closedPositions.Where(p => p.Strategy == StrategyType.PutCreditSpread).ToList();
        var coveredCalls = closedPositions.Where(p => p.Strategy == StrategyType.CoveredCall).ToList();
        
        // Sheet 1: Executive Summary
        CreateSummarySheet(workbookPart, sheets, "Executive_Summary", engine, putSpreads, coveredCalls, timestamp, 1);
        
        // Sheet 2: All Trades Ledger (with strategy cross-references)
        CreateTradesLedgerSheet(workbookPart, sheets, "All_Trades", allPositions, 2);
        
        // Sheet 3: Put Credit Spreads Strategy Details
        CreateStrategySheet(workbookPart, sheets, "PutCreditSpreads", putSpreads, "Primary Strategy - 15Δ Put Credit Spreads", 3);
        
        // Sheet 4: Covered Calls Strategy Details  
        CreateStrategySheet(workbookPart, sheets, "CoveredCalls", coveredCalls, "Secondary Strategy - 12Δ Covered Calls", 4);
        
        // Sheet 5: Daily Price Data
        CreatePriceDataSheet(workbookPart, sheets, "Daily_Prices", bars, 5);
        
        // Sheet 6: Monthly Performance
        CreateMonthlyPerformanceSheet(workbookPart, sheets, "Monthly_Performance", sessions, 6);
        
        // Sheet 7: Risk Analysis & Exceptions
        CreateRiskAnalysisSheet(workbookPart, sheets, "Risk_Analysis", allPositions, 7);
        
        workbookPart.Workbook.Save();
    }
    
    static void CreateSummarySheet(WorkbookPart workbookPart, Sheets sheets, string sheetName, TradingEngine engine, List<Position> putSpreads, List<Position> coveredCalls, string timestamp, uint sheetId)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(sheetData);
        
        var sheet = new Sheet() { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId, Name = sheetName };
        sheets.Append(sheet);
        
        // Headers and Summary Data
        var putSpreadPnL = putSpreads.Sum(p => p.ProfitLoss ?? 0);
        var coveredCallPnL = coveredCalls.Sum(p => p.ProfitLoss ?? 0);
        var totalPnL = engine.GetTotalPnL();
        var totalTrades = putSpreads.Count + coveredCalls.Count;
        
        // Title row
        AddRow(sheetData, "SOFI Options Strategy - Executive Summary", $"Generated: {timestamp}");
        AddEmptyRow(sheetData);
        
        // Performance summary
        AddRow(sheetData, "Strategy Component", "P&L (GBP)", "Trades", "Avg P&L", "Sheet Reference", "Notes");
        AddRow(sheetData, "Put Credit Spreads", putSpreadPnL.ToString("F0"), putSpreads.Count.ToString(), 
               putSpreads.Any() ? putSpreads.Average(p => p.ProfitLoss ?? 0).ToString("F2") : "0", 
               "=HYPERLINK(\"#PutCreditSpreads!A1\",\"→ Details\")", "Primary strategy - 15Δ 45DTE");
        AddRow(sheetData, "Covered Calls", coveredCallPnL.ToString("F0"), coveredCalls.Count.ToString(),
               coveredCalls.Any() ? coveredCalls.Average(p => p.ProfitLoss ?? 0).ToString("F2") : "0",
               "=HYPERLINK(\"#CoveredCalls!A1\",\"→ Details\")", "Secondary strategy - 12Δ");
        AddRow(sheetData, "TOTAL", totalPnL.ToString("F0"), totalTrades.ToString(), 
               totalTrades > 0 ? ((putSpreadPnL + coveredCallPnL) / totalTrades).ToString("F2") : "0",
               "=HYPERLINK(\"#All_Trades!A1\",\"→ All Trades\")", "Combined dual-strategy");
        
        AddEmptyRow(sheetData);
        AddRow(sheetData, "Quick Navigation");
        AddRow(sheetData, "All Trades Ledger", "=HYPERLINK(\"#All_Trades!A1\",\"→ View All Trades\")");
        AddRow(sheetData, "Monthly Performance", "=HYPERLINK(\"#Monthly_Performance!A1\",\"→ Monthly P&L\")");
        AddRow(sheetData, "Risk Analysis", "=HYPERLINK(\"#Risk_Analysis!A1\",\"→ Risk Metrics\")");
        AddRow(sheetData, "Price Data", "=HYPERLINK(\"#Daily_Prices!A1\",\"→ SOFI Prices\")");
    }
    
    static void CreateTradesLedgerSheet(WorkbookPart workbookPart, Sheets sheets, string sheetName, List<Position> allPositions, uint sheetId)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(sheetData);
        
        var sheet = new Sheet() { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId, Name = sheetName };
        sheets.Append(sheet);
        
        // Headers
        AddRow(sheetData, "All Trades Ledger - Cross-Referenced by Strategy");
        AddEmptyRow(sheetData);
        AddRow(sheetData, "Trade ID", "Strategy", "Entry Date", "Exit Date", "DTE", "Delta", "Strike", 
               "Entry Price", "Exit Price", "Premium", "P&L", "ROI %", "Contracts", "Capital", 
               "VIX", "Market Regime", "Vol Event", "Status", "Entry Reasoning", "Exit Reasoning", 
               "Strategy Sheet", "Notes");
        
        // Data rows with cross-references
        foreach (var pos in allPositions.OrderBy(p => p.EntryDate))
        {
            var strategySheetRef = pos.Strategy == StrategyType.PutCreditSpread 
                ? "=HYPERLINK(\"#PutCreditSpreads!A1\",\"→ PCS Details\")"
                : "=HYPERLINK(\"#CoveredCalls!A1\",\"→ CC Details\")";
                
            AddRow(sheetData,
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
                pos.ROIPercentage.ToString("P1"),
                pos.ContractSize.ToString(),
                pos.CapitalAllocated.ToString("F0"),
                pos.VixLevel.ToString("F1"),
                pos.MarketRegime.ToString(),
                pos.VolEvent.ToString(),
                pos.Status.ToString(),
                pos.EntryReasoning,
                pos.ExitReasoning,
                strategySheetRef,
                pos.Notes
            );
        }
    }
    
    static void CreateStrategySheet(WorkbookPart workbookPart, Sheets sheets, string sheetName, List<Position> positions, string strategyTitle, uint sheetId)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(sheetData);
        
        var sheet = new Sheet() { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId, Name = sheetName };
        sheets.Append(sheet);
        
        // Strategy-specific header
        AddRow(sheetData, strategyTitle);
        AddRow(sheetData, "=HYPERLINK(\"#All_Trades!A1\",\"← Back to All Trades\")", "=HYPERLINK(\"#Executive_Summary!A1\",\"← Back to Summary\")");
        AddEmptyRow(sheetData);
        
        // Strategy performance metrics
        var totalPnL = positions.Sum(p => p.ProfitLoss ?? 0);
        var avgPnL = positions.Any() ? positions.Average(p => p.ProfitLoss ?? 0) : 0;
        var winningTrades = positions.Count(p => (p.ProfitLoss ?? 0) > 0);
        var winRate = positions.Count > 0 ? (decimal)winningTrades / positions.Count : 0;
        
        AddRow(sheetData, "Strategy Metrics");
        AddRow(sheetData, "Total P&L:", totalPnL.ToString("F2"));
        AddRow(sheetData, "Total Trades:", positions.Count.ToString());
        AddRow(sheetData, "Win Rate:", winRate.ToString("P1"));
        AddRow(sheetData, "Average P&L:", avgPnL.ToString("F2"));
        AddEmptyRow(sheetData);
        
        // Detailed trades for this strategy
        AddRow(sheetData, "Trade Details");
        AddRow(sheetData, "Trade ID", "Entry Date", "Exit Date", "DTE", "Delta", "Strike", 
               "Premium", "P&L", "VIX Level", "Regime", "Status", "Notes");
        
        foreach (var pos in positions.OrderBy(p => p.EntryDate))
        {
            AddRow(sheetData,
                pos.Id,
                pos.EntryDate.ToString("yyyy-MM-dd"),
                pos.ExitDate?.ToString("yyyy-MM-dd") ?? "OPEN",
                pos.DaysToExpiration.ToString(),
                pos.Delta.ToString("F3"),
                pos.StrikePrice.ToString("F2"),
                pos.PremiumCollected.ToString("F2"),
                (pos.ProfitLoss ?? 0).ToString("F2"),
                pos.VixLevel.ToString("F1"),
                pos.VixRegime.ToString(),
                pos.Status.ToString(),
                pos.Notes
            );
        }
    }
    
    static void CreatePriceDataSheet(WorkbookPart workbookPart, Sheets sheets, string sheetName, List<DailyBar> bars, uint sheetId)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(sheetData);
        
        var sheet = new Sheet() { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId, Name = sheetName };
        sheets.Append(sheet);
        
        AddRow(sheetData, "SOFI Daily Price Data");
        AddRow(sheetData, "=HYPERLINK(\"#Executive_Summary!A1\",\"← Back to Summary\")");
        AddEmptyRow(sheetData);
        
        AddRow(sheetData, "Date", "Open", "High", "Low", "Close", "Volume");
        
        foreach (var bar in bars)
        {
            AddRow(sheetData, bar.Date.ToString("yyyy-MM-dd"), bar.Open.ToString("F2"), 
                   bar.High.ToString("F2"), bar.Low.ToString("F2"), bar.Close.ToString("F2"), 
                   bar.Volume.ToString());
        }
    }
    
    static void CreateMonthlyPerformanceSheet(WorkbookPart workbookPart, Sheets sheets, string sheetName, List<TradingSession> sessions, uint sheetId)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(sheetData);
        
        var sheet = new Sheet() { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId, Name = sheetName };
        sheets.Append(sheet);
        
        AddRow(sheetData, "Monthly Performance Analysis");
        AddRow(sheetData, "=HYPERLINK(\"#Executive_Summary!A1\",\"← Back to Summary\")");
        AddEmptyRow(sheetData);
        
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
            
        AddRow(sheetData, "Month", "Premium Collected", "Trading Days", "Goals Met Days", "Goals Met %");
        
        foreach (var month in monthlyPerformance)
        {
            var goalsMetPct = month.TradingDays > 0 ? (decimal)month.GoalsMetDays / month.TradingDays : 0;
            AddRow(sheetData, month.Month, month.TotalPremium.ToString("F0"), month.TradingDays.ToString(),
                   month.GoalsMetDays.ToString(), goalsMetPct.ToString("P0"));
        }
    }
    
    static void CreateRiskAnalysisSheet(WorkbookPart workbookPart, Sheets sheets, string sheetName, List<Position> allPositions, uint sheetId)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(sheetData);
        
        var sheet = new Sheet() { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId, Name = sheetName };
        sheets.Append(sheet);
        
        AddRow(sheetData, "Risk Analysis & Exceptions");
        AddRow(sheetData, "=HYPERLINK(\"#Executive_Summary!A1\",\"← Back to Summary\")");
        AddEmptyRow(sheetData);
        
        // Risk metrics
        var assignments = allPositions.Where(p => p.Status == PositionStatus.Assigned).ToList();
        var largeLosses = allPositions.Where(p => (p.ProfitLoss ?? 0) < -100).ToList();
        
        AddRow(sheetData, "Risk Summary");
        AddRow(sheetData, "Total Assignments:", assignments.Count.ToString());
        AddRow(sheetData, "Large Losses (>£100):", largeLosses.Count.ToString());
        AddRow(sheetData, "Max Single Loss:", allPositions.Min(p => p.ProfitLoss ?? 0).ToString("F2"));
        AddRow(sheetData, "Risk Status:", assignments.Any() || largeLosses.Any() ? "REVIEW REQUIRED" : "CLEAN");
        AddEmptyRow(sheetData);
        
        if (assignments.Any() || largeLosses.Any())
        {
            AddRow(sheetData, "Exception Details");
            AddRow(sheetData, "Trade ID", "Issue Type", "Details", "Resolution");
            
            foreach (var pos in assignments.Concat(largeLosses).Distinct())
            {
                var issueType = pos.Status == PositionStatus.Assigned ? "Assignment" : "Large Loss";
                var details = pos.Status == PositionStatus.Assigned ? "Stock assigned - manage shares" : $"Loss: £{pos.ProfitLoss:F0}";
                AddRow(sheetData, pos.Id, issueType, details, "Review required");
            }
        }
        else
        {
            AddRow(sheetData, "No exceptions found - Clean trading run with systematic risk management");
        }
    }
    
    static void AddRow(SheetData sheetData, params string[] values)
    {
        var row = new Row();
        foreach (var value in values)
        {
            var cell = new Cell() { DataType = CellValues.InlineString, InlineString = new InlineString() { Text = new Text(value) } };
            row.AppendChild(cell);
        }
        sheetData.AppendChild(row);
    }
    
    static void AddEmptyRow(SheetData sheetData)
    {
        sheetData.AppendChild(new Row());
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
