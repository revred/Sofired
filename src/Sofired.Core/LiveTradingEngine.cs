using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sofired.Core
{
    /// <summary>
    /// Live trading engine for real-time options trading execution
    /// Handles order management, position tracking, and risk controls
    /// </summary>
    public class LiveTradingEngine
    {
        private readonly IBrokerClient _brokerClient;
        private readonly RealOptionsEngine _optionsEngine;
        private readonly AdvancedRiskManager _riskManager;
        private readonly EnhancedPnLEngine _pnlEngine;
        private readonly Dictionary<string, LivePosition> _livePositions;
        private readonly Queue<TradeOrder> _pendingOrders;
        private bool _isMarketOpen;
        private bool _isPaperTrading;

        public LiveTradingEngine(
            IBrokerClient brokerClient, 
            RealOptionsEngine optionsEngine,
            AdvancedRiskManager riskManager,
            EnhancedPnLEngine pnlEngine,
            bool paperTradingMode = true)
        {
            _brokerClient = brokerClient;
            _optionsEngine = optionsEngine;
            _riskManager = riskManager;
            _pnlEngine = pnlEngine;
            _livePositions = new Dictionary<string, LivePosition>();
            _pendingOrders = new Queue<TradeOrder>();
            _isPaperTrading = paperTradingMode;
            _isMarketOpen = false;
        }

        /// <summary>
        /// Initialize live trading session with pre-market checks
        /// </summary>
        public async Task<LiveTradingSession> StartTradingSession(
            List<string> symbols, 
            decimal accountValue,
            Dictionary<string, SymbolConfig> symbolConfigs)
        {
            var session = new LiveTradingSession
            {
                SessionId = Guid.NewGuid().ToString(),
                StartTime = DateTime.Now,
                InitialAccountValue = accountValue,
                CurrentAccountValue = accountValue,
                TradingSymbols = symbols,
                IsPaperTrading = _isPaperTrading,
                Status = TradingSessionStatus.Initializing
            };

            // Pre-market validation
            await ValidateAccountStatus();
            await ValidateMarketHours();
            await ValidateSymbolConfigurations(symbolConfigs);
            
            session.Status = TradingSessionStatus.Active;
            return session;
        }

        /// <summary>
        /// Execute put credit spread order with advanced risk controls
        /// </summary>
        public async Task<TradeExecutionResult> ExecutePutCreditSpread(
            string symbol,
            decimal stockPrice,
            DateTime expirationDate,
            decimal shortStrike,
            decimal longStrike,
            SymbolConfig symbolConfig,
            decimal accountValue)
        {
            try
            {
                // Pre-trade risk validation
                var riskCheck = await _riskManager.ValidateTradeRisk(
                    symbol, stockPrice, shortStrike, longStrike, accountValue, symbolConfig);
                
                if (!riskCheck.IsApproved)
                {
                    return new TradeExecutionResult
                    {
                        Success = false,
                        ErrorMessage = riskCheck.RejectionReason,
                        RiskWarnings = riskCheck.Warnings
                    };
                }

                // Get real-time options pricing
                var pricing = await _optionsEngine.GetPutSpreadPricing(
                    symbol, stockPrice, expirationDate, shortStrike, longStrike, DateTime.Now);

                // Create trade order
                var order = new TradeOrder
                {
                    OrderId = Guid.NewGuid().ToString(),
                    Symbol = symbol,
                    Strategy = "PutCreditSpread",
                    ShortStrike = shortStrike,
                    LongStrike = longStrike,
                    Quantity = riskCheck.RecommendedQuantity,
                    ExpectedCredit = pricing.NetCreditReceived,
                    ExpirationDate = expirationDate,
                    OrderType = OrderType.MarketOrder,
                    Status = OrderStatus.Pending,
                    CreatedAt = DateTime.Now
                };

                // Execute through broker or paper trading
                TradeExecutionResult result;
                if (_isPaperTrading)
                {
                    result = await ExecutePaperTrade(order, pricing);
                }
                else
                {
                    result = await ExecuteLiveTrade(order);
                }

                // Update position tracking
                if (result.Success)
                {
                    await UpdateLivePositions(result);
                }

                return result;
            }
            catch (Exception ex)
            {
                return new TradeExecutionResult
                {
                    Success = false,
                    ErrorMessage = $"Trade execution failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        /// <summary>
        /// Monitor and manage existing positions
        /// </summary>
        public async Task<PositionManagementResult> ManagePositions(decimal currentVix)
        {
            var managementActions = new List<PositionAction>();
            var totalPnL = 0m;

            foreach (var kvp in _livePositions)
            {
                var position = kvp.Value;
                var currentPrice = await _brokerClient.GetCurrentPrice(position.Symbol);
                
                // Calculate current P&L
                var positionPnL = _pnlEngine.CalculatePositionPnL(
                    position.ToPosition(), currentPrice, currentVix, DateTime.Now);
                
                totalPnL += positionPnL.TotalPnL;

                // Check for closing conditions
                var action = await EvaluatePositionAction(position, positionPnL, currentVix);
                if (action.ActionType != PositionActionType.Hold)
                {
                    managementActions.Add(action);
                    
                    if (action.ActionType == PositionActionType.Close)
                    {
                        await ClosePosition(position.PositionId, action.Reason);
                    }
                }
            }

            return new PositionManagementResult
            {
                TotalPnL = totalPnL,
                ActivePositions = _livePositions.Count,
                ManagementActions = managementActions,
                RiskMetrics = await CalculatePortfolioRisk()
            };
        }

        /// <summary>
        /// Emergency stop all trading and close positions
        /// </summary>
        public async Task<EmergencyStopResult> EmergencyStop(string reason)
        {
            var closedPositions = new List<string>();
            var errors = new List<string>();

            // Stop accepting new orders
            _isMarketOpen = false;

            try
            {
                // Close all open positions
                foreach (var kvp in _livePositions.ToList())
                {
                    try
                    {
                        await ClosePosition(kvp.Key, $"Emergency stop: {reason}");
                        closedPositions.Add(kvp.Key);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Failed to close position {kvp.Key}: {ex.Message}");
                    }
                }

                return new EmergencyStopResult
                {
                    Success = errors.Count == 0,
                    ClosedPositions = closedPositions,
                    Errors = errors,
                    StopReason = reason,
                    StopTime = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                errors.Add($"Emergency stop failed: {ex.Message}");
                return new EmergencyStopResult
                {
                    Success = false,
                    Errors = errors,
                    StopReason = reason,
                    StopTime = DateTime.Now
                };
            }
        }

        private async Task<TradeExecutionResult> ExecutePaperTrade(TradeOrder order, RealOptionsPricing pricing)
        {
            // Simulate paper trading execution
            await Task.Delay(100); // Simulate network latency

            var fillPrice = pricing.NetCreditReceived * (1 - 0.01m); // 1% slippage simulation
            
            return new TradeExecutionResult
            {
                Success = true,
                OrderId = order.OrderId,
                FillPrice = fillPrice,
                FillQuantity = order.Quantity,
                FillTime = DateTime.Now,
                Commission = CalculateCommission(order.Quantity),
                IsPaperTrade = true
            };
        }

        private async Task<TradeExecutionResult> ExecuteLiveTrade(TradeOrder order)
        {
            // Execute through actual broker
            return await _brokerClient.SubmitOrder(order);
        }

        private async Task<PositionAction> EvaluatePositionAction(
            LivePosition position, 
            PositionPnL pnL, 
            decimal currentVix)
        {
            // Profit taking at 50% of max profit
            if (pnL.TotalPnL >= position.MaxProfit * 0.5m)
            {
                return new PositionAction
                {
                    PositionId = position.PositionId,
                    ActionType = PositionActionType.Close,
                    Reason = "Profit target reached (50% of max profit)",
                    Priority = ActionPriority.High
                };
            }

            // Risk management: Close if loss exceeds 200% of credit received
            if (pnL.TotalPnL <= -(position.CreditReceived * 2.0m))
            {
                return new PositionAction
                {
                    PositionId = position.PositionId,
                    ActionType = PositionActionType.Close,
                    Reason = "Stop loss triggered (200% of credit)",
                    Priority = ActionPriority.Critical
                };
            }

            // Expiration management: Close within 7 DTE
            var daysToExpiration = (position.ExpirationDate - DateTime.Now).Days;
            if (daysToExpiration <= 7)
            {
                return new PositionAction
                {
                    PositionId = position.PositionId,
                    ActionType = PositionActionType.Close,
                    Reason = "Approaching expiration (7 DTE)",
                    Priority = ActionPriority.Medium
                };
            }

            return new PositionAction
            {
                PositionId = position.PositionId,
                ActionType = PositionActionType.Hold,
                Reason = "Position within normal parameters"
            };
        }

        private async Task ClosePosition(string positionId, string reason)
        {
            if (_livePositions.TryGetValue(positionId, out var position))
            {
                // Create closing order (reverse of opening)
                var closeOrder = new TradeOrder
                {
                    OrderId = Guid.NewGuid().ToString(),
                    Symbol = position.Symbol,
                    Strategy = "ClosePutCreditSpread",
                    ShortStrike = position.ShortStrike,
                    LongStrike = position.LongStrike,
                    Quantity = -position.Quantity, // Negative to close
                    OrderType = OrderType.MarketOrder,
                    Status = OrderStatus.Pending,
                    CreatedAt = DateTime.Now,
                    CloseReason = reason
                };

                TradeExecutionResult result;
                if (_isPaperTrading)
                {
                    // Simulate closing in paper trading
                    result = new TradeExecutionResult
                    {
                        Success = true,
                        OrderId = closeOrder.OrderId,
                        FillPrice = 0.05m, // Assume small cost to close
                        FillQuantity = Math.Abs(closeOrder.Quantity),
                        FillTime = DateTime.Now,
                        IsPaperTrade = true
                    };
                }
                else
                {
                    result = await _brokerClient.SubmitOrder(closeOrder);
                }

                if (result.Success)
                {
                    position.Status = PositionStatus.Closed;
                    position.CloseDate = DateTime.Now;
                    position.CloseReason = reason;
                    _livePositions.Remove(positionId);
                }
            }
        }

        private async Task UpdateLivePositions(TradeExecutionResult result)
        {
            var livePosition = new LivePosition
            {
                PositionId = result.OrderId,
                Symbol = result.Symbol ?? "",
                Quantity = result.FillQuantity,
                ShortStrike = result.ShortStrike ?? 0m,
                LongStrike = result.LongStrike ?? 0m,
                CreditReceived = result.FillPrice,
                OpenDate = result.FillTime,
                ExpirationDate = result.ExpirationDate ?? DateTime.Now.AddDays(30),
                Status = PositionStatus.Open,
                MaxProfit = result.FillPrice,
                MaxLoss = Math.Abs((result.ShortStrike ?? 0m) - (result.LongStrike ?? 0m)) - result.FillPrice
            };

            _livePositions[result.OrderId] = livePosition;
        }

        private async Task ValidateAccountStatus()
        {
            if (!_isPaperTrading)
            {
                var account = await _brokerClient.GetAccountInfo();
                if (!account.IsApproved || account.BuyingPower < 1000m)
                {
                    throw new InvalidOperationException("Account not ready for trading");
                }
            }
        }

        private async Task ValidateMarketHours()
        {
            var now = DateTime.Now;
            var marketOpen = now.Date.Add(new TimeSpan(9, 30, 0)); // 9:30 AM
            var marketClose = now.Date.Add(new TimeSpan(16, 0, 0)); // 4:00 PM
            
            _isMarketOpen = now >= marketOpen && now <= marketClose && 
                           now.DayOfWeek != DayOfWeek.Saturday && 
                           now.DayOfWeek != DayOfWeek.Sunday;

            if (!_isMarketOpen && !_isPaperTrading)
            {
                throw new InvalidOperationException("Market is closed");
            }
        }

        private async Task ValidateSymbolConfigurations(Dictionary<string, SymbolConfig> configs)
        {
            foreach (var config in configs)
            {
                if (config.Value.Risk.MaxPositionSize <= 0)
                {
                    throw new ArgumentException($"Invalid risk configuration for {config.Key}");
                }
            }
        }

        private async Task<PortfolioRiskMetrics> CalculatePortfolioRisk()
        {
            var totalDelta = 0m;
            var totalGamma = 0m;
            var totalTheta = 0m;
            var totalVega = 0m;

            foreach (var position in _livePositions.Values)
            {
                var currentPrice = await _brokerClient.GetCurrentPrice(position.Symbol);
                var pnl = _pnlEngine.CalculatePositionPnL(
                    position.ToPosition(), currentPrice, 22m, DateTime.Now);
                
                totalDelta += pnl.Delta;
                totalGamma += pnl.Gamma;
                totalTheta += pnl.Theta;
                totalVega += pnl.Vega;
            }

            return new PortfolioRiskMetrics
            {
                TotalDelta = totalDelta,
                TotalGamma = totalGamma,
                TotalTheta = totalTheta,
                TotalVega = totalVega,
                PositionCount = _livePositions.Count
            };
        }

        private decimal CalculateCommission(int quantity)
        {
            // $0.65 per contract typical for options
            return Math.Abs(quantity) * 0.65m;
        }
    }

    // Supporting classes and enums for live trading
    public class LiveTradingSession
    {
        public string SessionId { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public decimal InitialAccountValue { get; set; }
        public decimal CurrentAccountValue { get; set; }
        public List<string> TradingSymbols { get; set; } = new();
        public bool IsPaperTrading { get; set; }
        public TradingSessionStatus Status { get; set; }
    }

    public class TradeExecutionResult
    {
        public bool Success { get; set; }
        public string OrderId { get; set; } = "";
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
        public List<string> RiskWarnings { get; set; } = new();
        public Exception? Exception { get; set; }
    }

    public class LivePosition
    {
        public string PositionId { get; set; } = "";
        public string Symbol { get; set; } = "";
        public int Quantity { get; set; }
        public decimal ShortStrike { get; set; }
        public decimal LongStrike { get; set; }
        public decimal CreditReceived { get; set; }
        public DateTime OpenDate { get; set; }
        public DateTime? CloseDate { get; set; }
        public DateTime ExpirationDate { get; set; }
        public PositionStatus Status { get; set; }
        public decimal MaxProfit { get; set; }
        public decimal MaxLoss { get; set; }
        public string? CloseReason { get; set; }

        public Position ToPosition()
        {
            return new Position
            {
                Id = PositionId,
                Symbol = Symbol,
                StrategyType = "PutCreditSpread",
                Quantity = Quantity,
                EntryPrice = CreditReceived,
                ExpirationDate = ExpirationDate,
                OpenDate = OpenDate,
                IsOpen = Status == PositionStatus.Open,
                ShortStrike = ShortStrike,
                LongStrike = LongStrike
            };
        }
    }

    public enum TradingSessionStatus
    {
        Initializing,
        Active,
        Paused,
        Stopped,
        Error
    }


    public enum OrderStatus
    {
        Pending,
        Filled,
        PartiallyFilled,
        Cancelled,
        Rejected
    }

    public enum OrderType
    {
        MarketOrder,
        LimitOrder,
        StopOrder
    }

    public class PositionManagementResult
    {
        public decimal TotalPnL { get; set; }
        public int ActivePositions { get; set; }
        public List<PositionAction> ManagementActions { get; set; } = new();
        public PortfolioRiskMetrics RiskMetrics { get; set; } = new();
    }

    public class PositionAction
    {
        public string PositionId { get; set; } = "";
        public PositionActionType ActionType { get; set; }
        public string Reason { get; set; } = "";
        public ActionPriority Priority { get; set; }
    }

    public enum PositionActionType
    {
        Hold,
        Close,
        Adjust,
        Roll
    }

    public enum ActionPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class EmergencyStopResult
    {
        public bool Success { get; set; }
        public List<string> ClosedPositions { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public string StopReason { get; set; } = "";
        public DateTime StopTime { get; set; }
    }

    public class PortfolioRiskMetrics
    {
        public decimal TotalDelta { get; set; }
        public decimal TotalGamma { get; set; }
        public decimal TotalTheta { get; set; }
        public decimal TotalVega { get; set; }
        public int PositionCount { get; set; }
    }
}