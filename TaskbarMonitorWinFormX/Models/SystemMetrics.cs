namespace TaskbarMonitorWinFormX.Models;

public readonly record struct SystemMetrics(
    int CpuUsagePercent,
    int RamUsagePercent,
    int NetworkSpeedMbps,
    int AverageNetworkSpeedMbps,
    DateTime Timestamp)
{
    public static SystemMetrics Empty => new(0, 0, 0, 0, DateTime.UtcNow);

    public string TooltipText =>
        $"CPU: {CpuUsagePercent}% | RAM: {RamUsagePercent}% | Net: {NetworkSpeedMbps} MB/s";
}

public sealed class MetricsHistory
{
    private readonly int[] _values;
    private readonly int _maxSize;
    private int _currentIndex;
    private int _count;

    public MetricsHistory(int maxSize)
    {
        _maxSize = maxSize;
        _values = new int[maxSize];
        _currentIndex = 0;
        _count = 0;
    }

    public void Add(int value)
    {
        _values[_currentIndex] = value;
        _currentIndex = (_currentIndex + 1) % _maxSize;
        if (_count < _maxSize) _count++;
    }

    public int Average => _count > 0 ? _values.Take(_count).Sum() / _count : 0;
    public int Current => _count > 0 ? _values[(_currentIndex - 1 + _maxSize) % _maxSize] : 0;
    public int[] Values => _values.Take(_count).ToArray();
    public int Count => _count;
}