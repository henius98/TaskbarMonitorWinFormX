namespace TaskbarMonitorWinFormX.Models;

public sealed record MonitoringOptions
{
    public int UpdateIntervalMs { get; init; } = 500; // Changed from 1000ms to 2000ms
    public int HistorySize { get; init; } = 30; // Changed from 60 to 30
    public int NetworkThresholdMbps { get; init; } = 10;
    public int IconSize { get; init; } = 16;
    public int GraphWidth { get; init; } = 14;
    public int GraphHeight { get; init; } = 14;
    public int GraphPadding { get; init; } = 1;
    // FIXED: Increased cache bucket size to reduce icon generation
    public int IconCacheBucketSize { get; init; } = 10; // Changed from 5 to 10 (cache icons in 10% buckets)
}