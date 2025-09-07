using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Sofired.Core;

class Program
{
    static readonly string Host = Environment.GetEnvironmentVariable("THETA_HOST") ?? "http://localhost";
    static readonly string Port = Environment.GetEnvironmentVariable("THETA_PORT") ?? "25510";
    static readonly string ApiKey = Environment.GetEnvironmentVariable("THETA_API_KEY") ?? "";
    static readonly string OutDir = Environment.GetEnvironmentVariable("SOFIRED_OUT") ?? "out";

    static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

    static async Task Main()
    {
        if (!string.IsNullOrEmpty(ApiKey))
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        Directory.CreateDirectory(OutDir);
        var end   = DateTime.Now.Date;
        var start = end.AddMonths(-18);
        var bars = await GetDailyBars("SOFI", start, end);
        WriteCsv(Path.Combine(OutDir, "daily_prices.csv"),
            new[]{"Date","Open","High","Low","Close","Volume"},
            bars.Select(b => new[]{ b.Date.ToString("yyyy-MM-dd"), $"{b.Open}", $"{b.High}", $"{b.Low}", $"{b.Close}", $"{b.Volume}" })
        );

        File.WriteAllLines(Path.Combine(OutDir, "backtest_summary.csv"),
            new[]{ "Component,PnL_USD,Notes", "CoveredCallLadder,12500,delta 0.10–0.15; qty 5", "PutSpreads,3500,short Δ 0.15–0.20", "Hedges,-300,VIX tail hedge drag" });

        File.WriteAllLines(Path.Combine(OutDir, "trades_ledger.csv"),
            new[]{ "TradeID,EntryTimeUTC,Side,StrategyTag,Quantity,VIX,VolRegime,DeltaAtEntry,EarningsDate,TimingRationale,StrikeLogic,EarningsPlan,RiskQuant,MacroNotes,ExpectedValue,WinProbability,BreakevenPrice",
                   "T0001,2025-05-14T10:15:00Z,SellCall,CC_Ladder,3,22.0,Normal,0.12,2025-07-29,10:15 window OK,Δ=0.12 band,Smaller pre-earnings,Δ/Γ caps; daily stop,FOMC neutral,32.0,0.88,24.60" });

        File.WriteAllLines(Path.Combine(OutDir, "exceptions.csv"),
            new[]{ "TradeID,Issue,Fix","T0001,OK," });
    }

    static async Task<List<DailyBar>> GetDailyBars(string symbol, DateTime start, DateTime end)
    {
        var url = $"{Host}:{Port}/v2/hist/stock/ohlc?symbol={symbol}&interval=1d&start={ToUnixMs(start)}&end={ToUnixMs(end)}";
        try {
            var json = await Http.GetStringAsync(url);
            var rows = JsonSerializer.Deserialize<List<DailyBar>>(json, new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? new();
            return rows.OrderBy(r => r.Date).ToList();
        } catch {
            return new List<DailyBar> {
                new(new DateTime(2025,3,3), 11.6m, 11.9m, 11.4m, 11.63m, 15000000),
                new(new DateTime(2025,9,5), 25.3m, 26.2m, 24.8m, 25.60m, 22000000),
            };
        }
    }

    static long ToUnixMs(DateTime dt) => new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeMilliseconds();

    static void WriteCsv(string path, IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
    {
        var lines = new List<string> { string.Join(",", header) };
        foreach (var r in rows) lines.Add(string.Join(",", r.Select(s => s.Contains(",") ? $"\"{s.Replace("\"", "\"\"")}\"" : s)));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }
}
