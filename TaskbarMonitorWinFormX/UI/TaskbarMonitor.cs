using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using TaskbarMonitorWinFormX.Models;
using TaskbarMonitorWinFormX.Services;

namespace TaskbarMonitorWinFormX.UI;

public sealed class TaskbarMonitor : IDisposable
{
    private readonly ISystemMetricsService _metricsService;
    private readonly IIconGeneratorService _iconGeneratorService;
    private readonly ILogger<TaskbarMonitor> _logger;
    private readonly MonitoringOptions _options;

    private NotifyIcon? _cpuNotifyIcon;
    private NotifyIcon? _ramNotifyIcon;
    private NotifyIcon? _networkNotifyIcon;
    private ContextMenuStrip? _contextMenu;

    // Thread synchronization for UI updates
    private readonly SynchronizationContext? _uiContext;
    private readonly object _updateLock = new();

    // Icon disposal management - simplified and more reliable
    private readonly ConcurrentQueue<Icon> _iconDisposalQueue = new();
    private readonly System.Threading.Timer _disposalTimer;

    // Rate limiting to prevent UI flooding
    private readonly object _rateLimitLock = new();
    private DateTime _lastUpdateTime = DateTime.MinValue;
    private readonly TimeSpan _minUpdateInterval = TimeSpan.FromMilliseconds(100); // Max 10 FPS

    private bool _disposed;
    private volatile bool _isInitialized;

    public TaskbarMonitor(
        ISystemMetricsService metricsService,
        IIconGeneratorService iconGeneratorService,
        ILogger<TaskbarMonitor> logger,
        MonitoringOptions options)
    {
        _metricsService = metricsService;
        _iconGeneratorService = iconGeneratorService;
        _logger = logger;
        _options = options;

        // Capture UI synchronization context
        _uiContext = SynchronizationContext.Current;

        // Timer for safe icon disposal - more frequent to prevent buildup
        _disposalTimer = new System.Threading.Timer(
            DisposeQueuedIcons,
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2));
    }

    public void Initialize()
    {
        if (_isInitialized) return;

        try
        {
            LogInformation("Initializing taskbar monitor");

            CreateNotifyIcons();
            CreateContextMenu();
            SubscribeToEvents();

            _metricsService.StartMonitoring();
            _isInitialized = true;

            LogInformation("Taskbar monitor initialized successfully");
        }
        catch (Exception ex)
        {
            LogError(ex, "Failed to initialize taskbar monitor");
            throw;
        }
    }

    private void CreateNotifyIcons()
    {
        using var emptyHistory = CreateEmptyHistory();

        _cpuNotifyIcon = new NotifyIcon
        {
            Icon = _iconGeneratorService.GenerateCpuIcon(emptyHistory),
            Visible = true,
            Text = "CPU: Initializing..."
        };

        _ramNotifyIcon = new NotifyIcon
        {
            Icon = _iconGeneratorService.GenerateRamIcon(emptyHistory),
            Visible = true,
            Text = "RAM: Initializing..."
        };

        _networkNotifyIcon = new NotifyIcon
        {
            Icon = _iconGeneratorService.GenerateNetworkIcon(emptyHistory, _options.NetworkThresholdMbps),
            Visible = true,
            Text = "Network: Initializing..."
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MetricsHistory CreateEmptyHistory()
    {
        var history = new MetricsHistory(_options.HistorySize);
        // Add some initial zero values for empty graph display
        for (int i = 0; i < Math.Min(5, _options.HistorySize); i++)
            history.Add(0);
        return history;
    }

    private void CreateContextMenu()
    {
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(CreateMenuItem("Show Details", OnShowDetails));
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(CreateMenuItem("Refresh Now", OnRefreshNow));
        _contextMenu.Items.Add(CreateMenuItem("Clear Cache", OnClearCache));
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(CreateMenuItem("Exit All", OnExitAll));

        // Assign context menus to all icons
        AssignContextMenus();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AssignContextMenus()
    {
        if (_cpuNotifyIcon != null)
            _cpuNotifyIcon.ContextMenuStrip = _contextMenu;
        if (_ramNotifyIcon != null)
            _ramNotifyIcon.ContextMenuStrip = _contextMenu;
        if (_networkNotifyIcon != null)
            _networkNotifyIcon.ContextMenuStrip = _contextMenu;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ToolStripMenuItem CreateMenuItem(string text, EventHandler handler)
    {
        var menuItem = new ToolStripMenuItem(text);
        menuItem.Click += handler;
        return menuItem;
    }

    private void SubscribeToEvents()
    {
        _metricsService.MetricsUpdated += OnMetricsUpdated;

        // Subscribe to double-click events
        if (_cpuNotifyIcon != null)
            _cpuNotifyIcon.DoubleClick += OnShowDetails;
        if (_ramNotifyIcon != null)
            _ramNotifyIcon.DoubleClick += OnShowDetails;
        if (_networkNotifyIcon != null)
            _networkNotifyIcon.DoubleClick += OnShowDetails;
    }

    /// <summary>
    /// Thread-safe metrics update with rate limiting to prevent UI flooding
    /// </summary>
    private void OnMetricsUpdated(object? sender, SystemMetrics metrics)
    {
        if (_disposed || !_isInitialized) return;

        // Rate limiting to prevent UI flooding
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;
            if (now - _lastUpdateTime < _minUpdateInterval)
            {
                return; // Skip this update
            }
            _lastUpdateTime = now;
        }

        // Marshal to UI thread safely
        if (_uiContext != null && _uiContext != SynchronizationContext.Current)
        {
            try
            {
                _uiContext.Post(_ => UpdateIconsSafe(metrics), null);
            }
            catch (Exception ex)
            {
                LogError(ex, "Error marshaling to UI thread");
            }
        }
        else
        {
            UpdateIconsSafe(metrics);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateIconsSafe(SystemMetrics metrics)
    {
        if (_disposed || !_isInitialized) return;

        try
        {
            // Use single lock for all icon updates to prevent race conditions
            lock (_updateLock)
            {
                if (_disposed || !_isInitialized) return;

                var cpuHistory = _metricsService.GetCpuHistory();
                var ramHistory = _metricsService.GetRamHistory();
                var networkHistory = _metricsService.GetNetworkHistory();

                // Update icons with proper error handling for each
                UpdateNotifyIconSafe(
                    _cpuNotifyIcon,
                    () => _iconGeneratorService.GenerateCpuIcon(cpuHistory),
                    $"CPU: {metrics.CpuUsagePercent}%\nClick for details",
                    "CPU");

                UpdateNotifyIconSafe(
                    _ramNotifyIcon,
                    () => _iconGeneratorService.GenerateRamIcon(ramHistory),
                    $"RAM: {metrics.RamUsagePercent}%\nClick for details",
                    "RAM");

                UpdateNotifyIconSafe(
                    _networkNotifyIcon,
                    () => _iconGeneratorService.GenerateNetworkIcon(networkHistory, _options.NetworkThresholdMbps),
                    $"Network: {metrics.NetworkSpeedMbps} MB/s\nAvg: {metrics.AverageNetworkSpeedMbps} MB/s\nClick for details",
                    "Network");
            }
        }
        catch (Exception ex)
        {
            LogError(ex, "Error updating taskbar icons");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateNotifyIconSafe(NotifyIcon? notifyIcon, Func<Icon> iconFactory, string text, string iconType)
    {
        if (notifyIcon == null || _disposed) return;

        try
        {
            // Store reference to old icon for safe disposal
            var oldIcon = notifyIcon.Icon;

            // Generate new icon
            var newIcon = iconFactory();

            // Atomic updates to prevent UI thread blocking
            notifyIcon.Icon = newIcon;
            notifyIcon.Text = TruncateTooltipText(text);

            // Queue old icon for disposal if it exists
            if (oldIcon != null)
            {
                _iconDisposalQueue.Enqueue(oldIcon);
            }

            LogDebug($"Updated {iconType} icon successfully");
        }
        catch (Exception ex)
        {
            LogError(ex, $"Error updating {iconType} notify icon");

            // Try to recover by setting a simple fallback
            try
            {
                if (notifyIcon.Icon == null)
                {
                    // Create a simple fallback icon to prevent null reference
                    notifyIcon.Text = $"{iconType}: Error";
                }
            }
            catch (Exception fallbackEx)
            {
                LogError(fallbackEx, $"Failed to set fallback for {iconType} icon");
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string TruncateTooltipText(string text)
    {
        // Windows tooltip limit is 127 characters
        return text.Length > 127 ? text[..124] + "..." : text;
    }

    /// <summary>
    /// Aggressively dispose queued icons to prevent GDI resource leaks
    /// </summary>
    private void DisposeQueuedIcons(object? state)
    {
        if (_disposed) return;

        var disposedCount = 0;
        const int maxDisposalsPerCycle = 20; // Increased from 10

        try
        {
            while (_iconDisposalQueue.TryDequeue(out var icon) && disposedCount < maxDisposalsPerCycle)
            {
                try
                {
                    icon?.Dispose();
                    disposedCount++;
                }
                catch (Exception ex)
                {
                    LogError(ex, "Error disposing queued icon");
                }
            }

            if (disposedCount > 0)
            {
                LogDebug($"Disposed {disposedCount} queued icons, remaining: {_iconDisposalQueue.Count}");
            }

            // Force garbage collection if queue is getting large
            if (_iconDisposalQueue.Count > 50)
            {
                LogInformation($"Icon disposal queue is large ({_iconDisposalQueue.Count}), forcing GC");
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }
        catch (Exception ex)
        {
            LogError(ex, "Error in icon disposal timer");
        }
    }

    #region Event Handlers

    private void OnShowDetails(object? sender, EventArgs e)
    {
        try
        {
            var currentMetrics = _metricsService.GetCurrentMetrics();
            var detailsText = $"""
                System Resource Monitor
                ====================================
                
                CPU Usage: {currentMetrics.CpuUsagePercent}%
                RAM Usage: {currentMetrics.RamUsagePercent}%
                Network Speed: {currentMetrics.NetworkSpeedMbps} MB/s
                Average Network: {currentMetrics.AverageNetworkSpeedMbps} MB/s
                
                Last Updated: {currentMetrics.Timestamp:HH:mm:ss}
                Update Interval: {_options.UpdateIntervalMs}ms
                History Size: {_options.HistorySize} samples
                
                Pending Icon Disposals: {_iconDisposalQueue.Count}
                """;

            MessageBox.Show(detailsText, "System Monitor Details",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            LogError(ex, "Error showing details dialog");
            MessageBox.Show("Error retrieving system details.", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnRefreshNow(object? sender, EventArgs e)
    {
        try
        {
            // Force immediate update by bypassing rate limiting
            lock (_rateLimitLock)
            {
                _lastUpdateTime = DateTime.MinValue;
            }

            var currentMetrics = _metricsService.GetCurrentMetrics();
            UpdateIconsSafe(currentMetrics);

            LogInformation("Manual refresh completed");
        }
        catch (Exception ex)
        {
            LogError(ex, "Error during manual refresh");
        }
    }

    private void OnClearCache(object? sender, EventArgs e)
    {
        try
        {
            // Clear icon cache and force disposal of queued icons
            DisposeQueuedIcons(null);

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            MessageBox.Show($"Cache cleared. Disposed {_iconDisposalQueue.Count} pending icons.",
                "Cache Cleared", MessageBoxButtons.OK, MessageBoxIcon.Information);

            LogInformation("Cache cleared manually");
        }
        catch (Exception ex)
        {
            LogError(ex, "Error clearing cache");
        }
    }

    private void OnExitAll(object? sender, EventArgs e)
    {
        try
        {
            LogInformation("Exit requested by user");
            Application.Exit();
        }
        catch (Exception ex)
        {
            LogError(ex, "Error during application exit");
            Environment.Exit(1);
        }
    }

    #endregion

    #region Optimized Logging

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LogInformation(string message)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(message);
    }

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
            LogInformation("Disposing taskbar monitor");

            _disposed = true;
            _isInitialized = false;

            // Stop monitoring first
            _metricsService?.StopMonitoring();

            // Dispose timer
            _disposalTimer?.Dispose();

            // Unsubscribe from events
            if (_metricsService != null)
                _metricsService.MetricsUpdated -= OnMetricsUpdated;

            // Dispose UI components
            DisposeNotifyIcons();
            _contextMenu?.Dispose();

            // Aggressively dispose all remaining queued icons
            DisposeAllQueuedIcons();

            LogInformation("Taskbar monitor disposed successfully");
        }
        catch (Exception ex)
        {
            LogError(ex, "Error during TaskbarMonitor disposal");
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DisposeNotifyIcons()
    {
        try
        {
            // Hide icons first to prevent UI issues
            if (_cpuNotifyIcon != null)
            {
                _cpuNotifyIcon.Visible = false;
                _cpuNotifyIcon.Dispose();
            }
            if (_ramNotifyIcon != null)
            {
                _ramNotifyIcon.Visible = false;
                _ramNotifyIcon.Dispose();
            }
            if (_networkNotifyIcon != null)
            {
                _networkNotifyIcon.Visible = false;
                _networkNotifyIcon.Dispose();
            }
        }
        catch (Exception ex)
        {
            LogError(ex, "Error disposing notify icons");
        }
    }

    private void DisposeAllQueuedIcons()
    {
        var disposedCount = 0;
        try
        {
            // Dispose all queued icons
            while (_iconDisposalQueue.TryDequeue(out var icon))
            {
                try
                {
                    icon?.Dispose();
                    disposedCount++;
                }
                catch (Exception ex)
                {
                    LogError(ex, "Error disposing final queued icon");
                }
            }

            if (disposedCount > 0)
            {
                LogInformation($"Disposed {disposedCount} remaining icons during cleanup");
            }

            // Force cleanup
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        catch (Exception ex)
        {
            LogError(ex, "Error disposing all queued icons");
        }
    }

    #endregion
}