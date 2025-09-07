using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sofired.Core
{
    /// <summary>
    /// Manages symbol-specific trading configurations
    /// Allows customization for different tickers (SOFI, APP, etc.) with different
    /// trade windows, deltas, risk parameters, and symbol-specific factors
    /// </summary>
    public class ConfigurationManager
    {
        private readonly Dictionary<string, SymbolConfig> _configs = new();
        private readonly IDeserializer _deserializer;
        
        public ConfigurationManager()
        {
            _deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
        }
        
        /// <summary>
        /// Load configuration for a specific symbol
        /// </summary>
        public SymbolConfig LoadSymbolConfig(string symbol)
        {
            if (_configs.TryGetValue(symbol, out var cachedConfig))
            {
                return cachedConfig;
            }
            
            var configPath = Path.Combine(".", $"config_{symbol.ToLower()}.yml");
            
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"Configuration file not found: {configPath}");
            }
            
            var yaml = File.ReadAllText(configPath);
            var config = _deserializer.Deserialize<SymbolConfig>(yaml);
            
            // Cache the loaded configuration
            _configs[symbol] = config;
            
            Console.WriteLine($"✅ Loaded symbol-specific configuration for {symbol}");
            Console.WriteLine($"   Entry window: {config.Trading.EntryWindow.Start} - {config.Trading.EntryWindow.End}");
            Console.WriteLine($"   Put delta range: {config.Trading.Options.PutDeltaMin} to {config.Trading.Options.PutDeltaMax}");
            Console.WriteLine($"   Sector: {config.Company.Sector}");
            
            return config;
        }
        
        /// <summary>
        /// Get list of available symbol configurations
        /// </summary>
        public List<string> GetAvailableSymbols()
        {
            var symbols = new List<string>();
            var configFiles = Directory.GetFiles(".", "config_*.yml");
            
            foreach (var file in configFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName.StartsWith("config_"))
                {
                    var symbol = fileName.Substring(7).ToUpper(); // Remove "config_" prefix
                    symbols.Add(symbol);
                }
            }
            
            return symbols;
        }
        
        /// <summary>
        /// Compare configuration differences between symbols
        /// </summary>
        public void CompareSymbolConfigs(string symbol1, string symbol2)
        {
            var config1 = LoadSymbolConfig(symbol1);
            var config2 = LoadSymbolConfig(symbol2);
            
            Console.WriteLine($"\n📊 CONFIGURATION COMPARISON: {symbol1} vs {symbol2}");
            Console.WriteLine("=".PadRight(60, '='));
            
            // Trading window comparison
            Console.WriteLine($"Entry Window:");
            Console.WriteLine($"  {symbol1}: {config1.Trading.EntryWindow.Start}-{config1.Trading.EntryWindow.End}");
            Console.WriteLine($"  {symbol2}: {config2.Trading.EntryWindow.Start}-{config2.Trading.EntryWindow.End}");
            
            // Delta comparison
            Console.WriteLine($"Put Delta Range:");
            Console.WriteLine($"  {symbol1}: {config1.Trading.Options.PutDeltaMin} to {config1.Trading.Options.PutDeltaMax}");
            Console.WriteLine($"  {symbol2}: {config2.Trading.Options.PutDeltaMin} to {config2.Trading.Options.PutDeltaMax}");
            
            // Risk comparison
            Console.WriteLine($"Position Sizing:");
            Console.WriteLine($"  {symbol1}: {config1.Risk.MaxPositionSize:P0} max, {config1.Risk.CapitalAllocation:P0} per trade");
            Console.WriteLine($"  {symbol2}: {config2.Risk.MaxPositionSize:P0} max, {config2.Risk.CapitalAllocation:P0} per trade");
            
            // Strategy mix comparison
            Console.WriteLine($"Strategy Mix (PCS/CC):");
            Console.WriteLine($"  {symbol1}: {config1.Strategy.PutCreditSpreadWeight:P0}/{config1.Strategy.CoveredCallWeight:P0}");
            Console.WriteLine($"  {symbol2}: {config2.Strategy.PutCreditSpreadWeight:P0}/{config2.Strategy.CoveredCallWeight:P0}");
            
            Console.WriteLine();
        }
    }
    
    /// <summary>
    /// Symbol-specific configuration structure
    /// </summary>
    public class SymbolConfig
    {
        public string Symbol { get; set; } = "";
        public AccountConfig Account { get; set; } = new();
        public TradingConfig Trading { get; set; } = new();
        public RiskConfig Risk { get; set; } = new();
        public MarketConfig Market { get; set; } = new();
        public SymbolStrategyConfig Strategy { get; set; } = new();
        public CompanyConfig Company { get; set; } = new();
        public DataConfig Data { get; set; } = new();
        public BacktestConfig Backtest { get; set; } = new();
    }
    
    public class AccountConfig
    {
        public decimal Equity { get; set; }
        public int Shares { get; set; }
        public int BaselineQty { get; set; }
    }
    
    public class TradingConfig
    {
        public TimeWindowConfig EntryWindow { get; set; } = new();
        public TimeWindowConfig ExitWindow { get; set; } = new();
        public OptionsConfig Options { get; set; } = new();
    }
    
    public class TimeWindowConfig
    {
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
    }
    
    public class OptionsConfig
    {
        public decimal PutDeltaMin { get; set; }
        public decimal PutDeltaMax { get; set; }
        public decimal CallDeltaMin { get; set; }
        public decimal CallDeltaMax { get; set; }
        public int DteMin { get; set; }
        public int DteMax { get; set; }
        public int DteOptimal { get; set; }
        public decimal StrikeInterval { get; set; }
        public decimal MinPremium { get; set; }
        public decimal MaxPremium { get; set; }
    }
    
    public class RiskConfig
    {
        public decimal MaxPositionSize { get; set; }
        public decimal CapitalAllocation { get; set; }
        public decimal MaxLossPerTrade { get; set; }
        public decimal MaxPortfolioDrawdown { get; set; }
        public decimal FintechCorrelationLimit { get; set; }
        public decimal AdtechCorrelationLimit { get; set; }
    }
    
    public class MarketConfig
    {
        public decimal VixLow { get; set; }
        public decimal VixNormal { get; set; }
        public decimal VixHigh { get; set; }
        public decimal VixCrisis { get; set; }
        public int EarningsBlackoutDays { get; set; }
        public decimal FintechSectorBeta { get; set; }
        public decimal AdtechSectorBeta { get; set; }
    }
    
    public class SymbolStrategyConfig
    {
        public decimal PutCreditSpreadWeight { get; set; }
        public decimal CoveredCallWeight { get; set; }
        public bool PreferMonthlyExpiration { get; set; }
        public bool AvoidWeeklyExpiration { get; set; }
        public decimal ProfitTarget { get; set; }
        public int EarlyCloseDte { get; set; }
    }
    
    public class CompanyConfig
    {
        public string Sector { get; set; } = "";
        public string MarketCap { get; set; } = "";
        public string EarningsFrequency { get; set; } = "";
        public List<string> KeyEvents { get; set; } = new();
    }
    
    public class DataConfig
    {
        public string Primary { get; set; } = "";
        public string Fallback { get; set; } = "";
    }
    
    public class BacktestConfig
    {
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public decimal InitialCapital { get; set; }
    }
}