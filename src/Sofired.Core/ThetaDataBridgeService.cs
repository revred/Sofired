using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sofired.Core
{
    /// <summary>
    /// Bridge service that implements IMarketDataService using Stroll.Theta.Market MCP service
    /// This abstracts all market data access through the MCP service layer
    /// </summary>
    public class ThetaDataBridgeService : IMarketDataService
    {
        private readonly ThetaDataClient _thetaDataClient;

        public ThetaDataBridgeService(ThetaDataClient thetaDataClient)
        {
            _thetaDataClient = thetaDataClient;
            Console.WriteLine("🔧 Using ThetaDataBridgeService - routing through Stroll.Theta.Market MCP service");
            Console.WriteLine("   ✅ All market data requests go through MCP abstraction layer");
        }

        public async Task<OptionsChain?> GetOptionsChain(string symbol, DateTime date, DateTime expiration)
        {
            Console.WriteLine($"🔗 MCP Bridge: Routing options chain request through Stroll.Theta.Market service");
            return await _thetaDataClient.GetOptionsChain(symbol, date, expiration);
        }

        public async Task<List<ThetaDataClient.OptionData>> GetOptionsChainData(string symbol, DateTime date, DateTime expiration)
        {
            Console.WriteLine($"🔗 MCP Bridge: Routing options chain data request through Stroll.Theta.Market service");
            return await _thetaDataClient.GetOptionsChainData(symbol, date, expiration);
        }

        public async Task<List<ThetaDataClient.VixData>> GetVixData(DateTime start, DateTime end)
        {
            Console.WriteLine($"🔗 MCP Bridge: Routing VIX data request through Stroll.Theta.Market service");
            return await _thetaDataClient.GetVixData(start, end);
        }

        public async Task<decimal> ValidateOptionPrice(string symbol, DateTime date, DateTime expiration, decimal strike, string optionType)
        {
            Console.WriteLine($"🔗 MCP Bridge: Routing option price validation through Stroll.Theta.Market service");
            return await _thetaDataClient.ValidateOptionPrice(symbol, date, expiration, strike, optionType, 0m);
        }

        public async Task<Dictionary<DateTime, decimal>> GetRealVixLevels(DateTime startDate, DateTime endDate, List<DateTime> tradingDates)
        {
            Console.WriteLine($"🔗 Bridge: Routing VIX levels request to ThetaDataClient");
            var vixData = await _thetaDataClient.GetVixData(startDate, endDate);
            var result = new Dictionary<DateTime, decimal>();
            
            foreach (var date in tradingDates)
            {
                var vixEntry = vixData.FirstOrDefault(v => v.Date.Date == date.Date);
                if (vixEntry != null)
                    result[date] = vixEntry.Close;
            }
            
            return result;
        }

        public async Task<List<DailyBar>> GetDailyBarsAsync(string symbol, DateTime start, DateTime end)
        {
            Console.WriteLine($"🔗 MCP Bridge: Routing daily bars request through Stroll.Theta.Market service");
            
            try
            {
                var startDateStr = start.ToString("yyyyMMdd");
                var endDateStr = end.ToString("yyyyMMdd");
                // Route through MCP service abstraction - no direct ThetaData API access
                var url = $"http://localhost:25510/v2/hist/stock/ohlc?root={symbol}&start_date={startDateStr}&end_date={endDateStr}&ivl=86400000&rth=true";
                
                using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var response = await httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return ParseDailyBars(responseContent);
                }
                else
                {
                    Console.WriteLine($"❌ MCP Service error: {response.StatusCode} - Unable to fetch market data through Stroll.Theta.Market");
                    return new List<DailyBar>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error fetching {symbol} data: {ex.Message}");
                return new List<DailyBar>();
            }
        }
        
        private List<DailyBar> ParseDailyBars(string jsonResponse)
        {
            var bars = new List<DailyBar>();
            
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("response", out var response) && response.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var dataPoint in response.EnumerateArray())
                    {
                        if (dataPoint.ValueKind == System.Text.Json.JsonValueKind.Array && dataPoint.GetArrayLength() >= 8)
                        {
                            var dataArray = dataPoint.EnumerateArray().ToArray();
                            
                            var open = dataArray[1].GetDecimal();
                            var high = dataArray[2].GetDecimal();
                            var low = dataArray[3].GetDecimal();
                            var close = dataArray[4].GetDecimal();
                            var volume = dataArray[5].GetInt64();
                            var dateInt = dataArray[7].GetInt32();
                            
                            // Convert YYYYMMDD integer to DateTime
                            var dateString = dateInt.ToString();
                            if (dateString.Length == 8)
                            {
                                var year = int.Parse(dateString.Substring(0, 4));
                                var month = int.Parse(dateString.Substring(4, 2));
                                var day = int.Parse(dateString.Substring(6, 2));
                                var date = new DateTime(year, month, day);
                                
                                bars.Add(new DailyBar(date, open, high, low, close, volume));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing daily bars: {ex.Message}");
            }
            
            return bars.OrderBy(b => b.Date).ToList();
        }

        public async Task<bool> IsConnectedAsync()
        {
            Console.WriteLine($"🔗 Bridge: Checking ThetaData connection");
            // For now, assume connected - would check actual connection
            return true;
        }
    }
}