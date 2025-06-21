using System.Diagnostics;

namespace TaskbarMonitorWinFormX.Services;

public interface IPerformanceCounterService : IDisposable
{
    float GetCpuUsage();
    float GetRamUsagePercent();
    float GetNetworkSpeedMbps();
    bool IsInitialized { get; }
}

public sealed class PerformanceCounterService : IPerformanceCounterService
{
    private readonly PerformanceCounter? _cpuCounter;
    private readonly PerformanceCounter? _ramCounter;
    private PerformanceCounter? _networkSentCounter;
    private PerformanceCounter? _networkReceivedCounter;
    private readonly long _totalRamMb;
    private bool _disposed;

    public bool IsInitialized { get; private set; }

    public PerformanceCounterService()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "Available MBytes");

            // Get total RAM
            _totalRamMb = (long)(new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / (1024 * 1024));

            // Initialize network counters
            InitializeNetworkCounters();

            IsInitialized = true;
        }
        catch (Exception)
        {
            IsInitialized = false;
            Dispose();
        }
    }

    private void InitializeNetworkCounters()
    {
        try
        {
            var networkInterface = new PerformanceCounterCategory("Network Interface")
                .GetInstanceNames()
                .FirstOrDefault(name =>
                    !name.Contains("Loopback", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Teredo", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("isatap", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(networkInterface))
            {
                _networkSentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", networkInterface);
                _networkReceivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", networkInterface);
            }
        }
        catch
        {
            // Network counters are optional
        }
    }

    public float GetCpuUsage()
    {
        try
        {
            return _cpuCounter?.NextValue() ?? 0f;
        }
        catch
        {
            return 0f;
        }
    }

    public float GetRamUsagePercent()
    {
        try
        {
            if (_ramCounter == null || _totalRamMb == 0) return 0f;

            var availableRam = _ramCounter.NextValue();
            return (((_totalRamMb - availableRam) / _totalRamMb) * 100f);
        }
        catch
        {
            return 0f;
        }
    }

    public float GetNetworkSpeedMbps()
    {
        try
        {
            if (_networkSentCounter == null || _networkReceivedCounter == null) return 0f;

            var sent = _networkSentCounter.NextValue();
            var received = _networkReceivedCounter.NextValue();
            return (sent + received) / (1024f * 1024f); // Convert to MB/s
        }
        catch
        {
            return 0f;
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