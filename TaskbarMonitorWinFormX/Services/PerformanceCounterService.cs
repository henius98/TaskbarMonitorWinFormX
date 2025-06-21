using System.Diagnostics;
using System.Net.NetworkInformation;

namespace TaskbarMonitorWinFormX.Services;

public interface IPerformanceCounterService : IDisposable
{
    int GetCpuUsage();
    int GetRamUsagePercent();
    int GetNetworkSpeedMbps();
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
            var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                           ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            var perfCounterInstances = new PerformanceCounterCategory("Network Interface")
                .GetInstanceNames()
                .Where(name => !name.Contains("Loopback") && !name.StartsWith("_"))
                .ToList();

            var selectedInstance = perfCounterInstances.FirstOrDefault();
            if (selectedInstance != null)
            {
                _networkSentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", selectedInstance);
                _networkReceivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", selectedInstance);
                _networkSentCounter.NextValue();
                _networkReceivedCounter.NextValue();
            }
        }
        catch
        {
            // Network counters are optional
        }
    }

    public int GetCpuUsage()
    {
        try
        {
            return (int)(_cpuCounter?.NextValue() ?? 0);
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
            return (((_totalRamMb - availableRam) * 100) / _totalRamMb);
        }
        catch
        {
            return 0;
        }
    }

    public int GetNetworkSpeedMbps()
    {
        try
        {
            if (_networkSentCounter == null || _networkReceivedCounter == null) return 0;
            var sent = (int)_networkSentCounter.NextValue();
            var received = (int)_networkReceivedCounter.NextValue();
            return (sent + received) / (1024 * 1024);
        }
        catch
        {
            return 0;
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