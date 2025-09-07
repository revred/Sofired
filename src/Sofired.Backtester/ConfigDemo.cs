using System;
using System.Linq;
using Sofired.Core;

namespace Sofired.Backtester
{
    /// <summary>
    /// Demonstration of the symbol-specific configuration system
    /// Shows how different symbols can have customized parameters
    /// </summary>
    public class ConfigDemo
    {
        public static void RunDemo()
        {
            Console.WriteLine("\n🔧 SYMBOL-SPECIFIC CONFIGURATION SYSTEM DEMO");
            Console.WriteLine("=".PadRight(60, '='));
            
            var configManager = new ConfigurationManager();
            
            try
            {
                // Show available symbols
                var availableSymbols = configManager.GetAvailableSymbols();
                Console.WriteLine($"📋 Available symbol configurations: {string.Join(", ", availableSymbols)}");
                
                // Load SOFI configuration
                Console.WriteLine("\n📊 LOADING SOFI CONFIGURATION");
                var sofiConfig = configManager.LoadSymbolConfig("SOFI");
                ShowConfigSummary("SOFI", sofiConfig);
                
                // Load APP configuration if available
                if (availableSymbols.Contains("APP"))
                {
                    Console.WriteLine("\n📊 LOADING APP CONFIGURATION");
                    var appConfig = configManager.LoadSymbolConfig("APP");
                    ShowConfigSummary("APP", appConfig);
                    
                    // Compare the two configurations
                    configManager.CompareSymbolConfigs("SOFI", "APP");
                }
                
                Console.WriteLine("\n✅ Configuration system demonstration completed successfully!");
                Console.WriteLine("   - Symbol-specific parameters loaded correctly");
                Console.WriteLine("   - Trade windows, deltas, and risk parameters are customizable per symbol");
                Console.WriteLine("   - Ready to support SOFI, APP, and additional symbols as needed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Configuration demo error: {ex.Message}");
            }
        }
        
        private static void ShowConfigSummary(string symbol, SymbolConfig config)
        {
            Console.WriteLine($"\n📈 {symbol} CONFIGURATION SUMMARY:");
            Console.WriteLine($"   Sector: {config.Company.Sector}");
            Console.WriteLine($"   Entry Window: {config.Trading.EntryWindow.Start} - {config.Trading.EntryWindow.End}");
            Console.WriteLine($"   Exit Window: {config.Trading.ExitWindow.Start} - {config.Trading.ExitWindow.End}");
            Console.WriteLine($"   Put Delta Range: {config.Trading.Options.PutDeltaMin} to {config.Trading.Options.PutDeltaMax}");
            Console.WriteLine($"   DTE Optimal: {config.Trading.Options.DteOptimal} days");
            Console.WriteLine($"   Max Position Size: {config.Risk.MaxPositionSize:P0}");
            Console.WriteLine($"   Capital per Trade: {config.Risk.CapitalAllocation:P0}");
            Console.WriteLine($"   Strategy Mix: {config.Strategy.PutCreditSpreadWeight:P0} PCS / {config.Strategy.CoveredCallWeight:P0} CC");
            Console.WriteLine($"   VIX Thresholds: Low {config.Market.VixLow} | Normal {config.Market.VixNormal} | High {config.Market.VixHigh} | Crisis {config.Market.VixCrisis}");
            
            if (config.Company.KeyEvents.Count > 0)
            {
                Console.WriteLine($"   Key Catalysts: {string.Join(", ", config.Company.KeyEvents.Take(3))}");
            }
        }
    }
}