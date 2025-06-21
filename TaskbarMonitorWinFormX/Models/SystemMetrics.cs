namespace TaskbarMonitorWinFormX.Models;

public readonly record struct SystemMetrics(
    float CpuUsagePercent,
    float RamUsagePercent,
    float NetworkSpeedMbps,
    float AverageNetworkSpeedMbps,
    DateTime Timestamp)
{
    public static SystemMetrics Empty => new(0, 0, 0, 0, DateTime.UtcNow);

    public string TooltipText =>
        $"CPU: {CpuUsagePercent:F1}% | RAM: {RamUsagePercent:F1}% | Net: {NetworkSpeedMbps:F1} MB/s";
}

public sealed class MetricsHistory
{
    private readonly Queue<float> _values;
    private readonly int _maxSize;

    public MetricsHistory(int maxSize)
    {
        _maxSize = maxSize;
        _values = new Queue<float>(maxSize);
    }

    public void Add(float value)
    {
        _values.Enqueue(value);
        while (_values.Count > _maxSize)
        {
            _values.Dequeue();
        }
    }

    public float Average => _values.Count > 0 ? _values.Average() : 0f;
    public float Current => _values.Count > 0 ? _values.Last() : 0f;
    public IReadOnlyCollection<float> Values => _values.ToArray();
    public int Count => _values.Count;
}