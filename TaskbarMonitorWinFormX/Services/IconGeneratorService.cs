using System.Runtime.CompilerServices;
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
        private readonly BitmapPool _bitmapPool;
        private readonly IconCache _iconCache;

        // Reuse brushes to avoid allocation overhead
        private readonly SolidBrush _cpuBrush = new(Color.FromArgb(255, 80, 80));
        private readonly SolidBrush _ramBrush = new(Color.FromArgb(80, 255, 80));
        private readonly SolidBrush _netBrush = new(Color.FromArgb(80, 160, 255));
        private readonly SolidBrush _blackBrush = new(Color.Black);

        // Pre-allocated buffer for rendering values
        private readonly int[] _renderBuffer;

        public IconGeneratorService(MonitoringOptions options)
        {
            _options = options;
            _bitmapPool = new BitmapPool(_options.IconSize);
            _iconCache = new IconCache(_options.IconCacheBucketSize);
            _renderBuffer = new int[_options.IconSize];
        }

        #region Public Methods

        public Icon GenerateCpuIcon(MetricsHistory cpuHistory) =>
            GenerateIconWithCache(cpuHistory, IconType.Cpu, _cpuBrush, 100);

        public Icon GenerateRamIcon(MetricsHistory ramHistory) =>
            GenerateIconWithCache(ramHistory, IconType.Ram, _ramBrush, 100);

        public Icon GenerateNetworkIcon(MetricsHistory networkHistory, int threshold) =>
            GenerateIconWithCache(networkHistory, IconType.Network, _netBrush, threshold);

        #endregion

        #region Core Implementation

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Icon GenerateIconWithCache(MetricsHistory history, IconType type, SolidBrush brush, int maxValue)
        {
            var currentValue = history.Current;

            return _iconCache.GetOrCreate(currentValue, type, () =>
                GenerateIconCore(history, brush, maxValue));
        }

        /// <summary>
        /// High-performance icon generation with object pooling and span operations
        /// </summary>
        private Icon GenerateIconCore(MetricsHistory history, SolidBrush brush, int maxValue)
        {
            var size = _options.IconSize;

            using var pooledBitmap = _bitmapPool.Rent();
            var bitmap = pooledBitmap.Bitmap;

            using var g = Graphics.FromImage(bitmap);

            // Optimize graphics settings for performance
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

            // Clear background
            g.FillRectangle(_blackBrush, 0, 0, size, size);

            // Render bars using span operations
            RenderBarsOptimized(history, g, brush, maxValue, size);

            // Create safe icon copy
            return CreateSafeIcon(bitmap);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderBarsOptimized(MetricsHistory history, Graphics g, SolidBrush brush, int maxValue, int size)
        {
            var renderSpan = _renderBuffer.AsSpan();

            // Copy values using zero-allocation span operations
            history.CopyValuesForRendering(renderSpan, size);

            var valueCount = Math.Min(history.Count, size);
            if (valueCount == 0) return;

            // Use span for high-performance iteration
            var values = renderSpan[..valueCount];
            var step = (float)size / valueCount;

            for (int i = 0; i < valueCount; i++)
            {
                var value = values[i];
                if (value <= 0) continue;

                var barHeight = Math.Min(size, (value * size) / maxValue);
                if (barHeight == 0) continue;

                var x = (int)(i * step);
                var nextX = (int)((i + 1) * step);
                var colWidth = Math.Max(1, nextX - x);

                g.FillRectangle(brush, x, size - barHeight, colWidth, barHeight);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Icon CreateSafeIcon(Bitmap bitmap)
        {
            var hIcon = bitmap.GetHicon();
            try
            {
                var tempIcon = Icon.FromHandle(hIcon);
                return (Icon)tempIcon.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        #endregion

        #region IDisposable

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;

            _cpuBrush.Dispose();
            _ramBrush.Dispose();
            _netBrush.Dispose();
            _blackBrush.Dispose();

            _bitmapPool.Dispose();
            _iconCache.Dispose();

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion

        #region P/Invoke

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        #endregion
    }
}