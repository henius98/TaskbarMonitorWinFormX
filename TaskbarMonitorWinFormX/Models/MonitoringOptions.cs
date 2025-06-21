namespace TaskbarMonitorWinFormX.Models;
public sealed record MonitoringOptions
{
    public int UpdateIntervalMs { get; init; } = 1000;
    public int HistorySize { get; init; } = 60;
    public float NetworkThresholdMbps { get; init; } = 10.0f;
    public int IconSize { get; init; } = 16;
    public int BarCount { get; init; } = 3;
    public int BarWidth { get; init; } = 4;
    public int BarSpacing { get; init; } = 1;
}