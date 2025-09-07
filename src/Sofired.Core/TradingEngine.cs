using System;
using System.Collections.Generic;
using System.Linq;

namespace Sofired.Core;

public class TradingEngine
{
    private readonly StrategyConfig _config;
    private readonly List<Position> _openPositions = new();
    private readonly List<Position> _closedPositions = new();
    private decimal _weeklyPremium = 0m;
    private decimal _monthlyPremium = 0m;
    private DateTime _lastResetDate = DateTime.MinValue;

    public TradingEngine(StrategyConfig config)
    {
        _config = config;
    }

    public TradingSession ProcessTradingDay(DateTime date, DailyBar sofiBar, decimal vixLevel)
    {
        ResetGoalsIfNeeded(date);
        
        var regime = DetermineVolRegime(vixLevel);
        var session = new TradingSession { Date = date };
        
        // Daily routine: Check all positions first
        var closedToday = ReviewAndClosePositions(date, sofiBar, vixLevel);
        var openedToday = new List<Position>();
        
        // Only open new positions if goals not met and market conditions are favorable
        if (!GoalsMet() && ShouldTradeToday(date, sofiBar, regime))
        {
            // Primary strategy: Put Credit Spreads (70% allocation)
            var putSpread = CreatePutCreditSpread(date, sofiBar, vixLevel, regime);
            if (putSpread != null)
            {
                _openPositions.Add(putSpread);
                openedToday.Add(putSpread);
                _weeklyPremium += putSpread.PremiumCollected;
                _monthlyPremium += putSpread.PremiumCollected;
            }
            
            // Secondary strategy: Covered Calls (30% allocation)
            var coveredCall = CreateCoveredCall(date, sofiBar, vixLevel, regime);
            if (coveredCall != null)
            {
                _openPositions.Add(coveredCall);
                openedToday.Add(coveredCall);
                _weeklyPremium += coveredCall.PremiumCollected;
                _monthlyPremium += coveredCall.PremiumCollected;
            }
        }

        return new TradingSession
        {
            Date = date,
            Positions = new List<Position>(_openPositions),
            DailyPremium = openedToday.Sum(p => p.PremiumCollected),
            WeeklyPremium = _weeklyPremium,
            MonthlyPremium = _monthlyPremium,
            GoalsMet = GoalsMet(),
            PositionsOpened = openedToday.Count,
            PositionsClosed = closedToday.Count,
            TotalPnL = _closedPositions.Sum(p => p.ProfitLoss ?? 0)
        };
    }

    private Position? CreatePutCreditSpread(DateTime date, DailyBar sofiBar, decimal vixLevel, VolRegime regime)
    {
        // TWEAK 1: Only enter positions during 10:10-10:30 AM window
        var entryTime = date.TimeOfDay;
        if (entryTime < TimeSpan.FromHours(10).Add(TimeSpan.FromMinutes(10)) || 
            entryTime > TimeSpan.FromHours(10).Add(TimeSpan.FromMinutes(30)))
        {
            return null; // Outside entry window
        }
        
        // Calculate strike price based on 15 delta (approximately 10% below current price)
        var strikePrice = sofiBar.Close * 0.90m; // 10% discount
        var delta = 0.15m; // 15 delta target
        
        // TWEAK 3: Even more conservative deltas in high volatility
        delta = regime switch
        {
            VolRegime.Low => 0.20m,     // More aggressive in low vol
            VolRegime.Normal => 0.15m,  // Standard delta
            VolRegime.High => 0.08m,    // Very conservative in high vol  
            _ => 0.15m
        };
        
        var expirationDate = GetNextExpirationDate(date, _config.PreferredDTE);
        var dte = (expirationDate - date).Days;
        
        // Skip if outside DTE range
        if (dte < _config.MinDTE || dte > _config.MaxDTE) return null;
        
        // Premium estimation (simplified Black-Scholes approximation)
        var premium = EstimatePutSpreadPremium(sofiBar.Close, strikePrice, dte, vixLevel);
        var maxProfit = premium;
        
        return new Position
        {
            Id = $"PCS_{date:yyyyMMdd}_{strikePrice:F2}",
            Strategy = StrategyType.PutCreditSpread,
            EntryDate = date,
            EntryPrice = sofiBar.Close,
            StrikePrice = strikePrice,
            Delta = delta,
            DaysToExpiration = dte,
            ExpirationDate = expirationDate,
            PremiumCollected = premium,
            MaxProfit = maxProfit,
            Status = PositionStatus.Open,
            VixRegime = regime,
            VixLevel = vixLevel,
            UnderlyingPrice = sofiBar.Close,
            Notes = $"15Δ put credit spread, {regime} vol regime"
        };
    }
    
    private Position? CreateCoveredCall(DateTime date, DailyBar sofiBar, decimal vixLevel, VolRegime regime)
    {
        // TWEAK 1: Only enter positions during 10:10-10:30 AM window
        var entryTime = date.TimeOfDay;
        if (entryTime < TimeSpan.FromHours(10).Add(TimeSpan.FromMinutes(10)) || 
            entryTime > TimeSpan.FromHours(10).Add(TimeSpan.FromMinutes(30)))
        {
            return null; // Outside entry window
        }
        
        // Calculate strike price based on 12-15 delta (approximately 5-10% above current price)
        var strikePrice = sofiBar.Close * 1.05m; // 5% above current price
        var delta = 0.12m; // 12 delta for covered calls (more conservative)
        
        // TWEAK 3: Even more conservative covered call deltas in high volatility
        delta = regime switch
        {
            VolRegime.Low => 0.15m,     // More aggressive in low vol
            VolRegime.Normal => 0.12m,  // Standard delta
            VolRegime.High => 0.06m,    // Very conservative in high vol  
            _ => 0.12m
        };
        
        // Even smaller positions pre-earnings
        if (IsNearEarnings(date)) delta *= 0.5m;
        
        var expirationDate = GetNextExpirationDate(date, _config.PreferredDTE);
        var dte = (expirationDate - date).Days;
        
        // Skip if outside DTE range
        if (dte < _config.MinDTE || dte > _config.MaxDTE) return null;
        
        // Premium estimation
        var premium = EstimateCoveredCallPremium(sofiBar.Close, strikePrice, dte, vixLevel);
        var maxProfit = premium; // Max profit is premium collected
        
        return new Position
        {
            Id = $"CC_{date:yyyyMMdd}_{strikePrice:F2}",
            Strategy = StrategyType.CoveredCall,
            EntryDate = date,
            EntryPrice = sofiBar.Close,
            StrikePrice = strikePrice,
            Delta = delta,
            DaysToExpiration = dte,
            ExpirationDate = expirationDate,
            PremiumCollected = premium,
            MaxProfit = maxProfit,
            Status = PositionStatus.Open,
            VixRegime = regime,
            VixLevel = vixLevel,
            UnderlyingPrice = sofiBar.Close,
            Notes = $"12Δ covered call, {regime} vol regime"
        };
    }

    private List<Position> ReviewAndClosePositions(DateTime date, DailyBar sofiBar, decimal vixLevel)
    {
        var closedToday = new List<Position>();
        var positionsToClose = new List<Position>();
        
        // TWEAK 1: Only exit positions during 15:20-15:35 PM window (unless expiring)
        var exitTime = date.TimeOfDay;
        var inExitWindow = exitTime >= TimeSpan.FromHours(15).Add(TimeSpan.FromMinutes(20)) && 
                          exitTime <= TimeSpan.FromHours(15).Add(TimeSpan.FromMinutes(35));
        
        foreach (var position in _openPositions.ToList())
        {
            var currentValue = EstimateCurrentPositionValue(position, sofiBar, vixLevel);
            var unrealizedPnL = position.PremiumCollected - currentValue;
            var profitPercentage = position.MaxProfit > 0 ? unrealizedPnL / position.MaxProfit : 0;
            
            // Always close on expiration day regardless of timing
            var isExpiringToday = position.ExpirationDate.Date == date.Date;
            
            // Early closing logic: 70-90% profit target (only in exit window or expiring)
            if (profitPercentage >= _config.EarlyCloseThreshold && (inExitWindow || isExpiringToday))
            {
                var closedPosition = position with
                {
                    ExitDate = date,
                    ExitPrice = sofiBar.Close,
                    ProfitLoss = unrealizedPnL,
                    Status = PositionStatus.Closed,
                    Notes = position.Notes + $" | Closed at {profitPercentage:P1} profit"
                };
                
                positionsToClose.Add(position);
                _closedPositions.Add(closedPosition);
                closedToday.Add(closedPosition);
            }
            // Don't let profits slip away - close at 95% (only in exit window or expiring)
            else if (profitPercentage >= _config.MaxCloseThreshold && (inExitWindow || isExpiringToday))
            {
                var closedPosition = position with
                {
                    ExitDate = date,
                    ExitPrice = sofiBar.Close,
                    ProfitLoss = unrealizedPnL,
                    Status = PositionStatus.Closed,
                    Notes = position.Notes + $" | Max threshold close at {profitPercentage:P1}"
                };
                
                positionsToClose.Add(position);
                _closedPositions.Add(closedPosition);
                closedToday.Add(closedPosition);
            }
            // Close at expiration
            else if (date >= position.ExpirationDate)
            {
                var finalPnL = CalculateFinalPnL(position, sofiBar.Close);
                var closedPosition = position with
                {
                    ExitDate = date,
                    ExitPrice = sofiBar.Close,
                    ProfitLoss = finalPnL,
                    Status = position.Strategy == StrategyType.CoveredCall && sofiBar.Close > position.StrikePrice 
                        ? PositionStatus.Assigned : PositionStatus.Closed,
                    Notes = position.Notes + " | Expired"
                };
                
                positionsToClose.Add(position);
                _closedPositions.Add(closedPosition);
                closedToday.Add(closedPosition);
            }
        }
        
        // Remove closed positions from open positions
        foreach (var pos in positionsToClose)
        {
            _openPositions.Remove(pos);
        }
        
        return closedToday;
    }
    
    private decimal EstimatePutSpreadPremium(decimal stockPrice, decimal strikePrice, int dte, decimal vixLevel)
    {
        // More realistic premium estimation based on ThetaData market observations
        var timeValue = (decimal)Math.Sqrt(dte / 365.0) * (vixLevel / 100m);
        var moneyness = (stockPrice - strikePrice) / stockPrice;
        
        // Base premium calculation - more realistic for 15 delta puts
        var basePremium = stockPrice * timeValue * moneyness * 0.15m; // Increased multiplier
        
        // Apply realistic minimum based on market conditions
        var minPremium = stockPrice switch
        {
            <= 10m => 0.75m,   // Lower price stocks
            <= 15m => 1.25m,   // Mid-range 
            <= 25m => 2.00m,   // Current SOFI range
            <= 50m => 3.50m,   // Higher priced stocks
            _ => 5.00m          // Very high priced
        };
        
        // Standard VIX scaling - higher VIX = higher premiums
        var volMultiplier = vixLevel switch
        {
            <= 15m => 0.8m,    // Low vol environment
            <= 25m => 1.0m,    // Normal vol
            <= 35m => 1.4m,    // High vol
            _ => 1.8m           // Very high vol
        };
        
        return Math.Max(minPremium * volMultiplier, basePremium);
    }
    
    private decimal EstimateCoveredCallPremium(decimal stockPrice, decimal strikePrice, int dte, decimal vixLevel)
    {
        // More realistic covered call premium estimation
        var timeValue = (decimal)Math.Sqrt(dte / 365.0) * (vixLevel / 100m);
        var moneyness = Math.Max(0, (strikePrice - stockPrice) / stockPrice);
        
        // Base premium for 12 delta calls - more realistic
        var basePremium = stockPrice * timeValue * (0.5m + moneyness) * 0.08m; // Increased multiplier
        
        // Apply realistic minimum based on stock price
        var minPremium = stockPrice switch
        {
            <= 10m => 0.35m,   // Lower price stocks
            <= 15m => 0.65m,   // Mid-range
            <= 25m => 1.00m,   // Current SOFI range
            <= 50m => 1.75m,   // Higher priced stocks
            _ => 2.50m          // Very high priced
        };
        
        // Standard VIX scaling for covered calls
        var volMultiplier = vixLevel switch
        {
            <= 15m => 0.7m,    // Low vol environment
            <= 25m => 1.0m,    // Normal vol
            <= 35m => 1.3m,    // High vol
            _ => 1.6m           // Very high vol
        };
        
        return Math.Max(minPremium * volMultiplier, basePremium);
    }
    
    private decimal EstimateCurrentPositionValue(Position position, DailyBar sofiBar, decimal vixLevel)
    {
        var daysLeft = (position.ExpirationDate - DateTime.Now).Days;
        if (daysLeft <= 0) return 0;
        
        if (position.Strategy == StrategyType.PutCreditSpread)
        {
            return EstimatePutSpreadPremium(sofiBar.Close, position.StrikePrice, daysLeft, vixLevel) * 0.5m;
        }
        else
        {
            return EstimateCoveredCallPremium(sofiBar.Close, position.StrikePrice, daysLeft, vixLevel) * 0.5m;
        }
    }
    
    private decimal CalculateFinalPnL(Position position, decimal finalPrice)
    {
        if (position.Strategy == StrategyType.PutCreditSpread)
        {
            // Put spread: profit = premium collected if stock above strike
            return finalPrice >= position.StrikePrice ? position.PremiumCollected : 
                   position.PremiumCollected - Math.Max(0, position.StrikePrice - finalPrice);
        }
        else // Covered Call
        {
            // Covered call: profit = premium + min(0, strike - final price)
            return position.PremiumCollected + Math.Min(0, position.StrikePrice - finalPrice);
        }
    }
    
    private VolRegime DetermineVolRegime(decimal vixLevel)
    {
        return vixLevel switch
        {
            < 15m => VolRegime.Low,
            > 25m => VolRegime.High,
            _ => VolRegime.Normal
        };
    }
    
    private DateTime GetNextExpirationDate(DateTime currentDate, int preferredDTE)
    {
        // Find next monthly expiration (3rd Friday of month)
        var targetDate = currentDate.AddDays(preferredDTE);
        var year = targetDate.Year;
        var month = targetDate.Month;
        
        // Third Friday calculation
        var firstDay = new DateTime(year, month, 1);
        var firstFriday = firstDay.AddDays((5 - (int)firstDay.DayOfWeek + 7) % 7);
        var thirdFriday = firstFriday.AddDays(14);
        
        return thirdFriday;
    }
    
    private bool IsNearEarnings(DateTime date)
    {
        // Simplified: assume earnings every quarter, around 15th of Jan, Apr, Jul, Oct
        var month = date.Month;
        var day = date.Day;
        var isEarningsMonth = month % 3 == 1; // Jan, Apr, Jul, Oct approximation
        var isEarningsWindow = day >= 10 && day <= 20;
        
        return isEarningsMonth && isEarningsWindow;
    }
    
    private bool ShouldTradeToday(DateTime date, DailyBar sofiBar, VolRegime regime)
    {
        // Entry window: 10:10-10:30 (simplified to any weekday)
        if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            return false;
            
        // Don't trade in extreme volatility
        if (regime == VolRegime.High && _openPositions.Count > 3)
            return false;
            
        return true;
    }
    
    private bool GoalsMet()
    {
        return _weeklyPremium >= _config.WeeklyPremiumGoal;
    }
    
    private void ResetGoalsIfNeeded(DateTime currentDate)
    {
        if (_lastResetDate == DateTime.MinValue)
        {
            _lastResetDate = currentDate;
            return;
        }
        
        // Reset weekly goal every Monday
        if (currentDate.DayOfWeek == DayOfWeek.Monday && _lastResetDate.DayOfWeek != DayOfWeek.Monday)
        {
            _weeklyPremium = 0m;
        }
        
        // Reset monthly goal on 1st of month
        if (currentDate.Day == 1 && _lastResetDate.Day != 1)
        {
            _monthlyPremium = 0m;
        }
        
        _lastResetDate = currentDate;
    }
    
    public List<Position> GetAllPositions() => _openPositions.Concat(_closedPositions).ToList();
    public List<Position> GetOpenPositions() => new(_openPositions);
    public List<Position> GetClosedPositions() => new(_closedPositions);
    public decimal GetTotalPnL() => _closedPositions.Sum(p => p.ProfitLoss ?? 0);
}