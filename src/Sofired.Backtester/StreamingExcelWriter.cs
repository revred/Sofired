using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Sofired.Core;

namespace Sofired.Backtester;

/// <summary>
/// Streaming Excel writer that can append data incrementally without holding everything in memory
/// </summary>
public class StreamingExcelWriter : IDisposable
{
    private readonly string _filePath;
    private SpreadsheetDocument? _document;
    private WorkbookPart? _workbookPart;
    private Sheets? _sheets;
    private uint _nextSheetId = 1;
    private bool _isInitialized = false;
    
    // Sheet references for appending
    private WorksheetPart? _summarySheet;
    private WorksheetPart? _tradesSheet;
    private WorksheetPart? _dailySheet;
    private SheetData? _summaryData;
    private SheetData? _tradesData;
    private SheetData? _dailyData;
    
    // Track row counts for appending
    private uint _tradesRowCount = 0;
    private uint _dailyRowCount = 0;
    
    public StreamingExcelWriter(string filePath)
    {
        _filePath = filePath;
    }
    
    /// <summary>
    /// Initialize the Excel file with headers
    /// </summary>
    public void Initialize(string symbol, DateTime startDate, DateTime endDate, decimal initialCapital)
    {
        if (_isInitialized) 
        {
            Console.WriteLine($"🔄 Excel writer already initialized for: {_filePath}");
            return;
        }
        
        try
        {
            Console.WriteLine($"🚀 Initializing Excel writer for: {_filePath}");
            
            // Ensure directory exists
            var directory = Path.GetDirectoryName(_filePath);
            Console.WriteLine($"📁 Creating directory: {directory}");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                Console.WriteLine($"✅ Directory created successfully: {directory}");
            }
            
            // Create the Excel file
            Console.WriteLine($"📝 Creating Excel document...");
            _document = SpreadsheetDocument.Create(_filePath, SpreadsheetDocumentType.Workbook);
            _workbookPart = _document.AddWorkbookPart();
            _workbookPart.Workbook = new Workbook();
            _sheets = new Sheets();
            _workbookPart.Workbook.AppendChild(_sheets);
            
            Console.WriteLine($"📋 Creating sheets...");
            // Create Summary Sheet
            CreateSummarySheet(symbol, startDate, endDate, initialCapital);
            
            // Create Trades Sheet with headers
            CreateTradesSheet();
            
            // Create Daily Performance Sheet with headers
            CreateDailyPerformanceSheet();
            
            // Save initial structure
            Console.WriteLine($"💾 Saving Excel document...");
            _document.Save();
            _isInitialized = true;
            
            Console.WriteLine($"📊 Excel file initialized: {_filePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ERROR initializing Excel file: {ex.Message}");
            Console.WriteLine($"❌ Stack trace: {ex.StackTrace}");
            throw;
        }
    }
    
    /// <summary>
    /// Append trading sessions to the Excel file
    /// </summary>
    public void AppendSessions(List<TradingSession> sessions)
    {
        if (!_isInitialized || _document == null) 
        {
            Console.WriteLine($"❌ Excel writer not initialized when trying to append {sessions.Count} sessions");
            throw new InvalidOperationException("Excel writer not initialized. Call Initialize first.");
        }
        
        Console.WriteLine($"📝 Appending {sessions.Count} sessions to Excel...");
        
        foreach (var session in sessions)
        {
            // Append to trades sheet
            if (_tradesData != null && (session.PositionsOpened > 0 || session.PositionsClosed > 0))
            {
                var row = new Row();
                row.Append(
                    CreateCell(session.Date.ToString("yyyy-MM-dd")),
                    CreateCell(session.Date.ToString("HH:mm")),
                    CreateCell(session.PositionsOpened.ToString()),
                    CreateCell(session.PositionsClosed.ToString()),
                    CreateCell(session.DailyPremium.ToString("F2")),
                    CreateCell(session.WeeklyPremium.ToString("F2")),
                    CreateCell(session.MonthlyPremium.ToString("F2")),
                    CreateCell(session.TotalPnL.ToString("F2")),
                    CreateCell(session.GoalsMet ? "Yes" : "No")
                );
                _tradesData.AppendChild(row);
                _tradesRowCount++;
            }
            
            // Append to daily performance (aggregate by day)
            if (_dailyData != null)
            {
                var row = new Row();
                row.Append(
                    CreateCell(session.Date.ToString("yyyy-MM-dd")),
                    CreateCell(session.DailyPremium.ToString("F2")),
                    CreateCell(session.TotalPnL.ToString("F2")),
                    CreateCell(session.PositionsOpened.ToString()),
                    CreateCell(session.PositionsClosed.ToString())
                );
                _dailyData.AppendChild(row);
                _dailyRowCount++;
            }
        }
        
        // Save changes
        _tradesSheet?.Worksheet.Save();
        _dailySheet?.Worksheet.Save();
        _document.Save();
        
        Console.WriteLine($"📝 Appended {sessions.Count} sessions to Excel (Total rows: {_tradesRowCount})");
    }
    
    /// <summary>
    /// Update the summary sheet with final statistics
    /// </summary>
    public void UpdateSummary(BacktestCheckpoint checkpoint)
    {
        if (!_isInitialized || _summaryData == null) return;
        
        // Clear existing summary data (except headers)
        var rows = _summaryData.Elements<Row>().Skip(1).ToList();
        foreach (var row in rows)
        {
            row.Remove();
        }
        
        // Add updated summary rows
        AddSummaryRow(_summaryData, "Backtest ID", checkpoint.BacktestId);
        AddSummaryRow(_summaryData, "Symbol", checkpoint.Symbol);
        AddSummaryRow(_summaryData, "Start Date", checkpoint.StartDate.ToString("yyyy-MM-dd"));
        AddSummaryRow(_summaryData, "End Date", checkpoint.EndDate.ToString("yyyy-MM-dd"));
        AddSummaryRow(_summaryData, "Last Processed", checkpoint.LastProcessedDate.ToString("yyyy-MM-dd"));
        AddEmptyRow(_summaryData);
        
        AddSummaryRow(_summaryData, "Initial Capital", $"${checkpoint.CurrentCapital - checkpoint.RunningPnL:F2}");
        AddSummaryRow(_summaryData, "Final Capital", $"${checkpoint.CurrentCapital:F2}");
        AddSummaryRow(_summaryData, "Total P&L", $"${checkpoint.RunningPnL:F2}");
        AddSummaryRow(_summaryData, "ROI", $"{(checkpoint.RunningPnL / (checkpoint.CurrentCapital - checkpoint.RunningPnL)) * 100:F2}%");
        AddSummaryRow(_summaryData, "Max Drawdown", $"{checkpoint.MaxDrawdown * 100:F2}%");
        AddEmptyRow(_summaryData);
        
        AddSummaryRow(_summaryData, "Total Trades", checkpoint.TotalTrades.ToString());
        AddSummaryRow(_summaryData, "Win Rate", $"{checkpoint.WinRate * 100:F2}%");
        AddSummaryRow(_summaryData, "Weekly Premium", $"${checkpoint.WeeklyPremium:F2}");
        AddSummaryRow(_summaryData, "Monthly Premium", $"${checkpoint.MonthlyPremium:F2}");
        AddEmptyRow(_summaryData);
        
        AddSummaryRow(_summaryData, "Completion", $"{checkpoint.EstimatedCompletionPct:F1}%");
        AddSummaryRow(_summaryData, "Last Checkpoint", checkpoint.LastCheckpointTime.ToString("yyyy-MM-dd HH:mm"));
        
        _summarySheet?.Worksheet.Save();
        _document?.Save();
        
        Console.WriteLine($"📊 Updated summary sheet with latest statistics");
    }
    
    private void CreateSummarySheet(string symbol, DateTime startDate, DateTime endDate, decimal initialCapital)
    {
        _summarySheet = _workbookPart!.AddNewPart<WorksheetPart>();
        _summaryData = new SheetData();
        _summarySheet.Worksheet = new Worksheet(_summaryData);
        
        var sheet = new Sheet()
        {
            Id = _workbookPart.GetIdOfPart(_summarySheet),
            SheetId = _nextSheetId++,
            Name = "Summary"
        };
        _sheets!.Append(sheet);
        
        // Add header
        var headerRow = new Row();
        headerRow.Append(
            CreateCell("Metric"),
            CreateCell("Value")
        );
        _summaryData.AppendChild(headerRow);
        
        // Initial summary data
        AddSummaryRow(_summaryData, "Symbol", symbol);
        AddSummaryRow(_summaryData, "Start Date", startDate.ToString("yyyy-MM-dd"));
        AddSummaryRow(_summaryData, "End Date", endDate.ToString("yyyy-MM-dd"));
        AddSummaryRow(_summaryData, "Initial Capital", $"${initialCapital:F2}");
        AddSummaryRow(_summaryData, "Status", "In Progress...");
    }
    
    private void CreateTradesSheet()
    {
        _tradesSheet = _workbookPart!.AddNewPart<WorksheetPart>();
        _tradesData = new SheetData();
        _tradesSheet.Worksheet = new Worksheet(_tradesData);
        
        var sheet = new Sheet()
        {
            Id = _workbookPart.GetIdOfPart(_tradesSheet),
            SheetId = _nextSheetId++,
            Name = "Trades"
        };
        _sheets!.Append(sheet);
        
        // Add headers
        var headerRow = new Row();
        headerRow.Append(
            CreateCell("Date"),
            CreateCell("Time"),
            CreateCell("Positions Opened"),
            CreateCell("Positions Closed"),
            CreateCell("Daily Premium"),
            CreateCell("Weekly Premium"),
            CreateCell("Monthly Premium"),
            CreateCell("Total P&L"),
            CreateCell("Goals Met")
        );
        _tradesData.AppendChild(headerRow);
        _tradesRowCount = 1;
    }
    
    private void CreateDailyPerformanceSheet()
    {
        _dailySheet = _workbookPart!.AddNewPart<WorksheetPart>();
        _dailyData = new SheetData();
        _dailySheet.Worksheet = new Worksheet(_dailyData);
        
        var sheet = new Sheet()
        {
            Id = _workbookPart.GetIdOfPart(_dailySheet),
            SheetId = _nextSheetId++,
            Name = "Daily Performance"
        };
        _sheets!.Append(sheet);
        
        // Add headers
        var headerRow = new Row();
        headerRow.Append(
            CreateCell("Date"),
            CreateCell("Daily Premium"),
            CreateCell("Cumulative P&L"),
            CreateCell("Positions Opened"),
            CreateCell("Positions Closed")
        );
        _dailyData.AppendChild(headerRow);
        _dailyRowCount = 1;
    }
    
    private void AddSummaryRow(SheetData sheetData, string metric, string value)
    {
        var row = new Row();
        row.Append(CreateCell(metric), CreateCell(value));
        sheetData.AppendChild(row);
    }
    
    private void AddEmptyRow(SheetData sheetData)
    {
        sheetData.AppendChild(new Row());
    }
    
    private Cell CreateCell(string value)
    {
        return new Cell()
        {
            CellValue = new CellValue(value),
            DataType = CellValues.String
        };
    }
    
    public void Dispose()
    {
        if (_document != null)
        {
            _document.Save();
            _document.Dispose();
            _document = null;
            Console.WriteLine($"💾 Excel file saved and closed: {_filePath}");
        }
    }
}