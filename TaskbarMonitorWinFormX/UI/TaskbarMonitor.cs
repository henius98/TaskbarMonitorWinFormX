using Microsoft.Extensions.Logging;
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
    private bool _disposed;

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
    }

    public void Initialize()
    {
        try
        {
            _logger.LogInformation("Initializing taskbar monitor");

            CreateNotifyIcons();
            CreateContextMenu();
            SubscribeToEvents();

            _metricsService.StartMonitoring();

            _logger.LogInformation("Taskbar monitor initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize taskbar monitor");
            throw;
        }
    }

    private void CreateNotifyIcons()
    {
        var emptyCpuHistory = new MetricsHistory(_options.HistorySize);
        var emptyRamHistory = new MetricsHistory(_options.HistorySize);
        var emptyNetworkHistory = new MetricsHistory(_options.HistorySize);

        // Add initial zero values to show empty graphs
        for (int i = 0; i < 5; i++)
        {
            emptyCpuHistory.Add(0);
            emptyRamHistory.Add(0);
            emptyNetworkHistory.Add(0);
        }

        _cpuNotifyIcon = new NotifyIcon
        {
            Icon = _iconGeneratorService.GenerateCpuIcon(emptyCpuHistory),
            Visible = true,
            Text = "CPU: Initializing..."
        };

        _ramNotifyIcon = new NotifyIcon
        {
            Icon = _iconGeneratorService.GenerateRamIcon(emptyRamHistory),
            Visible = true,
            Text = "RAM: Initializing..."
        };

        _networkNotifyIcon = new NotifyIcon
        {
            Icon = _iconGeneratorService.GenerateNetworkIcon(emptyNetworkHistory, _options.NetworkThresholdMbps),
            Visible = true,
            Text = "Network: Initializing..."
        };
    }

    private void CreateContextMenu()
    {
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(CreateMenuItem("Show Details", OnShowDetails));
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(CreateMenuItem("Exit All", OnExitAll));

        // Assign the same context menu to all icons
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

    private void OnMetricsUpdated(object? sender, SystemMetrics metrics)
    {
        try
        {
            if (_disposed) return;

            var cpuHistory = _metricsService.GetCpuHistory();
            var ramHistory = _metricsService.GetRamHistory();
            var networkHistory = _metricsService.GetNetworkHistory();

            // Update CPU icon
            if (_cpuNotifyIcon != null)
            {
                var oldIcon = _cpuNotifyIcon.Icon;
                _cpuNotifyIcon.Icon = _iconGeneratorService.GenerateCpuIcon(cpuHistory);
                _cpuNotifyIcon.Text = $"CPU: {metrics.CpuUsagePercent:F1}%";
                oldIcon?.Dispose();
            }

            // Update RAM icon
            if (_ramNotifyIcon != null)
            {
                var oldIcon = _ramNotifyIcon.Icon;
                _ramNotifyIcon.Icon = _iconGeneratorService.GenerateRamIcon(ramHistory);
                _ramNotifyIcon.Text = $"RAM: {metrics.RamUsagePercent:F1}%";
                oldIcon?.Dispose();
            }

            // Update Network icon
            if (_networkNotifyIcon != null)
            {
                var oldIcon = _networkNotifyIcon.Icon;
                _networkNotifyIcon.Icon = _iconGeneratorService.GenerateNetworkIcon(networkHistory, _options.NetworkThresholdMbps);
                _networkNotifyIcon.Text = $"Network: {metrics.NetworkSpeedMbps:F1} MB/s";
                oldIcon?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating taskbar icons");
        }
    }

    private void OnShowDetails(object? sender, EventArgs e)
    {
        var metrics = _metricsService.GetCurrentMetrics();
        var message = $"""
            System Resource Monitor
            
            CPU Usage: {metrics.CpuUsagePercent:F1}%
            RAM Usage: {metrics.RamUsagePercent:F1}%
            Network Speed: {metrics.NetworkSpeedMbps:F2} MB/s
            Avg Network Speed: {metrics.AverageNetworkSpeedMbps:F2} MB/s
            
            Last Updated: {metrics.Timestamp:HH:mm:ss}
            """;

        MessageBox.Show(message, "System Resources", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void OnExitAll(object? sender, EventArgs e)
    {
        Application.Exit();
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _logger.LogInformation("Disposing taskbar monitor");

            _metricsService.MetricsUpdated -= OnMetricsUpdated;
            _metricsService.StopMonitoring();

            // Dispose CPU icon
            _cpuNotifyIcon?.Icon?.Dispose();
            _cpuNotifyIcon?.Dispose();

            // Dispose RAM icon
            _ramNotifyIcon?.Icon?.Dispose();
            _ramNotifyIcon?.Dispose();

            // Dispose Network icon
            _networkNotifyIcon?.Icon?.Dispose();
            _networkNotifyIcon?.Dispose();

            _contextMenu?.Dispose();

            _disposed = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing taskbar monitor");
        }
    }
}