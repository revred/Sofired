using System;
using System.Collections.Generic;
using System.Linq;

namespace Sofired.Core
{
    /// <summary>
    /// Advanced risk management system with real-time monitoring and dynamic adjustments
    /// Provides sophisticated position sizing, exposure limits, and risk controls
    /// </summary>
    public class AdvancedRiskManager
    {
        private readonly Dictionary<string, RiskMetrics> _symbolRiskMetrics;
        private readonly List<RiskAlert> _activeAlerts;
        private readonly Dictionary<string, decimal> _positionLimits;
        
        public AdvancedRiskManager()
        {
            _symbolRiskMetrics = new Dictionary<string, RiskMetrics>();
            _activeAlerts = new List<RiskAlert>();
            _positionLimits = new Dictionary<string, decimal>();
            
            InitializeRiskLimits();
        }
        
        /// <summary>
        /// Evaluate position sizing based on sophisticated risk models
        /// </summary>
        public PositionSizeRecommendation CalculateOptimalPositionSize(
            string symbol, 
            decimal accountValue, 
            decimal currentVix,
            PortfolioPnL portfolioPnL,
            SymbolConfig symbolConfig)
        {
            var recommendation = new PositionSizeRecommendation
            {
                Symbol = symbol,
                CalculationDate = DateTime.Now,
                AccountValue = accountValue,
                VixLevel = currentVix
            };
            
            // Base position size from configuration
            var baseSize = symbolConfig.Risk.CapitalAllocation * accountValue;
            
            // VIX adjustment
            var vixAdjustment = CalculateVixAdjustment(currentVix, symbolConfig);
            
            // Correlation adjustment
            var correlationAdjustment = CalculateCorrelationAdjustment(symbol, portfolioPnL);
            
            // Drawdown adjustment
            var drawdownAdjustment = CalculateDrawdownAdjustment(symbol);
            
            // Volatility adjustment based on historical performance
            var volatilityAdjustment = CalculateVolatilityAdjustment(symbol);
            
            // Kelly Criterion sizing
            var kellySize = CalculateKellyOptimalSize(symbol, accountValue);
            
            // Final position size calculation
            recommendation.BaseSize = baseSize;
            recommendation.VixAdjustment = vixAdjustment;
            recommendation.CorrelationAdjustment = correlationAdjustment;
            recommendation.DrawdownAdjustment = drawdownAdjustment;
            recommendation.VolatilityAdjustment = volatilityAdjustment;
            recommendation.KellySize = kellySize;
            
            // Apply all adjustments
            var adjustedSize = baseSize * vixAdjustment * correlationAdjustment * 
                             drawdownAdjustment * volatilityAdjustment;
            
            // Use Kelly criterion as a cap
            recommendation.RecommendedSize = Math.Min(adjustedSize, kellySize);
            
            // Apply absolute limits
            recommendation.RecommendedSize = Math.Min(
                recommendation.RecommendedSize, 
                accountValue * symbolConfig.Risk.MaxPositionSize);
            
            recommendation.RecommendedContracts = CalculateContractQuantity(
                recommendation.RecommendedSize, symbol);
                
            // Risk assessment
            recommendation.RiskLevel = AssessPositionRiskLevel(recommendation);
            recommendation.Warnings = GenerateRiskWarnings(recommendation, symbolConfig);
            
            return recommendation;
        }
        
        /// <summary>
        /// Monitor portfolio for risk limit breaches
        /// </summary>
        public List<RiskAlert> MonitorRiskLimits(PortfolioPnL portfolioPnL, 
            Dictionary<string, SymbolConfig> symbolConfigs, decimal totalAccountValue)
        {
            var alerts = new List<RiskAlert>();
            
            // Check portfolio-level risk limits
            alerts.AddRange(CheckPortfolioRiskLimits(portfolioPnL, totalAccountValue));
            
            // Check symbol-level risk limits
            foreach (var position in portfolioPnL.Positions)
            {
                if (symbolConfigs.ContainsKey(position.Symbol))
                {
                    alerts.AddRange(CheckSymbolRiskLimits(position, symbolConfigs[position.Symbol]));
                }
            }
            
            // Check Greeks exposure limits
            alerts.AddRange(CheckGreeksLimits(portfolioPnL));
            
            // Check concentration limits
            alerts.AddRange(CheckConcentrationLimits(portfolioPnL, totalAccountValue));
            
            // Check correlation limits
            alerts.AddRange(CheckCorrelationLimits(portfolioPnL));
            
            // Update active alerts
            UpdateActiveAlerts(alerts);
            
            return alerts;
        }
        
        /// <summary>
        /// Calculate VIX-based position sizing adjustment
        /// </summary>
        private decimal CalculateVixAdjustment(decimal currentVix, SymbolConfig config)
        {
            // Reduce size when VIX is high (market stress)
            if (currentVix >= config.Market.VixCrisis) return 0.3m;
            else if (currentVix >= config.Market.VixHigh) return 0.6m;
            else if (currentVix >= config.Market.VixNormal) return 0.8m;
            else if (currentVix >= config.Market.VixLow) return 1.0m;
            else return 1.2m; // Increase size in very low vol environment
        }
        
        /// <summary>
        /// Calculate correlation-based adjustment
        /// </summary>
        private decimal CalculateCorrelationAdjustment(string symbol, PortfolioPnL portfolioPnL)
        {
            if (portfolioPnL.Correlation == null || !portfolioPnL.Correlation.ContainsKey(symbol))
                return 1.0m;
            
            var avgCorrelation = portfolioPnL.Correlation.Values.Average();
            
            // Reduce size for highly correlated positions
            if (avgCorrelation > 0.8m) return 0.5m;
            else if (avgCorrelation > 0.6m) return 0.7m;
            else if (avgCorrelation > 0.4m) return 0.9m;
            else return 1.0m;
        }
        
        /// <summary>
        /// Calculate drawdown-based adjustment
        /// </summary>
        private decimal CalculateDrawdownAdjustment(string symbol)
        {
            if (!_symbolRiskMetrics.ContainsKey(symbol))
                return 1.0m;
            
            var drawdown = _symbolRiskMetrics[symbol].CurrentDrawdown;
            
            // Reduce size during drawdown periods
            if (drawdown > 0.2m) return 0.4m;
            else if (drawdown > 0.15m) return 0.6m;
            else if (drawdown > 0.1m) return 0.8m;
            else return 1.0m;
        }
        
        /// <summary>
        /// Calculate volatility-based adjustment
        /// </summary>
        private decimal CalculateVolatilityAdjustment(string symbol)
        {
            if (!_symbolRiskMetrics.ContainsKey(symbol))
                return 1.0m;
            
            var volatility = _symbolRiskMetrics[symbol].RollingVolatility;
            
            // Adjust for realized volatility
            if (volatility > 0.5m) return 0.6m;      // High vol - reduce size
            else if (volatility > 0.3m) return 0.8m; // Medium vol
            else return 1.0m;                        // Low vol
        }
        
        /// <summary>
        /// Calculate Kelly Criterion optimal position size
        /// </summary>
        private decimal CalculateKellyOptimalSize(string symbol, decimal accountValue)
        {
            if (!_symbolRiskMetrics.ContainsKey(symbol))
                return accountValue * 0.1m; // Conservative default
            
            var metrics = _symbolRiskMetrics[symbol];
            var winRate = metrics.WinRate;
            var avgWin = metrics.AverageWin;
            var avgLoss = Math.Abs(metrics.AverageLoss);
            
            if (avgLoss == 0 || winRate <= 0.5m) 
                return accountValue * 0.05m; // Very conservative
            
            // Kelly = (bp - q) / b where b = avg win / avg loss, p = win rate, q = loss rate
            var b = avgWin / avgLoss;
            var p = winRate;
            var q = 1 - winRate;
            
            var kelly = (b * p - q) / b;
            kelly = Math.Max(0, Math.Min(0.25m, kelly)); // Cap at 25%
            
            return accountValue * kelly;
        }
        
        /// <summary>
        /// Convert dollar size to contract quantity
        /// </summary>
        private int CalculateContractQuantity(decimal dollarSize, string symbol)
        {
            // Estimate contract value (simplified)
            var contractValue = symbol.ToUpper() switch
            {
                "AAPL" => 15000m,  // $150 stock * 100 shares
                "NVDA" => 40000m,  // $400 stock * 100 shares
                "TSLA" => 20000m,  // $200 stock * 100 shares
                "SOFI" => 1200m,   // $12 stock * 100 shares
                "APP" => 2500m,    // $25 stock * 100 shares
                _ => 10000m        // Default
            };
            
            var contracts = (int)(dollarSize / contractValue);
            return Math.Max(1, Math.Min(50, contracts)); // Min 1, Max 50 contracts
        }
        
        /// <summary>
        /// Assess risk level of position
        /// </summary>
        private RiskLevel AssessPositionRiskLevel(PositionSizeRecommendation recommendation)
        {
            var sizeRatio = recommendation.RecommendedSize / recommendation.AccountValue;
            
            if (sizeRatio > 0.2m) return RiskLevel.High;
            else if (sizeRatio > 0.1m) return RiskLevel.Medium;
            else return RiskLevel.Low;
        }
        
        /// <summary>
        /// Generate risk warnings for position
        /// </summary>
        private List<string> GenerateRiskWarnings(PositionSizeRecommendation recommendation, 
            SymbolConfig config)
        {
            var warnings = new List<string>();
            
            if (recommendation.VixAdjustment < 0.7m)
                warnings.Add($"High volatility environment (VIX {recommendation.VixLevel:F1}) - reduced position size");
            
            if (recommendation.CorrelationAdjustment < 0.8m)
                warnings.Add("High portfolio correlation - diversification limited");
            
            if (recommendation.DrawdownAdjustment < 0.8m)
                warnings.Add("Symbol experiencing drawdown - reduced sizing");
            
            if (recommendation.RiskLevel == RiskLevel.High)
                warnings.Add("HIGH RISK: Position exceeds 20% of account value");
            
            return warnings;
        }
        
        /// <summary>
        /// Check portfolio-level risk limits
        /// </summary>
        private List<RiskAlert> CheckPortfolioRiskLimits(PortfolioPnL portfolioPnL, decimal accountValue)
        {
            var alerts = new List<RiskAlert>();
            
            // Portfolio drawdown check
            if (portfolioPnL.MaxDrawdown > 0.25m) // 25% max drawdown
            {
                alerts.Add(new RiskAlert
                {
                    Type = RiskAlertType.MaxDrawdown,
                    Symbol = "PORTFOLIO",
                    Message = $"Portfolio drawdown {portfolioPnL.MaxDrawdown:P2} exceeds 25% limit",
                    Severity = AlertSeverity.Critical,
                    Value = portfolioPnL.MaxDrawdown,
                    Limit = 0.25m
                });
            }
            
            // Portfolio delta exposure check
            var deltaExposure = Math.Abs(portfolioPnL.PortfolioDelta) / accountValue;
            if (deltaExposure > 0.5m) // 50% delta exposure limit
            {
                alerts.Add(new RiskAlert
                {
                    Type = RiskAlertType.DeltaExposure,
                    Symbol = "PORTFOLIO",
                    Message = $"Portfolio delta exposure {deltaExposure:P2} exceeds 50% limit",
                    Severity = AlertSeverity.High,
                    Value = deltaExposure,
                    Limit = 0.5m
                });
            }
            
            return alerts;
        }
        
        /// <summary>
        /// Check symbol-level risk limits
        /// </summary>
        private List<RiskAlert> CheckSymbolRiskLimits(PositionPnL position, SymbolConfig config)
        {
            var alerts = new List<RiskAlert>();
            
            // Symbol drawdown check
            if (position.MaxDrawdown > config.Risk.MaxPortfolioDrawdown)
            {
                alerts.Add(new RiskAlert
                {
                    Type = RiskAlertType.MaxDrawdown,
                    Symbol = position.Symbol,
                    Message = $"{position.Symbol} drawdown {position.MaxDrawdown:P2} exceeds {config.Risk.MaxPortfolioDrawdown:P2} limit",
                    Severity = AlertSeverity.High,
                    Value = position.MaxDrawdown,
                    Limit = config.Risk.MaxPortfolioDrawdown
                });
            }
            
            // VaR check
            if (position.VaR95 > config.Risk.MaxLossPerTrade * 10000) // Scale for comparison
            {
                alerts.Add(new RiskAlert
                {
                    Type = RiskAlertType.VaR,
                    Symbol = position.Symbol,
                    Message = $"{position.Symbol} VaR95 ${position.VaR95:N0} exceeds risk limit",
                    Severity = AlertSeverity.Medium,
                    Value = position.VaR95,
                    Limit = config.Risk.MaxLossPerTrade * 10000
                });
            }
            
            return alerts;
        }
        
        /// <summary>
        /// Check Greeks exposure limits
        /// </summary>
        private List<RiskAlert> CheckGreeksLimits(PortfolioPnL portfolioPnL)
        {
            var alerts = new List<RiskAlert>();
            
            // Vega exposure check (volatility risk)
            if (Math.Abs(portfolioPnL.PortfolioVega) > 5000m)
            {
                alerts.Add(new RiskAlert
                {
                    Type = RiskAlertType.VegaExposure,
                    Symbol = "PORTFOLIO",
                    Message = $"Portfolio vega exposure ${portfolioPnL.PortfolioVega:N0} exceeds $5,000 limit",
                    Severity = AlertSeverity.Medium,
                    Value = Math.Abs(portfolioPnL.PortfolioVega),
                    Limit = 5000m
                });
            }
            
            // Theta exposure check (time decay benefit)
            if (portfolioPnL.PortfolioTheta < -1000m) // Negative theta exposure
            {
                alerts.Add(new RiskAlert
                {
                    Type = RiskAlertType.ThetaExposure,
                    Symbol = "PORTFOLIO",
                    Message = $"Portfolio theta exposure ${portfolioPnL.PortfolioTheta:N0} indicates high time decay risk",
                    Severity = AlertSeverity.Low,
                    Value = Math.Abs(portfolioPnL.PortfolioTheta),
                    Limit = 1000m
                });
            }
            
            return alerts;
        }
        
        /// <summary>
        /// Check concentration limits
        /// </summary>
        private List<RiskAlert> CheckConcentrationLimits(PortfolioPnL portfolioPnL, decimal totalAccountValue)
        {
            var alerts = new List<RiskAlert>();
            
            // Symbol concentration check
            var symbolExposures = portfolioPnL.Positions
                .GroupBy(p => p.Symbol)
                .Select(g => new { Symbol = g.Key, Exposure = g.Sum(p => Math.Abs(p.TotalPnL)) })
                .ToList();
            
            foreach (var exposure in symbolExposures)
            {
                var concentration = exposure.Exposure / totalAccountValue;
                if (concentration > 0.3m) // 30% concentration limit
                {
                    alerts.Add(new RiskAlert
                    {
                        Type = RiskAlertType.Concentration,
                        Symbol = exposure.Symbol,
                        Message = $"{exposure.Symbol} concentration {concentration:P2} exceeds 30% limit",
                        Severity = AlertSeverity.High,
                        Value = concentration,
                        Limit = 0.3m
                    });
                }
            }
            
            return alerts;
        }
        
        /// <summary>
        /// Check correlation limits
        /// </summary>
        private List<RiskAlert> CheckCorrelationLimits(PortfolioPnL portfolioPnL)
        {
            var alerts = new List<RiskAlert>();
            
            if (portfolioPnL.Correlation != null && portfolioPnL.Correlation.Count > 1)
            {
                var avgCorrelation = portfolioPnL.Correlation.Values.Average();
                if (avgCorrelation > 0.8m) // 80% average correlation limit
                {
                    alerts.Add(new RiskAlert
                    {
                        Type = RiskAlertType.Correlation,
                        Symbol = "PORTFOLIO",
                        Message = $"Average portfolio correlation {avgCorrelation:P2} exceeds 80% limit",
                        Severity = AlertSeverity.Medium,
                        Value = avgCorrelation,
                        Limit = 0.8m
                    });
                }
            }
            
            return alerts;
        }
        
        /// <summary>
        /// Update active alerts list
        /// </summary>
        private void UpdateActiveAlerts(List<RiskAlert> newAlerts)
        {
            // Clear old alerts
            _activeAlerts.RemoveAll(a => (DateTime.Now - a.Timestamp).TotalMinutes > 60);
            
            // Add new alerts
            foreach (var alert in newAlerts)
            {
                alert.Id = Guid.NewGuid().ToString();
                alert.Timestamp = DateTime.Now;
                _activeAlerts.Add(alert);
            }
        }
        
        /// <summary>
        /// Initialize risk limits for different symbols
        /// </summary>
        private void InitializeRiskLimits()
        {
            _positionLimits["AAPL"] = 0.3m;   // 30% max position
            _positionLimits["NVDA"] = 0.25m;  // 25% max position
            _positionLimits["TSLA"] = 0.15m;  // 15% max position (high vol)
            _positionLimits["SOFI"] = 0.2m;   // 20% max position
            _positionLimits["APP"] = 0.1m;    // 10% max position (smaller)
        }
        
        /// <summary>
        /// Get current active alerts
        /// </summary>
        public List<RiskAlert> GetActiveAlerts()
        {
            return _activeAlerts.ToList();
        }
        
        /// <summary>
        /// Update symbol risk metrics
        /// </summary>
        public void UpdateSymbolRiskMetrics(string symbol, PositionPnL positionPnL)
        {
            if (!_symbolRiskMetrics.ContainsKey(symbol))
            {
                _symbolRiskMetrics[symbol] = new RiskMetrics { Symbol = symbol };
            }
            
            var metrics = _symbolRiskMetrics[symbol];
            metrics.LastPnL = positionPnL.TotalPnL;
            metrics.CurrentDrawdown = positionPnL.MaxDrawdown;
            metrics.LastUpdate = DateTime.Now;
            
            // Update rolling metrics (simplified)
            metrics.RollingVolatility = Math.Abs(positionPnL.TotalPnL) / 1000m; // Simplified vol calc
        }
    }
    
    /// <summary>
    /// Position sizing recommendation
    /// </summary>
    public class PositionSizeRecommendation
    {
        public string Symbol { get; set; } = "";
        public DateTime CalculationDate { get; set; }
        public decimal AccountValue { get; set; }
        public decimal VixLevel { get; set; }
        
        // Size components
        public decimal BaseSize { get; set; }
        public decimal VixAdjustment { get; set; }
        public decimal CorrelationAdjustment { get; set; }
        public decimal DrawdownAdjustment { get; set; }
        public decimal VolatilityAdjustment { get; set; }
        public decimal KellySize { get; set; }
        
        // Final recommendation
        public decimal RecommendedSize { get; set; }
        public int RecommendedContracts { get; set; }
        public RiskLevel RiskLevel { get; set; }
        public List<string> Warnings { get; set; } = new();
    }
    
    /// <summary>
    /// Risk alert data
    /// </summary>
    public class RiskAlert
    {
        public string Id { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public RiskAlertType Type { get; set; }
        public string Symbol { get; set; } = "";
        public string Message { get; set; } = "";
        public AlertSeverity Severity { get; set; }
        public decimal Value { get; set; }
        public decimal Limit { get; set; }
    }
    
    /// <summary>
    /// Symbol risk metrics tracking
    /// </summary>
    public class RiskMetrics
    {
        public string Symbol { get; set; } = "";
        public decimal LastPnL { get; set; }
        public decimal CurrentDrawdown { get; set; }
        public decimal RollingVolatility { get; set; }
        public decimal WinRate { get; set; } = 0.6m;    // Default 60%
        public decimal AverageWin { get; set; } = 100m;  // Default $100
        public decimal AverageLoss { get; set; } = -60m; // Default -$60
        public DateTime LastUpdate { get; set; }
    }
    
    /// <summary>
    /// Risk level enumeration
    /// </summary>
    public enum RiskLevel
    {
        Low,
        Medium,
        High,
        Critical
    }
    
    /// <summary>
    /// Risk alert types
    /// </summary>
    public enum RiskAlertType
    {
        MaxDrawdown,
        DeltaExposure,
        VegaExposure,
        ThetaExposure,
        Concentration,
        Correlation,
        VaR,
        PositionSize
    }
    
    /// <summary>
    /// Alert severity levels
    /// </summary>
    public enum AlertSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
}