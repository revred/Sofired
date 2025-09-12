# SOFIRED ARCHITECTURE PRINCIPLES

## Core Principle: Separation of Concerns

**SOFIRED's sole responsibility is trading logic and strategy execution.**

## What SOFIRED Does

- Execute trading strategies (put credit spreads, covered calls)
- Manage risk and position sizing
- Track performance metrics
- Generate backtest results
- Process trading signals

## What SOFIRED Does NOT Do

**SOFIRED must NEVER:**
- Scout for data sources
- Directly connect to third-party data providers (ThetaData, Yahoo Finance, etc.)
- Implement data fetching logic
- Handle data provider authentication
- Manage data provider connections

## Data Access Architecture

All market data MUST flow through the MCP service abstraction:

```
[SOFIRED] --> [IMarketDataService] --> [Stroll.Theta.Market MCP] --> [Data Providers]
```

**SOFIRED only knows about IMarketDataService interface.**

## Enforcement

1. No direct HTTP calls to data providers
2. No data provider SDKs in SOFIRED dependencies
3. All data requests through IMarketDataService abstraction
4. Fail fast when data unavailable - no synthetic fallbacks

## Rationale

- **Single Responsibility**: SOFIRED focuses on trading, not data acquisition
- **Flexibility**: Data sources can change without touching SOFIRED code
- **Testability**: Easy to mock IMarketDataService for testing
- **Security**: Data provider credentials stay in MCP service layer

---

**Remember: SOFIRED executes trades. It does not scout for data.**