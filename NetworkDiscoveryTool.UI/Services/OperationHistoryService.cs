using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.Data;

namespace NetworkDiscoveryTool.UI.Services;

public interface IOperationHistoryService
{
    Task LogAsync(string operationName, string description, string result, long durationMs, string username);
    Task<List<OperationLog>> GetAllAsync();
    Task<List<OperationLog>> SearchAsync(string? operationName, string? username, string? result, DateTime? from, DateTime? to, int limit = 500);
    Task DeleteAsync(int id);
    Task ClearAllAsync();
    Task<int> GetTotalCountAsync();
    Task<Dictionary<string, int>> GetOperationStatsAsync();
    Task ExportToExcelAsync(List<OperationLog> entries, string filePath);
}

public sealed class OperationHistoryService : IOperationHistoryService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public OperationHistoryService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task LogAsync(string operationName, string description, string result, long durationMs, string username)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        ctx.OperationLogs.Add(new OperationLog
        {
            OperationName = operationName,
            Description = description,
            Timestamp = DateTime.Now,
            Result = result,
            DurationMs = durationMs,
            Username = username,
        });
        await ctx.SaveChangesAsync();
    }

    public async Task<List<OperationLog>> GetAllAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        return await ctx.OperationLogs
            .AsNoTracking()
            .OrderByDescending(o => o.Timestamp)
            .Take(500)
            .ToListAsync();
    }

    public async Task<List<OperationLog>> SearchAsync(string? operationName, string? username, string? result, DateTime? from, DateTime? to, int limit = 500)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var query = ctx.OperationLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(operationName))
            query = query.Where(o => o.OperationName == operationName);
        if (!string.IsNullOrWhiteSpace(username))
            query = query.Where(o => o.Username.Contains(username));
        if (!string.IsNullOrWhiteSpace(result))
            query = query.Where(o => o.Result == result);
        if (from.HasValue)
            query = query.Where(o => o.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(o => o.Timestamp <= to.Value.AddDays(1));

        return await query
            .OrderByDescending(o => o.Timestamp)
            .Take(limit)
            .ToListAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var entry = await ctx.OperationLogs.FindAsync(id);
        if (entry is not null)
        {
            ctx.OperationLogs.Remove(entry);
            await ctx.SaveChangesAsync();
        }
    }

    public async Task ClearAllAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM OperationLogs");
    }

    public async Task<int> GetTotalCountAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        return await ctx.OperationLogs.CountAsync();
    }

    public async Task<Dictionary<string, int>> GetOperationStatsAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        return await ctx.OperationLogs
            .GroupBy(o => o.OperationName)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Name, x => x.Count);
    }

    public async Task ExportToExcelAsync(List<OperationLog> entries, string filePath)
    {
        await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Operation History");

            ws.Cell(1, 1).Value = "ID";
            ws.Cell(1, 2).Value = "Operation";
            ws.Cell(1, 3).Value = "Description";
            ws.Cell(1, 4).Value = "Date";
            ws.Cell(1, 5).Value = "Time";
            ws.Cell(1, 6).Value = "Result";
            ws.Cell(1, 7).Value = "Duration (ms)";
            ws.Cell(1, 8).Value = "User";

            var headerRange = ws.Range(1, 1, 1, 8);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(37, 99, 235);
            headerRange.Style.Font.FontColor = XLColor.White;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                int row = i + 2;
                ws.Cell(row, 1).Value = e.Id;
                ws.Cell(row, 2).Value = e.OperationName;
                ws.Cell(row, 3).Value = e.Description;
                ws.Cell(row, 4).Value = e.Timestamp.ToString("yyyy-MM-dd");
                ws.Cell(row, 5).Value = e.Timestamp.ToString("HH:mm:ss");
                ws.Cell(row, 6).Value = e.Result;
                ws.Cell(row, 7).Value = e.DurationMs;
                ws.Cell(row, 8).Value = e.Username;
            }

            // Summary sheet
            var summary = workbook.Worksheets.Add("Summary");
            summary.Cell(1, 1).Value = "Metric";
            summary.Cell(1, 2).Value = "Value";
            summary.Range(1, 1, 1, 2).Style.Font.Bold = true;
            summary.Cell(2, 1).Value = "Total Operations";
            summary.Cell(2, 2).Value = entries.Count;
            summary.Cell(3, 1).Value = "Date Range";
            summary.Cell(3, 2).Value = entries.Count > 0
                ? $"{entries.Min(e => e.Timestamp):yyyy-MM-dd} to {entries.Max(e => e.Timestamp):yyyy-MM-dd}"
                : "N/A";
            summary.Cell(4, 1).Value = "Export Date";
            summary.Cell(4, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            ws.Columns().AdjustToContents();
            summary.Columns().AdjustToContents();

            workbook.SaveAs(filePath);
        });
    }
}
