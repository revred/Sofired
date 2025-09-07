# SOFIRED Troubleshooting Guide

## Common Issues and Solutions

### Build and Compilation Issues

#### Issue: "The type or namespace name 'X' could not be found"

**Symptoms:**
```
error CS0246: The type or namespace name 'RealOptionsEngine' could not be found
```

**Solutions:**
1. **Missing Using Statements:**
   ```csharp
   using Sofired.Core;
   using System.Threading.Tasks;
   using System.Linq;
   ```

2. **Project References:**
   - Ensure all project references are properly configured
   - Rebuild the entire solution: `dotnet build --no-incremental`

3. **Namespace Issues:**
   - Verify all classes are in the correct namespace
   - Check for circular dependencies between projects

#### Issue: Duplicate Type Definitions

**Symptoms:**
```
error CS0101: The namespace 'Sofired.Core' already contains a definition for 'PositionStatus'
```

**Solutions:**
1. **Remove Duplicate Enums/Classes:**
   - Search for duplicate definitions across files
   - Consolidate into single definition in appropriate file

2. **Check Models.cs:**
   - Ensure enums are defined once in Models.cs
   - Remove any duplicate definitions in other files

### Runtime Issues

#### Issue: ThetaData API Connection Failures

**Symptoms:**
```
❌ ThetaData API error: 472 - No data for the specified timeframe & contract
```

**Solutions:**
1. **Check ThetaData Terminal:**
   - Ensure ThetaData Terminal is running on localhost:25510
   - Verify subscription includes required data feeds
   - Check firewall settings

2. **Use Synthetic Fallback:**
   ```csharp
   // System automatically falls back to synthetic data
   🔄 Falling back to synthetic data generation...
   ```

3. **Configuration Check:**
   ```yaml
   data:
     primary: "theta"
     fallback: "synthetic"
   ```

#### Issue: Broker Connection Errors

**Symptoms:**
```
Failed to submit order: Unauthorized (401)
```

**Solutions:**
1. **API Key Validation:**
   - Verify TD Ameritrade API key is valid and active
   - Check account permissions for options trading
   - Ensure paper trading vs live trading mode is correct

2. **Account Status:**
   ```csharp
   var account = await brokerClient.GetAccountInfo();
   if (!account.IsApproved)
   {
       throw new InvalidOperationException("Account not approved for options trading");
   }
   ```

3. **Market Hours:**
   - Check if market is open for the requested operation
   - Use paper trading mode for after-hours testing

### Configuration Issues

#### Issue: YAML Configuration Parsing Errors

**Symptoms:**
```
Configuration error: Invalid YAML format in config_sofi.yml
```

**Solutions:**
1. **YAML Syntax Validation:**
   - Use online YAML validators
   - Check indentation (use spaces, not tabs)
   - Verify all strings are properly quoted

2. **Required Fields:**
   ```yaml
   symbol: "SOFI"
   account:
     equity: 50000
   trading:
     entry_window_start: "10:10"
     entry_window_end: "10:30"
   risk:
     capital_allocation: 0.15
   ```

3. **File Location:**
   - Ensure config files are in `configs/` directory
   - Check file naming convention: `config_{symbol}.yml`

#### Issue: Missing Configuration Files

**Symptoms:**
```
FileNotFoundException: Could not find config_sofi.yml
```

**Solutions:**
1. **Create Missing Configs:**
   ```bash
   mkdir -p configs
   cp configs/config_sofi.yml configs/config_aapl.yml
   # Edit symbol-specific values
   ```

2. **Verify File Paths:**
   - Use absolute paths in configuration loading
   - Check working directory when running application

### Performance Issues

#### Issue: Slow Options Pricing Calculations

**Symptoms:**
- Long delays in pricing calculations
- Timeout errors in ThetaData requests

**Solutions:**
1. **Enable Caching:**
   ```csharp
   // Options pricing caching is built-in
   // Check cache hit rates in logs
   ```

2. **Reduce API Calls:**
   - Batch requests when possible
   - Use synthetic pricing for backtesting
   - Implement request throttling

3. **Network Optimization:**
   - Check network latency to ThetaData servers
   - Consider using local market data feed

#### Issue: High Memory Usage

**Symptoms:**
- Out of memory exceptions
- Slow performance with large portfolios

**Solutions:**
1. **Memory Profiling:**
   ```csharp
   var initialMemory = GC.GetTotalMemory(true);
   // ... operations ...
   var finalMemory = GC.GetTotalMemory(false);
   Console.WriteLine($"Memory used: {(finalMemory - initialMemory) / 1024 / 1024} MB");
   ```

2. **Garbage Collection:**
   - Force GC collection after large operations
   - Dispose of large objects properly
   - Use memory-efficient data structures

### Trading Issues

#### Issue: Risk Validation Failures

**Symptoms:**
```
Trade rejected: Max loss (8.5%) exceeds configured limit (5.0%)
```

**Solutions:**
1. **Adjust Risk Parameters:**
   ```yaml
   risk:
     max_loss_per_trade: 0.08  # Increase to 8%
     max_position_size: 0.30   # Allow larger positions
   ```

2. **Position Sizing:**
   - Reduce number of contracts
   - Use wider spreads to reduce max loss
   - Consider different strike selections

3. **VIX Adjustments:**
   ```csharp
   // System automatically adjusts for VIX levels
   // High VIX = reduced position sizing
   ```

#### Issue: Execution Delays

**Symptoms:**
- Orders not filling promptly
- Significant slippage from expected prices

**Solutions:**
1. **Order Type Selection:**
   ```csharp
   var order = new TradeOrder
   {
       OrderType = OrderType.LimitOrder,  // Use limit orders for better fills
       // ... other properties
   };
   ```

2. **Market Hours:**
   - Execute during high-liquidity periods
   - Avoid first/last 30 minutes of trading
   - Check option volume before trading

3. **Spread Width:**
   - Use liquid strikes with tight bid-ask spreads
   - Avoid extremely wide or narrow spreads

### Testing Issues

#### Issue: Unit Tests Failing

**Symptoms:**
```
Test failed: Expected 0.15, but was 0.14999
```

**Solutions:**
1. **Floating Point Comparisons:**
   ```csharp
   result.Should().BeApproximately(0.15m, 0.001m);  // Use tolerance
   ```

2. **Date/Time Dependencies:**
   ```csharp
   // Use fixed dates in tests
   var testDate = DateTime.Parse("2024-01-15");
   ```

3. **Async Test Issues:**
   ```csharp
   [Fact]
   public async Task TestMethod()
   {
       await Task.Delay(100);  // Ensure proper async/await
       // ... test logic
   }
   ```

#### Issue: Integration Tests Timing Out

**Symptoms:**
```
Test 'MultiSymbolBacktest' timed out after 30000ms
```

**Solutions:**
1. **Increase Timeout:**
   ```csharp
   [Fact(Timeout = 60000)]  // 60 seconds
   public async Task LongRunningTest() { }
   ```

2. **Mock External Dependencies:**
   ```csharp
   var mockBroker = new Mock<IBrokerClient>();
   mockBroker.Setup(x => x.GetCurrentPrice(It.IsAny<string>()))
           .ReturnsAsync(15.0m);
   ```

### System Monitoring

#### Issue: Identifying Performance Bottlenecks

**Tools:**
1. **Built-in Performance Tests:**
   ```bash
   dotnet test src/Sofired.Tests/Performance/
   ```

2. **Memory Profiling:**
   ```csharp
   var stopwatch = Stopwatch.StartNew();
   // ... operation ...
   Console.WriteLine($"Operation took {stopwatch.ElapsedMilliseconds}ms");
   ```

3. **Logging Analysis:**
   ```csharp
   Console.WriteLine($"🔧 LOADING SYMBOL-SPECIFIC CONFIGURATION FOR {symbol}");
   Console.WriteLine($"✅ Loaded symbol-specific configuration for {symbol}");
   ```

### Emergency Procedures

#### Issue: System Malfunction During Live Trading

**Immediate Actions:**
1. **Emergency Stop:**
   ```csharp
   await tradingEngine.EmergencyStop("System malfunction detected");
   ```

2. **Manual Position Review:**
   - Check all open positions in broker platform
   - Close positions manually if automated system fails
   - Document all manual actions

3. **System Isolation:**
   - Stop all automated trading
   - Switch to paper trading mode
   - Investigate root cause

#### Issue: Data Feed Issues

**Symptoms:**
- Stale price data
- Missing options chains
- Inconsistent market data

**Actions:**
1. **Switch to Backup Feed:**
   ```csharp
   // System automatically uses synthetic data as fallback
   ```

2. **Verify Data Quality:**
   - Compare prices with other sources
   - Check for unusual patterns
   - Validate options chain completeness

3. **Halt Trading:**
   - Stop new position entries
   - Monitor existing positions manually
   - Wait for data feed restoration

## Debugging Tools and Techniques

### Logging Configuration

Enable detailed logging for troubleshooting:

```csharp
Console.WriteLine($"🔧 OPERATION: {operationName}");
Console.WriteLine($"✅ SUCCESS: {successMessage}");
Console.WriteLine($"❌ ERROR: {errorMessage}");
Console.WriteLine($"⚠️ WARNING: {warningMessage}");
Console.WriteLine($"🔄 FALLBACK: {fallbackMessage}");
```

### Diagnostic Commands

```bash
# Check system health
dotnet run --project src/Sofired.Core -- --health-check

# Validate configuration
dotnet run --project src/Sofired.Core -- --validate-config

# Run performance benchmarks
dotnet test src/Sofired.Tests/Performance/ --logger:console

# Memory usage analysis
dotnet run --project src/Sofired.Core -- --memory-analysis
```

### Environment Variables

```bash
# Enable debug logging
export SOFIRED_LOG_LEVEL=Debug

# Use paper trading mode
export SOFIRED_PAPER_TRADING=true

# Override API endpoints
export SOFIRED_THETA_ENDPOINT=http://localhost:25510
export SOFIRED_BROKER_ENDPOINT=https://api.tdameritrade.com
```

## Getting Help

### Before Contacting Support

1. **Check Logs:** Review application logs for error details
2. **Reproduce Issue:** Document steps to reproduce the problem
3. **Environment Info:** Note OS, .NET version, and configuration
4. **Recent Changes:** List any recent code or configuration changes

### Support Channels

1. **Documentation:** Check `docs/` folder for relevant guides
2. **GitHub Issues:** Create detailed issue reports
3. **Community Forum:** Ask questions in discussions
4. **Emergency Support:** Contact for live trading issues

### Issue Reporting Template

```markdown
## Issue Description
Brief description of the problem

## Environment
- OS: Windows 11 / macOS / Linux
- .NET Version: 8.0
- SOFIRED Version: 5.0
- Trading Mode: Paper / Live

## Steps to Reproduce
1. Step one
2. Step two
3. Step three

## Expected Behavior
What should have happened

## Actual Behavior
What actually happened

## Logs
```
[Paste relevant log output here]
```

## Additional Context
Any other relevant information
```

---

*This troubleshooting guide covers common issues encountered when using the SOFIRED system. For additional help, consult the documentation or contact support.*