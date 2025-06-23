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

    private readonly System.Threading.Timer _updateTimer;
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

        _updateTimer = new System.Threading.Timer(
            UpdateIcons,
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(_options.UpdateIntervalMs));
    }

    public void Initialize()
    {
        try
        {
            CreateNotifyIcons();
            CreateContextMenu();
            _metricsService.StartMonitoring();
            _logger.LogInformation("Taskbar monitor initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize taskbar monitor");
            throw;
        }
    }

    private void CreateNotifyIcons()
    {
        _cpuNotifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "CPU: Initializing..."
        };

        _ramNotifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "RAM: Initializing..."
        };

        _networkNotifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Network: Initializing..."
        };
    }

    private void CreateContextMenu()
    {
        _contextMenu = new ContextMenuStrip();

        var detailsItem = new ToolStripMenuItem("Show Details");
        detailsItem.Click += ShowDetails;
        _contextMenu.Items.Add(detailsItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => Application.Exit();
        _contextMenu.Items.Add(exitItem);

        _cpuNotifyIcon.ContextMenuStrip = _contextMenu;
        _ramNotifyIcon.ContextMenuStrip = _contextMenu;
        _networkNotifyIcon.ContextMenuStrip = _contextMenu;
    }

    private void UpdateIcons(object? state)
    {
        if (_disposed || !_metricsService.IsMonitoring) return;

        try
        {
            var metrics = _metricsService.GetCurrentMetrics();

            // Update on UI thread
            Application.OpenForms[0]?.BeginInvoke(new Action(() =>
            {
                try
                {
                    var cpuIcon = _iconGeneratorService.GenerateCpuIcon(_metricsService.GetCpuHistory());
                    var ramIcon = _iconGeneratorService.GenerateRamIcon(_metricsService.GetRamHistory());
                    var networkIcon = _iconGeneratorService.GenerateNetworkIcon(_metricsService.GetNetworkHistory(), _options.NetworkThresholdMbps);

                    UpdateNotifyIcon(_cpuNotifyIcon, cpuIcon, $"CPU: {metrics.CpuUsagePercent}%");
                    UpdateNotifyIcon(_ramNotifyIcon, ramIcon, $"RAM: {metrics.RamUsagePercent}%");
                    UpdateNotifyIcon(_networkNotifyIcon, networkIcon, $"Network: {metrics.NetworkSpeedMbps} MB/s");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating icons");
                }
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in update timer");
        }
    }

    private void UpdateNotifyIcon(NotifyIcon? notifyIcon, Icon newIcon, string text)
    {
        if (notifyIcon == null || _disposed) return;

        var oldIcon = notifyIcon.Icon;
        notifyIcon.Icon = newIcon;
        notifyIcon.Text = text.Length > 127 ? text.Substring(0, 124) + "..." : text;

        // Dispose old icon if it's not a system icon
        if (oldIcon != null && oldIcon != SystemIcons.Application)
        {
            oldIcon.Dispose();
        }
    }

    private void ShowDetails(object? sender, EventArgs e)
    {
        try
        {
            var metrics = _metricsService.GetCurrentMetrics();
            var message = $"CPU: {metrics.CpuUsagePercent}%\n" +
                         $"RAM: {metrics.RamUsagePercent}%\n" +
                         $"Network: {metrics.NetworkSpeedMbps} MB/s\n" +
                         $"Last Updated: {metrics.Timestamp:HH:mm:ss}";

            MessageBox.Show(message, "System Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing details");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _updateTimer?.Dispose();
        _metricsService?.StopMonitoring();

        _cpuNotifyIcon?.Dispose();
        _ramNotifyIcon?.Dispose();
        _networkNotifyIcon?.Dispose();
        _contextMenu?.Dispose();

        GC.SuppressFinalize(this);
    }
}