using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskbarMonitorWinFormX.Models;
using TaskbarMonitorWinFormX.Services;
using TaskbarMonitorWinFormX.UI;

namespace TaskbarMonitorWinFormX;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        var host = CreateHostBuilder(args).Build();

        try
        {
            var taskbarMonitor = host.Services.GetRequiredService<TaskbarMonitor>();
            taskbarMonitor.Initialize();

            Application.Run();
        }
        catch (Exception ex)
        {
            var loggerFactory = host.Services.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger("Program");
            logger?.LogCritical(ex, "Application failed to start");
            MessageBox.Show($"Application failed to start: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Core services
                services.AddSingleton<ISystemMetricsService, SystemMetricsService>();
                services.AddSingleton<IIconGeneratorService, IconGeneratorService>();
                services.AddSingleton<IPerformanceCounterService, PerformanceCounterService>();

                // UI services
                services.AddSingleton<TaskbarMonitor>();

                // Configuration
                services.AddSingleton<MonitoringOptions>(new MonitoringOptions
                {
                    UpdateIntervalMs = 1000,
                    HistorySize = 60,
                    NetworkThresholdMbps = 10
                });
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
#if DEBUG
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Debug);
#else
                logging.SetMinimumLevel(LogLevel.Warning);
#endif
            });
}