using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sofired.Core
{
    /// <summary>
    /// Market data service that connects to Stroll.Theta.Market MCP service via process communication
    /// This is the ONLY way SOFIRED accesses market data - never direct provider access
    /// </summary>
    public class StrollThetaMarketService : IMarketDataService
    {
        private readonly string _mcpServicePath;
        private Process? _mcpProcess;
        private bool _isInitialized = false;
        private int _requestId = 0;

        public StrollThetaMarketService(string mcpServicePath = @"C:\code\Stroll.Theta\src\Stroll.Theta.Market")
        {
            _mcpServicePath = mcpServicePath;
            Console.WriteLine("🔥 Using Stroll.Theta.Market MCP Service");
            Console.WriteLine($"   📡 MCP Service Path: {_mcpServicePath}");
            Console.WriteLine("   ✅ SOFIRED knows nothing about data providers - all through MCP");
            StartMcpService();
            
            // Wait for initialization to complete
            var waitCount = 0;
            while (!_isInitialized && waitCount < 50) // 5 second timeout
            {
                Thread.Sleep(100);
                waitCount++;
            }
            
            if (!_isInitialized)
            {
                Console.WriteLine("⚠️ MCP service initialization timeout - continuing anyway");
            }
        }

        public async Task<OptionsChain?> GetOptionsChain(string symbol, DateTime date, DateTime expiration)
        {
            Console.WriteLine($"📡 MCP Request: get_options_chain for {symbol} on {date:yyyy-MM-dd}");
            
            try
            {
                var mcpRequest = new
                {
                    tool = "get_options_chain",
                    parameters = new
                    {
                        symbol = symbol,
                        date = date.ToString("yyyy-MM-dd")
                    }
                };

                var response = await CallMcpTool(mcpRequest);
                if (response == null) return null;

                return ParseOptionsChain(response, symbol, date, expiration);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ MCP Service error for options chain: {ex.Message}");
                throw new InvalidOperationException($"Unable to fetch options chain from MCP service for {symbol}. MCP service unavailable.");
            }
        }

        public async Task<List<ThetaDataClient.OptionData>> GetOptionsChainData(string symbol, DateTime date, DateTime expiration)
        {
            Console.WriteLine($"📡 MCP Request: get_options_chain data for {symbol} on {date:yyyy-MM-dd}");
            
            try
            {
                var mcpRequest = new
                {
                    tool = "get_options_chain",
                    parameters = new
                    {
                        symbol = symbol,
                        date = date.ToString("yyyy-MM-dd")
                    }
                };

                var response = await CallMcpTool(mcpRequest);
                if (response == null) return new List<ThetaDataClient.OptionData>();

                return ParseOptionsData(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ MCP Service error for options data: {ex.Message}");
                throw new InvalidOperationException($"Unable to fetch options data from MCP service for {symbol}. MCP service unavailable.");
            }
        }

        public async Task<List<ThetaDataClient.VixData>> GetVixData(DateTime start, DateTime end)
        {
            Console.WriteLine($"📡 MCP Request: VIX data from {start:yyyy-MM-dd} to {end:yyyy-MM-dd}");
            
            try
            {
                var vixData = new List<ThetaDataClient.VixData>();
                var current = start.Date;
                
                while (current <= end.Date)
                {
                    var mcpRequest = new
                    {
                        tool = "get_ohlc_data",
                        parameters = new
                        {
                            symbol = "VIX",
                            date = current.ToString("yyyy-MM-dd")
                        }
                    };

                    var response = await CallMcpTool(mcpRequest);
                    if (response != null)
                    {
                        var vixValue = ParseVixValue(response, current);
                        if (vixValue > 0)
                        {
                            vixData.Add(new ThetaDataClient.VixData 
                            { 
                                Date = current, 
                                Close = vixValue 
                            });
                        }
                    }
                    
                    current = current.AddDays(1);
                }

                return vixData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ MCP Service error for VIX data: {ex.Message}");
                throw new InvalidOperationException($"Unable to fetch VIX data from MCP service. MCP service unavailable.");
            }
        }

        public async Task<decimal> ValidateOptionPrice(string symbol, DateTime date, DateTime expiration, decimal strike, string optionType)
        {
            Console.WriteLine($"📡 MCP Request: validate option price {symbol} {strike} {optionType} exp {expiration:yyyy-MM-dd}");
            
            try
            {
                var mcpRequest = new
                {
                    tool = "get_option_quote",
                    parameters = new
                    {
                        symbol = symbol,
                        date = date.ToString("yyyy-MM-dd"),
                        expiration = expiration.ToString("yyyy-MM-dd"),
                        strike = strike.ToString("F2"),
                        right = optionType.ToUpper() == "PUT" ? "P" : "C"
                    }
                };

                var response = await CallMcpTool(mcpRequest);
                if (response == null) return 0m;

                return ParseOptionPrice(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ MCP Service error for option validation: {ex.Message}");
                return 0m;
            }
        }

        public async Task<Dictionary<DateTime, decimal>> GetRealVixLevels(DateTime startDate, DateTime endDate, List<DateTime> tradingDates)
        {
            Console.WriteLine($"📡 MCP Request: Real VIX levels for {tradingDates.Count} trading dates");
            
            var vixLevels = new Dictionary<DateTime, decimal>();
            
            foreach (var date in tradingDates)
            {
                try
                {
                    var mcpRequest = new
                    {
                        tool = "get_ohlc_data",
                        parameters = new
                        {
                            symbol = "VIX",
                            date = date.ToString("yyyy-MM-dd")
                        }
                    };

                    var response = await CallMcpTool(mcpRequest);
                    if (response != null)
                    {
                        var vixValue = ParseVixValue(response, date);
                        if (vixValue > 0)
                        {
                            vixLevels[date] = vixValue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to get VIX for {date:yyyy-MM-dd}: {ex.Message}");
                }
            }

            Console.WriteLine($"✅ Retrieved {vixLevels.Count} VIX levels via MCP service");
            return vixLevels;
        }

        public async Task<List<DailyBar>> GetDailyBarsAsync(string symbol, DateTime start, DateTime end)
        {
            Console.WriteLine($"📡 MCP Request: Daily bars for {symbol} from {start:yyyy-MM-dd} to {end:yyyy-MM-dd}");
            
            try
            {
                var bars = new List<DailyBar>();
                var current = start.Date;

                while (current <= end.Date)
                {
                    var mcpRequest = new
                    {
                        tool = "get_ohlc_data",
                        parameters = new
                        {
                            symbol = symbol,
                            date = current.ToString("yyyy-MM-dd")
                        }
                    };

                    var response = await CallMcpTool(mcpRequest);
                    if (response != null)
                    {
                        var bar = ParseDailyBar(response, symbol, current);
                        if (bar != null)
                        {
                            bars.Add(bar);
                        }
                    }

                    current = current.AddDays(1);
                }

                Console.WriteLine($"✅ Retrieved {bars.Count} daily bars via MCP service");
                return bars;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ MCP Service error for daily bars: {ex.Message}");
                throw new InvalidOperationException($"Unable to fetch daily bars from MCP service for {symbol}. MCP service unavailable.");
            }
        }

        public async Task<bool> IsConnectedAsync()
        {
            try
            {
                var mcpRequest = new
                {
                    tool = "theta_connection_status",
                    parameters = new { }
                };

                var response = await CallMcpTool(mcpRequest);
                if (response == null) return false;

                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("connected", out var connected))
                {
                    return connected.GetBoolean();
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void StartMcpService()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "run",
                    WorkingDirectory = _mcpServicePath,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _mcpProcess = new Process { StartInfo = startInfo };
                _mcpProcess.Start();
                
                Console.WriteLine($"✅ Started Stroll.Theta.Market MCP service process");
                
                // Initialize MCP protocol synchronously to ensure it completes
                Task.Run(async () => await InitializeMcpProtocol()).Wait(5000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to start MCP service: {ex.Message}");
            }
        }
        
        private async Task InitializeMcpProtocol()
        {
            try
            {
                // Wait a moment for the process to start
                await Task.Delay(1000);
                
                // Send MCP initialization message
                var initRequest = new
                {
                    id = "init",
                    method = "initialize",
                    @params = new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new
                        {
                            roots = new { listChanged = true },
                            sampling = new { }
                        },
                        clientInfo = new
                        {
                            name = "sofired",
                            version = "1.0.0"
                        }
                    }
                };
                
                var json = JsonSerializer.Serialize(initRequest);
                Console.WriteLine($"📡 Sending MCP initialization: {json}");
                
                if (_mcpProcess != null && !_mcpProcess.HasExited)
                {
                    await _mcpProcess.StandardInput.WriteLineAsync(json);
                    await _mcpProcess.StandardInput.FlushAsync();
                    
                    var response = await _mcpProcess.StandardOutput.ReadLineAsync();
                    Console.WriteLine($"📨 MCP initialization response: {response}");
                    
                    if (!string.IsNullOrEmpty(response) && response.Contains("\"serverInfo\""))
                    {
                        Console.WriteLine($"✅ MCP protocol initialized successfully");
                        _isInitialized = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to initialize MCP protocol: {ex.Message}");
            }
        }

        private async Task<string?> CallMcpTool(object mcpRequest)
        {
            if (_mcpProcess == null || _mcpProcess.HasExited)
            {
                Console.WriteLine($"❌ MCP service process not available");
                return null;
            }

            try
            {
                // Convert the client format to proper MCP JSON-RPC format
                var requestDoc = JsonDocument.Parse(JsonSerializer.Serialize(mcpRequest));
                var toolName = requestDoc.RootElement.GetProperty("tool").GetString();
                var parameters = requestDoc.RootElement.GetProperty("parameters");
                
                // Convert parameters JsonElement to Dictionary<string, object?>
                var argumentsDict = new Dictionary<string, object?>();
                foreach (var prop in parameters.EnumerateObject())
                {
                    argumentsDict[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.GetDecimal(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.GetRawText()
                    };
                }
                
                // Use unique request ID for each call
                var requestId = Interlocked.Increment(ref _requestId);
                
                var mcpJsonRpcRequest = new
                {
                    id = requestId.ToString(),
                    method = "tools/call",
                    @params = new
                    {
                        name = toolName,
                        arguments = argumentsDict
                    }
                };
                
                var json = JsonSerializer.Serialize(mcpJsonRpcRequest);
                Console.WriteLine($"📡 Sending MCP request: {json}");
                
                await _mcpProcess.StandardInput.WriteLineAsync(json);
                await _mcpProcess.StandardInput.FlushAsync();
                
                var response = await _mcpProcess.StandardOutput.ReadLineAsync();
                Console.WriteLine($"📨 MCP response: {response}");
                
                // Extract the actual content from the JSON-RPC response
                if (!string.IsNullOrEmpty(response))
                {
                    try
                    {
                        var responseDoc = JsonDocument.Parse(response);
                        if (responseDoc.RootElement.TryGetProperty("result", out var result) &&
                            result.TryGetProperty("content", out var content) &&
                            content.GetArrayLength() > 0)
                        {
                            var firstContent = content[0];
                            if (firstContent.TryGetProperty("text", out var text))
                            {
                                return text.GetString();
                            }
                        }
                        else if (responseDoc.RootElement.TryGetProperty("error", out var error))
                        {
                            Console.WriteLine($"❌ MCP service error: {error.GetRawText()}");
                            return null;
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"❌ Failed to parse MCP response: {ex.Message}");
                        return response; // Return raw response as fallback
                    }
                }
                
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ MCP Service call failed: {ex.Message}");
                return null;
            }
        }

        private OptionsChain? ParseOptionsChain(string response, string symbol, DateTime date, DateTime expiration)
        {
            try
            {
                var optionsData = ParseOptionsData(response);
                if (optionsData.Count == 0) return null;

                // Filter for the specific expiration date
                var expirationOptions = optionsData.Where(o => o.ExpirationDate.Date == expiration.Date).ToList();
                if (expirationOptions.Count == 0) return null;

                var calls = expirationOptions
                    .Where(o => o.OptionType == "C")
                    .Select(o => new OptionContract
                    {
                        Strike = o.Strike,
                        Bid = o.Bid,
                        Ask = o.Ask,
                        Volume = (int)o.Volume,
                        OpenInterest = (int)o.OpenInterest,
                        Delta = o.Delta
                    }).ToList();

                var puts = expirationOptions
                    .Where(o => o.OptionType == "P")
                    .Select(o => new OptionContract
                    {
                        Strike = o.Strike,
                        Bid = o.Bid,
                        Ask = o.Ask,
                        Volume = (int)o.Volume,
                        OpenInterest = (int)o.OpenInterest,
                        Delta = o.Delta
                    }).ToList();

                return new OptionsChain
                {
                    ExpirationDate = expiration,
                    TradingDate = date,
                    PutOptions = puts,
                    CallOptions = calls,
                    UnderlyingPrice = 15.0m // Would need separate API call for current stock price
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to parse options chain: {ex.Message}");
                return null;
            }
        }

        private List<ThetaDataClient.OptionData> ParseOptionsData(string response)
        {
            try
            {
                var options = new List<ThetaDataClient.OptionData>();
                using var doc = JsonDocument.Parse(response);
                
                if (!doc.RootElement.TryGetProperty("response", out var responseArray))
                    return options;

                foreach (var item in responseArray.EnumerateArray())
                {
                    if (!item.TryGetProperty("contract", out var contract) ||
                        !item.TryGetProperty("ticks", out var ticks))
                        continue;

                    // Parse contract information
                    if (!contract.TryGetProperty("root", out var root) ||
                        !contract.TryGetProperty("expiration", out var expiration) ||
                        !contract.TryGetProperty("strike", out var strike) ||
                        !contract.TryGetProperty("right", out var right))
                        continue;

                    var symbol = root.GetString() ?? "";
                    var exp = DateTime.ParseExact(expiration.GetInt32().ToString(), "yyyyMMdd", null);
                    var strikePrice = strike.GetDecimal() / 1000m; // Strike is in thousandths
                    var optionType = right.GetString() ?? "";

                    // Parse the most recent tick data (last element) 
                    var ticksArray = ticks.EnumerateArray().ToArray();
                    if (ticksArray.Length == 0) continue;
                    
                    var lastTick = ticksArray[ticksArray.Length - 1].EnumerateArray().ToArray();
                    if (lastTick.Length < 9) continue;

                    // Format: [ms_of_day, bid_size, bid_exchange, bid, bid_condition, ask_size, ask_exchange, ask, ask_condition, date]
                    var bid = lastTick[3].GetDecimal();
                    var ask = lastTick[7].GetDecimal();
                    var mark = (bid + ask) / 2;
                    var dateInt = lastTick[9].GetInt32();
                    
                    // Convert YYYYMMDD to DateTime
                    var year = dateInt / 10000;
                    var month = (dateInt % 10000) / 100;
                    var day = dateInt % 100;
                    var tickDate = new DateTime(year, month, day);

                    options.Add(new ThetaDataClient.OptionData
                    {
                        Date = tickDate,
                        ExpirationDate = exp,
                        Strike = strikePrice,
                        OptionType = optionType, // "C" or "P"
                        Bid = bid,
                        Ask = ask,
                        Delta = 0, // Not provided in this data format
                        ImpliedVolatility = 0, // Not provided in this data format
                        Volume = 0, // Not provided in this data format
                        OpenInterest = 0 // Not provided in this data format
                    });
                }

                Console.WriteLine($"✅ Parsed {options.Count} options contracts from MCP response");
                return options;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to parse options data: {ex.Message}");
                return new List<ThetaDataClient.OptionData>();
            }
        }

        private decimal ParseVixValue(string response, DateTime date)
        {
            try
            {
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("close", out var close))
                {
                    return close.GetDecimal();
                }
                return 0m;
            }
            catch
            {
                return 0m;
            }
        }

        private decimal ParseOptionPrice(string response)
        {
            try
            {
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("bid", out var bid))
                {
                    return bid.GetDecimal();
                }
                return 0m;
            }
            catch
            {
                return 0m;
            }
        }

        private DailyBar? ParseDailyBar(string response, string symbol, DateTime date)
        {
            try
            {
                // CallMcpTool already extracts result.content[0].text, so response is the ThetaData JSON
                using var doc = JsonDocument.Parse(response);
                
                // Check for error first
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    Console.WriteLine($"📅 {symbol} {date:yyyy-MM-dd}: {error.GetString()}");
                    return null; // Weekend/holiday - skip
                }
                
                // Parse minute-level data from ThetaData response
                if (doc.RootElement.TryGetProperty("response", out var responseArray) && 
                    responseArray.ValueKind == JsonValueKind.Array)
                {
                    var minuteBars = new List<(decimal open, decimal high, decimal low, decimal close, long volume)>();
                    
                    foreach (var minuteElement in responseArray.EnumerateArray())
                    {
                        if (minuteElement.ValueKind == JsonValueKind.Array)
                        {
                            var values = minuteElement.EnumerateArray().ToArray();
                            if (values.Length >= 6)
                            {
                                // Format: [ms_of_day, open, high, low, close, volume, count, date]
                                var open = values[1].GetDecimal();
                                var high = values[2].GetDecimal();
                                var low = values[3].GetDecimal();
                                var close = values[4].GetDecimal();
                                var volume = values[5].GetInt64();
                                
                                minuteBars.Add((open, high, low, close, volume));
                            }
                        }
                    }
                    
                    if (minuteBars.Count > 0)
                    {
                        // Aggregate minute bars into daily OHLC
                        var dayOpen = minuteBars.First().open;
                        var dayHigh = minuteBars.Max(b => b.high);
                        var dayLow = minuteBars.Min(b => b.low);
                        var dayClose = minuteBars.Last().close;
                        var dayVolume = minuteBars.Sum(b => b.volume);
                        
                        Console.WriteLine($"📊 {symbol} {date:yyyy-MM-dd}: Aggregated {minuteBars.Count} minute bars -> OHLC({dayOpen:F2}, {dayHigh:F2}, {dayLow:F2}, {dayClose:F2})");
                        
                        return new DailyBar(date, dayOpen, dayHigh, dayLow, dayClose, dayVolume);
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error parsing daily bar for {symbol} {date:yyyy-MM-dd}: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (_mcpProcess != null && !_mcpProcess.HasExited)
            {
                _mcpProcess.Kill();
                _mcpProcess.Dispose();
            }
        }
    }
}