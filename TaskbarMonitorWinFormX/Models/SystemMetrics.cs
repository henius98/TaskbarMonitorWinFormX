namespace TaskbarMonitorWinFormX.Models;

public readonly record struct SystemMetrics(
    int CpuUsagePercent,
    int RamUsagePercent,
    int NetworkSpeedMbps,
    int AverageNetworkSpeedMbps,
    DateTime Timestamp)
{
    public static SystemMetrics Empty => new(0, 0, 0, 0, DateTime.UtcNow);
}

public sealed class MetricsHistory : IDisposable
{
    private readonly List<int> _values = new();
    private readonly int _maxSize;

    public MetricsHistory(int maxSize)
    {
        _maxSize = maxSize;
    }

    public void Add(int value)
    {
        _values.Add(value);
        if (_values.Count > _maxSize)
        {
            _values.RemoveAt(0);
        }
    }

    public int Average => _values.Count > 0 ? (int)_values.Average() : 0;
    public int Current => _values.Count > 0 ? _values[^1] : 0;
    public int[] Values => _values.ToArray();
    public int Count => _values.Count;

    public void Dispose()
    {
        _values.Clear();
    }
}

public record NetworkStats(float UploadBps, float DownloadBps, int TotalBps, string InterfaceName);