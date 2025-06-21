using System.Runtime.InteropServices;
using TaskbarMonitorWinFormX.Models;

namespace TaskbarMonitorWinFormX.Services
{
    public interface IIconGeneratorService : IDisposable
    {
        Icon GenerateCpuIcon(MetricsHistory cpuHistory);
        Icon GenerateRamIcon(MetricsHistory ramHistory);
        Icon GenerateNetworkIcon(MetricsHistory networkHistory, int threshold);
        Icon GenerateIcon(MetricsHistory history, SolidBrush brush, int maxValue);
    }

    public sealed class IconGeneratorService : IIconGeneratorService
    {
        private readonly MonitoringOptions _options;

        // Re‑use brushes so we’re not reallocating every tick
        private readonly SolidBrush _cpuBrush = new(Color.FromArgb(255, 80, 80));   // red‑ish
        private readonly SolidBrush _ramBrush = new(Color.FromArgb(80, 255, 80));   // green‑ish
        private readonly SolidBrush _netBrush = new(Color.FromArgb(80, 160, 255));   // blue‑ish
        private readonly SolidBrush _blackBrush = new(Color.Black);

        public IconGeneratorService(MonitoringOptions options)
        {
            _options = options;
        }

        #region Public Methods
        public Icon GenerateCpuIcon(MetricsHistory cpuHistory) =>
            GenerateIcon(cpuHistory, _cpuBrush, 100);

        public Icon GenerateRamIcon(MetricsHistory ramHistory) =>
            GenerateIcon(ramHistory, _ramBrush, 100);

        public Icon GenerateNetworkIcon(MetricsHistory netHistory, int threshold) =>
            GenerateIcon(netHistory, _netBrush, threshold);

        /// <summary>
        /// Core routine that converts a MetricsHistory series into a 16×16 tray‑icon bar chart.
        /// </summary>
        public Icon GenerateIcon(MetricsHistory history, SolidBrush brush, int maxValue)
        {
            var size = _options.IconSize;
            using var bitmap = new Bitmap(size, size);
            using var g = Graphics.FromImage(bitmap);

            // fast, aliased drawing – we’re in a 16×16 space anyway
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;

            // clear background
            g.FillRectangle(_blackBrush, 0, 0, size, size);

            // draw bars ------------------------------------------------------
            int[] values = history.Values;
            if (values.Length > 0)
            {
                /*
                 * If the history buffer is wider than the icon, take the most
                 * recent N samples that actually fit.  Use floating‑point math
                 * so “step” never collapses to 0 when values.Length > size.
                 */
                int columns = Math.Min(values.Length, size);
                float step = (float)size / columns;

                for (int i = 0; i < columns; i++)
                {
                    int sampleIndex = values.Length - columns + i;    // tail‑aligned
                    int barHeight = values[sampleIndex] * size / maxValue;
                    if (barHeight == 0) continue;                    // skip empty bars

                    int x = (int)Math.Round(i * step);
                    int nextX = (int)Math.Round((i + 1) * step);
                    int colWidth = Math.Max(1, nextX - x);

                    g.FillRectangle(
                        brush,
                        x,
                        size - barHeight,      // y
                        colWidth,
                        barHeight);
                }
            }

            // convert to a SAFE icon    --------------------------------------
            IntPtr hIcon = bitmap.GetHicon();          // unmanaged handle
            Icon tmp = Icon.FromHandle(hIcon);     // wraps it (doesn’t own)
            Icon safe = (Icon)tmp.Clone();          // managed copy
            DestroyIcon(hIcon);                        // release original handle

            return safe;
        }

        #endregion

        #region IDisposable
        public void Dispose()
        {
            _cpuBrush.Dispose();
            _ramBrush.Dispose();
            _netBrush.Dispose();
            _blackBrush.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region P/Invoke

        // Needed so `DestroyIcon` *does* exist “in the current context”
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        #endregion
    }
}
