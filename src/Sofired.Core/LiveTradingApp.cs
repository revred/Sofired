using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sofired.Core
{
    /// <summary>
    /// Main live trading application demonstrating Phase 5 capabilities
    /// Orchestrates real-time trading with comprehensive risk management
    /// </summary>
    public class LiveTradingApp
    {
        private readonly LiveTradingEngine _tradingEngine;
        private readonly ConfigurationManager _configManager;
        private readonly Dictionary<string, SymbolConfig> _symbolConfigs;
        private LiveTradingSession? _currentSession;

        public LiveTradingApp(bool paperTradingMode = true)
        {
            // Initialize components with new MCP client
            var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var thetaClient = new Stroll.Theta.Client.ThetaClient(httpClient);
            var realOptionsEngine = new RealOptionsEngine(thetaClient);
            var advancedRiskManager = new AdvancedRiskManager();
            var enhancedPnLEngine = new EnhancedPnLEngine();
            var brokerClient = new TDAmeritradeBrokerClient("demo_api_key", "demo_account", paperTradingMode);

            _tradingEngine = new LiveTradingEngine(
                brokerClient, 
                realOptionsEngine, 
                advancedRiskManager, 
                enhancedPnLEngine, 
                paperTradingMode);

            _configManager = new ConfigurationManager();
            _symbolConfigs = new Dictionary<string, SymbolConfig>();
        }

        /// <summary>
        /// Start live trading session with specified symbols
        /// </summary>
        public async Task<bool> StartTradingSession(List<string> symbols, decimal initialAccountValue)
        {
            try
            {
                Console.WriteLine("🚀 STARTING LIVE TRADING SESSION");
                Console.WriteLine($"Mode: {(_tradingEngine != null ? "Paper Trading" : "Live Trading")}");
                Console.WriteLine($"Initial Account Value: ${initialAccountValue:N2}");
                Console.WriteLine($"Trading Symbols: {string.Join(", ", symbols)}");
                Console.WriteLine();

                // Load symbol configurations
                foreach (var symbol in symbols)
                {
                    var config = _configManager.LoadSymbolConfig(symbol);
                    _symbolConfigs[symbol] = config;
                    Console.WriteLine($"✅ Loaded configuration for {symbol} ({config.Company.Sector} sector)");
                }

                // Start trading session
                _currentSession = await _tradingEngine.StartTradingSession(symbols, initialAccountValue, _symbolConfigs);
                Console.WriteLine($"🎯 Session started: {_currentSession.SessionId}");
                Console.WriteLine();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to start trading session: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Execute trading opportunities for the day
        /// </summary>
        public async Task ExecuteTradingDay()
        {
            if (_currentSession == null)
            {
                Console.WriteLine("❌ No active trading session");
                return;
            }

            Console.WriteLine("📊 EXECUTING TRADING DAY");
            Console.WriteLine("========================");

            var successfulTrades = 0;
            var rejectedTrades = 0;

            foreach (var symbol in _currentSession.TradingSymbols)
            {
                try
                {
                    Console.WriteLine($"\n🔍 Analyzing {symbol}...");
                    
                    // Simulate getting current market data
                    var currentPrice = await SimulateCurrentPrice(symbol);
                    Console.WriteLine($"Current price: ${currentPrice:F2}");

                    // Calculate strikes based on symbol configuration
                    var config = _symbolConfigs[symbol];
                    var shortStrike = Math.Round(currentPrice * 0.85m, 2); // 15% OTM
                    var longStrike = Math.Round(currentPrice * 0.80m, 2);  // 20% OTM
                    var expirationDate = GetNextFridayExpiration();

                    Console.WriteLine($"Proposed spread: ${shortStrike}/{longStrike} exp {expirationDate:MM/dd}");

                    // Execute trade
                    var result = await _tradingEngine.ExecutePutCreditSpread(
                        symbol,
                        currentPrice,
                        expirationDate,
                        shortStrike,
                        longStrike,
                        config,
                        _currentSession.CurrentAccountValue);

                    if (result.Success)
                    {
                        Console.WriteLine($"✅ Trade executed: {result.FillQuantity} contracts @ ${result.FillPrice:F2}");
                        Console.WriteLine($"   Commission: ${result.Commission:F2}");
                        successfulTrades++;
                        
                        // Update account value
                        _currentSession.CurrentAccountValue += result.FillPrice * result.FillQuantity * 100m - result.Commission;
                    }
                    else
                    {
                        Console.WriteLine($"❌ Trade rejected: {result.ErrorMessage}");
                        if (result.RiskWarnings.Count > 0)
                        {
                            foreach (var warning in result.RiskWarnings)
                            {
                                Console.WriteLine($"   ⚠️ {warning}");
                            }
                        }
                        rejectedTrades++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error processing {symbol}: {ex.Message}");
                    rejectedTrades++;
                }
            }

            Console.WriteLine($"\n📈 TRADING DAY SUMMARY");
            Console.WriteLine($"Successful trades: {successfulTrades}");
            Console.WriteLine($"Rejected trades: {rejectedTrades}");
            Console.WriteLine($"Account value: ${_currentSession.CurrentAccountValue:N2}");
            Console.WriteLine($"P&L: ${_currentSession.CurrentAccountValue - _currentSession.InitialAccountValue:N2}");
        }

        /// <summary>
        /// Monitor and manage existing positions
        /// </summary>
        public async Task MonitorPositions()
        {
            if (_currentSession == null)
            {
                Console.WriteLine("❌ No active trading session");
                return;
            }

            Console.WriteLine("\n👁️ POSITION MONITORING");
            Console.WriteLine("=====================");

            var managementResult = await _tradingEngine.ManagePositions(22.0m); // VIX simulation
            
            Console.WriteLine($"Active positions: {managementResult.ActivePositions}");
            Console.WriteLine($"Total P&L: ${managementResult.TotalPnL:N2}");
            Console.WriteLine($"Portfolio Delta: {managementResult.RiskMetrics.TotalDelta:F2}");
            Console.WriteLine($"Portfolio Gamma: {managementResult.RiskMetrics.TotalGamma:F2}");

            if (managementResult.ManagementActions.Count > 0)
            {
                Console.WriteLine("\n🎯 Position Actions:");
                foreach (var action in managementResult.ManagementActions)
                {
                    var priorityIcon = action.Priority switch
                    {
                        ActionPriority.Critical => "🚨",
                        ActionPriority.High => "⚡",
                        ActionPriority.Medium => "⚠️",
                        _ => "ℹ️"
                    };
                    
                    Console.WriteLine($"{priorityIcon} {action.ActionType} - {action.Reason}");
                }
            }
        }

        /// <summary>
        /// Emergency stop all trading activities
        /// </summary>
        public async Task EmergencyStop(string reason = "Manual stop")
        {
            Console.WriteLine("\n🛑 EMERGENCY STOP ACTIVATED");
            Console.WriteLine($"Reason: {reason}");

            var stopResult = await _tradingEngine.EmergencyStop(reason);
            
            if (stopResult.Success)
            {
                Console.WriteLine($"✅ Emergency stop completed successfully");
                Console.WriteLine($"Positions closed: {stopResult.ClosedPositions.Count}");
            }
            else
            {
                Console.WriteLine($"❌ Emergency stop encountered errors:");
                foreach (var error in stopResult.Errors)
                {
                    Console.WriteLine($"   • {error}");
                }
            }

            if (_currentSession != null)
            {
                _currentSession.Status = TradingSessionStatus.Stopped;
                _currentSession.EndTime = DateTime.Now;
            }
        }

        /// <summary>
        /// Generate end-of-day trading report
        /// </summary>
        public async Task GenerateEndOfDayReport()
        {
            if (_currentSession == null)
            {
                Console.WriteLine("❌ No active trading session");
                return;
            }

            Console.WriteLine("\n📋 END OF DAY REPORT");
            Console.WriteLine("===================");
            Console.WriteLine($"Session ID: {_currentSession.SessionId}");
            Console.WriteLine($"Start Time: {_currentSession.StartTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"End Time: {_currentSession.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Active"}");
            Console.WriteLine($"Mode: {(_currentSession.IsPaperTrading ? "Paper Trading" : "Live Trading")}");
            Console.WriteLine();

            Console.WriteLine("💰 FINANCIAL SUMMARY");
            Console.WriteLine($"Initial Value: ${_currentSession.InitialAccountValue:N2}");
            Console.WriteLine($"Final Value: ${_currentSession.CurrentAccountValue:N2}");
            var totalPnL = _currentSession.CurrentAccountValue - _currentSession.InitialAccountValue;
            var pnlPercent = (totalPnL / _currentSession.InitialAccountValue) * 100;
            Console.WriteLine($"Total P&L: ${totalPnL:N2} ({pnlPercent:F2}%)");
            Console.WriteLine();

            Console.WriteLine("🎯 SYMBOLS TRADED");
            foreach (var symbol in _currentSession.TradingSymbols)
            {
                var config = _symbolConfigs[symbol];
                Console.WriteLine($"• {symbol} ({config.Company.Sector} sector)");
                Console.WriteLine($"  Capital allocation: {config.Risk.CapitalAllocation:P1}");
                Console.WriteLine($"  Max position size: {config.Risk.MaxPositionSize:P1}");
            }
        }

        private async Task<decimal> SimulateCurrentPrice(string symbol)
        {
            // Simulate realistic price movement
            await Task.Delay(100); // Simulate API call delay
            
            var random = new Random();
            return symbol.ToUpper() switch
            {
                "SOFI" => 14.50m + (decimal)(random.NextDouble() * 2.0 - 1.0),
                "AAPL" => 175.0m + (decimal)(random.NextDouble() * 10.0 - 5.0),
                "NVDA" => 450.0m + (decimal)(random.NextDouble() * 50.0 - 25.0),
                "TSLA" => 250.0m + (decimal)(random.NextDouble() * 25.0 - 12.5),
                "APP" => 25.0m + (decimal)(random.NextDouble() * 5.0 - 2.5),
                _ => 100.0m + (decimal)(random.NextDouble() * 10.0 - 5.0)
            };
        }

        private DateTime GetNextFridayExpiration()
        {
            var today = DateTime.Today;
            var daysUntilFriday = ((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7;
            if (daysUntilFriday == 0) daysUntilFriday = 7; // Next Friday if today is Friday
            
            var nextFriday = today.AddDays(daysUntilFriday);
            
            // Add additional weeks to get reasonable DTE (14-45 days)
            var targetDays = new Random().Next(14, 46);
            while ((nextFriday - today).Days < targetDays)
            {
                nextFriday = nextFriday.AddDays(7);
            }
            
            return nextFriday;
        }
    }

    /// <summary>
    /// Console application entry point for live trading demonstration
    /// </summary>
    public class LiveTradingDemo
    {
        public static async Task RunDemo()
        {
            Console.WriteLine("🔥 SOFIRED LIVE TRADING SYSTEM - PHASE 5 DEMO");
            Console.WriteLine("================================================");
            Console.WriteLine();

            var app = new LiveTradingApp(paperTradingMode: true);
            var symbols = new List<string> { "SOFI", "AAPL", "NVDA" };
            var accountValue = 50000m;

            try
            {
                // Start trading session
                var sessionStarted = await app.StartTradingSession(symbols, accountValue);
                if (!sessionStarted)
                {
                    Console.WriteLine("Failed to start trading session");
                    return;
                }

                // Execute trading day
                await app.ExecuteTradingDay();

                // Monitor positions
                await app.MonitorPositions();

                // Generate end-of-day report
                await app.GenerateEndOfDayReport();

                Console.WriteLine("\n✅ Live trading demo completed successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Demo failed: {ex.Message}");
                
                // Emergency stop if something goes wrong
                await app.EmergencyStop("Demo exception occurred");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}