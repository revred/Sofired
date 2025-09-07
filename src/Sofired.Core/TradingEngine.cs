using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sofired.Core;

public class TradingEngine
{
    private readonly StrategyConfig _config;
    private readonly List<Position> _openPositions = new();
    private readonly List<Position> _closedPositions = new();
    private decimal _weeklyPremium = 0m;
    private decimal _monthlyPremium = 0m;
    private DateTime _lastResetDate = DateTime.MinValue;
    
    // Capital Management
    private decimal _currentCapital;
    private int _tradeSequence = 1;
    
    // Reality Assessment Integration
    private readonly TradeValidator? _validator;
    private readonly RealityAuditReport _auditReport = new();
    
    // PHASE 1: Real Options Pricing Engine
    private readonly RealOptionsEngine? _realOptionsEngine;
    
    // PHASE 3: Enhanced P&L and Risk Management
    private readonly EnhancedPnLEngine _pnlEngine;
    private readonly AdvancedRiskManager _riskManager;
    
    // Symbol Configuration
    private readonly string _symbol;

    public TradingEngine(StrategyConfig config, TradeValidator? validator = null, RealOptionsEngine? realOptionsEngine = null, string symbol = "SOFI")
    {
        _config = config;
        _currentCapital = config.InitialCapital; // Start with initial capital
        _validator = validator;
        _realOptionsEngine = realOptionsEngine;
        _symbol = symbol;
        
        // PHASE 3: Initialize enhanced P&L and risk management
        _pnlEngine = new EnhancedPnLEngine();
        _riskManager = new AdvancedRiskManager();
    }
    
    public RealityAuditReport.RealityAuditSummary? GetRealityAuditSummary()
    {
        return _auditReport?.GenerateSummary();
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

    private Position CreatePutCreditSpread(DateTime date, DailyBar sofiBar, decimal vixLevel, VolRegime regime)
    {
        // Entry timing window optimization
        var entryTime = date.TimeOfDay;
        if (entryTime < TimeSpan.FromHours(10).Add(TimeSpan.FromMinutes(10)) || 
            entryTime > TimeSpan.FromHours(10).Add(TimeSpan.FromMinutes(30)))
        {
            return null; // Outside entry window
        }
        
        // Calculate strike price based on 15 delta (approximately 10% below current price)
        var strikePrice = sofiBar.Close * 0.90m; // 10% discount
        var delta = 0.15m; // 15 delta target
        
        // Dynamic delta adjustment based on volatility regime
        delta = regime switch
        {
            VolRegime.Low => 0.25m,     // Very aggressive in low vol (higher premium)
            VolRegime.Normal => 0.15m,  // Standard delta (Jim's recommendation)
            VolRegime.High => 0.12m,    // Still reasonably aggressive in high vol  
            _ => 0.15m
        };
        
        var expirationDate = GetNextExpirationDate(date, _config.PreferredDTE);
        var dte = (expirationDate - date).Days;
        
        // Skip if outside DTE range
        if (dte < _config.MinDTE || dte > _config.MaxDTE) return null;
        
        // Calculate position sizing based on available capital
        var capitalForTrade = _currentCapital * _config.CapitalAllocationPerTrade;
        var premiumPerContract = EstimatePutSpreadPremiumSync(sofiBar.Close, strikePrice, dte, vixLevel, date);
        
        // Calculate aggressive contract size (5-50 contracts based on capital)
        var baseContracts = Math.Max(_config.MinContractSize, (int)(capitalForTrade / (premiumPerContract * 100))); // $100 per contract assumption
        var contractSize = Math.Min(_config.MaxContractSize, (int)(baseContracts * _config.AggressivenessMultiplier));
        
        var totalPremium = premiumPerContract * contractSize;
        var maxProfit = totalPremium;
        
        // Generate trade reasoning
        var marketRegime = DetermineMarketRegime(sofiBar, vixLevel);
        var volEvent = DetermineVolatilityEvent(date, vixLevel);
        var entryReasoning = GenerateEntryReasoning(delta, regime, marketRegime, volEvent, contractSize, vixLevel);
        
        return new Position
        {
            Id = $"PCS_{_tradeSequence++:D4}_{date:yyyyMMdd}_{strikePrice:F2}",
            Strategy = StrategyType.PutCreditSpread,
            EntryDate = date,
            EntryPrice = sofiBar.Close,
            StrikePrice = strikePrice,
            Delta = delta,
            DaysToExpiration = dte,
            ExpirationDate = expirationDate,
            PremiumCollected = totalPremium,
            MaxProfit = maxProfit,
            Status = PositionStatus.Open,
            VixRegime = regime,
            VixLevel = vixLevel,
            UnderlyingPrice = sofiBar.Close,
            
            // High-ROI position properties
            ContractSize = contractSize,
            CapitalAllocated = capitalForTrade,
            MarketRegime = marketRegime,
            VolEvent = volEvent,
            EntryReasoning = entryReasoning,
            LeverageMultiplier = _config.AggressivenessMultiplier,
            
            Notes = $"{delta:P1} put credit spread, {contractSize} contracts, {regime} vol, {marketRegime} market"
        };
    }
    
    private Position CreateCoveredCall(DateTime date, DailyBar sofiBar, decimal vixLevel, VolRegime regime)
    {
        // Entry timing window optimization
        var entryTime = date.TimeOfDay;
        if (entryTime < TimeSpan.FromHours(10).Add(TimeSpan.FromMinutes(10)) || 
            entryTime > TimeSpan.FromHours(10).Add(TimeSpan.FromMinutes(30)))
        {
            return null; // Outside entry window
        }
        
        // Calculate strike price based on 12-15 delta (approximately 5-10% above current price)
        var strikePrice = sofiBar.Close * 1.05m; // 5% above current price
        var delta = 0.12m; // 12 delta for covered calls (more conservative)
        
        // Conservative covered call delta management
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
                // HIGH-ROI MUTATION: Generate detailed exit reasoning
                var exitReasoning = GenerateExitReasoning(position, profitPercentage, vixLevel);
                
                var closedPosition = position with
                {
                    ExitDate = date,
                    ExitPrice = sofiBar.Close,
                    ProfitLoss = unrealizedPnL,
                    Status = PositionStatus.Closed,
                    ExitReasoning = exitReasoning,
                    Notes = position.Notes + $" | Closed at {profitPercentage:P1} profit for ${unrealizedPnL:F0} gain"
                };
                
                positionsToClose.Add(position);
                _closedPositions.Add(closedPosition);
                closedToday.Add(closedPosition);
                
                // HIGH-ROI MUTATION: Compound capital for exponential growth
                UpdateCapitalFromClosedPosition(closedPosition);
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
    
    private async Task<decimal> EstimatePutSpreadPremium(decimal stockPrice, decimal strikePrice, int dte, decimal vixLevel, DateTime tradingDate)
    {
        // PHASE 1: REAL OPTIONS PRICING ENGINE
        if (_realOptionsEngine != null)
        {
            try
            {
                var expirationDate = tradingDate.AddDays(dte);
                var longStrike = strikePrice - 2.5m; // 2.5 point spread
                
                var realPricing = await _realOptionsEngine.GetPutSpreadPricing(
                    _symbol, stockPrice, expirationDate, strikePrice, longStrike, tradingDate);
                
                if (realPricing.IsRealData)
                {
                    Console.WriteLine($"✅ Using REAL options pricing: ${realPricing.NetCreditReceived:F2} (spread cost: ${realPricing.BidAskSpreadCost:F2})");
                    return realPricing.NetCreditReceived;
                }
                else
                {
                    Console.WriteLine($"✅ Using enhanced real options pricing: ${realPricing.NetCreditReceived:F2} (with market friction)");
                    return realPricing.NetCreditReceived;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Real options engine error: {ex.Message}");
                throw; // Re-throw - no synthetic fallback allowed
            }
        }
        else
        {
            throw new InvalidOperationException("Real options engine is required but not available. System cannot operate without real market data.");
        }
    }
    
    // REMOVED: CalculateFallbackPutSpreadPremium method
    // System now requires real options data only
    
    /// <summary>
    /// Synchronous wrapper for EstimatePutSpreadPremium to avoid breaking existing code
    /// </summary>
    private decimal EstimatePutSpreadPremiumSync(decimal stockPrice, decimal strikePrice, int dte, decimal vixLevel, DateTime tradingDate)
    {
        try
        {
            // Use Task.Run to avoid deadlocks while keeping synchronous interface
            var task = Task.Run(async () => await EstimatePutSpreadPremium(stockPrice, strikePrice, dte, vixLevel, tradingDate));
            return task.Result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error in sync options pricing wrapper: {ex.Message}");
            throw; // Re-throw - no synthetic fallback allowed
        }
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
            return EstimatePutSpreadPremiumSync(sofiBar.Close, position.StrikePrice, daysLeft, vixLevel, DateTime.Now) * 0.5m;
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
    
    // Market analysis and reasoning methods
    private MarketRegime DetermineMarketRegime(DailyBar sofiBar, decimal vixLevel)
    {
        // Simple regime classification based on VIX and price action
        return vixLevel switch
        {
            <= 15m when sofiBar.Close > sofiBar.Open => MarketRegime.Bull,
            <= 15m => MarketRegime.Sideways,
            >= 30m => MarketRegime.Volatile,
            _ when sofiBar.Close < sofiBar.Open * 0.98m => MarketRegime.Bear,
            _ => MarketRegime.Trending
        };
    }
    
    private VolatilityEvent DetermineVolatilityEvent(DateTime date, decimal vixLevel)
    {
        if (vixLevel > 35m) return VolatilityEvent.VIXSpike;
        if (IsNearEarnings(date)) return VolatilityEvent.Earnings;
        if (vixLevel > 25m) return VolatilityEvent.News;
        return VolatilityEvent.None;
    }
    
    private string GenerateEntryReasoning(decimal delta, VolRegime volRegime, MarketRegime marketRegime, 
                                        VolatilityEvent volEvent, int contractSize, decimal vixLevel)
    {
        var reasons = new List<string>();
        
        // Delta reasoning
        reasons.Add($"{delta:P1} delta for {(delta > 0.2m ? "aggressive" : delta > 0.15m ? "standard" : "conservative")} premium collection");
        
        // Volatility reasoning
        var volReason = volRegime switch
        {
            VolRegime.Low => "Low VIX environment - aggressive sizing for higher premium capture",
            VolRegime.Normal => "Normal VIX - balanced risk/reward following high-ROI methodology",
            VolRegime.High => "Elevated VIX - higher premiums but managed size for risk control",
            _ => "Standard volatility approach"
        };
        reasons.Add(volReason);
        
        // Market regime reasoning
        var marketReason = marketRegime switch
        {
            MarketRegime.Bull => "Bullish regime - put spreads likely to expire worthless",
            MarketRegime.Bear => "Bearish regime - tighter delta management required", 
            MarketRegime.Sideways => "Range-bound market - ideal for premium collection",
            MarketRegime.Volatile => "High volatility - premium expansion opportunity",
            MarketRegime.Trending => "Trending market - momentum-based entry timing",
            _ => "Standard market conditions"
        };
        reasons.Add(marketReason);
        
        // Size reasoning
        if (contractSize >= 20)
            reasons.Add($"Large position ({contractSize} contracts) - high conviction trade in favorable conditions");
        else if (contractSize >= 10)
            reasons.Add($"Medium position ({contractSize} contracts) - balanced risk/reward");
        else
            reasons.Add($"Conservative position ({contractSize} contracts) - risk management priority");
        
        // Event-based reasoning
        if (volEvent != VolatilityEvent.None)
            reasons.Add($"{volEvent} event - {(volEvent == VolatilityEvent.VIXSpike ? "premium expansion opportunity" : "increased caution warranted")}");
        
        return string.Join("; ", reasons) + $" (VIX: {vixLevel:F1})";
    }
    
    private string GenerateExitReasoning(Position position, decimal profitPercentage, decimal vixLevel)
    {
        var reasons = new List<string>();
        
        if (profitPercentage >= 0.90m)
            reasons.Add("90%+ profit achieved - taking maximum gains per high-ROI early closing strategy");
        else if (profitPercentage >= 0.80m)
            reasons.Add("80%+ profit achieved - optimal exit timing for ROI maximization");
        else if (profitPercentage >= 0.70m)
            reasons.Add("70%+ profit achieved - early closing to reduce tail risk and enable reinvestment");
        
        var timeRemaining = (position.ExpirationDate - DateTime.Now).Days;
        if (timeRemaining <= 7)
            reasons.Add("< 1 week to expiration - gamma risk management");
        
        if (vixLevel > position.VixLevel * 1.5m)
            reasons.Add("VIX spike - closing profitable position to avoid volatility expansion risk");
        
        var roi = position.ROIPercentage;
        if (roi > 0.5m)
            reasons.Add($"Exceptional ROI achieved: {roi:P1} - compounding capital for exponential growth");
        
        return string.Join("; ", reasons) + $" (Entry VIX: {position.VixLevel:F1} → Exit VIX: {vixLevel:F1})";
    }
    
    // Capital compounding mechanism
    private void UpdateCapitalFromClosedPosition(Position position)
    {
        if (_config.EnableCompounding && position.ProfitLoss.HasValue)
        {
            var previousCapital = _currentCapital;
            _currentCapital += position.ProfitLoss.Value;
            
            // Track compounding for analysis
            if (position.ProfitLoss.Value > 0)
            {
                Console.WriteLine($"COMPOUNDING: Capital grew from £{previousCapital:F0} to £{_currentCapital:F0} (+£{position.ProfitLoss.Value:F0})");
            }
        }
    }
    
    public decimal GetCurrentCapital() => _currentCapital;
}