using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sofired.Core
{
    /// <summary>
    /// Market data service interface - abstracts away third-party data providers
    /// All market data flows through MCP service architecture
    /// </summary>
    public interface IMarketDataService
    {
        Task<OptionsChain?> GetOptionsChain(string symbol, DateTime date, DateTime expiration);
        Task<List<ThetaDataClient.OptionData>> GetOptionsChainData(string symbol, DateTime date, DateTime expiration);
        Task<List<ThetaDataClient.VixData>> GetVixData(DateTime start, DateTime end);
        Task<decimal> ValidateOptionPrice(string symbol, DateTime date, DateTime expiration, decimal strike, string optionType);
        Task<Dictionary<DateTime, decimal>> GetRealVixLevels(DateTime startDate, DateTime endDate, List<DateTime> tradingDates);
        Task<List<DailyBar>> GetDailyBarsAsync(string symbol, DateTime start, DateTime end);
        Task<bool> IsConnectedAsync();
    }

    /// <summary>
    /// MCP-based market data service using stroll.theta.market
    /// </summary>
    public class McpMarketDataService : IMarketDataService
    {
        private readonly McpClient _mcpClient;

        public McpMarketDataService(McpClient mcpClient)
        {
            _mcpClient = mcpClient;
        }

        public async Task<OptionsChain?> GetOptionsChain(string symbol, DateTime date, DateTime expiration)
        {
            try
            {
                var request = new McpRequest
                {
                    Service = "stroll.theta.market",
                    Method = "get_options_chain",
                    Parameters = new Dictionary<string, object>
                    {
                        ["symbol"] = symbol,
                        ["date"] = date.ToString("yyyy-MM-dd"),
                        ["expiration"] = expiration.ToString("yyyy-MM-dd")
                    }
                };

                Console.WriteLine($"🔥 MCP: Requesting options chain for {symbol} via stroll.theta.market service");
                var response = await _mcpClient.SendAsync(request);

                if (response.IsSuccess && response.Data != null)
                {
                    Console.WriteLine($"✅ MCP: Successfully received options chain data for {symbol}");
                    return ParseOptionsChainFromMcp(response.Data, date, expiration);
                }
                else
                {
                    Console.WriteLine($"❌ MCP: Options chain request failed - {response.Error}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ MCP: Exception in GetOptionsChain - {ex.Message}");
                return null;
            }
        }

        public async Task<List<ThetaDataClient.OptionData>> GetOptionsChainData(string symbol, DateTime date, DateTime expiration)
        {
            var chain = await GetOptionsChain(symbol, date, expiration);
            var result = new List<ThetaDataClient.OptionData>();
            
            if (chain != null)
            {
                foreach (var call in chain.CallOptions)
                {
                    result.Add(new ThetaDataClient.OptionData
                    {
                        Date = date,
                        ExpirationDate = expiration,
                        Strike = call.Strike,
                        Bid = call.Bid,
                        Ask = call.Ask,
                        Delta = call.Delta,
                        ImpliedVolatility = call.ImpliedVolatility,
                        Volume = call.Volume,
                        OpenInterest = call.OpenInterest,
                        OptionType = "C"
                    });
                }
                
                foreach (var put in chain.PutOptions)
                {
                    result.Add(new ThetaDataClient.OptionData
                    {
                        Date = date,
                        ExpirationDate = expiration,
                        Strike = put.Strike,
                        Bid = put.Bid,
                        Ask = put.Ask,
                        Delta = put.Delta,
                        ImpliedVolatility = put.ImpliedVolatility,
                        Volume = put.Volume,
                        OpenInterest = put.OpenInterest,
                        OptionType = "P"
                    });
                }
            }
            
            return result;
        }

        public async Task<List<ThetaDataClient.VixData>> GetVixData(DateTime start, DateTime end)
        {
            try
            {
                var request = new McpRequest
                {
                    Service = "stroll.theta.market",
                    Method = "get_vix_data",
                    Parameters = new Dictionary<string, object>
                    {
                        ["start_date"] = start.ToString("yyyy-MM-dd"),
                        ["end_date"] = end.ToString("yyyy-MM-dd")
                    }
                };

                Console.WriteLine($"🔥 MCP: Requesting VIX data from {start:yyyy-MM-dd} to {end:yyyy-MM-dd}");
                var response = await _mcpClient.SendAsync(request);

                if (response.IsSuccess && response.Data != null)
                {
                    Console.WriteLine($"✅ MCP: Successfully received VIX data");
                    return ParseVixDataFromMcp(response.Data);
                }
                else
                {
                    Console.WriteLine($"❌ MCP: VIX data request failed - {response.Error}");
                    return new List<ThetaDataClient.VixData>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ MCP: Exception in GetVixData - {ex.Message}");
                return new List<ThetaDataClient.VixData>();
            }
        }

        public async Task<decimal> ValidateOptionPrice(string symbol, DateTime date, DateTime expiration, decimal strike, string optionType)
        {
            try
            {
                var request = new McpRequest
                {
                    Service = "stroll.theta.market",
                    Method = "validate_option_price",
                    Parameters = new Dictionary<string, object>
                    {
                        ["symbol"] = symbol,
                        ["date"] = date.ToString("yyyy-MM-dd"),
                        ["expiration"] = expiration.ToString("yyyy-MM-dd"),
                        ["strike"] = strike,
                        ["option_type"] = optionType
                    }
                };

                var response = await _mcpClient.SendAsync(request);
                return response.IsSuccess && response.Data != null ? Convert.ToDecimal(response.Data) : 0m;
            }
            catch
            {
                return 0m;
            }
        }

        public async Task<Dictionary<DateTime, decimal>> GetRealVixLevels(DateTime startDate, DateTime endDate, List<DateTime> tradingDates)
        {
            var vixData = await GetVixData(startDate, endDate);
            var result = new Dictionary<DateTime, decimal>();
            
            foreach (var date in tradingDates)
            {
                var vixEntry = vixData.FirstOrDefault(v => v.Date.Date == date.Date);
                if (vixEntry != null)
                    result[date] = vixEntry.Close;
            }
            
            return result;
        }

        private OptionsChain ParseOptionsChainFromMcp(object data, DateTime date, DateTime expiration)
        {
            // This would parse the MCP response format
            // For now, return empty chain - implementation depends on MCP response format
            return new OptionsChain
            {
                ExpirationDate = expiration,
                TradingDate = date,
                PutOptions = new List<OptionContract>(),
                CallOptions = new List<OptionContract>(),
                UnderlyingPrice = 0m
            };
        }

        private List<ThetaDataClient.VixData> ParseVixDataFromMcp(object data)
        {
            // This would parse the MCP response format
            // For now, return empty list - implementation depends on MCP response format
            return new List<ThetaDataClient.VixData>();
        }

        public async Task<List<DailyBar>> GetDailyBarsAsync(string symbol, DateTime start, DateTime end)
        {
            // MCP implementation for daily bars - placeholder
            return new List<DailyBar>();
        }

        public async Task<bool> IsConnectedAsync()
        {
            // MCP connection check - placeholder
            return false;
        }
    }

    /// <summary>
    /// MCP client for communicating with MCP services
    /// </summary>
    public class McpClient
    {
        public async Task<McpResponse> SendAsync(McpRequest request)
        {
            // This would implement actual MCP protocol communication
            // For now, return error response
            return new McpResponse
            {
                IsSuccess = false,
                Error = "MCP service integration not yet implemented",
                Data = null
            };
        }
    }

    public class McpRequest
    {
        public string Service { get; set; } = "";
        public string Method { get; set; } = "";
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public class McpResponse
    {
        public bool IsSuccess { get; set; }
        public string? Error { get; set; }
        public object? Data { get; set; }
    }
}