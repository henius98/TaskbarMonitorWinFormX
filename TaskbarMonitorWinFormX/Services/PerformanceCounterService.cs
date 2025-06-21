using System.Diagnostics;
using System.Net.NetworkInformation;
using TaskbarMonitorWinFormX.Models;

namespace TaskbarMonitorWinFormX.Services;

public interface IPerformanceCounterService : IDisposable
{
    int GetCpuUsage();
    int GetRamUsagePercent();
    int GetNetworkSpeedMBps(); // Returns MB per second
    NetworkStats GetDetailedNetworkStats(); // New method for detailed stats
    bool IsInitialized { get; }
}

public sealed class PerformanceCounterService : IPerformanceCounterService
{
    private readonly PerformanceCounter? _cpuCounter;
    private readonly PerformanceCounter? _ramCounter;
    private PerformanceCounter? _networkSentCounter;
    private PerformanceCounter? _networkReceivedCounter;
    private readonly int _totalRamMb;
    private bool _disposed;
    private string _selectedNetworkInterface = "";

    // Keep track of previous values for rate calculation
    private bool _isFirstMeasurement = true;

    public bool IsInitialized { get; private set; }

    public PerformanceCounterService()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
            _totalRamMb = (int)(new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / (1024 * 1024));

            InitializeNetworkCounters();
            IsInitialized = true;
        }
        catch
        {
            IsInitialized = false;
            Dispose();
        }
    }

    private void InitializeNetworkCounters()
    {
        try
        {
            // Get active network interfaces with better selection logic
            var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                           ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                           ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                           ni.GetIPv4Statistics().BytesReceived > 0) // Has actual traffic
                .OrderByDescending(ni => ni.Speed) // Prefer higher speed interfaces
                .ThenByDescending(ni => ni.GetIPv4Statistics().BytesReceived + ni.GetIPv4Statistics().BytesSent)
                .ToList();

            var perfCounterInstances = new PerformanceCounterCategory("Network Interface")
                .GetInstanceNames()
                .Where(name => !name.Contains("Loopback") &&
                              !name.StartsWith("_") &&
                              !name.Contains("Teredo") &&
                              !name.Contains("isatap") &&
                              !name.Contains("Local Area Connection* "))
                .ToList();

            // Try to match active interfaces with performance counter instances
            var selectedInstance = perfCounterInstances
                .FirstOrDefault(instance => activeInterfaces
                    .Any(iface => SanitizeInterfaceName(iface.Name).Contains(SanitizeInterfaceName(instance)) ||
                                 SanitizeInterfaceName(instance).Contains(SanitizeInterfaceName(iface.Name))));

            // Fallback: try to find the most active interface by name patterns
            selectedInstance ??= perfCounterInstances
                .Where(name => name.ToLower().Contains("ethernet") ||
                              name.ToLower().Contains("wi-fi") ||
                              name.ToLower().Contains("wireless"))
                .FirstOrDefault();

            selectedInstance ??= perfCounterInstances.FirstOrDefault();

            if (selectedInstance != null)
            {
                _networkSentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", selectedInstance);
                _networkReceivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", selectedInstance);

                // Initialize counters (first call often returns 0)
                _networkSentCounter.NextValue();
                _networkReceivedCounter.NextValue();

                _selectedNetworkInterface = selectedInstance;

                // Wait a bit for initial measurement
                Thread.Sleep(200);
            }
        }
        catch (Exception ex)
        {
            // Log the exception if you have logging available
            System.Diagnostics.Debug.WriteLine($"Network counter initialization failed: {ex.Message}");
        }
    }

    private static string SanitizeInterfaceName(string name) =>
        name.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").ToLowerInvariant();

    public int GetCpuUsage()
    {
        try
        {
            return Math.Min(100, Math.Max(0, (int)(_cpuCounter?.NextValue() ?? 0)));
        }
        catch
        {
            return 0;
        }
    }

    public int GetRamUsagePercent()
    {
        try
        {
            if (_ramCounter == null || _totalRamMb == 0) return 0;
            var availableRam = (int)_ramCounter.NextValue();
            var usedPercent = (((_totalRamMb - availableRam) * 100) / _totalRamMb);
            return Math.Min(100, Math.Max(0, usedPercent));
        }
        catch
        {
            return 0;
        }
    }

    public int GetNetworkSpeedMBps()
    {
        var stats = GetDetailedNetworkStats();
        return stats.TotalBps;
    }

    public NetworkStats GetDetailedNetworkStats()
    {
        try
        {
            if (_networkSentCounter == null || _networkReceivedCounter == null)
                return new NetworkStats(0, 0, 0, "None");

            // Get current counter values (these are already rates per second from PerformanceCounter)
            var currentSentBps = _networkSentCounter.NextValue();
            var currentReceivedBps = _networkReceivedCounter.NextValue();

            // Convert to integers (bytes per second)
            float uploadBps = Math.Max(0, currentSentBps);
            float downloadBps = Math.Max(0, currentReceivedBps);

            // Skip first measurement as it's often inaccurate
            if (_isFirstMeasurement)
            {
                _isFirstMeasurement = false;
                return new NetworkStats(0, 0, 0, _selectedNetworkInterface);
            }

            // Convert to MB/s (1 MB = 1,048,576 bytes)
            var totalMBps = (int)(uploadBps + downloadBps) / 1048576;

            return new NetworkStats(uploadBps, downloadBps, totalMBps, _selectedNetworkInterface);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Network stats error: {ex.Message}");
            return new NetworkStats(0, 0, 0, "Error");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cpuCounter?.Dispose();
        _ramCounter?.Dispose();
        _networkSentCounter?.Dispose();
        _networkReceivedCounter?.Dispose();
        _disposed = true;
    }
}