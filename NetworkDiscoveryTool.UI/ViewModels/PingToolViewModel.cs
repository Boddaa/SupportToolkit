using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class PingToolViewModel : ObservableObject
{
    private readonly IOperationHistoryService _history;
    private readonly CurrentUserService _currentUser;
    private CancellationTokenSource? _cts;
    private int _successCount;
    private double _latencySum;
    private long _latencyMin = long.MaxValue;
    private long _latencyMax;

    [ObservableProperty] private string _targetHost = "8.8.8.8";
    [ObservableProperty] private int _timeout = 2000;
    [ObservableProperty] private int _count = 4;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _canStop;
    [ObservableProperty] private double _averageLatency;
    [ObservableProperty] private string _packetLossPercent = "0%";
    [ObservableProperty] private long _minLatency;
    [ObservableProperty] private long _maxLatency;
    [ObservableProperty] private int _totalSent;
    [ObservableProperty] private int _totalReceived;
    [ObservableProperty] private int _totalLost;
    [ObservableProperty] private string _elapsedTime = "00:00";
    [ObservableProperty] private string _statusMessage = "";

    public ObservableCollection<PingResultItem> Results { get; } = [];

    public PingToolViewModel(IOperationHistoryService history, CurrentUserService currentUser)
    {
        _history = history;
        _currentUser = currentUser;
    }

    [RelayCommand]
    private async Task PingAsync()
    {
        if (IsRunning) return;

        if (string.IsNullOrWhiteSpace(TargetHost))
        {
            StatusMessage = "Please enter a host address";
            return;
        }
        if (Count <= 0)
        {
            StatusMessage = "Count must be at least 1";
            return;
        }
        if (Timeout <= 0)
        {
            StatusMessage = "Timeout must be a positive value";
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var localCts = _cts;

        IsRunning = true;
        CanStop = true;
        StatusMessage = "";
        Results.Clear();
        ResetStats();

        var sw = Stopwatch.StartNew();

        try
        {
            for (int i = 0; i < Count; i++)
            {
                if (localCts.Token.IsCancellationRequested) break;

                try
                {
                    using var ping = new Ping();
                    var pingSw = Stopwatch.StartNew();
                    var reply = await ping.SendPingAsync(TargetHost, Timeout);
                    pingSw.Stop();

                    var isSuccess = reply.Status == IPStatus.Success;
                    Results.Add(new PingResultItem
                    {
                        Seq = i + 1,
                        Status = reply.Status.ToString(),
                        LatencyMs = isSuccess ? pingSw.ElapsedMilliseconds : -1,
                        TTL = reply.Options?.Ttl,
                        IP = reply.Address?.ToString() ?? TargetHost,
                        Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
                        IsSuccess = isSuccess,
                    });
                    UpdateRunningStats(isSuccess, isSuccess ? pingSw.ElapsedMilliseconds : (long?)null);
                    ElapsedTime = sw.Elapsed.ToString(@"mm\:ss");

                    if (reply.Status == IPStatus.TimedOut)
                        StatusMessage = $"Request timed out for {TargetHost}";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Results.Add(new PingResultItem
                    {
                        Seq = i + 1,
                        Status = "Error",
                        LatencyMs = -1,
                        Error = ex.Message,
                        IP = TargetHost,
                        Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
                        IsSuccess = false,
                    });
                    UpdateRunningStats(false, null);
                    ElapsedTime = sw.Elapsed.ToString(@"mm\:ss");
                    StatusMessage = ex.Message;
                }
            }
        }
        finally
        {
            localCts.Dispose();
            if (_cts == localCts)
            {
                _cts = null;
                IsRunning = false;
                CanStop = false;
            }
            ElapsedTime = sw.Elapsed.ToString(@"mm\:ss");
        }

        var success = Results.Count(r => r.IsSuccess);
        await _history.LogAsync("Ping",
            $"{TargetHost} | {success}/{Count} replies | avg={AverageLatency:F0}ms | loss={PacketLossPercent}",
            success > 0 ? "Success" : "Failed",
            sw.ElapsedMilliseconds,
            _currentUser.Username);
    }

    [RelayCommand]
    private void StopPing()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        CanStop = false;
        StatusMessage = "Ping cancelled by user";
    }

    [RelayCommand]
    private void ClearResults()
    {
        Results.Clear();
        ResetStats();
        ElapsedTime = "00:00";
        StatusMessage = "";
    }

    [RelayCommand]
    private void CopyResults()
    {
        if (Results.Count == 0)
        {
            StatusMessage = "No results to copy";
            return;
        }
        var lines = Results.Select(r =>
            $"#{r.Seq} | {r.IP} | {r.Status} | {(r.LatencyMs >= 0 ? $"{r.LatencyMs}ms" : "---")} | TTL:{r.TTL?.ToString() ?? "---"} | {r.Timestamp}");
        var text = string.Join(Environment.NewLine, lines);
        try
        {
            System.Windows.Clipboard.SetText(text);
            StatusMessage = $"Copied {Results.Count} results to clipboard";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to copy: {ex.Message}";
        }
    }

    private void ResetStats()
    {
        AverageLatency = 0;
        PacketLossPercent = "0%";
        MinLatency = 0;
        MaxLatency = 0;
        TotalSent = 0;
        TotalReceived = 0;
        TotalLost = 0;
        _successCount = 0;
        _latencySum = 0;
        _latencyMin = long.MaxValue;
        _latencyMax = 0;
    }

    private void UpdateRunningStats(bool isSuccess, long? latencyMs)
    {
        TotalSent++;
        if (isSuccess && latencyMs.HasValue)
        {
            _successCount++;
            _latencySum += latencyMs.Value;
            if (latencyMs.Value < _latencyMin) _latencyMin = latencyMs.Value;
            if (latencyMs.Value > _latencyMax) _latencyMax = latencyMs.Value;
            AverageLatency = _latencySum / _successCount;
            MinLatency = _latencyMin;
            MaxLatency = _latencyMax;
            TotalReceived = _successCount;
        }
        else
        {
            TotalLost++;
        }
        PacketLossPercent = TotalSent > 0
            ? $"{(double)TotalLost / TotalSent * 100:F1}%"
            : "0%";
    }
}

public sealed record PingResultItem
{
    public int Seq { get; init; }
    public string Status { get; init; } = "";
    public long LatencyMs { get; init; }
    public int? TTL { get; init; }
    public string? Error { get; init; }
    public string IP { get; init; } = "";
    public string Timestamp { get; init; } = "";
    public bool IsSuccess { get; init; }
}
