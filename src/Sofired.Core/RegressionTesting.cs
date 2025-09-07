using System;
using System.Collections.Generic;
using System.Linq;

namespace Sofired.Core;

/// <summary>
/// Regression testing capabilities to ensure strategy modifications don't break core functionality
/// </summary>
public class RegressionTesting
{
    private readonly Dictionary<string, object> _baselineResults = new();
    private readonly List<string> _regressionErrors = new();
    
    public void CaptureBaseline(string testName, object result)
    {
        _baselineResults[testName] = result;
        Console.WriteLine($"BASELINE CAPTURED: {testName} = {result}");
    }
    
    public bool ValidateAgainstBaseline(string testName, object currentResult, decimal tolerance = 0.01m)
    {
        if (!_baselineResults.ContainsKey(testName))
        {
            _regressionErrors.Add($"No baseline found for test: {testName}");
            return false;
        }
        
        var baseline = _baselineResults[testName];
        var isValid = CompareResults(baseline, currentResult, tolerance);
        
        if (!isValid)
        {
            var error = $"REGRESSION DETECTED: {testName} - Expected: {baseline}, Got: {currentResult}";
            _regressionErrors.Add(error);
            Console.WriteLine(error);
        }
        else
        {
            Console.WriteLine($"VALIDATION PASSED: {testName} - {currentResult} matches baseline");
        }
        
        return isValid;
    }
    
    private static bool CompareResults(object baseline, object current, decimal tolerance)
    {
        return (baseline, current) switch
        {
            (decimal baseDecimal, decimal currentDecimal) => 
                Math.Abs(baseDecimal - currentDecimal) <= tolerance,
            (int baseInt, int currentInt) => 
                baseInt == currentInt,
            (string baseString, string currentString) => 
                baseString == currentString,
            (bool baseBool, bool currentBool) => 
                baseBool == currentBool,
            _ => baseline?.Equals(current) ?? current == null
        };
    }
    
    public RegressionTestSuite CreateTestSuite(TradingEngine engine)
    {
        return new RegressionTestSuite(this, engine);
    }
    
    public List<string> GetRegressionErrors() => new(_regressionErrors);
    
    public bool HasRegressions() => _regressionErrors.Any();
    
    public void ClearErrors() => _regressionErrors.Clear();
}

/// <summary>
/// Comprehensive test suite for validating trading engine functionality
/// </summary>
public class RegressionTestSuite
{
    private readonly RegressionTesting _testing;
    private readonly TradingEngine _engine;
    
    public RegressionTestSuite(RegressionTesting testing, TradingEngine engine)
    {
        _testing = testing;
        _engine = engine;
    }
    
    public void RunFullSuite(List<DailyBar> testBars, bool captureBaseline = false)
    {
        Console.WriteLine("=== STARTING REGRESSION TEST SUITE ===");
        
        // Test 1: Basic Position Creation
        TestPositionCreation(testBars, captureBaseline);
        
        // Test 2: Capital Compounding
        TestCapitalCompounding(testBars, captureBaseline);
        
        // Test 3: Early Closing Logic
        TestEarlyClosingLogic(testBars, captureBaseline);
        
        // Test 4: VIX Regime Adjustments
        TestVixRegimeAdjustments(testBars, captureBaseline);
        
        // Test 5: Performance Metrics
        TestPerformanceMetrics(testBars, captureBaseline);
        
        // Test 6: Risk Management
        TestRiskManagement(testBars, captureBaseline);
        
        var hasRegressions = _testing.HasRegressions();
        Console.WriteLine($"=== REGRESSION TEST SUITE COMPLETED - {(hasRegressions ? "FAILURES DETECTED" : "ALL TESTS PASSED")} ===");
        
        if (hasRegressions)
        {
            Console.WriteLine("Regression Errors:");
            foreach (var error in _testing.GetRegressionErrors())
            {
                Console.WriteLine($"  - {error}");
            }
        }
    }
    
    private void TestPositionCreation(List<DailyBar> testBars, bool captureBaseline)
    {
        Console.WriteLine("Testing Position Creation...");
        
        var testBar = testBars.First();
        var session = _engine.ProcessTradingDay(testBar.Date, testBar, 20m);
        
        var testName = "position_creation_count";
        if (captureBaseline)
        {
            _testing.CaptureBaseline(testName, session.PositionsOpened);
        }
        else
        {
            _testing.ValidateAgainstBaseline(testName, session.PositionsOpened);
        }
    }
    
    private void TestCapitalCompounding(List<DailyBar> testBars, bool captureBaseline)
    {
        Console.WriteLine("Testing Capital Compounding...");
        
        var initialCapital = _engine.GetCurrentCapital();
        
        // Process several days to test compounding
        foreach (var bar in testBars.Take(5))
        {
            _engine.ProcessTradingDay(bar.Date, bar, 20m);
        }
        
        var finalCapital = _engine.GetCurrentCapital();
        var capitalGrowth = finalCapital - initialCapital;
        
        var testName = "capital_compounding_growth";
        if (captureBaseline)
        {
            _testing.CaptureBaseline(testName, Math.Round(capitalGrowth, 2));
        }
        else
        {
            _testing.ValidateAgainstBaseline(testName, Math.Round(capitalGrowth, 2), 1.0m); // £1 tolerance
        }
    }
    
    private void TestEarlyClosingLogic(List<DailyBar> testBars, bool captureBaseline)
    {
        Console.WriteLine("Testing Early Closing Logic...");
        
        var closedPositions = _engine.GetClosedPositions();
        var profitableCloses = closedPositions.Count(p => (p.ProfitLoss ?? 0) > 0);
        var totalCloses = closedPositions.Count;
        
        var testName = "early_closing_success_rate";
        if (captureBaseline && totalCloses > 0)
        {
            var successRate = (decimal)profitableCloses / totalCloses;
            _testing.CaptureBaseline(testName, Math.Round(successRate, 3));
        }
        else if (totalCloses > 0)
        {
            var successRate = (decimal)profitableCloses / totalCloses;
            _testing.ValidateAgainstBaseline(testName, Math.Round(successRate, 3), 0.05m);
        }
    }
    
    private void TestVixRegimeAdjustments(List<DailyBar> testBars, bool captureBaseline)
    {
        Console.WriteLine("Testing VIX Regime Adjustments...");
        
        // Test low volatility scenario
        var lowVolSession = _engine.ProcessTradingDay(testBars.First().Date, testBars.First(), 12m);
        
        // Test high volatility scenario  
        var highVolSession = _engine.ProcessTradingDay(testBars.Skip(1).First().Date, testBars.Skip(1).First(), 35m);
        
        var testName = "vix_regime_response";
        var regimeResponse = lowVolSession.PositionsOpened + highVolSession.PositionsOpened;
        
        if (captureBaseline)
        {
            _testing.CaptureBaseline(testName, regimeResponse);
        }
        else
        {
            _testing.ValidateAgainstBaseline(testName, regimeResponse);
        }
    }
    
    private void TestPerformanceMetrics(List<DailyBar> testBars, bool captureBaseline)
    {
        Console.WriteLine("Testing Performance Metrics...");
        
        var totalPnL = _engine.GetTotalPnL();
        var allPositions = _engine.GetAllPositions();
        var totalPositions = allPositions.Count;
        
        var testName = "performance_total_positions";
        if (captureBaseline)
        {
            _testing.CaptureBaseline(testName, totalPositions);
        }
        else
        {
            _testing.ValidateAgainstBaseline(testName, totalPositions);
        }
        
        var pnlTestName = "performance_total_pnl";
        if (captureBaseline)
        {
            _testing.CaptureBaseline(pnlTestName, Math.Round(totalPnL, 2));
        }
        else
        {
            _testing.ValidateAgainstBaseline(pnlTestName, Math.Round(totalPnL, 2), 5.0m); // £5 tolerance
        }
    }
    
    private void TestRiskManagement(List<DailyBar> testBars, bool captureBaseline)
    {
        Console.WriteLine("Testing Risk Management...");
        
        var positions = _engine.GetAllPositions();
        var maxContractSize = positions.Any() ? positions.Max(p => p.ContractSize) : 0;
        var maxCapitalAllocation = positions.Any() ? positions.Max(p => p.CapitalAllocated) : 0;
        
        var testName = "risk_max_contract_size";
        if (captureBaseline)
        {
            _testing.CaptureBaseline(testName, maxContractSize);
        }
        else
        {
            _testing.ValidateAgainstBaseline(testName, maxContractSize);
        }
        
        var capitalTestName = "risk_max_capital_allocation";
        if (captureBaseline)
        {
            _testing.CaptureBaseline(capitalTestName, Math.Round(maxCapitalAllocation, 2));
        }
        else
        {
            _testing.ValidateAgainstBaseline(capitalTestName, Math.Round(maxCapitalAllocation, 2), 10.0m);
        }
    }
}

/// <summary>
/// Performance regression detector for monitoring strategy modifications
/// </summary>
public static class PerformanceRegression
{
    public static void ValidatePerformanceMetrics(TradingEngine engine, 
        decimal expectedROI, 
        decimal expectedTotalPnL, 
        int expectedTotalTrades,
        decimal tolerance = 0.05m)
    {
        var actualPnL = engine.GetTotalPnL();
        var actualTrades = engine.GetAllPositions().Count;
        var initialCapital = 10000m; // Known from config
        var actualROI = actualPnL / initialCapital;
        
        Console.WriteLine("=== PERFORMANCE REGRESSION VALIDATION ===");
        
        // Validate ROI
        var roiDiff = Math.Abs(expectedROI - actualROI);
        var roiValid = roiDiff <= tolerance;
        Console.WriteLine($"ROI: Expected {expectedROI:P2}, Got {actualROI:P2}, Diff: {roiDiff:P3} - {(roiValid ? "PASS" : "FAIL")}");
        
        // Validate Total P&L
        var pnlDiff = Math.Abs(expectedTotalPnL - actualPnL);
        var pnlValid = pnlDiff <= (expectedTotalPnL * tolerance);
        Console.WriteLine($"Total P&L: Expected £{expectedTotalPnL:F0}, Got £{actualPnL:F0}, Diff: £{pnlDiff:F0} - {(pnlValid ? "PASS" : "FAIL")}");
        
        // Validate Trade Count
        var tradeValid = Math.Abs(expectedTotalTrades - actualTrades) <= 5; // Allow small variance
        Console.WriteLine($"Trade Count: Expected {expectedTotalTrades}, Got {actualTrades} - {(tradeValid ? "PASS" : "FAIL")}");
        
        var allValid = roiValid && pnlValid && tradeValid;
        Console.WriteLine($"=== OVERALL RESULT: {(allValid ? "NO PERFORMANCE REGRESSION" : "PERFORMANCE REGRESSION DETECTED")} ===");
        
        if (!allValid)
        {
            throw new InvalidOperationException("Performance regression detected! Strategy modifications have negatively impacted results.");
        }
    }
}