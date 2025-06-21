using Microsoft.Extensions.Logging;
using TaskbarMonitorWinFormX.Models;
using TaskbarMonitorWinFormX.Services;

namespace TaskbarMonitorWinFormX.UI;

public sealed class TaskbarMonitor : IDisposable
{
    private readonly ISystemMetricsService _metricsService;
    private readonly IIconGeneratorService _iconGeneratorService;
    private readonly ILogger<TaskbarMonitor> _logger;

    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;
    private bool _disposed;

    public TaskbarMonitor(
        ISystemMetricsService metricsService,
        IIconGeneratorService iconGeneratorService,
        ILogger<TaskbarMonitor> logger)
    {
        _metricsService = metricsService;
        _iconGeneratorService = iconGeneratorService;
        _logger = logger;
    }

    public void Initialize()
    {
        try
        {
            _logger.LogInformation("Initializing taskbar monitor");

            CreateNotifyIcon();
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

    private void CreateNotifyIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = _iconGeneratorService.GenerateIcon(SystemMetrics.Empty),
            Visible = true,
            Text = "System Resource Monitor - Initializing..."
        };
    }

    private void CreateContextMenu()
    {
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(CreateMenuItem("Show Details", OnShowDetails));
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(CreateMenuItem("Exit", OnExit));

        if (_notifyIcon != null)
        {
            _notifyIcon.ContextMenuStrip = _contextMenu;
        }
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

        if (_notifyIcon != null)
        {
            _notifyIcon.DoubleClick += OnShowDetails;
        }
    }

    private void OnMetricsUpdated(object? sender, SystemMetrics metrics)
    {
        try
        {
            if (_notifyIcon == null || _disposed) return;

            // Update icon
            var oldIcon = _notifyIcon.Icon;
            _notifyIcon.Icon = _iconGeneratorService.GenerateIcon(metrics);
            oldIcon?.Dispose();

            // Update tooltip
            _notifyIcon.Text = metrics.TooltipText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating taskbar icon");
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

    private static void OnExit(object? sender, EventArgs e)
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

            _notifyIcon?.Icon?.Dispose();
            _notifyIcon?.Dispose();
            _contextMenu?.Dispose();

            _disposed = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing taskbar monitor");
        }
    }
}