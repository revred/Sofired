using System;
using System.Collections.Generic;
using System.Linq;

namespace Sofired.Core
{
    /// <summary>
    /// Enhanced P&L calculation engine with sophisticated risk metrics
    /// Provides real-time position tracking, Greeks exposure, and risk-adjusted returns
    /// </summary>
    public class EnhancedPnLEngine
    {
        private readonly Dictionary<string, List<PositionPnL>> _positionHistory;
        private readonly Dictionary<string, PortfolioMetrics> _portfolioMetrics;
        
        public EnhancedPnLEngine()
        {
            _positionHistory = new Dictionary<string, List<PositionPnL>>();
            _portfolioMetrics = new Dictionary<string, PortfolioMetrics>();
        }
        
        /// <summary>
        /// Calculate comprehensive P&L for a position with Greeks and risk metrics
        /// </summary>
        public PositionPnL CalculatePositionPnL(Position position, decimal currentUnderlyingPrice, 
            decimal currentVix, DateTime currentDate)
        {
            var pnl = new PositionPnL
            {
                PositionId = position.Id,
                Symbol = position.Symbol,
                CalculationDate = currentDate,
                UnderlyingPrice = currentUnderlyingPrice,
                VixLevel = currentVix
            };
            
            // Calculate intrinsic value changes
            pnl.IntrinsicValue = CalculateIntrinsicValue(position, currentUnderlyingPrice);
            pnl.TimeValue = CalculateTimeValue(position, currentDate);
            pnl.VolatilityValue = CalculateVolatilityValue(position, currentVix);
            
            // Greeks calculations
            pnl.Delta = CalculateDelta(position, currentUnderlyingPrice);
            pnl.Gamma = CalculateGamma(position, currentUnderlyingPrice);
            pnl.Theta = CalculateTheta(position, currentDate);
            pnl.Vega = CalculateVega(position, currentVix);
            
            // Total P&L components
            pnl.RealizedPnL = position.ProfitLoss ?? 0m;
            pnl.UnrealizedPnL = pnl.IntrinsicValue + pnl.TimeValue + pnl.VolatilityValue;
            pnl.TotalPnL = pnl.RealizedPnL + pnl.UnrealizedPnL;
            
            // Risk metrics
            pnl.MaxDrawdown = CalculateMaxDrawdown(position.Symbol);
            pnl.Sharpe = CalculateSharpeRatio(position.Symbol);
            pnl.VaR95 = CalculateValueAtRisk(position, 0.95m);
            pnl.VaR99 = CalculateValueAtRisk(position, 0.99m);
            
            // Track position history
            TrackPositionHistory(pnl);
            
            return pnl;
        }
        
        /// <summary>
        /// Calculate portfolio-level P&L and risk metrics
        /// </summary>
        public PortfolioPnL CalculatePortfolioPnL(List<Position> positions, 
            Dictionary<string, decimal> currentPrices, decimal currentVix, DateTime currentDate)
        {
            var portfolioPnL = new PortfolioPnL
            {
                CalculationDate = currentDate,
                VixLevel = currentVix,
                Positions = new List<PositionPnL>()
            };
            
            decimal totalRealizedPnL = 0m;
            decimal totalUnrealizedPnL = 0m;
            decimal totalDelta = 0m;
            decimal totalGamma = 0m;
            decimal totalTheta = 0m;
            decimal totalVega = 0m;
            
            foreach (var position in positions)
            {
                var price = currentPrices.ContainsKey(position.Symbol) ? currentPrices[position.Symbol] : 100m;
                var positionPnL = CalculatePositionPnL(position, price, currentVix, currentDate);
                
                portfolioPnL.Positions.Add(positionPnL);
                
                totalRealizedPnL += positionPnL.RealizedPnL;
                totalUnrealizedPnL += positionPnL.UnrealizedPnL;
                totalDelta += positionPnL.Delta;
                totalGamma += positionPnL.Gamma;
                totalTheta += positionPnL.Theta;
                totalVega += positionPnL.Vega;
            }
            
            portfolioPnL.TotalRealizedPnL = totalRealizedPnL;
            portfolioPnL.TotalUnrealizedPnL = totalUnrealizedPnL;
            portfolioPnL.TotalPnL = totalRealizedPnL + totalUnrealizedPnL;
            portfolioPnL.PortfolioDelta = totalDelta;
            portfolioPnL.PortfolioGamma = totalGamma;
            portfolioPnL.PortfolioTheta = totalTheta;
            portfolioPnL.PortfolioVega = totalVega;
            
            // Portfolio-level risk metrics
            portfolioPnL.PortfolioSharpe = CalculatePortfolioSharpe();
            portfolioPnL.MaxDrawdown = CalculatePortfolioMaxDrawdown();
            portfolioPnL.BetaToSPY = CalculateBetaToSPY();
            portfolioPnL.Correlation = CalculateSymbolCorrelations();
            
            return portfolioPnL;
        }
        
        /// <summary>
        /// Calculate position Greeks - Delta exposure
        /// </summary>
        private decimal CalculateDelta(Position position, decimal underlyingPrice)
        {
            if (position.StrategyType == "PutCreditSpread")
            {
                // Put credit spread delta approximation
                var shortStrike = position.ShortStrike ?? 0m;
                var longStrike = position.LongStrike ?? 0m;
                
                // Estimate delta based on moneyness
                var shortDelta = EstimatePutDelta(underlyingPrice, shortStrike);
                var longDelta = EstimatePutDelta(underlyingPrice, longStrike);
                
                return (shortDelta - longDelta) * position.Quantity;
            }
            else if (position.StrategyType == "CoveredCall")
            {
                // Covered call: Long stock + Short call
                var callStrike = position.ShortStrike ?? 0m;
                var stockDelta = position.Quantity; // Stock delta = 1 per share
                var callDelta = -EstimateCallDelta(underlyingPrice, callStrike) * position.Quantity;
                
                return stockDelta + callDelta;
            }
            
            return 0m;
        }
        
        /// <summary>
        /// Estimate put option delta based on moneyness
        /// </summary>
        private decimal EstimatePutDelta(decimal spotPrice, decimal strike)
        {
            var moneyness = spotPrice / strike;
            
            // Simple delta approximation based on moneyness
            if (moneyness > 1.1m) return -0.05m;  // Deep OTM
            else if (moneyness > 1.05m) return -0.15m; // OTM
            else if (moneyness > 0.95m) return -0.45m; // ATM
            else if (moneyness > 0.9m) return -0.75m;  // ITM
            else return -0.95m; // Deep ITM
        }
        
        /// <summary>
        /// Estimate call option delta based on moneyness
        /// </summary>
        private decimal EstimateCallDelta(decimal spotPrice, decimal strike)
        {
            var moneyness = spotPrice / strike;
            
            // Simple delta approximation for calls
            if (moneyness < 0.9m) return 0.05m;   // Deep OTM
            else if (moneyness < 0.95m) return 0.15m; // OTM
            else if (moneyness < 1.05m) return 0.45m; // ATM
            else if (moneyness < 1.1m) return 0.75m;  // ITM
            else return 0.95m; // Deep ITM
        }
        
        /// <summary>
        /// Calculate Gamma exposure
        /// </summary>
        private decimal CalculateGamma(Position position, decimal underlyingPrice)
        {
            // Simplified gamma calculation
            // Gamma is highest ATM and decreases as options move ITM/OTM
            var avgStrike = ((position.ShortStrike ?? 0m) + (position.LongStrike ?? 0m)) / 2m;
            var moneyness = underlyingPrice / avgStrike;
            
            // Gamma approximation
            if (Math.Abs(moneyness - 1m) < 0.05m) return 0.05m * position.Quantity; // ATM
            else if (Math.Abs(moneyness - 1m) < 0.1m) return 0.03m * position.Quantity; // Near ATM
            else return 0.01m * position.Quantity; // Away from ATM
        }
        
        /// <summary>
        /// Calculate Theta (time decay)
        /// </summary>
        private decimal CalculateTheta(Position position, DateTime currentDate)
        {
            var daysToExpiration = (position.ExpirationDate - currentDate).Days;
            
            if (daysToExpiration <= 0) return 0m;
            
            // Theta increases (more negative) as expiration approaches
            var theta = position.StrategyType switch
            {
                "PutCreditSpread" => 0.05m / Math.Max(1, daysToExpiration) * position.Quantity,
                "CoveredCall" => 0.03m / Math.Max(1, daysToExpiration) * position.Quantity,
                _ => 0m
            };
            
            return theta;
        }
        
        /// <summary>
        /// Calculate Vega (volatility sensitivity)
        /// </summary>
        private decimal CalculateVega(Position position, decimal currentVix)
        {
            // Vega is highest for ATM options and decreases for ITM/OTM
            // Credit spreads are typically negative vega (benefit from vol decrease)
            
            var vega = position.StrategyType switch
            {
                "PutCreditSpread" => -0.02m * currentVix * position.Quantity,
                "CoveredCall" => -0.01m * currentVix * position.Quantity,
                _ => 0m
            };
            
            return vega;
        }
        
        /// <summary>
        /// Calculate intrinsic value component of P&L
        /// </summary>
        private decimal CalculateIntrinsicValue(Position position, decimal currentPrice)
        {
            var intrinsicValue = 0m;
            
            if (position.StrategyType == "PutCreditSpread")
            {
                var shortStrike = position.ShortStrike ?? 0m;
                var longStrike = position.LongStrike ?? 0m;
                
                // Put credit spread intrinsic value
                var shortIntrinsic = Math.Max(0, shortStrike - currentPrice);
                var longIntrinsic = Math.Max(0, longStrike - currentPrice);
                intrinsicValue = (longIntrinsic - shortIntrinsic) * position.Quantity * 100m;
            }
            else if (position.StrategyType == "CoveredCall")
            {
                var callStrike = position.ShortStrike ?? 0m;
                var callIntrinsic = Math.Max(0, currentPrice - callStrike);
                intrinsicValue = -callIntrinsic * position.Quantity * 100m; // Short call
            }
            
            return intrinsicValue;
        }
        
        /// <summary>
        /// Calculate time value component
        /// </summary>
        private decimal CalculateTimeValue(Position position, DateTime currentDate)
        {
            var daysToExpiration = (position.ExpirationDate - currentDate).Days;
            if (daysToExpiration <= 0) return 0m;
            
            // Time value decays exponentially
            var timeValueFactor = Math.Exp(-0.1 * (30 - daysToExpiration) / 30.0);
            
            return (decimal)timeValueFactor * 50m * position.Quantity; // Estimated time value
        }
        
        /// <summary>
        /// Calculate volatility component
        /// </summary>
        private decimal CalculateVolatilityValue(Position position, decimal currentVix)
        {
            // Volatility impact on option values
            var volImpact = (currentVix - 20m) / 100m; // Normalized VIX impact
            
            return position.StrategyType switch
            {
                "PutCreditSpread" => -volImpact * 30m * position.Quantity, // Negative vega
                "CoveredCall" => -volImpact * 20m * position.Quantity,     // Negative vega
                _ => 0m
            };
        }
        
        /// <summary>
        /// Calculate Value at Risk
        /// </summary>
        private decimal CalculateValueAtRisk(Position position, decimal confidenceLevel)
        {
            // Simplified VaR calculation based on position Greeks and market volatility
            var delta = CalculateDelta(position, 100m); // Normalized
            var gamma = CalculateGamma(position, 100m);
            
            // Assume 2% daily stock move for VaR calculation
            var stockMoveVaR = confidenceLevel == 0.95m ? 0.02m : 0.03m;
            
            // Delta VaR + Gamma VaR
            var deltaVaR = Math.Abs(delta) * stockMoveVaR * 100m; // Per $100 stock price
            var gammaVaR = 0.5m * Math.Abs(gamma) * stockMoveVaR * stockMoveVaR * 100m * 100m;
            
            return deltaVaR + gammaVaR;
        }
        
        /// <summary>
        /// Track position history for metrics calculation
        /// </summary>
        private void TrackPositionHistory(PositionPnL positionPnL)
        {
            if (!_positionHistory.ContainsKey(positionPnL.Symbol))
            {
                _positionHistory[positionPnL.Symbol] = new List<PositionPnL>();
            }
            
            _positionHistory[positionPnL.Symbol].Add(positionPnL);
            
            // Keep only last 252 days (1 year)
            if (_positionHistory[positionPnL.Symbol].Count > 252)
            {
                _positionHistory[positionPnL.Symbol].RemoveAt(0);
            }
        }
        
        /// <summary>
        /// Calculate maximum drawdown for a symbol
        /// </summary>
        private decimal CalculateMaxDrawdown(string symbol)
        {
            if (!_positionHistory.ContainsKey(symbol) || _positionHistory[symbol].Count < 2)
                return 0m;
            
            var history = _positionHistory[symbol];
            var peak = history[0].TotalPnL;
            var maxDrawdown = 0m;
            
            foreach (var pnl in history)
            {
                if (pnl.TotalPnL > peak)
                    peak = pnl.TotalPnL;
                
                var drawdown = (peak - pnl.TotalPnL) / Math.Max(Math.Abs(peak), 1m);
                if (drawdown > maxDrawdown)
                    maxDrawdown = drawdown;
            }
            
            return maxDrawdown;
        }
        
        /// <summary>
        /// Calculate Sharpe ratio for a symbol
        /// </summary>
        private decimal CalculateSharpeRatio(string symbol)
        {
            if (!_positionHistory.ContainsKey(symbol) || _positionHistory[symbol].Count < 10)
                return 0m;
            
            var returns = _positionHistory[symbol]
                .Select(p => p.TotalPnL)
                .ToList();
            
            if (returns.Count < 2) return 0m;
            
            var avgReturn = returns.Average();
            var variance = returns.Select(r => Math.Pow((double)(r - avgReturn), 2)).Average();
            var stdDev = (decimal)Math.Sqrt(variance);
            
            return stdDev > 0 ? (avgReturn - 0.02m) / stdDev : 0m; // Assume 2% risk-free rate
        }
        
        /// <summary>
        /// Calculate portfolio-level Sharpe ratio
        /// </summary>
        private decimal CalculatePortfolioSharpe()
        {
            var allReturns = _positionHistory.Values
                .SelectMany(history => history.Select(p => p.TotalPnL))
                .ToList();
            
            if (allReturns.Count < 10) return 0m;
            
            var avgReturn = allReturns.Average();
            var variance = allReturns.Select(r => Math.Pow((double)(r - avgReturn), 2)).Average();
            var stdDev = (decimal)Math.Sqrt(variance);
            
            return stdDev > 0 ? (avgReturn - 0.02m) / stdDev : 0m;
        }
        
        /// <summary>
        /// Calculate portfolio maximum drawdown
        /// </summary>
        private decimal CalculatePortfolioMaxDrawdown()
        {
            var portfolioValues = new List<decimal>();
            
            // Aggregate daily portfolio values
            var allDates = _positionHistory.Values
                .SelectMany(history => history.Select(p => p.CalculationDate))
                .Distinct()
                .OrderBy(d => d)
                .ToList();
            
            foreach (var date in allDates)
            {
                var dailyTotal = _positionHistory.Values
                    .SelectMany(history => history.Where(p => p.CalculationDate.Date == date.Date))
                    .Sum(p => p.TotalPnL);
                
                portfolioValues.Add(dailyTotal);
            }
            
            if (portfolioValues.Count < 2) return 0m;
            
            var peak = portfolioValues[0];
            var maxDrawdown = 0m;
            
            foreach (var value in portfolioValues)
            {
                if (value > peak) peak = value;
                
                var drawdown = (peak - value) / Math.Max(Math.Abs(peak), 1m);
                if (drawdown > maxDrawdown) maxDrawdown = drawdown;
            }
            
            return maxDrawdown;
        }
        
        /// <summary>
        /// Calculate beta to SPY (simplified)
        /// </summary>
        private decimal CalculateBetaToSPY()
        {
            // Simplified beta calculation
            // In reality, would correlate portfolio returns with SPY returns
            return 0.8m; // Assume conservative beta for options strategies
        }
        
        /// <summary>
        /// Calculate symbol correlations
        /// </summary>
        private Dictionary<string, decimal> CalculateSymbolCorrelations()
        {
            var correlations = new Dictionary<string, decimal>();
            var symbols = _positionHistory.Keys.ToList();
            
            foreach (var symbol in symbols)
            {
                // Simplified correlation - in reality would calculate between symbols
                correlations[symbol] = 0.6m; // Assume moderate correlation
            }
            
            return correlations;
        }
        
        /// <summary>
        /// Generate enhanced P&L report
        /// </summary>
        public string GeneratePnLReport(PortfolioPnL portfolioPnL)
        {
            var report = new System.Text.StringBuilder();
            
            report.AppendLine("ENHANCED P&L ANALYSIS REPORT");
            report.AppendLine(new string('=', 50));
            report.AppendLine($"Date: {portfolioPnL.CalculationDate:yyyy-MM-dd}");
            report.AppendLine($"VIX Level: {portfolioPnL.VixLevel:F1}");
            report.AppendLine();
            
            report.AppendLine("PORTFOLIO SUMMARY:");
            report.AppendLine($"Total Realized P&L: ${portfolioPnL.TotalRealizedPnL:N0}");
            report.AppendLine($"Total Unrealized P&L: ${portfolioPnL.TotalUnrealizedPnL:N0}");
            report.AppendLine($"Total P&L: ${portfolioPnL.TotalPnL:N0}");
            report.AppendLine();
            
            report.AppendLine("GREEKS EXPOSURE:");
            report.AppendLine($"Portfolio Delta: {portfolioPnL.PortfolioDelta:N0}");
            report.AppendLine($"Portfolio Gamma: {portfolioPnL.PortfolioGamma:N2}");
            report.AppendLine($"Portfolio Theta: ${portfolioPnL.PortfolioTheta:N0}");
            report.AppendLine($"Portfolio Vega: ${portfolioPnL.PortfolioVega:N0}");
            report.AppendLine();
            
            report.AppendLine("RISK METRICS:");
            report.AppendLine($"Sharpe Ratio: {portfolioPnL.PortfolioSharpe:F2}");
            report.AppendLine($"Max Drawdown: {portfolioPnL.MaxDrawdown:P2}");
            report.AppendLine($"Beta to SPY: {portfolioPnL.BetaToSPY:F2}");
            report.AppendLine();
            
            return report.ToString();
        }
    }
    
    /// <summary>
    /// Enhanced position-level P&L data
    /// </summary>
    public class PositionPnL
    {
        public string PositionId { get; set; } = "";
        public string Symbol { get; set; } = "";
        public DateTime CalculationDate { get; set; }
        public decimal UnderlyingPrice { get; set; }
        public decimal VixLevel { get; set; }
        
        // P&L Components
        public decimal RealizedPnL { get; set; }
        public decimal UnrealizedPnL { get; set; }
        public decimal TotalPnL { get; set; }
        public decimal IntrinsicValue { get; set; }
        public decimal TimeValue { get; set; }
        public decimal VolatilityValue { get; set; }
        
        // Greeks
        public decimal Delta { get; set; }
        public decimal Gamma { get; set; }
        public decimal Theta { get; set; }
        public decimal Vega { get; set; }
        
        // Risk Metrics
        public decimal MaxDrawdown { get; set; }
        public decimal Sharpe { get; set; }
        public decimal VaR95 { get; set; }
        public decimal VaR99 { get; set; }
    }
    
    /// <summary>
    /// Portfolio-level P&L data
    /// </summary>
    public class PortfolioPnL
    {
        public DateTime CalculationDate { get; set; }
        public decimal VixLevel { get; set; }
        public List<PositionPnL> Positions { get; set; } = new();
        
        // Aggregated P&L
        public decimal TotalRealizedPnL { get; set; }
        public decimal TotalUnrealizedPnL { get; set; }
        public decimal TotalPnL { get; set; }
        
        // Portfolio Greeks
        public decimal PortfolioDelta { get; set; }
        public decimal PortfolioGamma { get; set; }
        public decimal PortfolioTheta { get; set; }
        public decimal PortfolioVega { get; set; }
        
        // Risk Metrics
        public decimal PortfolioSharpe { get; set; }
        public decimal MaxDrawdown { get; set; }
        public decimal BetaToSPY { get; set; }
        public Dictionary<string, decimal> Correlation { get; set; } = new();
    }
    
    /// <summary>
    /// Portfolio metrics tracking
    /// </summary>
    public class PortfolioMetrics
    {
        public string Symbol { get; set; } = "";
        public decimal TotalPnL { get; set; }
        public decimal Sharpe { get; set; }
        public decimal MaxDrawdown { get; set; }
        public int TotalTrades { get; set; }
        public DateTime LastUpdate { get; set; }
    }
}