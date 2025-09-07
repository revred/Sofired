using System;
using System.Collections.Generic;
using System.Linq;

namespace Sofired.Core
{
    /// <summary>
    /// Enhanced trading session with comprehensive P&L tracking and risk analysis
    /// Contains advanced metrics and real-time monitoring capabilities
    /// </summary>
    public class EnhancedTradingSession
    {
        // Basic session data
        public DateTime Date { get; set; }
        public List<Position> Positions { get; set; } = new();
        public decimal DailyPremium { get; set; }
        public decimal WeeklyPremium { get; set; }
        public decimal MonthlyPremium { get; set; }
        public bool GoalsMet { get; set; }
        public int PositionsOpened { get; set; }
        public int PositionsClosed { get; set; }
        public decimal TotalPnL { get; set; }
        
        // Enhanced P&L tracking
        public PortfolioPnL PortfolioPnL { get; set; } = new();
        public List<PositionPnL> PositionPnLs { get; set; } = new();
        
        // Risk metrics
        public List<RiskAlert> RiskAlerts { get; set; } = new();
        public PositionSizeRecommendation SizeRecommendation { get; set; } = new();
        public RiskMetrics SessionRiskMetrics { get; set; } = new();
        
        // Greeks exposure
        public decimal SessionDelta { get; set; }
        public decimal SessionGamma { get; set; }
        public decimal SessionTheta { get; set; }
        public decimal SessionVega { get; set; }
        
        // Performance metrics
        public decimal SharpeRatio { get; set; }
        public decimal MaxDrawdown { get; set; }
        public decimal VaR95 { get; set; }
        public decimal VaR99 { get; set; }
        
        // Market context
        public decimal VixLevel { get; set; }
        public string MarketRegime { get; set; } = "";
        public Dictionary<string, decimal> CorrelationMatrix { get; set; } = new();
        
        // Enhanced position tracking
        public List<PositionLifecycle> PositionLifecycles { get; set; } = new();
        
        /// <summary>
        /// Calculate comprehensive session metrics
        /// </summary>
        public void CalculateSessionMetrics(EnhancedPnLEngine pnlEngine, 
            AdvancedRiskManager riskManager, decimal currentPrice, decimal vixLevel)
        {
            VixLevel = vixLevel;
            MarketRegime = DetermineMarketRegime(vixLevel);
            
            // Calculate P&L for all positions
            var currentPrices = new Dictionary<string, decimal>();
            
            // Extract symbols from positions and create price dictionary
            foreach (var position in Positions)
            {
                if (!currentPrices.ContainsKey(position.Symbol))
                {
                    currentPrices[position.Symbol] = currentPrice; // Simplified - would get real prices
                }
            }
            
            // Calculate portfolio P&L
            PortfolioPnL = pnlEngine.CalculatePortfolioPnL(Positions, currentPrices, vixLevel, Date);
            
            // Extract session-level Greeks
            SessionDelta = PortfolioPnL.PortfolioDelta;
            SessionGamma = PortfolioPnL.PortfolioGamma;
            SessionTheta = PortfolioPnL.PortfolioTheta;
            SessionVega = PortfolioPnL.PortfolioVega;
            
            // Session performance metrics
            SharpeRatio = PortfolioPnL.PortfolioSharpe;
            MaxDrawdown = PortfolioPnL.MaxDrawdown;
            
            // Risk alerts (would need symbol configs - simplified)
            // RiskAlerts = riskManager.MonitorRiskLimits(PortfolioPnL, symbolConfigs, accountValue);
            
            // Position lifecycles
            UpdatePositionLifecycles();
        }
        
        /// <summary>
        /// Determine market regime based on VIX
        /// </summary>
        private string DetermineMarketRegime(decimal vix)
        {
            return vix switch
            {
                < 15 => "Low Volatility",
                < 25 => "Normal Volatility", 
                < 35 => "High Volatility",
                < 50 => "Crisis Mode",
                _ => "Extreme Volatility"
            };
        }
        
        /// <summary>
        /// Update position lifecycles for tracking
        /// </summary>
        private void UpdatePositionLifecycles()
        {
            foreach (var position in Positions)
            {
                var lifecycle = PositionLifecycles.FirstOrDefault(p => p.PositionId == position.Id);
                if (lifecycle == null)
                {
                    lifecycle = new PositionLifecycle
                    {
                        PositionId = position.Id,
                        Symbol = position.Symbol,
                        EntryDate = position.OpenDate,
                        EntryPrice = position.EntryPrice,
                        Strategy = position.StrategyType,
                        InitialQuantity = position.Quantity
                    };
                    PositionLifecycles.Add(lifecycle);
                }
                
                // Update lifecycle data
                lifecycle.CurrentQuantity = position.Quantity;
                lifecycle.CurrentPnL = position.ProfitLoss.HasValue ? position.ProfitLoss.Value : 0m;
                lifecycle.DaysHeld = (Date - position.OpenDate).Days;
                lifecycle.Status = position.IsOpen ? EnhancedPositionStatus.Open : EnhancedPositionStatus.Closed;
                
                if (!position.IsOpen && position.CloseDate.HasValue)
                {
                    lifecycle.ExitDate = position.CloseDate;
                    lifecycle.ExitPrice = position.ClosePrice.HasValue ? position.ClosePrice.Value : 0m;
                    lifecycle.FinalPnL = position.ProfitLoss.HasValue ? position.ProfitLoss.Value : 0m;
                }
            }
        }
        
        /// <summary>
        /// Generate enhanced session report
        /// </summary>
        public string GenerateEnhancedReport()
        {
            var report = new System.Text.StringBuilder();
            
            report.AppendLine($"ENHANCED TRADING SESSION REPORT - {Date:yyyy-MM-dd}");
            report.AppendLine(new string('=', 60));
            report.AppendLine();
            
            // Market Context
            report.AppendLine("MARKET CONTEXT:");
            report.AppendLine($"VIX Level: {VixLevel:F1}");
            report.AppendLine($"Market Regime: {MarketRegime}");
            report.AppendLine();
            
            // Basic Session Metrics
            report.AppendLine("SESSION SUMMARY:");
            report.AppendLine($"Positions Opened: {PositionsOpened}");
            report.AppendLine($"Positions Closed: {PositionsClosed}");
            report.AppendLine($"Daily Premium: ${DailyPremium:N2}");
            report.AppendLine($"Total P&L: ${TotalPnL:N2}");
            report.AppendLine();
            
            // Enhanced P&L Analysis
            report.AppendLine("P&L BREAKDOWN:");
            report.AppendLine($"Realized P&L: ${PortfolioPnL.TotalRealizedPnL:N2}");
            report.AppendLine($"Unrealized P&L: ${PortfolioPnL.TotalUnrealizedPnL:N2}");
            report.AppendLine($"Total P&L: ${PortfolioPnL.TotalPnL:N2}");
            report.AppendLine();
            
            // Greeks Exposure
            report.AppendLine("GREEKS EXPOSURE:");
            report.AppendLine($"Delta: {SessionDelta:N0}");
            report.AppendLine($"Gamma: {SessionGamma:N2}");
            report.AppendLine($"Theta: ${SessionTheta:N0}");
            report.AppendLine($"Vega: ${SessionVega:N0}");
            report.AppendLine();
            
            // Risk Metrics
            report.AppendLine("RISK METRICS:");
            report.AppendLine($"Sharpe Ratio: {SharpeRatio:F2}");
            report.AppendLine($"Max Drawdown: {MaxDrawdown:P2}");
            report.AppendLine($"VaR 95%: ${VaR95:N0}");
            report.AppendLine($"VaR 99%: ${VaR99:N0}");
            report.AppendLine();
            
            // Risk Alerts
            if (RiskAlerts.Any())
            {
                report.AppendLine("RISK ALERTS:");
                foreach (var alert in RiskAlerts)
                {
                    report.AppendLine($"  {alert.Severity}: {alert.Message}");
                }
                report.AppendLine();
            }
            
            // Position Details
            if (PositionLifecycles.Any())
            {
                report.AppendLine("POSITION DETAILS:");
                report.AppendLine($"{"Symbol",-6} {"Strategy",-15} {"Days",-4} {"P&L",-10} {"Status",-8}");
                report.AppendLine(new string('-', 50));
                
                foreach (var pos in PositionLifecycles)
                {
                    report.AppendLine($"{pos.Symbol,-6} {pos.Strategy,-15} {pos.DaysHeld,-4} ${pos.CurrentPnL,-9:N0} {pos.Status,-8}");
                }
                report.AppendLine();
            }
            
            return report.ToString();
        }
        
        /// <summary>
        /// Get session risk score (0-100)
        /// </summary>
        public int GetRiskScore()
        {
            var score = 0;
            
            // VIX contribution (0-30 points)
            if (VixLevel > 40) score += 30;
            else if (VixLevel > 30) score += 20;
            else if (VixLevel > 20) score += 10;
            
            // Greeks exposure contribution (0-25 points)  
            var totalGreeksExposure = Math.Abs(SessionDelta) + Math.Abs(SessionVega / 100);
            if (totalGreeksExposure > 10000) score += 25;
            else if (totalGreeksExposure > 5000) score += 15;
            else if (totalGreeksExposure > 2000) score += 5;
            
            // Drawdown contribution (0-25 points)
            if (MaxDrawdown > 0.2m) score += 25;
            else if (MaxDrawdown > 0.15m) score += 15;
            else if (MaxDrawdown > 0.1m) score += 5;
            
            // Risk alerts contribution (0-20 points)
            var criticalAlerts = RiskAlerts.Count(a => a.Severity == AlertSeverity.Critical);
            var highAlerts = RiskAlerts.Count(a => a.Severity == AlertSeverity.High);
            score += criticalAlerts * 10 + highAlerts * 5;
            
            return Math.Min(100, score);
        }
        
        /// <summary>
        /// Check if session meets quality thresholds
        /// </summary>
        public SessionQuality GetSessionQuality()
        {
            var riskScore = GetRiskScore();
            var profitability = TotalPnL > 0;
            var efficiency = PositionsClosed > 0 ? TotalPnL / PositionsClosed : 0;
            
            if (riskScore > 70) return SessionQuality.Poor;
            if (riskScore > 50) return SessionQuality.Fair;
            if (profitability && efficiency > 50) return SessionQuality.Excellent;
            if (profitability) return SessionQuality.Good;
            
            return SessionQuality.Fair;
        }
    }
    
    /// <summary>
    /// Position lifecycle tracking
    /// </summary>
    public class PositionLifecycle
    {
        public string PositionId { get; set; } = "";
        public string Symbol { get; set; } = "";
        public DateTime EntryDate { get; set; }
        public DateTime? ExitDate { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public string Strategy { get; set; } = "";
        public int InitialQuantity { get; set; }
        public int CurrentQuantity { get; set; }
        public decimal CurrentPnL { get; set; }
        public decimal FinalPnL { get; set; }
        public int DaysHeld { get; set; }
        public EnhancedPositionStatus Status { get; set; }
        
        // Performance metrics
        public decimal ReturnOnCapital { get; set; }
        public decimal AnnualizedReturn { get; set; }
        public List<string> Notes { get; set; } = new();
    }
    
    /// <summary>
    /// Enhanced position status enumeration
    /// </summary>
    public enum EnhancedPositionStatus
    {
        Open,
        Closed,
        Expired,
        Assigned,
        Exercised
    }
    
    /// <summary>
    /// Session quality enumeration
    /// </summary>
    public enum SessionQuality
    {
        Poor,
        Fair,
        Good,
        Excellent
    }
}