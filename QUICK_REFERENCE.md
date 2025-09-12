# SOFIRED Quick Reference

## Backtest Any Symbol

### Basic Usage
```bash
cd C:\code\Sofired\src\Sofired.Backtester
dotnet run [SYMBOL]
```

### Popular Examples
```bash
dotnet run SOFI    # SoFi Technologies
dotnet run AAPL    # Apple Inc.
dotnet run NVDA    # NVIDIA Corporation
dotnet run TSLA    # Tesla Inc.
dotnet run PLTR    # Palantir Technologies
dotnet run HOOD    # Robinhood Markets
dotnet run SPY     # S&P 500 ETF
dotnet run QQQ     # NASDAQ 100 ETF
```

### Advanced Options
```bash
dotnet run SYMBOL --auto-resume     # Resume interrupted backtest
dotnet run SYMBOL --resume          # Manual resume with checkpoint selection
dotnet run config                   # Show configuration demo
dotnet run compare SYMBOL1 SYMBOL2  # Compare symbol configurations
```

## MCP Service Integration

### Automatic Features
- ✅ **Universal Symbol Support**: Any publicly traded symbol works
- ✅ **Real Data Primary**: ThetaData integration when available
- ✅ **Synthetic Fallback**: Automatic data generation when needed
- ✅ **Market Calendar**: Trading day validation and holiday handling
- ✅ **Error Recovery**: Continues execution despite data gaps

### What You'll See
```
🔥 Using Stroll.Theta.Market MCP Service
✅ Started Stroll.Theta.Market MCP service process
✅ MCP protocol initialized successfully
📡 MCP Request: Daily bars for SYMBOL from 2024-09-01 to 2024-09-30
📊 SYMBOL 2024-09-03: Aggregated 390 minute bars -> OHLC(7.89, 7.92, 7.48, 7.53)
🔥 Fetching real options data for SYMBOL expiring 2024-10-17
✅ Real pricing: Short Put 7.50 @ $0.45/$0.48, Long Put 7.00 @ $0.22/$0.25
```

## Troubleshooting

### Common Issues
| Issue | Solution |
|-------|----------|
| Symbol not found | System will use default configuration automatically |
| No market data | MCP service automatically uses synthetic data |
| MCP service fails | Check .NET 9.0 installation and restart |
| Data quality warnings | Normal behavior - system adapts to available data |

### Manual MCP Service Test
```bash
cd C:\code\Stroll.Theta\src\Stroll.Theta.Market
dotnet build && dotnet run
```

### ThetaData Connection Test
```bash
curl "http://127.0.0.1:25510/v2/list/expirations?root=SPY"
```

## Symbol Categories That Work

### Technology
- AAPL, MSFT, GOOGL, NVDA, AMD, INTC, CRM, ORCL

### Fintech  
- SOFI, PLTR, HOOD, SQ, PYPL, V, MA

### Electric Vehicles
- TSLA, RIVN, LCID, NIO, XPEV

### ETFs
- SPY, QQQ, IWM, GLD, TLT, XLE, XLF

### Meme/Reddit Stocks
- AMC, GME, BB, NOK, WISH

### Any other publicly traded symbol with options!

## Output Files

Results saved to `out/` directory:
- **`SYMBOL_backtest_YYYY-MM-DD.xlsx`**: Detailed trade analysis
- **`SYMBOL_summary.txt`**: Performance summary
- **`checkpoints/SYMBOL_*.json`**: Auto-save progress files

## Performance Expectations

### Typical Backtest
- **Duration**: 2-5 minutes for 1 month of data
- **Memory**: 50-200 MB depending on symbol activity
- **Data Quality**: 95%+ success rate with MCP fallback
- **Trades Analyzed**: 100-2000 potential option trades

### System Requirements
- **.NET 9.0** or higher
- **4GB RAM** minimum
- **ThetaData Terminal** (optional - MCP service provides fallback)

---

📖 **Full Documentation**: [BACKTEST_GUIDE.md](BACKTEST_GUIDE.md)  
🏠 **Main README**: [README.md](README.md)  
🔧 **MCP Service**: [Stroll.Theta.Market README](../Stroll.Theta/src/Stroll.Theta.Market/README.md)