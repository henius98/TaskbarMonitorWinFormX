using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
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

    // Use PeriodicTimer for better precision and reduced overhead
    private readonly PeriodicTimer _periodicTimer;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private readonly MetricsHistory _cpuHistory;
    private readonly MetricsHistory _ramHistory;
    private readonly MetricsHistory _networkHistory;

    private Task? _monitoringTask;
    private volatile bool _isMonitoring;
    private bool _disposed;

    // Cache for avoiding repeated allocations
    private SystemMetrics _lastMetrics = SystemMetrics.Empty;

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

        // Use PeriodicTimer for better performance than System.Threading.Timer
        _periodicTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.UpdateIntervalMs));

        _cpuHistory = new MetricsHistory(_options.HistorySize);
        _ramHistory = new MetricsHistory(_options.HistorySize);
        _networkHistory = new MetricsHistory(_options.HistorySize);
    }

    public MetricsHistory GetCpuHistory() => _cpuHistory;
    public MetricsHistory GetRamHistory() => _ramHistory;
    public MetricsHistory GetNetworkHistory() => _networkHistory;

    public void StartMonitoring()
    {
        if (_isMonitoring || !_performanceCounterService.IsInitialized || _disposed)
            return;

        _isMonitoring = true;
        _monitoringTask = MonitoringLoopAsync(_cancellationTokenSource.Token);

        LogDebug("System metrics monitoring started");
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring) return;

        _isMonitoring = false;
        _cancellationTokenSource.Cancel();

        try
        {
            _monitoringTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelling
        }
        catch (Exception ex)
        {
            LogError(ex, "Error stopping monitoring");
        }

        LogDebug("System metrics monitoring stopped");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SystemMetrics GetCurrentMetrics() => _lastMetrics;

    /// <summary>
    /// High-performance monitoring loop using PeriodicTimer
    /// </summary>
    private async Task MonitoringLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Wait for first tick to ensure proper initialization
            await Task.Delay(100, cancellationToken);

            while (_isMonitoring && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _periodicTimer.WaitForNextTickAsync(cancellationToken);

                    if (!_isMonitoring || cancellationToken.IsCancellationRequested)
                        break;

                    UpdateMetricsCore();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogError(ex, "Error in monitoring loop iteration");

                    // Brief delay before retrying to avoid tight error loops
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            LogError(ex, "Fatal error in monitoring loop");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateMetricsCore()
    {
        if (!_performanceCounterService.IsInitialized) return;

        // Get all metrics in one batch to reduce latency
        var cpu = _performanceCounterService.GetCpuUsage();
        var ram = _performanceCounterService.GetRamUsagePercent();
        var network = _performanceCounterService.GetNetworkSpeedMBps();

        // Update histories using high-performance Add method
        _cpuHistory.Add(cpu);
        _ramHistory.Add(ram);
        _networkHistory.Add(network);

        // Cache the current metrics to avoid repeated allocations
        _lastMetrics = new SystemMetrics(
            cpu,
            ram,
            network,
            _networkHistory.Average,
            DateTime.UtcNow);

        // Notify subscribers (this will be marshaled to UI thread)
        NotifyMetricsUpdated(_lastMetrics);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NotifyMetricsUpdated(SystemMetrics metrics)
    {
        try
        {
            MetricsUpdated?.Invoke(this, metrics);
        }
        catch (Exception ex)
        {
            LogError(ex, "Error notifying metrics update subscribers");
        }
    }

    #region Optimized Logging

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LogDebug(string message)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug(message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LogError(Exception ex, string message)
    {
        if (_logger.IsEnabled(LogLevel.Error))
            _logger.LogError(ex, message);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            StopMonitoring();

            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();

            _periodicTimer.Dispose();

            _cpuHistory.Dispose();
            _ramHistory.Dispose();
            _networkHistory.Dispose();

            _performanceCounterService?.Dispose();
        }
        catch (Exception ex)
        {
            LogError(ex, "Error during SystemMetricsService disposal");
        }
        finally
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    #endregion
}