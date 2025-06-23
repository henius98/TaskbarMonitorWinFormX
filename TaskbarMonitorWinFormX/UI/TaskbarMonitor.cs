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

    // UI synchronization context for thread marshalling
    private readonly SynchronizationContext? _uiContext;
    private readonly object _uiUpdateLock = new();

    // Background processing for icon generation
    private readonly TaskScheduler _backgroundScheduler;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    // Icon disposal management
    private readonly ConcurrentQueue<Icon> _iconDisposalQueue = new();
    private readonly System.Threading.Timer _disposalTimer;

    // High-performance rate limiting using atomic operations
    private long _lastUpdateTicks = 0;
    private readonly long _minUpdateIntervalTicks;

    // Pre-generated fallback icons to prevent null references
    private readonly Lazy<Icon> _fallbackCpuIcon;
    private readonly Lazy<Icon> _fallbackRamIcon;
    private readonly Lazy<Icon> _fallbackNetworkIcon;

    private volatile bool _disposed;
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

        // Capture UI synchronization context for thread marshalling
        _uiContext = SynchronizationContext.Current;

        // Use dedicated thread pool for background icon generation
        _backgroundScheduler = TaskScheduler.Default;

        // Convert rate limit to ticks for lockless comparison
        _minUpdateIntervalTicks = TimeSpan.FromMilliseconds(100).Ticks;

        // Timer for safe icon disposal
        _disposalTimer = new System.Threading.Timer(
            DisposeQueuedIcons,
            null,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(3));

        // Initialize fallback icons lazily
        _fallbackCpuIcon = new Lazy<Icon>(() => CreateFallbackIcon(Color.Red));
        _fallbackRamIcon = new Lazy<Icon>(() => CreateFallbackIcon(Color.Green));
        _fallbackNetworkIcon = new Lazy<Icon>(() => CreateFallbackIcon(Color.Blue));
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
        // Use fallback icons initially to prevent blocking
        _cpuNotifyIcon = new NotifyIcon
        {
            Icon = _fallbackCpuIcon.Value,
            Visible = true,
            Text = "CPU: Initializing..."
        };

        _ramNotifyIcon = new NotifyIcon
        {
            Icon = _fallbackRamIcon.Value,
            Visible = true,
            Text = "RAM: Initializing..."
        };

        _networkNotifyIcon = new NotifyIcon
        {
            Icon = _fallbackNetworkIcon.Value,
            Visible = true,
            Text = "Network: Initializing..."
        };
    }

    private Icon CreateFallbackIcon(Color color)
    {
        try
        {
            using var bitmap = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bitmap);
            using var brush = new SolidBrush(color);

            g.Clear(Color.Transparent);
            g.FillRectangle(brush, 2, 2, 12, 12);

            return Icon.FromHandle(bitmap.GetHicon());
        }
        catch
        {
            // Return system icon as ultimate fallback
            return SystemIcons.Application;
        }
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

        AssignContextMenus();
    }

    private void AssignContextMenus()
    {
        if (_cpuNotifyIcon != null)
            _cpuNotifyIcon.ContextMenuStrip = _contextMenu;
        if (_ramNotifyIcon != null)
            _ramNotifyIcon.ContextMenuStrip = _contextMenu;
        if (_networkNotifyIcon != null)
            _networkNotifyIcon.ContextMenuStrip = _contextMenu;
    }

    private static ToolStripMenuItem CreateMenuItem(string text, EventHandler handler)
    {
        var menuItem = new ToolStripMenuItem(text);
        menuItem.Click += handler;
        return menuItem;
    }

    private void SubscribeToEvents()
    {
        _metricsService.MetricsUpdated += OnMetricsUpdated;

        if (_cpuNotifyIcon != null)
            _cpuNotifyIcon.DoubleClick += OnShowDetails;
        if (_ramNotifyIcon != null)
            _ramNotifyIcon.DoubleClick += OnShowDetails;
        if (_networkNotifyIcon != null)
            _networkNotifyIcon.DoubleClick += OnShowDetails;
    }

    /// <summary>
    /// Non-blocking metrics update with background icon generation
    /// Key fix: Moved icon generation OFF the UI thread to prevent blocking
    /// </summary>
    private void OnMetricsUpdated(object? sender, SystemMetrics metrics)
    {
        if (_disposed || !_isInitialized) return;

        // High-performance lockless rate limiting using atomic operations
        var currentTicks = DateTime.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastUpdateTicks);

        if (currentTicks - lastTicks < _minUpdateIntervalTicks)
            return;

        // Atomic update of last update time
        if (Interlocked.CompareExchange(ref _lastUpdateTicks, currentTicks, lastTicks) != lastTicks)
            return; // Another thread already updated

        // Generate icons on background thread to avoid blocking UI
        Task.Factory.StartNew(
            () => UpdateIconsAsync(metrics),
            _cancellationTokenSource.Token,
            TaskCreationOptions.DenyChildAttach,
            _backgroundScheduler);
    }

    /// <summary>
    /// Background icon generation with non-blocking UI updates
    /// </summary>
    private async Task UpdateIconsAsync(SystemMetrics metrics)
    {
        if (_disposed || !_isInitialized) return;

        try
        {
            var cpuHistory = _metricsService.GetCpuHistory();
            var ramHistory = _metricsService.GetRamHistory();
            var networkHistory = _metricsService.GetNetworkHistory();

            // Generate all icons in parallel on background threads
            var cpuIconTask = Task.Run(() =>
                _iconGeneratorService.GenerateCpuIcon(cpuHistory), _cancellationTokenSource.Token);
            var ramIconTask = Task.Run(() =>
                _iconGeneratorService.GenerateRamIcon(ramHistory), _cancellationTokenSource.Token);
            var networkIconTask = Task.Run(() =>
                _iconGeneratorService.GenerateNetworkIcon(networkHistory, _options.NetworkThresholdMbps), _cancellationTokenSource.Token);

            // Wait for all icons to be generated
            await Task.WhenAll(cpuIconTask, ramIconTask, networkIconTask);

            // Marshal back to UI thread for final update
            MarshalToUIThread(() => UpdateUIFast(
                cpuIconTask.Result,
                ramIconTask.Result,
                networkIconTask.Result,
                metrics));
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }
        catch (Exception ex)
        {
            LogError(ex, "Error in background icon update");
        }
    }

    /// <summary>
    /// Efficiently marshal operations to UI thread
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MarshalToUIThread(Action action)
    {
        if (_uiContext != null)
        {
            _uiContext.Post(_ => action(), null);
        }
        else
        {
            // Fallback: Use Control.Invoke if we have a NotifyIcon
            if (_cpuNotifyIcon?.Container is ContainerControl container)
            {
                container.BeginInvoke(action);
            }
            else
            {
                // Last resort: execute directly (might cause cross-threading issues)
                action();
            }
        }
    }

    /// <summary>
    /// Fast UI update - no blocking operations, just atomic assignments
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateUIFast(Icon cpuIcon, Icon ramIcon, Icon networkIcon, SystemMetrics metrics)
    {
        if (_disposed || !_isInitialized) return;

        // Single lock for all UI updates to prevent race conditions
        lock (_uiUpdateLock)
        {
            if (_disposed || !_isInitialized) return;

            try
            {
                UpdateSingleIcon(_cpuNotifyIcon, cpuIcon,
                    $"CPU: {metrics.CpuUsagePercent}%");
                UpdateSingleIcon(_ramNotifyIcon, ramIcon,
                    $"RAM: {metrics.RamUsagePercent}%");
                UpdateSingleIcon(_networkNotifyIcon, networkIcon,
                    $"Net: {metrics.NetworkSpeedMbps} MB/s");
            }
            catch (Exception ex)
            {
                LogError(ex, "Error in fast UI update");
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateSingleIcon(NotifyIcon? notifyIcon, Icon newIcon, string text)
    {
        if (notifyIcon == null || _disposed) return;

        try
        {
            var oldIcon = notifyIcon.Icon;

            // Atomic updates
            notifyIcon.Icon = newIcon;
            notifyIcon.Text = TruncateTooltipText(text);

            // Queue old icon for disposal
            if (oldIcon != null && !IsSystemIcon(oldIcon))
            {
                _iconDisposalQueue.Enqueue(oldIcon);
            }
        }
        catch (Exception ex)
        {
            LogError(ex, "Error updating single icon");

            // Use fallback icon on error
            try
            {
                notifyIcon.Text = "Error";
            }
            catch { }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSystemIcon(Icon icon)
    {
        // Don't dispose system icons
        return icon == SystemIcons.Application ||
               icon == SystemIcons.Error ||
               icon == SystemIcons.Information;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string TruncateTooltipText(string text)
    {
        return text.Length > 127 ? text[..124] + "..." : text;
    }

    /// <summary>
    /// Aggressive icon disposal to prevent GDI leaks
    /// </summary>
    private void DisposeQueuedIcons(object? state)
    {
        if (_disposed) return;

        var disposedCount = 0;
        const int maxDisposalsPerCycle = 30;

        try
        {
            while (_iconDisposalQueue.TryDequeue(out var icon) && disposedCount < maxDisposalsPerCycle)
            {
                try
                {
                    if (!IsSystemIcon(icon))
                    {
                        icon?.Dispose();
                        disposedCount++;
                    }
                }
                catch (Exception ex)
                {
                    LogError(ex, "Error disposing queued icon");
                }
            }

            // Force GC if queue is getting large
            if (_iconDisposalQueue.Count > 100)
            {
                LogInformation($"Large icon queue ({_iconDisposalQueue.Count}), forcing GC");
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
        }
    }

    private void OnRefreshNow(object? sender, EventArgs e)
    {
        try
        {
            Interlocked.Exchange(ref _lastUpdateTicks, 0);
            var currentMetrics = _metricsService.GetCurrentMetrics();

            // Force immediate update
            Task.Factory.StartNew(
                () => UpdateIconsAsync(currentMetrics),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                _backgroundScheduler);

            LogInformation("Manual refresh initiated");
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
            DisposeQueuedIcons(null);
            GC.Collect();

            MessageBox.Show("Cache cleared successfully.", "Cache Cleared",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    #region Logging

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LogInformation(string message)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(message);
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
            _disposed = true;
            _isInitialized = false;

            _cancellationTokenSource.Cancel();
            _metricsService?.StopMonitoring();

            _disposalTimer?.Dispose();

            if (_metricsService != null)
                _metricsService.MetricsUpdated -= OnMetricsUpdated;

            DisposeNotifyIcons();
            _contextMenu?.Dispose();
            DisposeAllQueuedIcons();

            _cancellationTokenSource.Dispose();
        }
        catch (Exception ex)
        {
            LogError(ex, "Error during disposal");
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    private void DisposeNotifyIcons()
    {
        try
        {
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
        try
        {
            while (_iconDisposalQueue.TryDequeue(out var icon))
            {
                try
                {
                    if (!IsSystemIcon(icon))
                        icon?.Dispose();
                }
                catch { }
            }
        }
        catch { }
    }

    #endregion
}