using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeyGlass.Config;
using KeyGlass.Core;
using KeyGlass.Interop;

namespace KeyGlass.Ui;

/// <summary>
/// 叠加层窗口本身。
///
/// 关键窗口属性（决定了直播姬能不能正常采集、会不会抢焦点）：
///  - WS_EX_TRANSPARENT  → 鼠标穿透，点击落在下层程序上
///  - WS_EX_NOACTIVATE   → 永不抢焦点，不会打断正在播的游戏/操作
///  - WS_EX_TOOLWINDOW   → 不出现在 Alt+Tab（可关，见配置项）
///  - Topmost            → 顶层；注意「窗口捕获」与窗口层级无关，钉在顶层只是为了让主播自己看得见
///
/// 渲染循环：订阅 CompositionTarget.Rendering 跟随显示器 vsync；
/// 没有动画时主动退订，空闲 CPU 归零。
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly ConfigBox _box;
    private readonly OverlaySurface _surface;

    private bool _loopRunning;
    private long _lastTicks;
    private DispatcherTimer? _frameTimer;

    public OverlaySurface Surface => _surface;

    public OverlayWindow(ConfigBox box, StateStore store, LatencyTracker latency, ThemePalette theme)
    {
        _box = box;

        var cfg = box.Value;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Title = "KeyGlass 叠加层（直播姬采集这个）";
        AllowsTransparency = cfg.IsTransparent;
        Background = cfg.IsTransparent ? Brushes.Transparent : ChromaBrush(cfg.ChromaColor);
        WindowStartupLocation = WindowStartupLocation.Manual;

        Width = OverlaySurface.BaseWidth * cfg.Scale;
        Height = OverlaySurface.BaseHeight * cfg.Scale;

        _surface = new OverlaySurface(box, store, latency, theme);
        Content = _surface;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private static Brush ChromaBrush(string hex)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.Lime;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        int ex = Native.GetWindowLong(handle, Native.GWL_EXSTYLE);
        ex |= Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE;
        if (_box.Value.HideFromAltTab) ex |= Native.WS_EX_TOOLWINDOW;
        else ex &= ~Native.WS_EX_TOOLWINDOW;
        Native.SetWindowLong(handle, Native.GWL_EXSTYLE, ex);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 窗口先落到目标显示器，才能拿到正确的 DPI
        PositionWindow();
        StartLoop();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopLoop();
    }

    // ------------------------------------------------------------ 布局

    /// <summary>按锚点 / 边距把窗口摆到目标显示器上。</summary>
    public void PositionWindow()
    {
        var cfg = _box.Value;
        var monitor = MonitorHelper.GetMonitor(cfg.MonitorIndex);

        var dpi = VisualTreeHelper.GetDpi(this);
        double dpiX = dpi.DpiScaleX;
        double dpiY = dpi.DpiScaleY;

        double monLeft = monitor.Left / dpiX;
        double monTop = monitor.Top / dpiY;
        double monW = monitor.Width / dpiX;
        double monH = monitor.Height / dpiY;

        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;

        double mx = cfg.MarginX;
        double my = cfg.MarginY;

        var (hx, vy) = (cfg.Anchor.ToUpperInvariant()) switch
        {
            "TL" => (0, 0),
            "TC" => (1, 0),
            "TR" => (2, 0),
            "ML" => (0, 1),
            "MC" => (1, 1),
            "MR" => (2, 1),
            "BL" => (0, 2),
            "BC" => (1, 2),
            _ => (2, 2)
        };

        double x = hx switch
        {
            0 => monLeft + mx,
            1 => monLeft + (monW - w) / 2,
            _ => monLeft + monW - w - mx
        };

        double y = vy switch
        {
            0 => monTop + my,
            1 => monTop + (monH - h) / 2,
            _ => monTop + monH - h - my
        };

        // 「屏幕上看不见、但采集能拍到」：把窗口整体挪到该显示器可见区域之外。
        // 窗口没有最小化、没有隐藏，WPF 继续渲染，所以窗口捕获拿到的仍是完整画面。
        if (cfg.HideOnScreen)
        {
            Topmost = false;

            if (string.Equals(cfg.HideMode, "offscreen", StringComparison.OrdinalIgnoreCase))
            {
                // 完全挪出可见区域（采集端若按屏幕像素抓会得到空白）
                x = monLeft - w - 64;
                y = monTop;
            }
            else
            {
                // peek：只留 16px 在屏幕内，而这条正好被任务栏（topmost，48px）盖住。
                // 窗口与屏幕有交集 → DWM 继续合成 → WGC 系的窗口捕获能拿到完整画面；
                // 人眼看到的只有任务栏。
                const double peek = 16;
                x = monLeft + monW - w - 8;
                y = monTop + monH - peek;
            }
        }
        else
        {
            Topmost = true;
        }

        Left = x;
        Top = y;
    }

    /// <summary>缩放 / 主题 / 绿幕色变化后刷新窗口外观（不需重建窗口）。</summary>
    public void UpdateChrome()
    {
        var cfg = _box.Value;
        Width = OverlaySurface.BaseWidth * cfg.Scale;
        Height = OverlaySurface.BaseHeight * cfg.Scale;
        Background = cfg.IsTransparent ? Brushes.Transparent : ChromaBrush(cfg.ChromaColor);
        _surface.SetTheme(ThemePalette.Create(cfg.Theme, cfg.IsTransparent));
        PositionWindow();
        _surface.InvalidateVisual();
    }

    // ------------------------------------------------------------ 渲染循环

    public void Wake()
    {
        if (!IsVisible) return;
        StartLoop();
        _surface.Touch();
    }

    private void StartLoop()
    {
        if (_loopRunning) return;

        _loopRunning = true;
        _lastTicks = Stopwatch.GetTimestamp();

        // 用 DispatcherTimer 而不是 CompositionTarget.Rendering：
        // 在 Rendering 回调里 InvalidateVisual 会让 WPF 在同一合成周期内反复重绘，
        // 实测会空转到 300fps（CPU 白烧 5 倍）。定时器把节奏钉在 ~60fps。
        _frameTimer ??= new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(15)
        };
        _frameTimer.Tick -= OnFrame;
        _frameTimer.Tick += OnFrame;
        _frameTimer.Start();
        _surface.Touch();
    }

    private void StopLoop()
    {
        if (!_loopRunning) return;
        _loopRunning = false;
        _frameTimer?.Stop();
        _lastTicks = 0;
    }

    public bool IsAnimating => _loopRunning;

    private void OnFrame(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double dtMs = _lastTicks == 0 ? 16.0 : (now - _lastTicks) * 1000.0 / Stopwatch.Frequency;
        _lastTicks = now;

        if (dtMs <= 0 || dtMs > 120) dtMs = 16.0;

        if (!_surface.Advance(dtMs))
        {
            StopLoop();
        }
    }

    // ------------------------------------------------------------ 截图

    /// <summary>把当前渲染面导出为 PNG（用于样式回归 / 交付截图）。</summary>
    public void SavePng(string path, double pixelScale = 2.0)
    {
        _surface.UpdateLayout();
        double w = _surface.ActualWidth > 0 ? _surface.ActualWidth : OverlaySurface.BaseWidth;
        double h = _surface.ActualHeight > 0 ? _surface.ActualHeight : OverlaySurface.BaseHeight;

        int pw = Math.Max(1, (int)Math.Ceiling(w * pixelScale));
        int ph = Math.Max(1, (int)Math.Ceiling(h * pixelScale));

        var rtb = new RenderTargetBitmap(pw, ph, 96 * pixelScale, 96 * pixelScale, PixelFormats.Pbgra32);
        rtb.Render(_surface);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
