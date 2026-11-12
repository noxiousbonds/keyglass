using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyGlass.Ui;

/// <summary>
/// 离屏截图：把绘制面渲染成 PNG，并可选叠一张模拟「直播画面」背景，
/// 用来判断叠加层在实际画面上的对比度与可读性（样式回归用）。
/// </summary>
public static class Preview
{
    public static void Save(OverlaySurface surface, string path, bool withBackdrop = true, double pixelScale = 2.0)
    {
        surface.UpdateLayout();
        double w = surface.ActualWidth;
        double h = surface.ActualHeight;

        if (w <= 1 || h <= 1)
        {
            surface.Measure(new Size(OverlaySurface.BaseWidth, OverlaySurface.BaseHeight));
            surface.Arrange(new Rect(0, 0, OverlaySurface.BaseWidth, OverlaySurface.BaseHeight));
            w = surface.ActualWidth;
            h = surface.ActualHeight;
        }

        int pw = Math.Max(1, (int)Math.Ceiling(w * pixelScale));
        int ph = Math.Max(1, (int)Math.Ceiling(h * pixelScale));

        var layer = new RenderTargetBitmap(pw, ph, 96 * pixelScale, 96 * pixelScale, PixelFormats.Pbgra32);
        layer.Render(surface);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (withBackdrop)
            {
                dc.DrawRectangle(Backdrop(w, h), null, new Rect(0, 0, w, h));
            }
            dc.DrawImage(layer, new Rect(0, 0, w, h));
        }

        var final = new RenderTargetBitmap(pw, ph, 96 * pixelScale, 96 * pixelScale, PixelFormats.Pbgra32);
        final.Render(visual);

        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(final));
        using var fs = System.IO.File.Create(path);
        encoder.Save(fs);
    }

    /// <summary>模拟一张偏暗的直播画面（渐变 + 色块 + 地平线），只用于对比度评估。</summary>
    private static Brush Backdrop(double w, double h)
    {
        var brush = new LinearGradientBrush(
            Color.FromRgb(0x22, 0x27, 0x33), Color.FromRgb(0x0A, 0x0C, 0x11), 45);
        brush.StartPoint = new Point(0, 0);
        brush.EndPoint = new Point(1, 1);
        return brush;
    }
}
