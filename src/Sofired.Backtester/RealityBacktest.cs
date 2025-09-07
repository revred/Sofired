using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sofired.Core;

namespace Sofired.Backtester;

/// <summary>
/// Enhanced backtester with comprehensive reality assessment for each trade
/// </summary>
public class RealityBacktest
{
    private readonly TradingEngine _engine;
    private readonly TradeValidator _validator;
    private readonly RealityAuditReport _auditReport;
    private readonly List<EnhancedTradeLedger> _ledger = new();

    public record EnhancedTradeLedger
    {
        public DateTime Date { get; init; }
        public string TradeId { get; init; } = "";
        public StrategyType Strategy { get; init; }
        public decimal StrikePrice { get; init; }
        public int DTE { get; init; }
        public decimal Delta { get; init; }
        
        // Original theoretical values
        public decimal TheoreticalPremium { get; init; }
        public int TheoreticalContracts { get; init; }
        
        // Reality-adjusted values
        public decimal ActualPremium { get; init; }
        public int ActualContracts { get; init; }
        public decimal ExpectedSlippage { get; init; }
        
        // Reality assessment
        public decimal RealityScore { get; init; }
        public RealityAssessment.RealityLevel RealityLevel { get; init; }
        public bool CanExecute { get; init; }
        public List<string> Issues { get; init; } = new();
        public List<string> Adjustments { get; init; } = new();
        
        // Market microstructure
        public decimal BidPrice { get; init; }
        public decimal AskPrice { get; init; }
        public decimal SpreadWidth { get; init; }
        public long Volume { get; init; }
        public long OpenInterest { get; init; }
        
        // Financial impact
        public decimal TheoreticalProfit { get; init; }
        public decimal RealityAdjustedProfit { get; init; }
        public decimal ProfitImpact => RealityAdjustedProfit - TheoreticalProfit;
        public decimal ProfitImpactPercent => TheoreticalProfit != 0 ? 
            ProfitImpact / Math.Abs(TheoreticalProfit) : 0;
    }

    public RealityBacktest(StrategyConfig config, ThetaDataClient thetaClient)
    {
        _validator = new TradeValidator(thetaClient);
        _engine = new TradingEngine(config, _validator);
        _auditReport = new RealityAuditReport();
    }

    public async Task<BacktestResults> RunBacktestAsync(
        List<DailyBar> bars, 
        Dictionary<DateTime, decimal> vixData)
    {
        Console.WriteLine("=== STARTING REALITY-VALIDATED BACKTEST ===");
        Console.WriteLine($"Period: {bars.First().Date:yyyy-MM-dd} to {bars.Last().Date:yyyy-MM-dd}");
        Console.WriteLine($"Trading Days: {bars.Count}");
        Console.WriteLine();

        var sessions = new List<TradingSession>();
        var skippedTrades = 0;
        var adjustedTrades = 0;
        var greenTrades = 0;
        var yellowTrades = 0;
        var redTrades = 0;

        foreach (var bar in bars)
        {
            var vixLevel = vixData.ContainsKey(bar.Date.Date) ? 
                vixData[bar.Date.Date] : 
                EstimateVixFromVolatility(bars, bar);

            // Process trading day
            var session = _engine.ProcessTradingDay(bar.Date, bar, vixLevel);
            sessions.Add(session);

            // Validate all new positions opened today
            foreach (var position in session.Positions.Where(p => p.Status == PositionStatus.Open))
            {
                var validation = await _validator.ValidateTradeAsync(position, bar.Close);
                _auditReport.AddValidation(validation);

                // Create enhanced ledger entry
                var ledgerEntry = CreateLedgerEntry(position, validation, bar);
                _ledger.Add(ledgerEntry);

                // Track statistics
                if (!validation.CanExecute)
                {
                    skippedTrades++;
                    Console.WriteLine($"❌ SKIPPED: {position.Id} - Reality Score: {validation.Score?.TotalScore:F0}%");
                }
                else if (validation.AdjustedContractSize != position.ContractSize || 
                         Math.Abs(validation.AdjustedPremium - position.PremiumCollected) > 0.01m)
                {
                    adjustedTrades++;
                    Console.WriteLine($"⚠️  ADJUSTED: {position.Id} - Contracts: {position.ContractSize}→{validation.AdjustedContractSize}, " +
                                    $"Premium: ${position.PremiumCollected:F2}→${validation.AdjustedPremium:F2}");
                }

                // Track reality levels
                switch (validation.Score?.Level)
                {
                    case RealityAssessment.RealityLevel.GREEN:
                        greenTrades++;
                        break;
                    case RealityAssessment.RealityLevel.YELLOW:
                        yellowTrades++;
                        break;
                    case RealityAssessment.RealityLevel.RED:
                        redTrades++;
                        break;
                }
            }

            // Display progress every 20 days
            if (bars.IndexOf(bar) % 20 == 0 && bars.IndexOf(bar) > 0)
            {
                var currentCapital = _engine.GetCurrentCapital();
                var totalPnL = _engine.GetTotalPnL();
                Console.WriteLine($"{bar.Date:yyyy-MM-dd}: Capital: £{currentCapital:F0}, " +
                                $"P&L: £{totalPnL:F0}, Green: {greenTrades}, Yellow: {yellowTrades}, Red: {redTrades}");
            }
        }

        // Generate comprehensive audit
        var auditSummary = _auditReport.GenerateSummary();
        DisplayAuditSummary(auditSummary);

        // Generate detailed reality report
        var reportPath = $"out/{DateTime.Now:yyyyMMdd_HHmm}_reality_audit.txt";
        _auditReport.GenerateDetailedReport(reportPath);
        Console.WriteLine($"\n✅ Detailed reality audit saved to: {reportPath}");

        // Calculate reality-adjusted performance
        var results = CalculateRealityAdjustedResults(sessions, auditSummary);
        
        return results;
    }

    private EnhancedTradeLedger CreateLedgerEntry(
        Position position, 
        RealityAssessment.TradeValidation validation,
        DailyBar bar)
    {
        var market = validation.ActualMarket;
        
        return new EnhancedTradeLedger
        {
            Date = position.EntryDate,
            TradeId = position.Id,
            Strategy = position.Strategy,
            StrikePrice = position.StrikePrice,
            DTE = position.DaysToExpiration,
            Delta = position.Delta,
            
            // Theoretical values
            TheoreticalPremium = position.PremiumCollected,
            TheoreticalContracts = position.ContractSize,
            
            // Reality-adjusted values
            ActualPremium = validation.AdjustedPremium,
            ActualContracts = validation.AdjustedContractSize,
            ExpectedSlippage = validation.ExpectedSlippage,
            
            // Reality assessment
            RealityScore = validation.Score?.TotalScore ?? 0,
            RealityLevel = validation.Score?.Level ?? RealityAssessment.RealityLevel.RED,
            CanExecute = validation.CanExecute,
            Issues = validation.Score?.Issues ?? new List<string>(),
            Adjustments = validation.Score?.Adjustments ?? new List<string>(),
            
            // Market microstructure
            BidPrice = market?.BidPrice ?? 0,
            AskPrice = market?.AskPrice ?? 0,
            SpreadWidth = market?.SpreadWidth ?? 0,
            Volume = market?.Volume ?? 0,
            OpenInterest = market?.OpenInterest ?? 0,
            
            // Financial impact
            TheoreticalProfit = position.PremiumCollected * position.ContractSize * 100,
            RealityAdjustedProfit = validation.AdjustedPremium * validation.AdjustedContractSize * 100 
                                   - validation.ExpectedSlippage
        };
    }

    private void DisplayAuditSummary(RealityAuditReport.RealityAuditSummary summary)
    {
        Console.WriteLine("\n" + "=".PadRight(60, '='));
        Console.WriteLine("REALITY AUDIT SUMMARY");
        Console.WriteLine("=".PadRight(60, '='));
        
        Console.WriteLine($"\n📊 TRADE VALIDATION RESULTS");
        Console.WriteLine($"   Total Trades Analyzed: {summary.TotalTradesAnalyzed}");
        Console.WriteLine($"   Executable Trades: {summary.ExecutableTrades} ({summary.ExecutionRate:P1})");
        Console.WriteLine($"   Average Reality Score: {summary.AverageRealityScore:F1}%");
        
        Console.WriteLine($"\n🚦 REALITY CLASSIFICATION");
        Console.WriteLine($"   GREEN (Highly Executable): {summary.GreenTrades} trades");
        Console.WriteLine($"   YELLOW (Challenging): {summary.YellowTrades} trades");
        Console.WriteLine($"   RED (Unrealistic): {summary.RedTrades} trades");
        
        Console.WriteLine($"\n💰 MARKET MICROSTRUCTURE IMPACT");
        Console.WriteLine($"   Expected Slippage: £{summary.TotalExpectedSlippage:F2}");
        Console.WriteLine($"   Liquidity Constrained: {summary.LiquidityConstrainedTrades} trades");
        Console.WriteLine($"   Wide Spread Issues: {summary.SpreadImpactedTrades} trades");
        Console.WriteLine($"   Earnings Adjustments: {summary.EarningsAdjustedTrades} trades");
        
        if (summary.TopIssues.Any())
        {
            Console.WriteLine($"\n⚠️  TOP RECURRING ISSUES");
            foreach (var issue in summary.TopIssues.Take(5))
            {
                Console.WriteLine($"   - {issue}");
            }
        }
        
        var impact = summary.PerformanceImpact;
        Console.WriteLine($"\n📈 PERFORMANCE IMPACT");
        Console.WriteLine($"   Original Expected: £{impact.OriginalExpectedPremiums:F2}");
        Console.WriteLine($"   Reality-Adjusted: £{impact.RealityAdjustedPremiums:F2}");
        Console.WriteLine($"   Slippage Cost: £{impact.SlippageCost:F2}");
        Console.WriteLine($"   Skipped Trades: {impact.SkippedTradeCount} (£{impact.SkippedTradePremiums:F2} lost)");
        Console.WriteLine($"   Net Reality Impact: {impact.NetImpactPercentage:P1}");
    }

    private BacktestResults CalculateRealityAdjustedResults(
        List<TradingSession> sessions,
        RealityAuditReport.RealityAuditSummary auditSummary)
    {
        var totalPnL = _engine.GetTotalPnL();
        var initialCapital = 10000m; // From config
        var finalCapital = _engine.GetCurrentCapital();
        
        // Apply reality adjustments
        var realityImpact = auditSummary.PerformanceImpact.NetImpactPercentage;
        var adjustedPnL = totalPnL * (1 + realityImpact);
        var adjustedFinalCapital = initialCapital + adjustedPnL;
        var adjustedROI = adjustedPnL / initialCapital;
        
        return new BacktestResults
        {
            InitialCapital = initialCapital,
            FinalCapital = finalCapital,
            RealityAdjustedFinalCapital = adjustedFinalCapital,
            TotalPnL = totalPnL,
            RealityAdjustedPnL = adjustedPnL,
            ROI = totalPnL / initialCapital,
            RealityAdjustedROI = adjustedROI,
            TotalTrades = auditSummary.TotalTradesAnalyzed,
            ExecutableTrades = auditSummary.ExecutableTrades,
            ExecutionRate = auditSummary.ExecutionRate,
            AverageRealityScore = auditSummary.AverageRealityScore,
            SlippageCost = auditSummary.TotalExpectedSlippage,
            Sessions = sessions,
            Ledger = _ledger
        };
    }

    private decimal EstimateVixFromVolatility(List<DailyBar> bars, DailyBar currentBar)
    {
        // Simple 20-day historical volatility calculation as VIX proxy
        var lookback = 20;
        var recentBars = bars
            .Where(b => b.Date <= currentBar.Date)
            .OrderByDescending(b => b.Date)
            .Take(lookback + 1)
            .OrderBy(b => b.Date)
            .ToList();
        
        if (recentBars.Count < 2)
            return 20m; // Default VIX level
        
        var returns = new List<decimal>();
        for (int i = 1; i < recentBars.Count; i++)
        {
            var dailyReturn = (recentBars[i].Close - recentBars[i - 1].Close) / recentBars[i - 1].Close;
            returns.Add(dailyReturn);
        }
        
        var avgReturn = returns.Average();
        var variance = returns.Select(r => (r - avgReturn) * (r - avgReturn)).Average();
        var volatility = (decimal)Math.Sqrt((double)variance);
        
        // Convert to annualized volatility (VIX-like)
        return volatility * (decimal)Math.Sqrt(252) * 100;
    }

    public record BacktestResults
    {
        public decimal InitialCapital { get; init; }
        public decimal FinalCapital { get; init; }
        public decimal RealityAdjustedFinalCapital { get; init; }
        public decimal TotalPnL { get; init; }
        public decimal RealityAdjustedPnL { get; init; }
        public decimal ROI { get; init; }
        public decimal RealityAdjustedROI { get; init; }
        public int TotalTrades { get; init; }
        public int ExecutableTrades { get; init; }
        public decimal ExecutionRate { get; init; }
        public decimal AverageRealityScore { get; init; }
        public decimal SlippageCost { get; init; }
        public List<TradingSession> Sessions { get; init; } = new();
        public List<EnhancedTradeLedger> Ledger { get; init; } = new();
    }
}