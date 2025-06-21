using System.Diagnostics;
using System.Net.NetworkInformation;

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
    private string? _selectedNetworkInterface;

    public bool IsInitialized { get; private set; }

    public PerformanceCounterService()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "Available MBytes");

            // Get total RAM
            _totalRamMb = (long)(new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / (1024 * 1024));

            // Initialize network counters with better detection
            InitializeNetworkCounters();

            IsInitialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PerformanceCounterService initialization failed: {ex.Message}");
            IsInitialized = false;
            Dispose();
        }
    }

    private void InitializeNetworkCounters()
    {
        try
        {
            // Get active network interfaces from .NET
            var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                           ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                           ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .ToList();

            // Get performance counter instance names
            var perfCounterInstances = new PerformanceCounterCategory("Network Interface")
                .GetInstanceNames()
                .Where(name => !name.Contains("Loopback", StringComparison.OrdinalIgnoreCase) &&
                              !name.Contains("Teredo", StringComparison.OrdinalIgnoreCase) &&
                              !name.Contains("isatap", StringComparison.OrdinalIgnoreCase) &&
                              !name.StartsWith("_") &&
                              name != "MS TCP Loopback interface")
                .ToList();

            System.Diagnostics.Debug.WriteLine($"Found {activeInterfaces.Count} active interfaces");
            System.Diagnostics.Debug.WriteLine($"Found {perfCounterInstances.Count} performance counter instances");

            foreach (var instance in perfCounterInstances)
            {
                System.Diagnostics.Debug.WriteLine($"Performance counter instance: {instance}");
            }

            // Try to match active interfaces with performance counter instances
            string? selectedInstance = null;

            // First try: exact name match
            foreach (var activeInterface in activeInterfaces)
            {
                var matchingInstance = perfCounterInstances.FirstOrDefault(pi =>
                    pi.Equals(activeInterface.Name, StringComparison.OrdinalIgnoreCase));
                if (matchingInstance != null)
                {
                    selectedInstance = matchingInstance;
                    break;
                }
            }

            // Second try: partial name match
            if (selectedInstance == null)
            {
                foreach (var activeInterface in activeInterfaces)
                {
                    var matchingInstance = perfCounterInstances.FirstOrDefault(pi =>
                        pi.Contains(activeInterface.Name, StringComparison.OrdinalIgnoreCase) ||
                        activeInterface.Name.Contains(pi, StringComparison.OrdinalIgnoreCase));
                    if (matchingInstance != null)
                    {
                        selectedInstance = matchingInstance;
                        break;
                    }
                }
            }

            // Third try: just use the first available instance that looks like a real network adapter
            if (selectedInstance == null && perfCounterInstances.Any())
            {
                selectedInstance = perfCounterInstances.FirstOrDefault(pi =>
                    !pi.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                    !pi.Contains("VPN", StringComparison.OrdinalIgnoreCase)) ??
                    perfCounterInstances.First();
            }

            if (!string.IsNullOrEmpty(selectedInstance))
            {
                _selectedNetworkInterface = selectedInstance;
                _networkSentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", selectedInstance);
                _networkReceivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", selectedInstance);

                System.Diagnostics.Debug.WriteLine($"Selected network interface: {selectedInstance}");

                // Initialize counters with first reading
                _networkSentCounter.NextValue();
                _networkReceivedCounter.NextValue();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No suitable network interface found");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Network counter initialization failed: {ex.Message}");
            // Network counters are optional, don't fail the entire service
        }
    }

    public float GetCpuUsage()
    {
        try
        {
            return _cpuCounter?.NextValue() ?? 0f;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CPU counter error: {ex.Message}");
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RAM counter error: {ex.Message}");
            return 0f;
        }
    }

    public float GetNetworkSpeedMbps()
    {
        try
        {
            if (_networkSentCounter == null || _networkReceivedCounter == null)
            {
                System.Diagnostics.Debug.WriteLine($"Network counters not available. Interface: {_selectedNetworkInterface ?? "None"}");
                return 0f;
            }

            var sent = _networkSentCounter.NextValue();
            var received = _networkReceivedCounter.NextValue();
            var totalBytes = sent + received;
            var totalMB = totalBytes / (1024f * 1024f); // Convert to MB/s

            System.Diagnostics.Debug.WriteLine($"Network: {totalMB:F2} MB/s (Sent: {sent / 1024 / 1024:F2}, Received: {received / 1024 / 1024:F2})");
            return totalMB;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Network counter error: {ex.Message}");
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