using Microsoft.Extensions.Logging;
using TaskbarMonitorWinFormX.Models;

namespace TaskbarMonitorWinFormX.Services;

public interface ISystemMetricsService : IDisposable
{
    SystemMetrics GetCurrentMetrics();
    MetricsHistory GetCpuHistory();
    MetricsHistory GetRamHistory();
    MetricsHistory GetNetworkHistory();
    event EventHandler<SystemMetrics>? MetricsUpdated;
    void StartMonitoring();
    void StopMonitoring();
    bool IsMonitoring { get; }
}

public sealed class SystemMetricsService : ISystemMetricsService
{
    private readonly IPerformanceCounterService _performanceCounterService;
    private readonly ILogger<SystemMetricsService> _logger;
    private readonly MonitoringOptions _options;
    private readonly System.Threading.Timer _updateTimer;
    private readonly MetricsHistory _cpuHistory;
    private readonly MetricsHistory _ramHistory;
    private readonly MetricsHistory _networkHistory;
    private bool _disposed;
    private volatile bool _isMonitoring;

    public event EventHandler<SystemMetrics>? MetricsUpdated;
    public bool IsMonitoring => _isMonitoring;

    public SystemMetricsService(
        IPerformanceCounterService performanceCounterService,
        ILogger<SystemMetricsService> logger,
        MonitoringOptions options)
    {
        _performanceCounterService = performanceCounterService;
        _logger = logger;
        _options = options;
        _cpuHistory = new MetricsHistory(_options.HistorySize);
        _ramHistory = new MetricsHistory(_options.HistorySize);
        _networkHistory = new MetricsHistory(_options.HistorySize);
        _updateTimer = new System.Threading.Timer(UpdateMetrics, null, Timeout.Infinite, Timeout.Infinite);
    }

    public MetricsHistory GetCpuHistory() => _cpuHistory;
    public MetricsHistory GetRamHistory() => _ramHistory;
    public MetricsHistory GetNetworkHistory() => _networkHistory;

    public void StartMonitoring()
    {
        if (_isMonitoring || !_performanceCounterService.IsInitialized) return;
        _isMonitoring = true;
        _updateTimer.Change(0, _options.UpdateIntervalMs);
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring) return;
        _isMonitoring = false;
        _updateTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public SystemMetrics GetCurrentMetrics()
    {
        if (!_performanceCounterService.IsInitialized) return SystemMetrics.Empty;
        return new SystemMetrics(_cpuHistory.Current, _ramHistory.Current,
            _networkHistory.Current, _networkHistory.Average, DateTime.UtcNow);
    }

    private void UpdateMetrics(object? state)
    {
        try
        {
            if (!_isMonitoring || !_performanceCounterService.IsInitialized) return;

            var cpu = _performanceCounterService.GetCpuUsage();
            var ram = _performanceCounterService.GetRamUsagePercent();
            var network = _performanceCounterService.GetNetworkSpeedMBps();

            _cpuHistory.Add(cpu);
            _ramHistory.Add(ram);
            _networkHistory.Add(network);

            var metrics = new SystemMetrics(cpu, ram, network, _networkHistory.Average, DateTime.UtcNow);
            MetricsUpdated?.Invoke(this, metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating metrics");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopMonitoring();
        _updateTimer?.Dispose();
        _performanceCounterService?.Dispose();
        _disposed = true;
    }
}