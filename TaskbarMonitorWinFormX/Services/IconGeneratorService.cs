using System.Drawing.Drawing2D;
using TaskbarMonitorWinFormX.Models;

namespace TaskbarMonitorWinFormX.Services;

public interface IIconGeneratorService
{
    Icon GenerateIcon(SystemMetrics metrics);
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

    public Icon GenerateIcon(SystemMetrics metrics)
    {
        using var bitmap = new Bitmap(_options.IconSize, _options.IconSize);
        using var graphics = Graphics.FromImage(bitmap);

        ConfigureGraphics(graphics);
        DrawBackground(graphics);
        DrawMetricsBars(graphics, metrics);
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

    private void DrawMetricsBars(Graphics graphics, SystemMetrics metrics)
    {
        var barPositions = CalculateBarPositions();

        DrawBar(graphics, barPositions[0], metrics.CpuUsagePercent, 100f, CpuColor);
        DrawBar(graphics, barPositions[1], metrics.RamUsagePercent, 100f, RamColor);
        DrawBar(graphics, barPositions[2], metrics.NetworkSpeedMbps, _options.NetworkThresholdMbps, NetworkColor);
    }

    private int[] CalculateBarPositions()
    {
        var totalBarsWidth = _options.BarCount * _options.BarWidth + (_options.BarCount - 1) * _options.BarSpacing;
        var startX = (_options.IconSize - totalBarsWidth) / 2;

        var positions = new int[_options.BarCount];
        for (var i = 0; i < _options.BarCount; i++)
        {
            positions[i] = startX + i * (_options.BarWidth + _options.BarSpacing);
        }

        return positions;
    }

    private void DrawBar(Graphics graphics, int x, float value, float maxValue, Color color)
    {
        var normalizedValue = Math.Clamp(value / maxValue, 0f, 1f);
        var barHeight = (int)(normalizedValue * (_options.IconSize - 2));

        if (barHeight <= 0) return;

        using var brush = new SolidBrush(color);
        graphics.FillRectangle(brush, x, _options.IconSize - 1 - barHeight, _options.BarWidth, barHeight);
    }

    private void DrawBorder(Graphics graphics)
    {
        using var pen = new Pen(BorderColor);
        graphics.DrawRectangle(pen, 0, 0, _options.IconSize - 1, _options.IconSize - 1);
    }
}