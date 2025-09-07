# Risk Management Framework

## Overview

The SOFIRED system implements a comprehensive multi-layered risk management framework designed to protect capital while maximizing risk-adjusted returns in options trading.

## Risk Management Layers

### 1. Pre-Trade Risk Validation

Every trade must pass through rigorous pre-trade validation before execution:

```csharp
public async Task<TradeRiskValidation> ValidateTradeRisk(
    string symbol,
    decimal stockPrice,
    decimal shortStrike,
    decimal longStrike,
    decimal accountValue,
    SymbolConfig symbolConfig)
```

**Validation Checks:**
- Strike price validity (short > long for put spreads)
- Assignment risk assessment (stock price vs strike prices)
- Account value sufficiency
- Position sizing within configured limits
- Maximum loss per trade compliance
- Moneyness validation (ITM risk assessment)
- Days to expiration warnings
- Market hours verification
- Volatility environment alerts

### 2. Position Sizing Framework

#### Kelly Criterion Implementation
The system uses the Kelly Criterion for optimal position sizing:

```
Kelly = (bp - q) / b
where:
- b = average win / average loss
- p = win rate
- q = loss rate (1 - p)
```

**Enhancements:**
- Capped at 25% maximum allocation per trade
- VIX-based adjustments for volatility regimes
- Sector correlation adjustments
- Account value scaling

#### Risk-Based Allocation
```yaml
risk:
  capital_allocation: 0.15    # 15% base allocation
  max_position_size: 0.25     # 25% maximum single position
  max_loss_per_trade: 0.05    # 5% maximum loss per trade
```

### 3. Real-Time Risk Monitoring

#### Greeks Exposure Tracking
- **Delta**: Directional exposure monitoring
- **Gamma**: Acceleration risk in volatile markets
- **Theta**: Time decay benefits and risks
- **Vega**: Volatility sensitivity tracking

#### Portfolio-Level Metrics
```csharp
public class PortfolioRiskMetrics
{
    public decimal TotalDelta { get; set; }
    public decimal TotalGamma { get; set; }
    public decimal TotalTheta { get; set; }
    public decimal TotalVega { get; set; }
    public int PositionCount { get; set; }
}
```

### 4. Position Management Rules

#### Profit Taking Strategy
- **Target**: 50% of maximum profit
- **Rationale**: Optimal risk-adjusted returns based on historical analysis
- **Implementation**: Automated monitoring with alert system

#### Stop Loss Framework
- **Trigger**: 200% of credit received
- **Logic**: Limits losses to 2x initial credit while allowing for temporary adverse moves
- **Execution**: Automated market orders for immediate execution

#### Expiration Management
- **7 DTE Rule**: Close all positions within 7 days of expiration
- **Assignment Risk**: Enhanced monitoring for ITM positions
- **Rolling Strategy**: Evaluate rolling vs closing based on P&L

### 5. Market Regime Adaptation

#### VIX-Based Adjustments
```csharp
private decimal CalculateVixAdjustment(decimal currentVix, SymbolConfig config)
{
    return currentVix switch
    {
        <= config.Market.VixLow => 1.2m,      // Increase size in low vol
        >= config.Market.VixHigh => 0.6m,     // Reduce size in high vol
        >= config.Market.VixCrisis => 0.3m,   // Minimal size in crisis
        _ => 1.0m                              // Normal sizing
    };
}
```

**VIX Thresholds:**
- **Low VIX**: <15 (Increase position sizing)
- **Normal VIX**: 15-25 (Standard sizing)
- **High VIX**: 25-35 (Reduce sizing)
- **Crisis VIX**: >35 (Minimal sizing)

#### Sector Correlation Management
- Maximum 40% allocation to any single sector
- Cross-sector diversification requirements
- Correlation-adjusted position sizing

### 6. Emergency Risk Controls

#### Emergency Stop System
```csharp
public async Task<EmergencyStopResult> EmergencyStop(string reason)
{
    // Stop accepting new orders
    _isMarketOpen = false;
    
    // Close all open positions
    foreach (var position in _livePositions.ToList())
    {
        await ClosePosition(position.Key, $"Emergency stop: {reason}");
    }
}
```

**Trigger Conditions:**
- Manual emergency stop command
- System-wide risk limit breach
- Market circuit breaker activation
- API connectivity issues
- Unusual market volatility

#### Circuit Breakers
- **Daily Loss Limit**: 10% of account value
- **Portfolio Delta Limit**: ±500 delta exposure
- **Concentration Limit**: 50% in single symbol
- **Margin Requirement**: 80% of available margin

### 7. Risk Reporting and Monitoring

#### Daily Risk Report
- Current portfolio exposure
- Greeks breakdown by position
- Risk limit utilization
- P&L attribution
- Volatility regime assessment

#### Risk Alerts
- Real-time notifications for limit breaches
- Position management recommendations
- Market regime changes
- Unusual volatility events

#### Historical Risk Analysis
- Value at Risk (VaR) calculations
- Maximum drawdown tracking
- Risk-adjusted performance metrics
- Stress testing results

## Risk Configuration Examples

### Conservative Profile
```yaml
risk:
  capital_allocation: 0.10
  max_position_size: 0.15
  max_loss_per_trade: 0.03
  vix_adjustment_multiplier: 0.8
```

### Aggressive Profile
```yaml
risk:
  capital_allocation: 0.20
  max_position_size: 0.35
  max_loss_per_trade: 0.08
  vix_adjustment_multiplier: 1.2
```

### Balanced Profile (Default)
```yaml
risk:
  capital_allocation: 0.15
  max_position_size: 0.25
  max_loss_per_trade: 0.05
  vix_adjustment_multiplier: 1.0
```

## Implementation Best Practices

1. **Never Override Risk Controls**: All risk limits are hard-coded and cannot be bypassed
2. **Regular Backtesting**: Validate risk parameters with historical data
3. **Stress Testing**: Test system behavior under extreme market conditions
4. **Documentation**: Maintain detailed logs of all risk decisions
5. **Regular Review**: Monthly review of risk parameters and performance
6. **Continuous Monitoring**: 24/7 monitoring of portfolio risk metrics

## Risk Metrics Definitions

### Value at Risk (VaR)
- **95% VaR**: Maximum expected loss over 1 day with 95% confidence
- **99% VaR**: Maximum expected loss over 1 day with 99% confidence
- **Calculation**: Historical simulation method with 252-day window

### Maximum Drawdown
- Peak-to-trough decline in portfolio value
- Measured both in absolute dollars and percentage terms
- Rolling 30/60/90 day calculations

### Sharpe Ratio
- Risk-adjusted return measure: (Return - Risk-Free Rate) / Volatility
- Calculated daily, monthly, and annually
- Benchmark comparison against S&P 500

### Greeks Exposure Limits
- **Delta Limit**: ±500 per $50k account value
- **Gamma Limit**: ±50 per $50k account value
- **Vega Limit**: ±1000 per $50k account value
- **Theta Target**: Positive theta exposure preferred

## Compliance and Audit Trail

### Regulatory Compliance
- All trades logged with risk justification
- Position sizing documentation
- Risk limit breach reporting
- Performance attribution records

### Audit Requirements
- Daily risk reports archived for 7 years
- Real-time risk decision logging
- Exception handling documentation
- System performance metrics

---

*This risk management framework is designed to protect capital while enabling consistent profitable trading. All risk controls are implemented as hard limits and cannot be overridden during live trading.*