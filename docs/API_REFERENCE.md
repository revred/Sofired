# SOFIRED API Reference

## Overview

This document provides a comprehensive reference for the SOFIRED API, including all classes, methods, and interfaces available for options trading system integration.

## Core Classes

### RealOptionsEngine

Primary engine for options pricing and market data integration.

```csharp
namespace Sofired.Core
{
    public class RealOptionsEngine
}
```

#### Methods

##### GetPutSpreadPricing
```csharp
public async Task<RealOptionsPricing> GetPutSpreadPricing(
    string symbol, 
    decimal stockPrice, 
    DateTime expirationDate,
    decimal shortStrike, 
    decimal longStrike, 
    DateTime tradingDate)
```

**Parameters:**
- `symbol`: Trading symbol (e.g., "SOFI", "AAPL")
- `stockPrice`: Current underlying price
- `expirationDate`: Options expiration date
- `shortStrike`: Short put strike price (higher)
- `longStrike`: Long put strike price (lower)
- `tradingDate`: Current trading date

**Returns:** `RealOptionsPricing` object with pricing details

**Example:**
```csharp
var engine = new RealOptionsEngine(thetaDataClient);
var pricing = await engine.GetPutSpreadPricing(
    "SOFI", 15.0m, DateTime.Parse("2024-01-19"), 14.0m, 13.0m, DateTime.Now);
```

##### CalculateSectorVolatility
```csharp
public decimal CalculateSectorVolatility(string symbol, decimal currentPrice)
```

**Parameters:**
- `symbol`: Trading symbol
- `currentPrice`: Current stock price

**Returns:** Sector-specific volatility estimate

### LiveTradingEngine

Real-time trading execution and position management.

```csharp
namespace Sofired.Core
{
    public class LiveTradingEngine
}
```

#### Constructor
```csharp
public LiveTradingEngine(
    IBrokerClient brokerClient, 
    RealOptionsEngine optionsEngine,
    AdvancedRiskManager riskManager,
    EnhancedPnLEngine pnlEngine,
    bool paperTradingMode = true)
```

#### Methods

##### StartTradingSession
```csharp
public async Task<LiveTradingSession> StartTradingSession(
    List<string> symbols, 
    decimal accountValue,
    Dictionary<string, SymbolConfig> symbolConfigs)
```

**Parameters:**
- `symbols`: List of symbols to trade
- `accountValue`: Initial account value
- `symbolConfigs`: Configuration for each symbol

**Returns:** `LiveTradingSession` object

##### ExecutePutCreditSpread
```csharp
public async Task<TradeExecutionResult> ExecutePutCreditSpread(
    string symbol,
    decimal stockPrice,
    DateTime expirationDate,
    decimal shortStrike,
    decimal longStrike,
    SymbolConfig symbolConfig,
    decimal accountValue)
```

**Returns:** `TradeExecutionResult` with execution details

##### ManagePositions
```csharp
public async Task<PositionManagementResult> ManagePositions(decimal currentVix)
```

**Parameters:**
- `currentVix`: Current VIX level

**Returns:** Position management actions and portfolio metrics

##### EmergencyStop
```csharp
public async Task<EmergencyStopResult> EmergencyStop(string reason)
```

**Parameters:**
- `reason`: Reason for emergency stop

**Returns:** Results of emergency stop operation

### AdvancedRiskManager

Sophisticated risk management and position sizing.

```csharp
namespace Sofired.Core
{
    public class AdvancedRiskManager
}
```

#### Methods

##### CalculateOptimalPositionSize
```csharp
public PositionSizeRecommendation CalculateOptimalPositionSize(
    string symbol, 
    decimal accountValue, 
    decimal currentVix,
    PortfolioPnL portfolioPnL,
    SymbolConfig symbolConfig)
```

**Parameters:**
- `symbol`: Trading symbol
- `accountValue`: Current account value
- `currentVix`: Current VIX level
- `portfolioPnL`: Current portfolio P&L
- `symbolConfig`: Symbol-specific configuration

**Returns:** `PositionSizeRecommendation` with sizing details

##### ValidateTradeRisk
```csharp
public async Task<TradeRiskValidation> ValidateTradeRisk(
    string symbol,
    decimal stockPrice,
    decimal shortStrike,
    decimal longStrike,
    decimal accountValue,
    SymbolConfig symbolConfig)
```

**Returns:** `TradeRiskValidation` with approval status and warnings

### EnhancedPnLEngine

Comprehensive P&L tracking with Greeks calculations.

```csharp
namespace Sofired.Core
{
    public class EnhancedPnLEngine
}
```

#### Methods

##### CalculatePositionPnL
```csharp
public PositionPnL CalculatePositionPnL(
    Position position, 
    decimal currentUnderlyingPrice, 
    decimal currentVix, 
    DateTime currentDate)
```

**Parameters:**
- `position`: Position to calculate P&L for
- `currentUnderlyingPrice`: Current underlying price
- `currentVix`: Current VIX level
- `currentDate`: Current date

**Returns:** `PositionPnL` with detailed P&L breakdown

##### CalculatePortfolioPnL
```csharp
public PortfolioPnL CalculatePortfolioPnL(
    List<Position> positions, 
    Dictionary<string, decimal> currentPrices, 
    decimal currentVix, 
    DateTime currentDate)
```

**Returns:** `PortfolioPnL` with portfolio-level metrics

### ConfigurationManager

YAML-based configuration management for trading symbols.

```csharp
namespace Sofired.Core
{
    public class ConfigurationManager
}
```

#### Methods

##### LoadSymbolConfig
```csharp
public SymbolConfig LoadSymbolConfig(string symbol)
```

**Parameters:**
- `symbol`: Symbol to load configuration for

**Returns:** `SymbolConfig` object with all settings

##### CompareSymbolConfigs
```csharp
public void CompareSymbolConfigs(string symbol1, string symbol2)
```

**Parameters:**
- `symbol1`: First symbol for comparison
- `symbol2`: Second symbol for comparison

**Effects:** Prints configuration differences to console

## Data Models

### RealOptionsPricing
```csharp
public class RealOptionsPricing
{
    public decimal NetCreditReceived { get; set; }
    public decimal MaxRisk { get; set; }
    public decimal MaxProfit { get; set; }
    public decimal BidAskSpreadCost { get; set; }
    public decimal ImpliedVolatility { get; set; }
    public decimal Delta { get; set; }
    public decimal Theta { get; set; }
    public decimal Gamma { get; set; }
    public bool IsRealData { get; set; }
}
```

### TradeExecutionResult
```csharp
public class TradeExecutionResult
{
    public bool Success { get; set; }
    public string OrderId { get; set; }
    public string? Symbol { get; set; }
    public decimal? ShortStrike { get; set; }
    public decimal? LongStrike { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public decimal FillPrice { get; set; }
    public int FillQuantity { get; set; }
    public DateTime FillTime { get; set; }
    public decimal Commission { get; set; }
    public bool IsPaperTrade { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> RiskWarnings { get; set; }
}
```

### PositionPnL
```csharp
public class PositionPnL
{
    public string PositionId { get; set; }
    public string Symbol { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public decimal RealizedPnL { get; set; }
    public decimal Delta { get; set; }
    public decimal Gamma { get; set; }
    public decimal Theta { get; set; }
    public decimal Vega { get; set; }
    public decimal ImpliedVolatility { get; set; }
    public decimal TimeValue { get; set; }
    public decimal IntrinsicValue { get; set; }
    public int DaysToExpiration { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal VaR95 { get; set; }
    public decimal VaR99 { get; set; }
}
```

### SymbolConfig
```csharp
public class SymbolConfig
{
    public string Symbol { get; set; }
    public AccountConfig Account { get; set; }
    public TradingConfig Trading { get; set; }
    public RiskConfig Risk { get; set; }
    public MarketConfig Market { get; set; }
    public SymbolStrategyConfig Strategy { get; set; }
    public CompanyConfig Company { get; set; }
    public DataConfig Data { get; set; }
    public BacktestConfig Backtest { get; set; }
}
```

## Interfaces

### IBrokerClient
```csharp
public interface IBrokerClient
{
    Task<TradeExecutionResult> SubmitOrder(TradeOrder order);
    Task<decimal> GetCurrentPrice(string symbol);
    Task<AccountInfo> GetAccountInfo();
    Task<List<BrokerPosition>> GetPositions();
    Task<OptionsChain> GetOptionsChain(string symbol, DateTime expirationDate);
    Task<bool> CancelOrder(string orderId);
    Task<bool> IsMarketOpen();
}
```

## Enumerations

### PositionStatus
```csharp
public enum PositionStatus 
{ 
    Open, 
    Closed, 
    Rolled, 
    Assigned, 
    Expired 
}
```

### OrderStatus
```csharp
public enum OrderStatus
{
    Pending,
    Filled,
    PartiallyFilled,
    Cancelled,
    Rejected
}
```

### OrderType
```csharp
public enum OrderType
{
    MarketOrder,
    LimitOrder,
    StopOrder
}
```

### TradingSessionStatus
```csharp
public enum TradingSessionStatus
{
    Initializing,
    Active,
    Paused,
    Stopped,
    Error
}
```

## Usage Examples

### Basic Trading Session Setup
```csharp
// Initialize components
var thetaClient = new ThetaDataClient("http://localhost", "25510");
var optionsEngine = new RealOptionsEngine(thetaClient);
var riskManager = new AdvancedRiskManager();
var pnlEngine = new EnhancedPnLEngine();
var brokerClient = new TDAmeritradeBrokerClient("api_key", "account_id", true);

// Create trading engine
var tradingEngine = new LiveTradingEngine(
    brokerClient, optionsEngine, riskManager, pnlEngine, true);

// Load configurations
var configManager = new ConfigurationManager();
var symbols = new List<string> { "SOFI", "AAPL", "NVDA" };
var configs = symbols.ToDictionary(
    s => s, 
    s => configManager.LoadSymbolConfig(s));

// Start trading session
var session = await tradingEngine.StartTradingSession(
    symbols, 50000m, configs);
```

### Execute Options Trade
```csharp
var result = await tradingEngine.ExecutePutCreditSpread(
    symbol: "SOFI",
    stockPrice: 15.0m,
    expirationDate: DateTime.Parse("2024-01-19"),
    shortStrike: 14.0m,
    longStrike: 13.0m,
    symbolConfig: configs["SOFI"],
    accountValue: 50000m);

if (result.Success)
{
    Console.WriteLine($"Trade executed: {result.FillQuantity} contracts @ ${result.FillPrice}");
}
else
{
    Console.WriteLine($"Trade rejected: {result.ErrorMessage}");
}
```

### Monitor Portfolio
```csharp
var managementResult = await tradingEngine.ManagePositions(22.0m);

Console.WriteLine($"Portfolio P&L: ${managementResult.TotalPnL:N2}");
Console.WriteLine($"Active Positions: {managementResult.ActivePositions}");
Console.WriteLine($"Total Delta: {managementResult.RiskMetrics.TotalDelta:F2}");

foreach (var action in managementResult.ManagementActions)
{
    Console.WriteLine($"Action: {action.ActionType} - {action.Reason}");
}
```

## Error Handling

All async methods return structured result objects with error information:

```csharp
if (!result.Success)
{
    Console.WriteLine($"Error: {result.ErrorMessage}");
    
    if (result.RiskWarnings.Any())
    {
        Console.WriteLine("Warnings:");
        foreach (var warning in result.RiskWarnings)
        {
            Console.WriteLine($"  - {warning}");
        }
    }
}
```

## Configuration Examples

### Paper Trading Setup
```csharp
var brokerClient = new TDAmeritradeBrokerClient(
    apiKey: "demo_key",
    accountId: "demo_account", 
    paperTradingMode: true);
```

### Live Trading Setup
```csharp
var brokerClient = new TDAmeritradeBrokerClient(
    apiKey: Environment.GetEnvironmentVariable("TD_API_KEY"),
    accountId: Environment.GetEnvironmentVariable("TD_ACCOUNT_ID"), 
    paperTradingMode: false);
```

---

*This API reference covers the complete public interface of the SOFIRED system. For implementation details and private methods, refer to the source code documentation.*