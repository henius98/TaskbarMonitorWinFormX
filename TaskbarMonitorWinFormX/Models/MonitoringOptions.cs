namespace TaskbarMonitorWinFormX.Models;

public sealed record MonitoringOptions
{
    public int UpdateIntervalMs { get; init; } = 1000;
    public int HistorySize { get; init; } = 20; // Changed to 20 seconds
    public float NetworkThresholdMbps { get; init; } = 10.0f;
    public int IconSize { get; init; } = 16;
    public int GraphWidth { get; init; } = 14;
    public int GraphHeight { get; init; } = 14;
    public int GraphPadding { get; init; } = 1;
}