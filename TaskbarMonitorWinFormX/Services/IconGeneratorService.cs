using System.Runtime.InteropServices;
using TaskbarMonitorWinFormX.Models;

namespace TaskbarMonitorWinFormX.Services
{
    public interface IIconGeneratorService : IDisposable
    {
        Icon GenerateCpuIcon(MetricsHistory cpuHistory);
        Icon GenerateRamIcon(MetricsHistory ramHistory);
        Icon GenerateNetworkIcon(MetricsHistory networkHistory, int threshold);
    }

    public sealed class IconGeneratorService : IIconGeneratorService
    {
        private readonly MonitoringOptions _options;
        private readonly SolidBrush _cpuBrush = new(Color.FromArgb(255, 80, 80));
        private readonly SolidBrush _ramBrush = new(Color.FromArgb(80, 255, 80));
        private readonly SolidBrush _netBrush = new(Color.FromArgb(80, 160, 255));
        private readonly SolidBrush _blackBrush = new(Color.Black);

        public IconGeneratorService(MonitoringOptions options)
        {
            _options = options;
        }

        public Icon GenerateCpuIcon(MetricsHistory cpuHistory) =>
            GenerateIcon(cpuHistory, _cpuBrush, 100);

        public Icon GenerateRamIcon(MetricsHistory ramHistory) =>
            GenerateIcon(ramHistory, _ramBrush, 100);

        public Icon GenerateNetworkIcon(MetricsHistory networkHistory, int threshold) =>
            GenerateIcon(networkHistory, _netBrush, threshold);

        private Icon GenerateIcon(MetricsHistory history, SolidBrush brush, int maxValue)
        {
            var size = _options.IconSize;
            using var bitmap = new Bitmap(size, size);
            using var g = Graphics.FromImage(bitmap);

            g.Clear(Color.Black);

            var values = history.Values;
            if (values.Length == 0) return CreateIcon(bitmap);

            var step = (float)size / values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                if (value <= 0) continue;

                var barHeight = Math.Min(size, (value * size) / maxValue);
                if (barHeight == 0) continue;

                var x = (int)(i * step);
                var width = Math.Max(1, (int)step);

                g.FillRectangle(brush, x, size - barHeight, width, barHeight);
            }

            return CreateIcon(bitmap);
        }

        private static Icon CreateIcon(Bitmap bitmap)
        {
            IntPtr hIcon = IntPtr.Zero;
            try
            {
                hIcon = bitmap.GetHicon();
                using var tempIcon = Icon.FromHandle(hIcon);

                // Create a copy that owns its resources
                using var ms = new MemoryStream();
                tempIcon.Save(ms);
                ms.Position = 0;
                return new Icon(ms);
            }
            finally
            {
                if (hIcon != IntPtr.Zero)
                    DestroyIcon(hIcon);
            }
        }

        public void Dispose()
        {
            _cpuBrush?.Dispose();
            _ramBrush?.Dispose();
            _netBrush?.Dispose();
            _blackBrush?.Dispose();
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}