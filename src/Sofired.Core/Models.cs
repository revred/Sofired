using System;
using System.Collections.Generic;

namespace Sofired.Core;

public record DailyBar(DateTime Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

public enum VolRegime { Low, Normal, High }

public enum StrategyType { PutCreditSpread, CoveredCall }

public enum PositionStatus { Open, Closed, Rolled, Assigned, Expired }

public enum MarketRegime { Bull, Bear, Sideways, Volatile, Trending }

public enum VolatilityEvent { None, VIXSpike, Earnings, News, Breakout }

public record Position
{
    public string Id { get; init; } = "";
    public StrategyType Strategy { get; init; }
    public DateTime EntryDate { get; init; }
    public DateTime? ExitDate { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal? ExitPrice { get; init; }
    public decimal StrikePrice { get; init; }
    public decimal Delta { get; init; }
    public int DaysToExpiration { get; init; }
    public DateTime ExpirationDate { get; init; }
    public decimal PremiumCollected { get; init; }
    public decimal? ProfitLoss { get; init; }
    public decimal MaxProfit { get; init; }
    public PositionStatus Status { get; init; }
    public VolRegime VixRegime { get; init; }
    public decimal VixLevel { get; init; }
    public decimal UnderlyingPrice { get; init; }
    public string Notes { get; init; } = "";
    
    // High-ROI Extensions
    public int ContractSize { get; init; } = 1;
    public decimal CapitalAllocated { get; init; }
    public MarketRegime MarketRegime { get; init; } = MarketRegime.Sideways;
    public VolatilityEvent VolEvent { get; init; } = VolatilityEvent.None;
    public string EntryReasoning { get; init; } = "";
    public string ExitReasoning { get; init; } = "";
    public decimal LeverageMultiplier { get; init; } = 1.0m;
    
    // Phase 3: Enhanced P&L properties
    public string Symbol { get; init; } = "";
    public string StrategyType { get; init; } = "";
    public decimal? ShortStrike { get; init; }
    public decimal? LongStrike { get; init; }
    public int Quantity { get; init; } = 1;
    public DateTime OpenDate { get; init; }
    public DateTime? CloseDate { get; init; }
    public decimal? ClosePrice { get; init; }
    public bool IsOpen { get; init; } = true;
    
    // Performance Calculations
    public decimal ProfitPercentage => MaxProfit > 0 ? (ProfitLoss ?? 0) / MaxProfit : 0;
    public decimal ROIPercentage => CapitalAllocated > 0 ? (ProfitLoss ?? 0) / CapitalAllocated : 0;
    public decimal AnnualizedROI => CapitalAllocated > 0 && DaysToExpiration > 0 ? 
        ((ProfitLoss ?? 0) / CapitalAllocated) * (365m / DaysToExpiration) : 0;
}

public record StrategyConfig
{
    public int PreferredDTE { get; init; } = 45;
    public int MinDTE { get; init; } = 30;
    public int MaxDTE { get; init; } = 60;
    public decimal TargetDelta { get; init; } = 0.15m;
    public decimal EarlyCloseThreshold { get; init; } = 0.70m;
    public decimal OptimalCloseThreshold { get; init; } = 0.80m;
    public decimal MaxCloseThreshold { get; init; } = 0.90m; // Close earlier for reinvestment
    public bool UseDelayedRolling { get; init; } = true;
    public decimal WeeklyPremiumGoal { get; init; } = 2000m; // £2000
    public decimal MonthlyPremiumGoal { get; init; } = 8000m; // £8000
    
    // High-ROI Configuration
    public decimal InitialCapital { get; init; } = 10000m; // £10k starting capital
    public decimal MaxPortfolioRisk { get; init; } = 0.05m; // 5% per trade
    public bool EnableCompounding { get; init; } = true; // Reinvest profits
    public decimal AggressivenessMultiplier { get; init; } = 5.0m; // Jim's 5x aggressiveness multiplier
    public int MinContractSize { get; init; } = 5; // Minimum 5 contracts
    public int MaxContractSize { get; init; } = 50; // Maximum 50 contracts
    public decimal CapitalAllocationPerTrade { get; init; } = 0.10m; // 10% capital per trade
}

public record TradingSession
{
    public DateTime Date { get; init; }
    public List<Position> Positions { get; init; } = new();
    public decimal DailyPremium { get; init; }
    public decimal WeeklyPremium { get; init; }
    public decimal MonthlyPremium { get; init; }
    public bool GoalsMet { get; init; }
    public int PositionsOpened { get; init; }
    public int PositionsClosed { get; init; }
    public decimal TotalPnL { get; init; }
}