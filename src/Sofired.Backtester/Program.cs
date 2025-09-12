using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Sofired.Core;
using Sofired.Backtester;

class Program
{
    static readonly string OutDir = Environment.GetEnvironmentVariable("SOFIRED_OUT") ?? "out";

    static async Task Main(string[] args)
    {
        Directory.CreateDirectory(OutDir);
        
        // Parse command line arguments
        var (symbol, resume, autoResume, specificCheckpoint) = ParseArguments(args);
        
        // Handle special operations first
        if (args.Length > 0)
        {
            switch (args[0].ToLower())
            {
                case "config":
                    Sofired.Backtester.ConfigDemo.RunDemo();
                    return;
                case "compare":
                    if (args.Length >= 3)
                    {
                        var compareManager = new ConfigurationManager();
                        compareManager.CompareSymbolConfigs(args[1], args[2]);
                    }
                    else
                    {
                        Console.WriteLine("Usage: dotnet run compare SYMBOL1 SYMBOL2");
                    }
                    return;
                case "checkpoints":
                    ListCheckpoints(symbol);
                    return;
            }
        }
        
        // Initialize checkpoint manager
        var checkpointManager = new CheckpointManager();
        
        // Handle resume options
        BacktestCheckpoint? checkpoint = null;
        if (resume || autoResume)
        {
            if (!string.IsNullOrEmpty(specificCheckpoint))
            {
                checkpoint = checkpointManager.LoadCheckpoint(specificCheckpoint);
                if (checkpoint is null)
                {
                    Console.WriteLine($"❌ Checkpoint '{specificCheckpoint}' not found");
                    return;
                }
            }
            else
            {
                checkpoint = checkpointManager.LoadMostRecentCheckpoint(symbol);
            }
            
            if (checkpoint is null && !autoResume)
            {
                Console.WriteLine($"❌ No checkpoints found for {symbol} to resume");
                return;
            }
            
            if (checkpoint is not null)
            {
                symbol = checkpoint.Symbol; // Use symbol from checkpoint
                Console.WriteLine($"🔄 RESUMING BACKTEST: {checkpoint.BacktestId}");
                Console.WriteLine($"   Last processed: {checkpoint.LastProcessedDate:yyyy-MM-dd}");
                Console.WriteLine($"   Progress: {checkpoint.EstimatedCompletionPct:F1}%");
                Console.WriteLine($"   Current P&L: £{checkpoint.RunningPnL:N0}");
            }
        }
        else if (autoResume && checkpointManager.HasIncompleteBacktest(symbol))
        {
            Console.WriteLine($"🔍 Found incomplete backtest for {symbol}");
            checkpoint = checkpointManager.LoadMostRecentCheckpoint(symbol);
        }
        
        Console.WriteLine($"\n🔧 LOADING SYMBOL-SPECIFIC CONFIGURATION FOR {symbol}");
        var configManager = new ConfigurationManager();
        SymbolConfig symbolConfig;
        
        try
        {
            symbolConfig = configManager.LoadSymbolConfig(symbol);
        }
        catch (FileNotFoundException)
        {
            Console.WriteLine($"⚠️  Configuration file for {symbol} not found, using SOFI defaults for {symbol}");
            symbolConfig = configManager.LoadSymbolConfig("SOFI");
            // Keep original symbol, just use SOFI's config as template
            symbolConfig.Symbol = symbol; // Update the config to reflect the actual symbol
        }
        
        // Use dates from symbol configuration
        var startDate = DateTime.Parse(symbolConfig.Backtest.StartDate);
        var endDate = DateTime.Parse(symbolConfig.Backtest.EndDate);
        var bars = await GetDailyBars(symbol, startDate, endDate);
        
        // If no real data available, fail the backtest
        if (bars.Count == 0)
        {
            Console.WriteLine($"❌ CRITICAL: No real market data available for {symbol}. Backtesting requires real market data.");
            Console.WriteLine("❌ BACKTEST FAILED: Cannot proceed without real price data from ThetaData API.");
            Console.WriteLine("💡 Solution: Start ThetaData terminal or ensure MCP service has data access.");
            return;
        }
        
        Console.WriteLine($"Running comprehensive dual-strategy backtest from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
        Console.WriteLine($"Total trading days: {bars.Count}");
        Console.WriteLine($"Using symbol-specific configuration for {symbol} ({symbolConfig.Company.Sector} sector)");
        
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
        
        // PHASE 1: Initialize Real Options Pricing Engine with MarketDataService
        Console.WriteLine("🔥 PHASE 1: Initializing Real Options Pricing Engine");
        IMarketDataService marketDataService = new StrollThetaMarketService();
        
        // Check ThetaData connection
        // Skip connection check for now - using bridge service
        Console.WriteLine("⚠️  Skipping connection check - using MCP bridge service");
        var realOptionsEngine = new RealOptionsEngine(marketDataService);
        
        // Create trading engine with real options pricing
        var engine = new TradingEngine(config, null, realOptionsEngine, symbol);
        
        // Initialize or resume checkpoint
        BacktestCheckpoint backtestCheckpoint;
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
        var excelFileName = $"{timestamp}_{symbol}_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.xlsx";
        var excelFilePath = Path.Combine(OutDir, DateTime.Now.ToString("yyyyMMdd"), excelFileName);
        
        // Initialize streaming Excel writer
        StreamingExcelWriter? excelWriter = null;
        
        if (checkpoint is not null)
        {
            // Resume from checkpoint
            backtestCheckpoint = checkpoint;
            excelFilePath = backtestCheckpoint.ExcelFilePath;
            Console.WriteLine($"📊 Resuming Excel: {excelFilePath}");
            // For resume, we'd need to open existing Excel - for now create new
            excelWriter = new StreamingExcelWriter(excelFilePath);
            excelWriter.Initialize(symbol, startDate, endDate, config.InitialCapital);
        }
        else
        {
            // Create new checkpoint
            backtestCheckpoint = checkpointManager.CreateInitialCheckpoint(symbol, startDate, endDate, config, excelFilePath);
            checkpointManager.SaveCheckpoint(backtestCheckpoint);
            
            // Create Excel file with streaming writer
            excelWriter = new StreamingExcelWriter(excelFilePath);
            excelWriter.Initialize(symbol, startDate, endDate, config.InitialCapital);
            Console.WriteLine($"📊 Created Excel: {excelFilePath}");
        }
        
        // Attempt to get real VIX data using MarketDataService
        Console.WriteLine("Fetching real VIX data for accurate volatility analysis...");
        var realVixData = await GetRealVixData(marketDataService, startDate, endDate);
        
        if (realVixData.Count > 0)
        {
            Console.WriteLine($"✅ Loaded {realVixData.Count} real VIX data points");
        }
        else
        {
            Console.WriteLine("❌ CRITICAL: No real VIX data available. Backtesting requires real market data.");
            Console.WriteLine("❌ BACKTEST FAILED: Cannot proceed without real VIX data.");
            return;
        }
        
        // REGRESSION TESTING - Validate against known performance benchmarks
        var regressionTester = new RegressionTesting();
        var testSuite = regressionTester.CreateTestSuite(engine);
        
        Console.WriteLine("\nExecuting trades...");
        
        // Streaming approach - small buffer instead of accumulating everything
        var weeklySessionBuffer = new List<TradingSession>();
        var totalBars = bars.Count;
        var processedBars = 0;
        var daysSinceCheckpoint = 0;
        
        // Filter bars to start from checkpoint date if resuming
        var startFromDate = backtestCheckpoint.LastProcessedDate.AddDays(1);
        var barsToProcess = bars.Where(b => 
            b.Date >= startFromDate && 
            b.Date.DayOfWeek != DayOfWeek.Saturday && 
            b.Date.DayOfWeek != DayOfWeek.Sunday).ToList();
        
        if (checkpoint is not null)
        {
            Console.WriteLine($"🔄 Resuming from {startFromDate:yyyy-MM-dd} ({barsToProcess.Count} remaining days)");
        }
        
        foreach (var bar in barsToProcess)
        {
            // Fixed entry and exit timing windows (no randomization for reproducible results)
            var entryTime = bar.Date.Date.Add(TimeSpan.FromHours(10).Add(TimeSpan.FromMinutes(20)));
            var exitTime = bar.Date.Date.Add(TimeSpan.FromHours(15).Add(TimeSpan.FromMinutes(30)));
            
            // Get VIX level - only real data allowed, fail if unavailable
            if (!realVixData.ContainsKey(bar.Date.Date))
            {
                Console.WriteLine($"❌ CRITICAL: No real VIX data available for {bar.Date.Date:yyyy-MM-dd}. Synthetic data is prohibited.");
                throw new InvalidOperationException($"Unable to fetch real VIX data for {bar.Date.Date:yyyy-MM-dd}. Synthetic data is prohibited. Backtest terminated.");
            }
            var vixLevel = realVixData[bar.Date.Date];
            
            // Process entry timing
            var entrySession = engine.ProcessTradingDay(entryTime, bar, vixLevel);
            weeklySessionBuffer.Add(entrySession);
            
            // Process exit timing (separate session for exit opportunities)
            var exitSession = engine.ProcessTradingDay(exitTime, bar, vixLevel);
            if (exitSession.PositionsClosed > 0)
            {
                weeklySessionBuffer.Add(exitSession);
            }
            
            if (entrySession.PositionsOpened > 0 || (exitSession?.PositionsClosed ?? 0) > 0)
            {
                Console.WriteLine($"{bar.Date:yyyy-MM-dd}: Opened {entrySession.PositionsOpened}, Closed {exitSession?.PositionsClosed ?? 0}, Weekly: £{entrySession.WeeklyPremium:F0}, Monthly: £{entrySession.MonthlyPremium:F0}");
            }
            
            processedBars++;
            daysSinceCheckpoint++;
            
            // Checkpoint every 50 days or weekly
            var isWeekend = bar.Date.DayOfWeek == DayOfWeek.Friday;
            var shouldCheckpoint = daysSinceCheckpoint >= 50 || isWeekend;
            
            if (shouldCheckpoint || processedBars == barsToProcess.Count)
            {
                // Update checkpoint with latest session data
                var latestSession = weeklySessionBuffer.LastOrDefault() ?? entrySession;
                
                // Get current capital from engine (you'll need to expose this)
                var currentCapital = config.InitialCapital + latestSession.TotalPnL;
                checkpointManager.UpdateCheckpoint(backtestCheckpoint, latestSession, bar.Date, totalBars, 
                    backtestCheckpoint.TotalBarsProcessed + processedBars, currentCapital);
                
                // Write weekly buffer to Excel (streaming)
                if (excelWriter != null && weeklySessionBuffer.Count > 0)
                {
                    excelWriter.AppendSessions(weeklySessionBuffer);
                    excelWriter.UpdateSummary(backtestCheckpoint);
                }
                
                // Save checkpoint
                checkpointManager.SaveCheckpoint(backtestCheckpoint);
                
                // Clear buffer to free memory
                weeklySessionBuffer.Clear();
                daysSinceCheckpoint = 0;
                
                // Progress update
                var overallProgress = (decimal)(backtestCheckpoint.TotalBarsProcessed + processedBars) / totalBars * 100m;
                Console.WriteLine($"📍 Progress: {overallProgress:F1}% complete, P&L: £{backtestCheckpoint.RunningPnL:N0}");
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
        
        // RUN COMPREHENSIVE REGRESSION TEST SUITE - Skip if no data
        if (bars.Count > 10)
        {
            testSuite.RunFullSuite(bars.Take(10).ToList(), captureBaseline: false);
        }
        else
        {
            Console.WriteLine("⚠️  Skipping regression test suite - insufficient data");
        }
        
        // COMPREHENSIVE PERFORMANCE ANALYSIS
        Console.WriteLine("\n" + "=".PadRight(60, '='));
        Console.WriteLine("GENERATING COMPREHENSIVE PERFORMANCE ANALYTICS");
        Console.WriteLine("=".PadRight(60, '='));
        
        // TODO: Re-enable after fixing PerformanceAnalytics compilation
        // var performanceMetrics = Sofired.Core.PerformanceAnalytics.AnalyzePerformance(sessions, config.InitialCapital);
        // Sofired.Core.PerformanceAnalytics.PrintPerformanceReport(performanceMetrics);
        
        // Finalize checkpoint and Excel
        checkpointManager.FinalizeBacktest(backtestCheckpoint);
        
        // Basic performance summary from checkpoint
        var totalPnL = engine.GetTotalPnL();
        var totalPositions = engine.GetAllPositions().Count;
        var finalCapital = engine.GetCurrentCapital();
        var roi = totalPnL / config.InitialCapital;
        
        // Update final summary in Excel
        backtestCheckpoint.RunningPnL = totalPnL;
        backtestCheckpoint.CurrentCapital = finalCapital;
        backtestCheckpoint.TotalTrades = totalPositions;
        excelWriter?.UpdateSummary(backtestCheckpoint);
        excelWriter?.Dispose();
        
        Console.WriteLine($"📊 PERFORMANCE SUMMARY");
        Console.WriteLine($"   Starting Capital: £{config.InitialCapital:F0}");
        Console.WriteLine($"   Final Capital: £{finalCapital:F0}");  
        Console.WriteLine($"   Total P&L: £{totalPnL:F0}");
        Console.WriteLine($"   Total ROI: {roi:P1}");
        Console.WriteLine($"   Total Positions: {totalPositions}");
        Console.WriteLine($"   Max Drawdown: {backtestCheckpoint.MaxDrawdown:P2}");
        Console.WriteLine($"   Total Trades: {backtestCheckpoint.TotalTrades}");
        
        Console.WriteLine($"\n✅ Backtest complete! Checkpoint: {backtestCheckpoint.BacktestId}");
        Console.WriteLine($"📊 Excel file: {excelFilePath}");
        Console.WriteLine($"💰 Final P&L: £{totalPnL:F0}");
        Console.WriteLine($"📈 ROI: {roi:P1}");
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
    
    static Task GenerateResults(List<DailyBar> bars, List<TradingSession> sessions, TradingEngine engine, string timestamp, DateTime start, DateTime end, string symbol)
    {
        // Create comprehensive Excel workbook with multiple sheets
        var fileTime = DateTime.Now.ToString("HHmm");
        var timeSpan = $"{start:yyyyMMdd}_{end:yyyyMMdd}";
        var excelPath = Path.Combine(OutDir, "20250908", $"{fileTime}_{symbol}_{timeSpan}.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(excelPath));
        
        CreateExcelWorkbook(excelPath, bars, sessions, engine, timestamp, symbol);
        
        Console.WriteLine($"\nExcel workbook generated: {excelPath}");
        return Task.CompletedTask;
    }
    
    static void CreateExcelWorkbook(string filePath, List<DailyBar> bars, List<TradingSession> sessions, TradingEngine engine, string timestamp, string symbol)
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
        CreateSummarySheet(workbookPart, sheets, "Executive_Summary", engine, putSpreads, coveredCalls, timestamp, symbol, 1);
        
        // Sheet 2: All Trades Ledger (with strategy cross-references)
        CreateTradesLedgerSheet(workbookPart, sheets, "All_Trades", allPositions, 2);
        
        // Sheet 3: Put Credit Spreads Strategy Details
        CreateStrategySheet(workbookPart, sheets, "PutCreditSpreads", putSpreads, "Primary Strategy - 15Δ Put Credit Spreads", 3);
        
        // Sheet 4: Covered Calls Strategy Details  
        CreateStrategySheet(workbookPart, sheets, "CoveredCalls", coveredCalls, "Secondary Strategy - 12Δ Covered Calls", 4);
        
        // Sheet 5: Daily Price Data
        CreatePriceDataSheet(workbookPart, sheets, "Daily_Prices", bars, symbol, 5);
        
        // Sheet 6: Monthly Performance
        CreateMonthlyPerformanceSheet(workbookPart, sheets, "Monthly_Performance", sessions, 6);
        
        // Sheet 7: Risk Analysis & Exceptions
        CreateRiskAnalysisSheet(workbookPart, sheets, "Risk_Analysis", allPositions, 7);
        
        workbookPart.Workbook.Save();
    }
    
    static void CreateSummarySheet(WorkbookPart workbookPart, Sheets sheets, string sheetName, TradingEngine engine, List<Position> putSpreads, List<Position> coveredCalls, string timestamp, string symbol, uint sheetId)
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
        AddRow(sheetData, $"{symbol} Options Strategy - Executive Summary", $"Generated: {timestamp}");
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
        AddRow(sheetData, "Price Data", $"=HYPERLINK(\"#Daily_Prices!A1\",\"→ {symbol} Prices\")");
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
    
    static void CreatePriceDataSheet(WorkbookPart workbookPart, Sheets sheets, string sheetName, List<DailyBar> bars, string symbol, uint sheetId)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(sheetData);
        
        var sheet = new Sheet() { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId, Name = sheetName };
        sheets.Append(sheet);
        
        AddRow(sheetData, $"{symbol} Daily Price Data");
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
        IMarketDataService marketDataService = new StrollThetaMarketService();
        return await marketDataService.GetDailyBarsAsync(symbol, start, end);
    }

    // REMOVED: Synthetic data generation is prohibited
    // System requires real market data to operate

    static async Task<Dictionary<DateTime, decimal>> GetRealVixData(IMarketDataService marketDataService, DateTime startDate, DateTime endDate)
    {
        var vixData = new Dictionary<DateTime, decimal>();
        
        try
        {
            var vixBars = await marketDataService.GetDailyBarsAsync("VIX", startDate, endDate);
            
            foreach (var bar in vixBars)
            {
                vixData[bar.Date] = bar.Close;
            }
            
            if (vixData.Count > 0)
            {
                Console.WriteLine($"✅ Loaded {vixData.Count} real VIX data points");
            }
            else
            {
                Console.WriteLine("⚠️  No VIX data available, using default value of 20");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Failed to load VIX data: {ex.Message}, using default value of 20");
        }
        
        return vixData;
    }

    static long ToUnixMs(DateTime dt) => new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeMilliseconds();
    
    /// <summary>
    /// Parse command line arguments for resume functionality
    /// </summary>
    static (string symbol, bool resume, bool autoResume, string? specificCheckpoint) ParseArguments(string[] args)
    {
        var symbol = "SOFI";
        var resume = false;
        var autoResume = false;
        string? specificCheckpoint = null;
        
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--resume":
                case "-r":
                    resume = true;
                    break;
                case "--auto-resume":
                case "-ar":
                    autoResume = true;
                    break;
                case "--checkpoint":
                case "-c":
                    if (i + 1 < args.Length)
                    {
                        specificCheckpoint = args[++i];
                        resume = true;
                    }
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    // If it doesn't start with --, treat as symbol
                    if (!args[i].StartsWith("-") && !resume && !autoResume)
                    {
                        symbol = args[i].ToUpper();
                    }
                    break;
            }
        }
        
        return (symbol, resume, autoResume, specificCheckpoint);
    }
    
    /// <summary>
    /// Print usage information
    /// </summary>
    static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("SOFIRED Backtester - Usage:");
        Console.WriteLine();
        Console.WriteLine("New Backtest:");
        Console.WriteLine("  dotnet run [SYMBOL]                    - Run new backtest (default: SOFI)");
        Console.WriteLine();
        Console.WriteLine("Resume Options:");
        Console.WriteLine("  dotnet run [SYMBOL] --resume          - Resume most recent incomplete backtest");
        Console.WriteLine("  dotnet run [SYMBOL] --auto-resume     - Auto-detect and resume if incomplete");
        Console.WriteLine("  dotnet run --checkpoint BACKTEST_ID   - Resume specific checkpoint");
        Console.WriteLine();
        Console.WriteLine("Other Commands:");
        Console.WriteLine("  dotnet run checkpoints [SYMBOL]       - List available checkpoints");
        Console.WriteLine("  dotnet run config                     - Show configuration demo");
        Console.WriteLine("  dotnet run compare SYMBOL1 SYMBOL2    - Compare symbol configurations");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run SOFI");
        Console.WriteLine("  dotnet run AAPL --resume");
        Console.WriteLine("  dotnet run --checkpoint SOFI_20250909_1421");
        Console.WriteLine("  dotnet run checkpoints SOFI");
        Console.WriteLine();
    }
    
    /// <summary>
    /// List available checkpoints for a symbol
    /// </summary>
    static void ListCheckpoints(string symbol)
    {
        var checkpointManager = new CheckpointManager();
        var checkpoints = checkpointManager.ListCheckpoints(symbol);
        
        if (!checkpoints.Any())
        {
            Console.WriteLine($"No checkpoints found for {symbol}");
            return;
        }
        
        Console.WriteLine($"\nAvailable checkpoints for {symbol}:");
        Console.WriteLine("".PadRight(80, '='));
        Console.WriteLine($"{"Backtest ID",-25} {"Status",-12} {"Progress",-10} {"Last Date",-12} {"P&L",-15}");
        Console.WriteLine("".PadRight(80, '-'));
        
        foreach (var cp in checkpoints)
        {
            var status = cp.IsCompleted ? "Completed" : "Incomplete";
            var progress = $"{cp.EstimatedCompletionPct:F1}%";
            var lastDate = cp.LastProcessedDate.ToString("yyyy-MM-dd");
            var pnl = $"£{cp.RunningPnL:N0}";
            
            Console.WriteLine($"{cp.BacktestId,-25} {status,-12} {progress,-10} {lastDate,-12} {pnl,-15}");
        }
        
        Console.WriteLine();
        Console.WriteLine("To resume: dotnet run --checkpoint BACKTEST_ID");
        Console.WriteLine("To resume latest: dotnet run SYMBOL --resume");
    }
}
