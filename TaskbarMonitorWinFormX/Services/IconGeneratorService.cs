using TaskbarMonitorWinFormX.Models;

namespace TaskbarMonitorWinFormX.Services;

public interface IIconGeneratorService
{
    Icon GenerateCpuIcon(MetricsHistory cpuHistory);
    Icon GenerateRamIcon(MetricsHistory ramHistory);
    Icon GenerateNetworkIcon(MetricsHistory networkHistory, int threshold);
    Icon GenerateIcon(MetricsHistory history, SolidBrush brush, int maxValue);
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

    public Icon GenerateIcon(MetricsHistory history, SolidBrush brush, int maxValue)
    {
        var bitmap = new Bitmap(_options.IconSize, _options.IconSize);
        using var g = Graphics.FromImage(bitmap);

        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
        g.FillRectangle(_blackBrush, 0, 0, _options.IconSize, _options.IconSize);

        var values = history.Values;
        if (values.Length == 0) return Icon.FromHandle(bitmap.GetHicon());

        var width = _options.IconSize;
        var height = _options.IconSize;

        if (values.Length == 1)
        {
            var barHeight = (values[0] * height) / maxValue;
            if (barHeight > 0 && barHeight <= height)
                g.FillRectangle(brush, 0, height - barHeight, width, barHeight);
        }
        else
        {
            var step = width / values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                var x = i * step;
                var colWidth = Math.Max(1, step);
                var barHeight = (values[i] * height) / maxValue;

                if (barHeight > 0 && barHeight <= height)
                    g.FillRectangle(brush, x, height - barHeight, colWidth, barHeight);
            }
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        _cpuBrush?.Dispose();
        _ramBrush?.Dispose();
        _netBrush?.Dispose();
        _blackBrush?.Dispose();
    }
}