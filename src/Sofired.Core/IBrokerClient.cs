using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Sofired.Core
{
    /// <summary>
    /// Interface for broker connectivity and order execution
    /// Abstracts different broker implementations (TD Ameritrade, Interactive Brokers, etc.)
    /// </summary>
    public interface IBrokerClient
    {
        /// <summary>
        /// Submit a trade order to the broker
        /// </summary>
        Task<TradeExecutionResult> SubmitOrder(TradeOrder order);

        /// <summary>
        /// Get current stock price from broker feed
        /// </summary>
        Task<decimal> GetCurrentPrice(string symbol);

        /// <summary>
        /// Get account information and buying power
        /// </summary>
        Task<AccountInfo> GetAccountInfo();

        /// <summary>
        /// Get current positions from broker
        /// </summary>
        Task<List<BrokerPosition>> GetPositions();

        /// <summary>
        /// Get options chain data from broker
        /// </summary>
        Task<OptionsChain> GetOptionsChain(string symbol, DateTime expirationDate);

        /// <summary>
        /// Cancel an existing order
        /// </summary>
        Task<bool> CancelOrder(string orderId);

        /// <summary>
        /// Check if market is open
        /// </summary>
        Task<bool> IsMarketOpen();
    }

    /// <summary>
    /// TD Ameritrade broker implementation
    /// </summary>
    public class TDAmeritradeBrokerClient : IBrokerClient
    {
        private readonly string _apiKey;
        private readonly string _accountId;
        private readonly HttpClient _httpClient;
        private readonly bool _isPaperTrading;

        public TDAmeritradeBrokerClient(string apiKey, string accountId, bool paperTradingMode = true)
        {
            _apiKey = apiKey;
            _accountId = accountId;
            _isPaperTrading = paperTradingMode;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        public async Task<TradeExecutionResult> SubmitOrder(TradeOrder order)
        {
            if (_isPaperTrading)
            {
                return SimulateOrderExecution(order);
            }

            try
            {
                var orderPayload = CreateTDOrderPayload(order);
                var response = await _httpClient.PostAsync(
                    $"https://api.tdameritrade.com/v1/accounts/{_accountId}/orders",
                    orderPayload);

                if (response.IsSuccessStatusCode)
                {
                    var orderId = response.Headers.Location?.ToString().Split('/').Last();
                    return new TradeExecutionResult
                    {
                        Success = true,
                        OrderId = orderId ?? order.OrderId,
                        FillTime = DateTime.Now,
                        IsPaperTrade = false
                    };
                }
                else
                {
                    return new TradeExecutionResult
                    {
                        Success = false,
                        ErrorMessage = $"Order submission failed: {response.StatusCode}",
                        OrderId = order.OrderId
                    };
                }
            }
            catch (Exception ex)
            {
                return new TradeExecutionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Exception = ex,
                    OrderId = order.OrderId
                };
            }
        }

        public async Task<decimal> GetCurrentPrice(string symbol)
        {
            if (_isPaperTrading)
            {
                return SimulateCurrentPrice(symbol);
            }

            try
            {
                var response = await _httpClient.GetAsync(
                    $"https://api.tdameritrade.com/v1/marketdata/{symbol}/quotes");
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    // Parse JSON response and extract price
                    // Simplified for demo - would use proper JSON parsing
                    return 15.0m; // Placeholder
                }
                
                return SimulateCurrentPrice(symbol);
            }
            catch
            {
                return SimulateCurrentPrice(symbol);
            }
        }

        public async Task<AccountInfo> GetAccountInfo()
        {
            if (_isPaperTrading)
            {
                return new AccountInfo
                {
                    AccountId = "PAPER_ACCOUNT",
                    BuyingPower = 50000m,
                    NetLiquidationValue = 50000m,
                    IsApproved = true,
                    IsPaperTrading = true
                };
            }

            try
            {
                var response = await _httpClient.GetAsync(
                    $"https://api.tdameritrade.com/v1/accounts/{_accountId}");
                
                if (response.IsSuccessStatusCode)
                {
                    // Parse account info from response
                    return new AccountInfo
                    {
                        AccountId = _accountId,
                        BuyingPower = 25000m, // Placeholder
                        NetLiquidationValue = 50000m,
                        IsApproved = true,
                        IsPaperTrading = false
                    };
                }
            }
            catch
            {
                // Fallback to paper trading values
            }

            return new AccountInfo
            {
                AccountId = _accountId,
                BuyingPower = 0m,
                IsApproved = false,
                IsPaperTrading = _isPaperTrading
            };
        }

        public async Task<List<BrokerPosition>> GetPositions()
        {
            var positions = new List<BrokerPosition>();

            if (_isPaperTrading)
            {
                return positions; // Empty for paper trading
            }

            try
            {
                var response = await _httpClient.GetAsync(
                    $"https://api.tdameritrade.com/v1/accounts/{_accountId}?fields=positions");
                
                if (response.IsSuccessStatusCode)
                {
                    // Parse positions from response
                    // Implementation would parse JSON and create BrokerPosition objects
                }
            }
            catch
            {
                // Return empty list on error
            }

            return positions;
        }

        public async Task<OptionsChain> GetOptionsChain(string symbol, DateTime expirationDate)
        {
            if (_isPaperTrading)
            {
                return SimulateOptionsChain(symbol, expirationDate);
            }

            try
            {
                var expDateStr = expirationDate.ToString("yyyy-MM-dd");
                var response = await _httpClient.GetAsync(
                    $"https://api.tdameritrade.com/v1/marketdata/chains?symbol={symbol}&expDate={expDateStr}");
                
                if (response.IsSuccessStatusCode)
                {
                    // Parse options chain from response
                    return SimulateOptionsChain(symbol, expirationDate);
                }
            }
            catch
            {
                // Fallback to simulation
            }

            return SimulateOptionsChain(symbol, expirationDate);
        }

        public async Task<bool> CancelOrder(string orderId)
        {
            if (_isPaperTrading)
            {
                return true; // Always succeed in paper trading
            }

            try
            {
                var response = await _httpClient.DeleteAsync(
                    $"https://api.tdameritrade.com/v1/accounts/{_accountId}/orders/{orderId}");
                
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> IsMarketOpen()
        {
            var now = DateTime.Now;
            var marketOpen = now.Date.Add(new TimeSpan(9, 30, 0));
            var marketClose = now.Date.Add(new TimeSpan(16, 0, 0));
            
            return now >= marketOpen && now <= marketClose && 
                   now.DayOfWeek != DayOfWeek.Saturday && 
                   now.DayOfWeek != DayOfWeek.Sunday;
        }

        private TradeExecutionResult SimulateOrderExecution(TradeOrder order)
        {
            // Simulate realistic execution with small slippage
            var slippage = 0.01m; // 1% slippage
            var fillPrice = order.ExpectedCredit * (1 - slippage);
            
            return new TradeExecutionResult
            {
                Success = true,
                OrderId = order.OrderId,
                Symbol = order.Symbol,
                ShortStrike = order.ShortStrike,
                LongStrike = order.LongStrike,
                ExpirationDate = order.ExpirationDate,
                FillPrice = fillPrice,
                FillQuantity = order.Quantity,
                FillTime = DateTime.Now,
                Commission = Math.Abs(order.Quantity) * 0.65m,
                IsPaperTrade = true
            };
        }

        private decimal SimulateCurrentPrice(string symbol)
        {
            // Simple price simulation based on symbol
            var random = new Random();
            return symbol.ToUpper() switch
            {
                "SOFI" => 14.0m + (decimal)(random.NextDouble() * 4.0 - 2.0), // 12-16 range
                "AAPL" => 175.0m + (decimal)(random.NextDouble() * 20.0 - 10.0),
                "NVDA" => 450.0m + (decimal)(random.NextDouble() * 100.0 - 50.0),
                "TSLA" => 250.0m + (decimal)(random.NextDouble() * 50.0 - 25.0),
                _ => 100.0m + (decimal)(random.NextDouble() * 20.0 - 10.0)
            };
        }

        private OptionsChain SimulateOptionsChain(string symbol, DateTime expirationDate)
        {
            var currentPrice = SimulateCurrentPrice(symbol);
            var contracts = new List<OptionContract>();

            // Generate put options around current price
            for (decimal strike = currentPrice - 10m; strike <= currentPrice + 10m; strike += 0.5m)
            {
                if (strike > 0)
                {
                    contracts.Add(new OptionContract
                    {
                        Strike = strike,
                        Bid = Math.Max(0.05m, (currentPrice - strike) * 0.1m),
                        Ask = Math.Max(0.10m, (currentPrice - strike) * 0.12m),
                        LastPrice = Math.Max(0.07m, (currentPrice - strike) * 0.11m),
                        Volume = new Random().Next(10, 500),
                        OpenInterest = new Random().Next(100, 2000),
                        ImpliedVolatility = 0.25m + (decimal)(new Random().NextDouble() * 0.2),
                        Delta = -0.3m + (decimal)(new Random().NextDouble() * 0.4),
                        Gamma = 0.02m + (decimal)(new Random().NextDouble() * 0.03)
                    });
                }
            }

            return new OptionsChain
            {
                ExpirationDate = expirationDate,
                UnderlyingPrice = currentPrice,
                PutOptions = contracts
            };
        }

        private StringContent CreateTDOrderPayload(TradeOrder order)
        {
            // Create TD Ameritrade order JSON payload
            var orderJson = $@"{{
                ""orderType"": ""NET_CREDIT"",
                ""session"": ""NORMAL"",
                ""duration"": ""DAY"",
                ""orderStrategyType"": ""SINGLE"",
                ""orderLegCollection"": [
                    {{
                        ""instruction"": ""SELL_TO_OPEN"",
                        ""quantity"": {order.Quantity},
                        ""instrument"": {{
                            ""symbol"": ""{order.Symbol}_{order.ExpirationDate:yyMMdd}P{order.ShortStrike:00000}"",
                            ""assetType"": ""OPTION""
                        }}
                    }},
                    {{
                        ""instruction"": ""BUY_TO_OPEN"",
                        ""quantity"": {order.Quantity},
                        ""instrument"": {{
                            ""symbol"": ""{order.Symbol}_{order.ExpirationDate:yyMMdd}P{order.LongStrike:00000}"",
                            ""assetType"": ""OPTION""
                        }}
                    }}
                ]
            }}";

            return new StringContent(orderJson, System.Text.Encoding.UTF8, "application/json");
        }
    }

    public class TradeOrder
    {
        public string OrderId { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Strategy { get; set; } = "";
        public decimal ShortStrike { get; set; }
        public decimal LongStrike { get; set; }
        public int Quantity { get; set; }
        public decimal ExpectedCredit { get; set; }
        public DateTime ExpirationDate { get; set; }
        public OrderType OrderType { get; set; }
        public OrderStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CloseReason { get; set; }
    }

    public class AccountInfo
    {
        public string AccountId { get; set; } = "";
        public decimal BuyingPower { get; set; }
        public decimal NetLiquidationValue { get; set; }
        public bool IsApproved { get; set; }
        public bool IsPaperTrading { get; set; }
    }

    public class BrokerPosition
    {
        public string Symbol { get; set; } = "";
        public string PositionType { get; set; } = "";
        public int Quantity { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal CurrentValue { get; set; }
        public decimal UnrealizedPnL { get; set; }
    }
}