using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using Stroll.Theta.Client;

namespace Sofired.Core
{
    /// <summary>
    /// Market data service using Stroll.Theta client for real ThetaData integration
    /// Replaces direct HTTP coupling with production-grade ThetaData interface
    /// </summary>
    public class MarketDataService : IDisposable
    {
        private readonly ThetaClient _thetaClient;
        private readonly HttpClient _httpClient;

        public MarketDataService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _thetaClient = new ThetaClient(_httpClient);
        }

        /// <summary>
        /// Check if ThetaData Terminal is connected
        /// </summary>
        public async Task<bool> IsConnectedAsync()
        {
            return await _thetaClient.IsConnectedAsync();
        }

        /// <summary>
        /// Get daily OHLC bars for a symbol using production Stroll.Theta client
        /// </summary>
        public async Task<List<DailyBar>> GetDailyBarsAsync(string symbol, DateTime startDate, DateTime endDate)
        {
            var bars = new List<DailyBar>();
            
            // Check connection first
            if (!await IsConnectedAsync())
            {
                Console.WriteLine("❌ ThetaData Terminal is not connected");
                Console.WriteLine("Please ensure ThetaData Terminal is running and accessible.");
                return bars;
            }

            Console.WriteLine($"📊 Fetching {symbol} data from ThetaData using Stroll.Theta client...");
            Console.WriteLine($"📅 Date range: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");

            // Fetch data day by day using ThetaClient
            var currentDate = startDate;
            while (currentDate <= endDate)
            {
                try
                {
                    var dateOnly = DateOnly.FromDateTime(currentDate);
                    var jsonResponse = await _thetaClient.GetIndexOhlc1mAsync(symbol, dateOnly);
                    
                    // Convert minute data to daily bars
                    var dailyBar = ProcessMinuteDataToDaily(jsonResponse, symbol, currentDate);
                    if (dailyBar != null)
                    {
                        bars.Add(dailyBar);
                        
                        // Log first few bars for validation
                        if (bars.Count <= 3)
                        {
                            Console.WriteLine($"✅ {symbol} {currentDate:yyyy-MM-dd}: O:{dailyBar.Open:F2} H:{dailyBar.High:F2} L:{dailyBar.Low:F2} C:{dailyBar.Close:F2} V:{dailyBar.Volume}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to fetch {symbol} data for {currentDate:yyyy-MM-dd}: {ex.Message}");
                }
                
                currentDate = currentDate.AddDays(1);
                
                // Progress update every 10 days
                if ((currentDate - startDate).Days % 10 == 0)
                {
                    Console.WriteLine($"Progress: {symbol} - {currentDate:yyyy-MM-dd} ({bars.Count} bars collected)");
                }
            }

            if (bars.Count > 0)
            {
                var firstBar = bars.First();
                var lastBar = bars.Last();
                Console.WriteLine($"✅ Successfully collected {bars.Count} real market bars for {symbol}");
                Console.WriteLine($"📈 {symbol}: {firstBar.Date:yyyy-MM-dd} (${firstBar.Close:F2}) → {lastBar.Date:yyyy-MM-dd} (${lastBar.Close:F2})");
                Console.WriteLine($"📊 Price change: ${lastBar.Close - firstBar.Close:F2} ({((lastBar.Close - firstBar.Close) / firstBar.Close):P1})");
            }
            else
            {
                Console.WriteLine($"❌ No market data collected for {symbol}");
            }

            return bars;
        }

        /// <summary>
        /// Process minute-level JSON data into daily bars
        /// </summary>
        private DailyBar? ProcessMinuteDataToDaily(JsonElement jsonResponse, string symbol, DateTime date)
        {
            try
            {
                // Check for ThetaData error responses
                if (jsonResponse.TryGetProperty("header", out var header))
                {
                    if (header.TryGetProperty("error_type", out var errorType) && !errorType.GetString().Equals("null"))
                    {
                        Console.WriteLine($"ThetaData API error for {symbol} {date:yyyy-MM-dd}: {errorType.GetString()}");
                        return null;
                    }
                }

                // Process ThetaData response format
                if (jsonResponse.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Array)
                {
                    var minuteBars = new List<(decimal open, decimal high, decimal low, decimal close, long volume)>();
                    
                    foreach (var dataPoint in response.EnumerateArray())
                    {
                        if (dataPoint.ValueKind == JsonValueKind.Array && dataPoint.GetArrayLength() >= 6)
                        {
                            var dataArray = dataPoint.EnumerateArray().ToArray();
                            
                            // ThetaData format: [ms_of_day, open, high, low, close, volume]
                            var open = dataArray[1].GetDecimal();
                            var high = dataArray[2].GetDecimal();
                            var low = dataArray[3].GetDecimal();
                            var close = dataArray[4].GetDecimal();
                            var volume = dataArray[5].GetInt64();
                            
                            minuteBars.Add((open, high, low, close, volume));
                        }
                    }

                    // Aggregate minute bars into daily bar
                    if (minuteBars.Count > 0)
                    {
                        var dailyOpen = minuteBars.First().open;
                        var dailyHigh = minuteBars.Max(b => b.high);
                        var dailyLow = minuteBars.Min(b => b.low);
                        var dailyClose = minuteBars.Last().close;
                        var dailyVolume = minuteBars.Sum(b => b.volume);

                        return new DailyBar(
                            Date: date,
                            Open: dailyOpen,
                            High: dailyHigh,
                            Low: dailyLow,
                            Close: dailyClose,
                            Volume: dailyVolume
                        );
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error processing minute data for {symbol} {date:yyyy-MM-dd}: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}