namespace TaskbarMonitorWinFormX.Models;

public sealed record MonitoringOptions
{
    public int UpdateIntervalMs { get; init; } = 1000;
    public int HistorySize { get; init; } = 20;
    public int NetworkThresholdMbps { get; init; } = 300;
    public int IconSize { get; init; } = 16;
    public int GraphWidth { get; init; } = 14;
    public int GraphHeight { get; init; } = 14;
    public int GraphPadding { get; init; } = 1;
}