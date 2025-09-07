using System;
using System.Collections.Generic;

namespace Sofired.Core;

public record DailyBar(System.DateTime Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

public enum VolRegime { Low, Normal, High }

public enum StrategyType { PutCreditSpread, CoveredCall, IronCondor }

public enum PositionStatus { Open, Closed, Rolled, Assigned }

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
    public decimal ProfitPercentage => MaxProfit > 0 ? (ProfitLoss ?? 0) / MaxProfit : 0;
    public PositionStatus Status { get; init; }
    public VolRegime VixRegime { get; init; }
    public decimal VixLevel { get; init; }
    public decimal UnderlyingPrice { get; init; }
    public string Notes { get; init; } = "";
}

public record StrategyConfig
{
    public int PreferredDTE { get; init; } = 45;
    public int MinDTE { get; init; } = 30;
    public int MaxDTE { get; init; } = 70;
    public decimal TargetDelta { get; init; } = 0.15m;
    public decimal EarlyCloseThreshold { get; init; } = 0.70m;
    public decimal OptimalCloseThreshold { get; init; } = 0.80m;
    public decimal MaxCloseThreshold { get; init; } = 0.95m;
    public bool UseDelayedRolling { get; init; } = true;
    public decimal WeeklyPremiumGoal { get; init; } = 2000m; // £2000
    public decimal MonthlyPremiumGoal { get; init; } = 8000m; // £8000
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