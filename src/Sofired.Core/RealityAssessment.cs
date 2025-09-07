using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sofired.Core;

/// <summary>
/// Comprehensive reality assessment system for validating each trade against real market conditions
/// </summary>
public class RealityAssessment
{
    public enum RealityLevel
    {
        GREEN,   // 80-100% confidence - Highly realistic, executable trade
        YELLOW,  // 60-79% confidence - Possible but challenging execution  
        RED      // <60% confidence - Unrealistic, likely wouldn't execute
    }

    public record RealityScore
    {
        public decimal TotalScore { get; init; }
        public decimal LiquidityScore { get; init; }
        public decimal PremiumRealityScore { get; init; }
        public decimal MarketConditionsScore { get; init; }
        public decimal ExecutionFeasibilityScore { get; init; }
        public RealityLevel Level { get; init; }
        public List<string> Issues { get; init; } = new();
        public List<string> Adjustments { get; init; } = new();
    }

    public record MarketMicrostructure
    {
        public decimal BidPrice { get; init; }
        public decimal AskPrice { get; init; }
        public decimal SpreadWidth => AskPrice - BidPrice;
        public decimal MidPrice => (BidPrice + AskPrice) / 2m;
        public long Volume { get; init; }
        public long OpenInterest { get; init; }
        public decimal ImpliedVolatility { get; init; }
        public DateTime Timestamp { get; init; }
    }

    public record TradeValidation
    {
        public Position ProposedTrade { get; init; }
        public bool CanExecute { get; init; }
        public RealityScore Score { get; init; }
        public MarketMicrostructure? ActualMarket { get; init; }
        public decimal AdjustedPremium { get; init; }
        public int AdjustedContractSize { get; init; }
        public string ValidationNotes { get; init; } = "";
        public decimal ExpectedSlippage { get; init; }
    }
}

/// <summary>
/// Validates each trade against real market conditions
/// </summary>
public class TradeValidator
{
    private readonly ThetaDataClient _thetaClient;
    private readonly Dictionary<DateTime, List<ThetaDataClient.OptionData>> _optionsCache = new();
    private readonly HashSet<DateTime> _marketHolidays;
    private readonly Dictionary<string, DateTime> _earningsDates;
    private readonly decimal _minVolumeThreshold = 100;
    private readonly decimal _minOpenInterestThreshold = 500;
    private readonly decimal _maxSpreadThreshold = 0.10m;

    public TradeValidator(ThetaDataClient thetaClient)
    {
        _thetaClient = thetaClient;
        _marketHolidays = LoadMarketHolidays();
        _earningsDates = LoadEarningsDates();
    }

    public async Task<RealityAssessment.TradeValidation> ValidateTradeAsync(
        Position proposedTrade, decimal currentStockPrice)
    {
        var validation = new RealityAssessment.TradeValidation
        {
            ProposedTrade = proposedTrade,
            CanExecute = false,
            AdjustedPremium = proposedTrade.PremiumCollected,
            AdjustedContractSize = proposedTrade.ContractSize
        };

        // Step 1: Check market hours and holidays
        if (!IsMarketOpen(proposedTrade.EntryDate))
        {
            validation = validation with
            {
                ValidationNotes = "Market closed - holiday or weekend",
                Score = new RealityAssessment.RealityScore
                {
                    TotalScore = 0,
                    Level = RealityAssessment.RealityLevel.RED,
                    Issues = new List<string> { "Market closed on this date" }
                }
            };
            return validation;
        }

        // Step 2: Check for earnings events
        if (IsNearEarnings(proposedTrade.EntryDate, 5))
        {
            validation = validation with
            {
                AdjustedContractSize = Math.Max(1, proposedTrade.ContractSize / 2),
                ValidationNotes = "Reduced position size due to upcoming earnings"
            };
        }

        // Step 3: Fetch real options data
        var optionsChain = await GetOptionsChainAsync(
            "SOFI", 
            proposedTrade.EntryDate, 
            proposedTrade.ExpirationDate);

        if (!optionsChain.Any())
        {
            validation = validation with
            {
                ValidationNotes = "No options data available",
                Score = new RealityAssessment.RealityScore
                {
                    TotalScore = 20,
                    Level = RealityAssessment.RealityLevel.RED,
                    Issues = new List<string> { "No real options data for validation" }
                }
            };
            return validation;
        }

        // Step 4: Find matching strike and validate liquidity
        var matchingOption = FindBestMatchingOption(
            optionsChain, 
            proposedTrade.StrikePrice,
            proposedTrade.Strategy == StrategyType.PutCreditSpread ? "P" : "C");

        if (matchingOption == null)
        {
            validation = validation with
            {
                ValidationNotes = "No matching strike found in options chain",
                Score = new RealityAssessment.RealityScore
                {
                    TotalScore = 30,
                    Level = RealityAssessment.RealityLevel.RED,
                    Issues = new List<string> { "Strike price not available" }
                }
            };
            return validation;
        }

        // Step 5: Calculate reality scores
        var realityScore = CalculateRealityScore(proposedTrade, matchingOption, currentStockPrice);

        // Step 6: Adjust for market microstructure
        var microstructure = new RealityAssessment.MarketMicrostructure
        {
            BidPrice = matchingOption.Bid,
            AskPrice = matchingOption.Ask,
            Volume = matchingOption.Volume,
            OpenInterest = matchingOption.OpenInterest,
            ImpliedVolatility = matchingOption.ImpliedVolatility,
            Timestamp = proposedTrade.EntryDate
        };

        // Adjust premium for spread impact (we're selling, so we get bid price)
        var adjustedPremium = matchingOption.Bid;
        var expectedSlippage = (matchingOption.Mid - matchingOption.Bid) * proposedTrade.ContractSize * 100;

        // Step 7: Final validation
        validation = validation with
        {
            CanExecute = realityScore.Level != RealityAssessment.RealityLevel.RED,
            Score = realityScore,
            ActualMarket = microstructure,
            AdjustedPremium = adjustedPremium,
            ExpectedSlippage = expectedSlippage,
            ValidationNotes = GenerateValidationNotes(realityScore, microstructure)
        };

        return validation;
    }

    private RealityAssessment.RealityScore CalculateRealityScore(
        Position trade, 
        ThetaDataClient.OptionData option,
        decimal stockPrice)
    {
        var issues = new List<string>();
        var adjustments = new List<string>();

        // Liquidity Score (25%)
        var liquidityScore = 100m;
        if (option.Volume < _minVolumeThreshold)
        {
            liquidityScore -= 30;
            issues.Add($"Low volume: {option.Volume} contracts");
        }
        if (option.OpenInterest < _minOpenInterestThreshold)
        {
            liquidityScore -= 20;
            issues.Add($"Low open interest: {option.OpenInterest}");
        }
        var spreadWidth = option.Ask - option.Bid;
        if (spreadWidth > _maxSpreadThreshold)
        {
            liquidityScore -= 25;
            issues.Add($"Wide spread: ${spreadWidth:F2}");
        }
        liquidityScore = Math.Max(0, liquidityScore);

        // Premium Reality Score (25%)
        var premiumScore = 100m;
        var priceDifference = Math.Abs(trade.PremiumCollected - option.Mid);
        var percentDifference = option.Mid > 0 ? priceDifference / option.Mid : 1;
        if (percentDifference > 0.20m)
        {
            premiumScore -= 40;
            issues.Add($"Premium mismatch: {percentDifference:P0}");
        }
        else if (percentDifference > 0.10m)
        {
            premiumScore -= 20;
            issues.Add($"Minor premium variance: {percentDifference:P0}");
        }

        // Market Conditions Score (25%)
        var marketScore = 100m;
        if (trade.VixLevel > 30)
        {
            marketScore -= 10;
            adjustments.Add("High VIX environment");
        }
        if (IsNearEarnings(trade.EntryDate, 10))
        {
            marketScore -= 15;
            adjustments.Add("Near earnings date");
        }

        // Execution Feasibility Score (25%)
        var executionScore = 100m;
        if (trade.ContractSize > 20)
        {
            executionScore -= 20;
            issues.Add($"Large position size: {trade.ContractSize} contracts");
        }
        if (!IsOptimalTradingTime(trade.EntryDate))
        {
            executionScore -= 10;
            adjustments.Add("Non-optimal trading time");
        }

        // Calculate total score
        var totalScore = (liquidityScore * 0.25m) + (premiumScore * 0.25m) + 
                        (marketScore * 0.25m) + (executionScore * 0.25m);

        var level = totalScore >= 80 ? RealityAssessment.RealityLevel.GREEN :
                   totalScore >= 60 ? RealityAssessment.RealityLevel.YELLOW :
                   RealityAssessment.RealityLevel.RED;

        return new RealityAssessment.RealityScore
        {
            TotalScore = totalScore,
            LiquidityScore = liquidityScore,
            PremiumRealityScore = premiumScore,
            MarketConditionsScore = marketScore,
            ExecutionFeasibilityScore = executionScore,
            Level = level,
            Issues = issues,
            Adjustments = adjustments
        };
    }

    private async Task<List<ThetaDataClient.OptionData>> GetOptionsChainAsync(
        string symbol, DateTime date, DateTime expiration)
    {
        var cacheKey = date.Date;
        if (_optionsCache.ContainsKey(cacheKey))
        {
            return _optionsCache[cacheKey];
        }

        var options = await _thetaClient.GetOptionsChain(symbol, date, expiration);
        _optionsCache[cacheKey] = options;
        return options;
    }

    private ThetaDataClient.OptionData? FindBestMatchingOption(
        List<ThetaDataClient.OptionData> chain, 
        decimal targetStrike, 
        string optionType)
    {
        return chain
            .Where(o => o.OptionType == optionType)
            .OrderBy(o => Math.Abs(o.Strike - targetStrike))
            .FirstOrDefault();
    }

    private bool IsMarketOpen(DateTime date)
    {
        if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            return false;
        
        return !_marketHolidays.Contains(date.Date);
    }

    private bool IsNearEarnings(DateTime date, int daysThreshold)
    {
        return _earningsDates.Values.Any(e => 
            Math.Abs((e - date).TotalDays) <= daysThreshold);
    }

    private bool IsOptimalTradingTime(DateTime datetime)
    {
        var timeOfDay = datetime.TimeOfDay;
        var optimalStart = new TimeSpan(10, 10, 0);  // 10:10 AM
        var optimalEnd = new TimeSpan(10, 30, 0);    // 10:30 AM
        
        return timeOfDay >= optimalStart && timeOfDay <= optimalEnd;
    }

    private string GenerateValidationNotes(
        RealityAssessment.RealityScore score, 
        RealityAssessment.MarketMicrostructure market)
    {
        var notes = $"Reality Score: {score.TotalScore:F0}% ({score.Level})\n";
        notes += $"Market: Bid ${market.BidPrice:F2} / Ask ${market.AskPrice:F2} ";
        notes += $"(Spread: ${market.SpreadWidth:F2})\n";
        notes += $"Volume: {market.Volume:N0} | OI: {market.OpenInterest:N0}\n";
        
        if (score.Issues.Any())
        {
            notes += "Issues: " + string.Join(", ", score.Issues) + "\n";
        }
        
        if (score.Adjustments.Any())
        {
            notes += "Adjustments: " + string.Join(", ", score.Adjustments);
        }
        
        return notes;
    }

    private HashSet<DateTime> LoadMarketHolidays()
    {
        // 2024-2025 US Market Holidays
        return new HashSet<DateTime>
        {
            new DateTime(2024, 1, 1),   // New Year's Day
            new DateTime(2024, 1, 15),  // MLK Day
            new DateTime(2024, 2, 19),  // Presidents Day
            new DateTime(2024, 3, 29),  // Good Friday
            new DateTime(2024, 5, 27),  // Memorial Day
            new DateTime(2024, 6, 19),  // Juneteenth
            new DateTime(2024, 7, 4),   // Independence Day
            new DateTime(2024, 9, 2),   // Labor Day
            new DateTime(2024, 11, 28), // Thanksgiving
            new DateTime(2024, 12, 25), // Christmas
            new DateTime(2025, 1, 1),   // New Year's Day
            new DateTime(2025, 1, 20),  // MLK Day
            new DateTime(2025, 2, 17),  // Presidents Day
            new DateTime(2025, 4, 18),  // Good Friday
            new DateTime(2025, 5, 26),  // Memorial Day
            new DateTime(2025, 6, 19),  // Juneteenth
            new DateTime(2025, 7, 4),   // Independence Day
            new DateTime(2025, 9, 1),   // Labor Day
            new DateTime(2025, 11, 27), // Thanksgiving
            new DateTime(2025, 12, 25), // Christmas
        };
    }

    private Dictionary<string, DateTime> LoadEarningsDates()
    {
        // SOFI historical and projected earnings dates
        return new Dictionary<string, DateTime>
        {
            ["Q4_2023"] = new DateTime(2024, 1, 29),
            ["Q1_2024"] = new DateTime(2024, 4, 29),
            ["Q2_2024"] = new DateTime(2024, 7, 30),
            ["Q3_2024"] = new DateTime(2024, 10, 29),
            ["Q4_2024"] = new DateTime(2025, 1, 28),
            ["Q1_2025"] = new DateTime(2025, 4, 28),
            ["Q2_2025"] = new DateTime(2025, 7, 29),
            ["Q3_2025"] = new DateTime(2025, 10, 28),
        };
    }
}

/// <summary>
/// Generates comprehensive reality audit reports
/// </summary>
public class RealityAuditReport
{
    private readonly List<RealityAssessment.TradeValidation> _validations = new();
    private readonly Dictionary<string, decimal> _adjustmentImpacts = new();

    public void AddValidation(RealityAssessment.TradeValidation validation)
    {
        _validations.Add(validation);
    }

    public RealityAuditSummary GenerateSummary()
    {
        var totalTrades = _validations.Count;
        var executableTrades = _validations.Count(v => v.CanExecute);
        var greenTrades = _validations.Count(v => v.Score?.Level == RealityAssessment.RealityLevel.GREEN);
        var yellowTrades = _validations.Count(v => v.Score?.Level == RealityAssessment.RealityLevel.YELLOW);
        var redTrades = _validations.Count(v => v.Score?.Level == RealityAssessment.RealityLevel.RED);

        var totalSlippage = _validations.Sum(v => v.ExpectedSlippage);
        var averageRealityScore = _validations
            .Where(v => v.Score != null)
            .Average(v => v.Score.TotalScore);

        var liquidityIssues = _validations
            .Where(v => v.Score?.Issues.Any(i => i.Contains("volume") || i.Contains("interest")) ?? false)
            .Count();

        var spreadIssues = _validations
            .Where(v => v.Score?.Issues.Any(i => i.Contains("spread")) ?? false)
            .Count();

        var earningsAdjustments = _validations
            .Where(v => v.ValidationNotes.Contains("earnings"))
            .Count();

        return new RealityAuditSummary
        {
            TotalTradesAnalyzed = totalTrades,
            ExecutableTrades = executableTrades,
            ExecutionRate = totalTrades > 0 ? (decimal)executableTrades / totalTrades : 0,
            GreenTrades = greenTrades,
            YellowTrades = yellowTrades,
            RedTrades = redTrades,
            AverageRealityScore = averageRealityScore,
            TotalExpectedSlippage = totalSlippage,
            LiquidityConstrainedTrades = liquidityIssues,
            SpreadImpactedTrades = spreadIssues,
            EarningsAdjustedTrades = earningsAdjustments,
            TopIssues = GetTopIssues(),
            PerformanceImpact = CalculatePerformanceImpact()
        };
    }

    private List<string> GetTopIssues()
    {
        var allIssues = _validations
            .Where(v => v.Score != null)
            .SelectMany(v => v.Score.Issues)
            .GroupBy(i => i)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"{g.Key} ({g.Count()} occurrences)")
            .ToList();

        return allIssues;
    }

    private RealityPerformanceImpact CalculatePerformanceImpact()
    {
        var originalPremiums = _validations.Sum(v => v.ProposedTrade.PremiumCollected * v.ProposedTrade.ContractSize * 100);
        var adjustedPremiums = _validations.Sum(v => v.AdjustedPremium * v.AdjustedContractSize * 100);
        var slippageImpact = _validations.Sum(v => v.ExpectedSlippage);
        var skippedTrades = _validations.Count(v => !v.CanExecute);
        var skippedPremiums = _validations
            .Where(v => !v.CanExecute)
            .Sum(v => v.ProposedTrade.PremiumCollected * v.ProposedTrade.ContractSize * 100);

        return new RealityPerformanceImpact
        {
            OriginalExpectedPremiums = originalPremiums,
            RealityAdjustedPremiums = adjustedPremiums,
            SlippageCost = slippageImpact,
            SkippedTradeCount = skippedTrades,
            SkippedTradePremiums = skippedPremiums,
            NetImpactPercentage = originalPremiums > 0 ? 
                ((adjustedPremiums - slippageImpact - originalPremiums) / originalPremiums) : 0
        };
    }

    public record RealityAuditSummary
    {
        public int TotalTradesAnalyzed { get; init; }
        public int ExecutableTrades { get; init; }
        public decimal ExecutionRate { get; init; }
        public int GreenTrades { get; init; }
        public int YellowTrades { get; init; }
        public int RedTrades { get; init; }
        public decimal AverageRealityScore { get; init; }
        public decimal TotalExpectedSlippage { get; init; }
        public int LiquidityConstrainedTrades { get; init; }
        public int SpreadImpactedTrades { get; init; }
        public int EarningsAdjustedTrades { get; init; }
        public List<string> TopIssues { get; init; } = new();
        public RealityPerformanceImpact PerformanceImpact { get; init; }
    }

    public record RealityPerformanceImpact
    {
        public decimal OriginalExpectedPremiums { get; init; }
        public decimal RealityAdjustedPremiums { get; init; }
        public decimal SlippageCost { get; init; }
        public int SkippedTradeCount { get; init; }
        public decimal SkippedTradePremiums { get; init; }
        public decimal NetImpactPercentage { get; init; }
    }

    public void GenerateDetailedReport(string filePath)
    {
        using var writer = new System.IO.StreamWriter(filePath);
        
        writer.WriteLine("=== COMPREHENSIVE REALITY AUDIT REPORT ===");
        writer.WriteLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine();

        var summary = GenerateSummary();
        
        writer.WriteLine("EXECUTIVE SUMMARY");
        writer.WriteLine("-----------------");
        writer.WriteLine($"Total Trades Analyzed: {summary.TotalTradesAnalyzed}");
        writer.WriteLine($"Executable Trades: {summary.ExecutableTrades} ({summary.ExecutionRate:P1})");
        writer.WriteLine($"Average Reality Score: {summary.AverageRealityScore:F1}%");
        writer.WriteLine();

        writer.WriteLine("TRADE CLASSIFICATION");
        writer.WriteLine("--------------------");
        writer.WriteLine($"GREEN (Highly Executable): {summary.GreenTrades} trades");
        writer.WriteLine($"YELLOW (Challenging): {summary.YellowTrades} trades");
        writer.WriteLine($"RED (Unrealistic): {summary.RedTrades} trades");
        writer.WriteLine();

        writer.WriteLine("MARKET MICROSTRUCTURE IMPACT");
        writer.WriteLine("----------------------------");
        writer.WriteLine($"Total Expected Slippage: £{summary.TotalExpectedSlippage:F2}");
        writer.WriteLine($"Liquidity Constrained: {summary.LiquidityConstrainedTrades} trades");
        writer.WriteLine($"Wide Spread Issues: {summary.SpreadImpactedTrades} trades");
        writer.WriteLine($"Earnings Adjustments: {summary.EarningsAdjustedTrades} trades");
        writer.WriteLine();

        writer.WriteLine("TOP RECURRING ISSUES");
        writer.WriteLine("--------------------");
        foreach (var issue in summary.TopIssues)
        {
            writer.WriteLine($"- {issue}");
        }
        writer.WriteLine();

        writer.WriteLine("PERFORMANCE IMPACT ANALYSIS");
        writer.WriteLine("---------------------------");
        var impact = summary.PerformanceImpact;
        writer.WriteLine($"Original Expected Premiums: £{impact.OriginalExpectedPremiums:F2}");
        writer.WriteLine($"Reality-Adjusted Premiums: £{impact.RealityAdjustedPremiums:F2}");
        writer.WriteLine($"Slippage Cost: £{impact.SlippageCost:F2}");
        writer.WriteLine($"Skipped Trades: {impact.SkippedTradeCount} (£{impact.SkippedTradePremiums:F2} lost)");
        writer.WriteLine($"Net Reality Impact: {impact.NetImpactPercentage:P1}");
        writer.WriteLine();

        writer.WriteLine("DETAILED TRADE-BY-TRADE ANALYSIS");
        writer.WriteLine("--------------------------------");
        
        foreach (var validation in _validations.Take(50)) // Show first 50 for brevity
        {
            writer.WriteLine($"\nTrade ID: {validation.ProposedTrade.Id}");
            writer.WriteLine($"Date: {validation.ProposedTrade.EntryDate:yyyy-MM-dd}");
            writer.WriteLine($"Strategy: {validation.ProposedTrade.Strategy}");
            writer.WriteLine($"Strike: ${validation.ProposedTrade.StrikePrice:F2}");
            writer.WriteLine($"Reality Score: {validation.Score?.TotalScore:F0}% ({validation.Score?.Level})");
            writer.WriteLine($"Can Execute: {(validation.CanExecute ? "YES" : "NO")}");
            
            if (validation.ActualMarket != null)
            {
                var market = validation.ActualMarket;
                writer.WriteLine($"Market: Bid ${market.BidPrice:F2} / Ask ${market.AskPrice:F2}");
                writer.WriteLine($"Volume: {market.Volume:N0} | Open Interest: {market.OpenInterest:N0}");
            }
            
            writer.WriteLine($"Expected Slippage: £{validation.ExpectedSlippage:F2}");
            writer.WriteLine($"Notes: {validation.ValidationNotes}");
        }
        
        writer.WriteLine();
        writer.WriteLine("=== END OF REALITY AUDIT REPORT ===");
    }
}