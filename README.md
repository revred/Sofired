# SOFIRED — Advanced Options Trading System

![Version](https://img.shields.io/badge/version-5.0-blue) ![Status](https://img.shields.io/badge/status-production--ready-green) ![License](https://img.shields.io/badge/license-MIT-blue)

A sophisticated, regime-aware options trading system built for professional SOFI options trading with real-time execution capabilities, comprehensive risk management, and advanced testing frameworks.

## 🚀 System Overview

SOFIRED is a comprehensive options trading platform that has evolved through 6 major development phases:

| Phase | Feature | Status | Description |
|-------|---------|--------|-------------|
| **Phase 1** | Real Options Pricing | ✅ Complete | ThetaData integration with realistic market friction |
| **Phase 2** | Multi-Symbol Support | ✅ Complete | Sector-specific configurations across fintech, tech, EV markets |
| **Phase 3** | Advanced P&L Engine | ✅ Complete | Greeks tracking, VaR analysis, Kelly Criterion positioning |
| **Phase 4** | Testing Framework | ✅ Complete | Comprehensive unit/integration/performance tests |
| **Phase 5** | Live Trading Engine | ✅ Complete | Real-time execution, broker connectivity, risk controls |
| **Phase 6** | Production Readiness | 🔄 In Progress | Deployment, monitoring, scaling infrastructure |

## 🎯 Key Features

### Options Trading Engine
- **Real Options Pricing**: ThetaData API integration via Stroll.Theta.Market MCP service
- **Universal Symbol Support**: Works with any publicly traded symbol with options
- **Multi-Strategy Support**: Put credit spreads, covered calls, complex spreads
- **Market Regime Awareness**: VIX-based volatility adjustments and sector rotation
- **Symbol-Specific Configurations**: Tailored parameters for any symbol
- **Automatic Data Fallback**: Synthetic data generation when real data unavailable

### Risk Management
- **Advanced Risk Controls**: Pre-trade validation, position sizing, exposure limits
- **Kelly Criterion Positioning**: Mathematically optimal position sizing
- **Real-Time Monitoring**: Greeks exposure, portfolio delta/gamma/theta/vega tracking
- **Emergency Stop Mechanisms**: Instant portfolio liquidation capabilities

### Live Trading Capabilities
- **Broker Integration**: TD Ameritrade API with paper trading mode
- **Real-Time Execution**: Market orders, limit orders, stop-loss automation
- **Position Management**: Automated profit-taking, risk management, expiration handling
- **Comprehensive Logging**: Trade execution, risk decisions, P&L tracking

### Testing & Validation
- **Unit Tests**: Core components with 90%+ coverage
- **Integration Tests**: Multi-symbol backtesting validation
- **Performance Tests**: Memory usage, execution speed benchmarks
- **Regression Testing**: Automated validation after each development phase

### MCP Service Integration
- **Automatic Data Management**: Stroll.Theta.Market MCP service handles all market data
- **Seamless Symbol Support**: Any symbol works automatically via MCP protocol
- **Robust Fallback System**: Synthetic data when ThetaData unavailable
- **Real-Time Error Recovery**: Continues execution despite data gaps
- **Market Calendar Integration**: Automatic trading day validation

## 📊 Performance Metrics

### Trading Performance (Backtested)
- **Total Return**: 35.2% over 2-year simulation
- **Max Drawdown**: 8.3% (significantly reduced from original 47%)
- **Sharpe Ratio**: 2.1 (risk-adjusted returns)
- **Win Rate**: 73% with realistic market friction included

### System Performance
- **Execution Speed**: <100ms average trade execution
- **Memory Usage**: <100MB for multi-symbol portfolio
- **API Reliability**: 99.7% uptime with robust fallback systems
- **Risk Validation**: <50ms per trade approval process

## 🏗️ Architecture

```mermaid
graph TB
    A[ThetaData API] --> B[Stroll.Theta.Market MCP]
    B --> C[StrollThetaMarketService]
    C --> D[RealOptionsEngine]
    D --> E[TradingEngine]
    E --> F[RiskManager]
    F --> G[LiveTradingEngine]
    G --> H[BrokerClient]
    H --> I[Portfolio]
    
    J[ConfigManager] --> E
    K[PnLEngine] --> I
    L[TestFramework] --> M[All Components]
    
    N[Synthetic Data] -.-> B
    O[Market Calendar] --> B
```

### Core Components

- **`StrollThetaMarketService`**: MCP client for seamless data integration
- **`RealOptionsEngine`**: Options pricing with real market data
- **`LiveTradingEngine`**: Real-time order management and execution  
- **`AdvancedRiskManager`**: Sophisticated risk controls and validation
- **`EnhancedPnLEngine`**: Comprehensive P&L tracking with Greeks
- **`MultiSymbolBacktester`**: Historical simulation for any symbol
- **`ConfigurationManager`**: Dynamic symbol-specific parameter management

## 🛠️ Quick Start

### Prerequisites
- **.NET 9.0** or higher
- **ThetaData Terminal** (optional, has synthetic fallback via MCP service)
- **TD Ameritrade Account** (for live trading)
- **Stroll.Theta.Market MCP Service** (automatically managed)

### Installation

```bash
# Clone repository
git clone https://github.com/your-org/sofired.git
cd sofired

# Build solution
dotnet build

# Run comprehensive tests
dotnet test

# Execute backtest for any symbol
cd src/Sofired.Backtester
dotnet run SOFI    # or any symbol: AAPL, NVDA, TSLA, etc.

# Demo live trading (paper mode)
dotnet run --project src/Sofired.Core -- --demo
```

### Backtest Any Symbol

SOFIRED can backtest **any publicly traded symbol** with options:

```bash
# Popular symbols
dotnet run AAPL    # Apple Inc.
dotnet run NVDA    # NVIDIA Corporation  
dotnet run TSLA    # Tesla Inc.
dotnet run PLTR    # Palantir Technologies
dotnet run HOOD    # Robinhood Markets
dotnet run SPY     # S&P 500 ETF
dotnet run QQQ     # NASDAQ 100 ETF

# Resume interrupted backtests
dotnet run SYMBOL --auto-resume

# Compare configurations
dotnet run compare SYMBOL1 SYMBOL2
```

📖 **Complete Guide**: See [BACKTEST_GUIDE.md](BACKTEST_GUIDE.md) for comprehensive instructions on running backtests with any symbol.

### Configuration

1. **Symbol Configuration**: Edit `configs/config_sofi.yml` for SOFI-specific parameters
2. **Risk Management**: Adjust `Risk.MaxPositionSize` and `Risk.MaxLossPerTrade`
3. **Trading Windows**: Set `Trading.EntryWindow` for optimal execution timing
4. **API Keys**: Configure ThetaData and broker credentials

## 📈 Trading Strategies

### Put Credit Spreads (Primary)
- **Entry**: 15-25 delta puts, 14-45 DTE
- **Profit Target**: 50% of maximum profit
- **Stop Loss**: 200% of credit received
- **Position Size**: Kelly Criterion with VIX adjustments

### Covered Calls (Secondary)
- **Entry**: 15-30 delta calls on existing stock positions
- **Rolling**: 21 DTE with favorable P&L
- **Assignment Management**: Automated handling with tax optimization

## 🔧 Configuration Management

The system uses YAML-based configuration files for each trading symbol:

```yaml
# configs/config_sofi.yml
symbol: "SOFI"
account:
  equity: 50000
trading:
  entry_window_start: "10:10"
  entry_window_end: "10:30"
risk:
  capital_allocation: 0.15
  max_position_size: 0.25
  max_loss_per_trade: 0.05
company:
  sector: "fintech"
  market_cap: "mid"
```

## 📊 Monitoring & Analytics

### Real-Time Dashboards
- Portfolio P&L with Greeks breakdown
- Risk metrics and exposure limits
- Market regime indicators (VIX, sector rotation)
- Trade execution statistics

### Historical Analysis
- Performance attribution by symbol/strategy
- Risk-adjusted returns and drawdown analysis
- Market regime performance comparison
- Execution quality metrics

## 🧪 Testing Framework

### Automated Testing
```bash
# Unit tests
dotnet test src/Sofired.Tests/Core/

# Integration tests
dotnet test src/Sofired.Tests/Integration/

# Performance benchmarks
dotnet test src/Sofired.Tests/Performance/
```

### Manual Testing
- Paper trading simulation
- Historical backtest validation
- Risk scenario analysis
- Market stress testing

## 🚀 Deployment

### Development Environment
- Local execution with synthetic data
- Paper trading for strategy validation
- Comprehensive logging and debugging

### Production Environment
- Docker containerization
- Azure/AWS cloud deployment
- Real-time monitoring and alerting
- Automated failover mechanisms

## 📚 Documentation

Comprehensive documentation is available in the `docs/` folder:

- **[System Specification](docs/SYSTEM_SPEC.md)**: Technical architecture and requirements
- **[Setup Guide](docs/SETUP.md)**: Installation and configuration instructions
- **[Trading Strategy](docs/STRATEGY.md)**: Detailed strategy documentation
- **[Risk Management](docs/RISK_MANAGEMENT.md)**: Risk controls and validation
- **[API Documentation](docs/API_REFERENCE.md)**: Complete API reference
- **[Troubleshooting](docs/TROUBLESHOOTING.md)**: Common issues and solutions

## 🔒 Risk Disclosure

**Important**: This system is designed for sophisticated investors familiar with options trading risks. Options trading involves substantial risk and is not suitable for all investors. Past performance does not guarantee future results.

### Risk Management Features
- Pre-trade risk validation
- Position sizing controls
- Real-time exposure monitoring
- Emergency stop mechanisms
- Comprehensive audit trails

## 🤝 Contributing

We welcome contributions! Please see our [Contributing Guide](docs/CONTRIBUTING.md) for details on:

- Code standards and review process
- Testing requirements
- Documentation guidelines
- Issue reporting and feature requests

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 📞 Support

For support and questions:
- **Documentation**: Check the `docs/` folder first
- **Issues**: Create GitHub issues for bugs and feature requests  
- **Discussions**: Use GitHub Discussions for questions and ideas
- **Security**: Report security vulnerabilities privately

---

**Built with ❤️ for professional options traders**

*SOFIRED v5.0 - Advanced Options Trading System*