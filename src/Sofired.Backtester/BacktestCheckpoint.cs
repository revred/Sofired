using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Sofired.Core;

namespace Sofired.Backtester;

/// <summary>
/// Represents a checkpoint state for resumable backtesting
/// </summary>
public class BacktestCheckpoint
{
    [JsonPropertyName("backtest_id")]
    public string BacktestId { get; set; } = string.Empty;
    
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;
    
    [JsonPropertyName("start_date")]
    public DateTime StartDate { get; set; }
    
    [JsonPropertyName("end_date")]
    public DateTime EndDate { get; set; }
    
    [JsonPropertyName("last_processed_date")]
    public DateTime LastProcessedDate { get; set; }
    
    [JsonPropertyName("last_checkpoint_time")]
    public DateTime LastCheckpointTime { get; set; }
    
    [JsonPropertyName("current_capital")]
    public decimal CurrentCapital { get; set; }
    
    [JsonPropertyName("total_trades")]
    public int TotalTrades { get; set; }
    
    [JsonPropertyName("running_pnl")]
    public decimal RunningPnL { get; set; }
    
    [JsonPropertyName("weekly_premium")]
    public decimal WeeklyPremium { get; set; }
    
    [JsonPropertyName("monthly_premium")]
    public decimal MonthlyPremium { get; set; }
    
    [JsonPropertyName("max_drawdown")]
    public decimal MaxDrawdown { get; set; }
    
    [JsonPropertyName("peak_capital")]
    public decimal PeakCapital { get; set; }
    
    [JsonPropertyName("win_rate")]
    public decimal WinRate { get; set; }
    
    [JsonPropertyName("excel_file_path")]
    public string ExcelFilePath { get; set; } = string.Empty;
    
    [JsonPropertyName("config_hash")]
    public string ConfigHash { get; set; } = string.Empty;
    
    [JsonPropertyName("is_completed")]
    public bool IsCompleted { get; set; }
    
    [JsonPropertyName("trading_engine_state")]
    public TradingEngineState? TradingEngineState { get; set; }
    
    [JsonPropertyName("total_bars_processed")]
    public int TotalBarsProcessed { get; set; }
    
    [JsonPropertyName("estimated_completion_pct")]
    public decimal EstimatedCompletionPct { get; set; }
}

/// <summary>
/// Serializable state of the trading engine for checkpoints
/// </summary>
public class TradingEngineState
{
    [JsonPropertyName("open_positions_count")]
    public int OpenPositionsCount { get; set; }
    
    [JsonPropertyName("closed_positions_count")]
    public int ClosedPositionsCount { get; set; }
    
    [JsonPropertyName("current_capital")]
    public decimal CurrentCapital { get; set; }
    
    [JsonPropertyName("weekly_premium")]
    public decimal WeeklyPremium { get; set; }
    
    [JsonPropertyName("monthly_premium")]
    public decimal MonthlyPremium { get; set; }
    
    [JsonPropertyName("trade_sequence")]
    public int TradeSequence { get; set; }
    
    [JsonPropertyName("last_reset_date")]
    public DateTime LastResetDate { get; set; }
}