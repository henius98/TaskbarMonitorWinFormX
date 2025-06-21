using System.Drawing.Drawing2D;
using TaskbarMonitorWinFormX.Models;

namespace TaskbarMonitorWinFormX.Services;

public interface IIconGeneratorService
{
    Icon GenerateCpuIcon(MetricsHistory cpuHistory);
    Icon GenerateRamIcon(MetricsHistory ramHistory);
    Icon GenerateNetworkIcon(MetricsHistory networkHistory, float threshold);
}

public sealed class IconGeneratorService : IIconGeneratorService
{
    private readonly MonitoringOptions _options;

    private static readonly Color BackgroundColor = Color.FromArgb(200, 40, 40, 40);
    private static readonly Color BorderColor = Color.FromArgb(255, 80, 80, 80);
    private static readonly Color CpuColor = Color.FromArgb(255, 255, 100, 100);
    private static readonly Color RamColor = Color.FromArgb(255, 100, 255, 100);
    private static readonly Color NetworkColor = Color.FromArgb(255, 100, 150, 255);

    public IconGeneratorService(MonitoringOptions options)
    {
        _options = options;
    }

    public Icon GenerateCpuIcon(MetricsHistory cpuHistory)
    {
        return GenerateIcon(cpuHistory, CpuColor, 100f);
    }

    public Icon GenerateRamIcon(MetricsHistory ramHistory)
    {
        return GenerateIcon(ramHistory, RamColor, 100f);
    }

    public Icon GenerateNetworkIcon(MetricsHistory networkHistory, float threshold)
    {
        return GenerateIcon(networkHistory, NetworkColor, threshold);
    }

    private Icon GenerateIcon(MetricsHistory history, Color graphColor, float maxValue)
    {
        using var bitmap = new Bitmap(_options.IconSize, _options.IconSize);
        using var graphics = Graphics.FromImage(bitmap);

        ConfigureGraphics(graphics);
        DrawBackground(graphics);
        DrawGraph(graphics, history, graphColor, maxValue);
        DrawBorder(graphics);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private void ConfigureGraphics(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.Clear(Color.Transparent);
    }

    private void DrawBackground(Graphics graphics)
    {
        using var brush = new SolidBrush(BackgroundColor);
        graphics.FillRectangle(brush, 0, 0, _options.IconSize, _options.IconSize);
    }

    private void DrawGraph(Graphics graphics, MetricsHistory history, Color graphColor, float maxValue)
    {
        var values = history.Values.ToArray();
        if (values.Length < 2) return;

        var graphRect = new Rectangle(
            _options.GraphPadding,
            _options.GraphPadding,
            _options.GraphWidth,
            _options.GraphHeight);

        using var pen = new Pen(graphColor, 1.0f);

        // Calculate points for the graph
        var points = new List<PointF>();
        for (int i = 0; i < values.Length; i++)
        {
            var x = graphRect.X + (float)i / (values.Length - 1) * graphRect.Width;
            var normalizedValue = Math.Clamp(values[i] / maxValue, 0f, 1f);
            var y = graphRect.Bottom - (normalizedValue * graphRect.Height);
            points.Add(new PointF(x, y));
        }

        // Draw the graph line
        if (points.Count > 1)
        {
            graphics.DrawLines(pen, points.ToArray());
        }

        // Fill area under the curve for better visibility
        if (points.Count > 1)
        {
            using var fillBrush = new SolidBrush(Color.FromArgb(80, graphColor));
            var fillPoints = new List<PointF>(points);
            fillPoints.Add(new PointF(points.Last().X, graphRect.Bottom));
            fillPoints.Add(new PointF(points.First().X, graphRect.Bottom));
            graphics.FillPolygon(fillBrush, fillPoints.ToArray());
        }
    }

    private void DrawBorder(Graphics graphics)
    {
        using var pen = new Pen(BorderColor);
        graphics.DrawRectangle(pen, 0, 0, _options.IconSize - 1, _options.IconSize - 1);
    }
}