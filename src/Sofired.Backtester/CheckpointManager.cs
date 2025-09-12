using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sofired.Core;

namespace Sofired.Backtester;

/// <summary>
/// Manages checkpoint creation, saving, and recovery for resumable backtesting
/// </summary>
public class CheckpointManager
{
    private readonly string _checkpointDir;
    private readonly JsonSerializerOptions _jsonOptions;
    
    public CheckpointManager(string checkpointDir = "checkpoints")
    {
        _checkpointDir = checkpointDir;
        Directory.CreateDirectory(_checkpointDir);
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
    }
    
    /// <summary>
    /// Check if there's an incomplete backtest for a symbol
    /// </summary>
    public bool HasIncompleteBacktest(string symbol)
    {
        var checkpoints = GetCheckpointFiles(symbol);
        return checkpoints.Any(cp => !IsCompleted(cp));
    }
    
    /// <summary>
    /// Get the most recent incomplete checkpoint for a symbol
    /// </summary>
    public BacktestCheckpoint? LoadMostRecentCheckpoint(string symbol)
    {
        var incompleteCheckpoints = GetCheckpointFiles(symbol)
            .Where(f => !IsCompleted(f))
            .OrderByDescending(File.GetLastWriteTime)
            .ToList();
            
        if (!incompleteCheckpoints.Any())
        {
            Console.WriteLine($"No incomplete checkpoints found for {symbol}");
            return null;
        }
        
        var latestFile = incompleteCheckpoints.First();
        Console.WriteLine($"🔄 Loading checkpoint: {Path.GetFileName(latestFile)}");
        
        var json = File.ReadAllText(latestFile);
        return JsonSerializer.Deserialize<BacktestCheckpoint>(json, _jsonOptions);
    }
    
    /// <summary>
    /// Load a specific checkpoint by backtest ID
    /// </summary>
    public BacktestCheckpoint? LoadCheckpoint(string backtestId)
    {
        var checkpointFile = Path.Combine(_checkpointDir, $"checkpoint_{backtestId}.json");
        if (!File.Exists(checkpointFile))
        {
            Console.WriteLine($"Checkpoint file not found: {checkpointFile}");
            return null;
        }
        
        var json = File.ReadAllText(checkpointFile);
        return JsonSerializer.Deserialize<BacktestCheckpoint>(json, _jsonOptions);
    }
    
    /// <summary>
    /// Save checkpoint state to disk
    /// </summary>
    public void SaveCheckpoint(BacktestCheckpoint checkpoint)
    {
        checkpoint.LastCheckpointTime = DateTime.Now;
        
        var fileName = $"checkpoint_{checkpoint.BacktestId}.json";
        var filePath = Path.Combine(_checkpointDir, fileName);
        
        var json = JsonSerializer.Serialize(checkpoint, _jsonOptions);
        File.WriteAllText(filePath, json);
        
        Console.WriteLine($"📍 Checkpoint saved: {checkpoint.LastProcessedDate:yyyy-MM-dd} " +
                         $"({checkpoint.EstimatedCompletionPct:F1}% complete)");
    }
    
    /// <summary>
    /// Mark backtest as completed and clean up
    /// </summary>
    public void FinalizeBacktest(BacktestCheckpoint checkpoint)
    {
        checkpoint.IsCompleted = true;
        checkpoint.LastCheckpointTime = DateTime.Now;
        checkpoint.EstimatedCompletionPct = 100m;
        
        SaveCheckpoint(checkpoint);
        
        Console.WriteLine($"✅ Backtest completed and finalized: {checkpoint.BacktestId}");
    }
    
    /// <summary>
    /// Create initial checkpoint for new backtest
    /// </summary>
    public BacktestCheckpoint CreateInitialCheckpoint(
        string symbol, 
        DateTime startDate, 
        DateTime endDate,
        StrategyConfig config,
        string excelFilePath)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
        var backtestId = $"{symbol}_{timestamp}";
        
        return new BacktestCheckpoint
        {
            BacktestId = backtestId,
            Symbol = symbol,
            StartDate = startDate,
            EndDate = endDate,
            LastProcessedDate = startDate.AddDays(-1), // Will start from startDate
            CurrentCapital = config.InitialCapital,
            RunningPnL = 0m,
            TotalTrades = 0,
            WeeklyPremium = 0m,
            MonthlyPremium = 0m,
            MaxDrawdown = 0m,
            PeakCapital = config.InitialCapital,
            WinRate = 0m,
            ExcelFilePath = excelFilePath,
            ConfigHash = ComputeConfigHash(config),
            IsCompleted = false,
            TotalBarsProcessed = 0,
            EstimatedCompletionPct = 0m
        };
    }
    
    /// <summary>
    /// Update checkpoint with current trading state  
    /// </summary>
    public void UpdateCheckpoint(BacktestCheckpoint checkpoint, TradingSession session, DateTime currentDate, int totalBars, int processedBars, decimal currentCapital)
    {
        checkpoint.LastProcessedDate = currentDate;
        checkpoint.TotalBarsProcessed = processedBars;
        checkpoint.EstimatedCompletionPct = (decimal)processedBars / totalBars * 100m;
        
        // Update running metrics
        checkpoint.CurrentCapital = currentCapital;
        checkpoint.RunningPnL = session.TotalPnL;
        checkpoint.WeeklyPremium = session.WeeklyPremium;
        checkpoint.MonthlyPremium = session.MonthlyPremium;
        
        if (session.PositionsOpened > 0 || session.PositionsClosed > 0)
        {
            checkpoint.TotalTrades += session.PositionsOpened + session.PositionsClosed;
        }
        
        // Track drawdown
        if (currentCapital > checkpoint.PeakCapital)
        {
            checkpoint.PeakCapital = currentCapital;
        }
        
        var currentDrawdown = (checkpoint.PeakCapital - currentCapital) / checkpoint.PeakCapital;
        if (currentDrawdown > checkpoint.MaxDrawdown)
        {
            checkpoint.MaxDrawdown = currentDrawdown;
        }
    }
    
    /// <summary>
    /// List all available checkpoints for a symbol
    /// </summary>
    public List<BacktestCheckpoint> ListCheckpoints(string symbol)
    {
        return GetCheckpointFiles(symbol)
            .Select(file => 
            {
                try
                {
                    var json = File.ReadAllText(file);
                    return JsonSerializer.Deserialize<BacktestCheckpoint>(json, _jsonOptions);
                }
                catch
                {
                    return null;
                }
            })
            .Where(cp => cp != null)
            .Cast<BacktestCheckpoint>()
            .OrderByDescending(cp => cp.LastCheckpointTime)
            .ToList();
    }
    
    /// <summary>
    /// Clean up old completed checkpoints (keep last 5)
    /// </summary>
    public void CleanupOldCheckpoints(string symbol, int keepCount = 5)
    {
        var completedCheckpoints = GetCheckpointFiles(symbol)
            .Where(f => IsCompleted(f))
            .OrderByDescending(File.GetLastWriteTime)
            .Skip(keepCount)
            .ToList();
            
        foreach (var file in completedCheckpoints)
        {
            try
            {
                File.Delete(file);
                Console.WriteLine($"🗑️  Cleaned up old checkpoint: {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Failed to delete checkpoint {file}: {ex.Message}");
            }
        }
    }
    
    private List<string> GetCheckpointFiles(string symbol)
    {
        return Directory.GetFiles(_checkpointDir, $"checkpoint_{symbol}_*.json").ToList();
    }
    
    private bool IsCompleted(string checkpointFile)
    {
        try
        {
            var json = File.ReadAllText(checkpointFile);
            var checkpoint = JsonSerializer.Deserialize<BacktestCheckpoint>(json, _jsonOptions);
            return checkpoint?.IsCompleted ?? false;
        }
        catch
        {
            return false;
        }
    }
    
    private string ComputeConfigHash(StrategyConfig config)
    {
        var configJson = JsonSerializer.Serialize(config);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(configJson));
        return Convert.ToHexString(hashBytes)[..12]; // First 12 characters
    }
}