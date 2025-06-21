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

    // Icon disposal queue to prevent memory leaks
    private readonly ConcurrentQueue<Icon> _iconDisposalQueue = new();
    private readonly System.Threading.Timer _disposalTimer;

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

        // Timer for safe icon disposal (every 5 seconds)
        _disposalTimer = new System.Threading.Timer(
            DisposeQueuedIcons,
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));
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
        var emptyHistory = CreateEmptyHistory();

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

        emptyHistory.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MetricsHistory CreateEmptyHistory()
    {
        var history = new MetricsHistory(_options.HistorySize);
        // Add some initial zero values for empty graph display
        for (int i = 0; i < 5; i++)
            history.Add(0);
        return history;
    }

    private void CreateContextMenu()
    {
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(CreateMenuItem("Show Details", OnShowDetails));
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(CreateMenuItem("Exit All", OnExitAll));

        // Assign context menus
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

        if (_cpuNotifyIcon != null)
            _cpuNotifyIcon.DoubleClick += OnShowDetails;
        if (_ramNotifyIcon != null)
            _ramNotifyIcon.DoubleClick += OnShowDetails;
        if (_networkNotifyIcon != null)
            _networkNotifyIcon.DoubleClick += OnShowDetails;
    }

    /// <summary>
    /// Thread-safe metrics update with proper icon lifecycle management
    /// </summary>
    private void OnMetricsUpdated(object? sender, SystemMetrics metrics)
    {
        if (_disposed || !_isInitialized) return;

        // Marshal to UI thread if needed
        if (_uiContext != null && _uiContext != SynchronizationContext.Current)
        {
            _uiContext.Post(_ => UpdateIconsSafe(metrics), null);
        }
        else
        {
            UpdateIconsSafe(metrics);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateIconsSafe(SystemMetrics metrics)
    {
        try
        {
            lock (_updateLock)
            {
                if (_disposed) return;

                var cpuHistory = _metricsService.GetCpuHistory();
                var ramHistory = _metricsService.GetRamHistory();
                var networkHistory = _metricsService.GetNetworkHistory();

                // Update CPU icon with safe disposal
                UpdateNotifyIcon(
                    _cpuNotifyIcon,
                    () => _iconGeneratorService.GenerateCpuIcon(cpuHistory),
                    $"CPU: {metrics.CpuUsagePercent}%");

                // Update RAM icon with safe disposal
                UpdateNotifyIcon(
                    _ramNotifyIcon,
                    () => _iconGeneratorService.GenerateRamIcon(ramHistory),
                    $"RAM: {metrics.RamUsagePercent}%");

                // Update Network icon with safe disposal
                UpdateNotifyIcon(
                    _networkNotifyIcon,
                    () => _iconGeneratorService.GenerateNetworkIcon(networkHistory, _options.NetworkThresholdMbps),
                    $"Network: {metrics.NetworkSpeedMbps} MB/s");
            }
        }
        catch (Exception ex)
        {
            LogError(ex, "Error updating taskbar icons");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateNotifyIcon(NotifyIcon? notifyIcon, Func<Icon> iconFactory, string text)
    {
        if (notifyIcon == null) return;

        var oldIcon = notifyIcon.Icon;
        var newIcon = iconFactory();

        // Atomic update
        notifyIcon.Icon = newIcon;
        notifyIcon.Text = text;

        // Queue old icon for safe disposal
        if (oldIcon !=