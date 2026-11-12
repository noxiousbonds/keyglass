using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KeyGlass.Config;
using KeyGlass.Core;

namespace KeyGlass.Ui;

/// <summary>
/// 叠加层绘制面。全部图形（键帽、鼠标、统计）都在这一个 Visual 里自绘，
/// 不依赖任何控件模板，因此 60fps 下没有布局/样式开销。
///
/// 坐标系为「设计基准」，固定 880 × 196；缩放由窗口尺寸 + ScaleTransform 完成。
///
/// 版式（左右对称留白 18px，上下 18px）：
///   行 1：修饰键胶囊  Ctrl Shift Alt Win        （右侧留给调试 HUD）
///   行 2：键帽流 6 槽（76×96）   鼠标图形 + L/M/R 胶囊 + 滚轮    KPS / CPS
/// </summary>
public sealed class OverlaySurface : FrameworkElement
{
    public const double BaseWidth = 880;
    public const double BaseHeight = 184;

    private const double CardInset = 10;
    private const double CardRadius = 18;
    private const double ContentLeft = 28;
    private const double ContentRight = 852;

    private const double ModTop = 28;
    private const double ModHeight = 28;
    private const double ModWidth = 58;
    private const double ModGap = 8;

    private const double ChipTop = 72;
    private const double ChipHeight = 84;
    private const double ChipWidth = 76;
    private const double ChipGap = 10;
    private const double ChipAreaWidth = 506;

    private const double MouseX = 560;
    private const double MouseY = 72;
    private const double MouseW = 62;
    private const double MouseH = 84;

    private const double ChevronX = 630;
    private const double ButtonX = 640;
    private const double ButtonW = 44;
    private const double ButtonH = 24;

    private const double StatX1 = 700;
    private const double StatX2 = 778;
    private const double StatCenterY = 114;

    private readonly ConfigBox _box;
    private readonly StateStore _store;
    private readonly LatencyTracker _latency;

    private ThemePalette _theme;
    private double _pixelsPerDip = 1.0;

    private readonly Pen _sheenPen;
    private readonly Pen _dividerPen;
    private readonly Pen _panelGlowPen;
    private readonly Brush _cardSheen;
    private readonly Brush _glowSoft;
    private readonly Brush _ghostFill;
    private readonly Pen _ghostBorder;

    /// <summary>FormattedText 很贵，键帽文案又是有限集合，缓存后每帧零文本分配。</summary>
    private readonly Dictionary<(string Text, double Size, Typeface Face, Brush Brush, double Dip), FormattedText> _textCache = new();

    /// <summary>量化透明度的画刷缓存：用半透明画刷代替 PushOpacity，避免每帧生成合成层。</summary>
    private readonly Dictionary<long, Brush> _alphaBrushCache = new();
    private readonly Dictionary<long, Pen> _alphaPenCache = new();

    private static readonly StreamGeometry ChevronUp = MakeChevron(new Point(ChevronX, ChevronYUp), 5, up: true);
    private static readonly StreamGeometry ChevronDown = MakeChevron(new Point(ChevronX, ChevronYDown), 5, up: false);

    public OverlaySurface(ConfigBox box, StateStore store, LatencyTracker latency, ThemePalette theme)
    {
        _box = box;
        _store = store;
        _latency = latency;
        _theme = theme;

        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        IsHitTestVisible = false;

        _sheenPen = new Pen(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), 1.2);
        _sheenPen.Freeze();
        _dividerPen = new Pen(new SolidColorBrush(Color.FromArgb(44, 255, 255, 255)), 1.0);
        _dividerPen.Freeze();
        _cardSheen = new LinearGradientBrush(
            Color.FromArgb(26, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90);
        _cardSheen.Freeze();
        _glowSoft = new SolidColorBrush(Color.FromArgb(24, 0, 0, 0));
        _glowSoft.Freeze();
        _panelGlowPen = new Pen(_glowSoft, 8);
        _panelGlowPen.Freeze();

        _ghostFill = new SolidColorBrush(Color.FromArgb(11, 255, 255, 255));
        _ghostFill.Freeze();
        _ghostBorder = new Pen(new SolidColorBrush(Color.FromArgb(17, 255, 255, 255)), 1.2);
        _ghostBorder.Freeze();
    }

    public ThemePalette Theme => _theme;

    /// <summary>绘制面自身的耗时（只包含 OnRender 里的构建时间，不含 DWM 合成）。</summary>
    public long Frames { get; private set; }
    public double RenderMsTotal { get; private set; }
    public double AverageRenderMs => Frames == 0 ? 0 : RenderMsTotal / Frames;

    public void SetTheme(ThemePalette theme)
    {
        _theme = theme;
        _textCache.Clear();
        _alphaBrushCache.Clear();
        _alphaPenCache.Clear();
        InvalidateVisual();
    }

    private static long ColorKey(Color c) => ((long)c.A << 32) | ((long)c.R << 24) | ((long)c.G << 16) | ((long)c.B << 8);

    /// <summary>把 0..1 透明度量化成 24 档，命中缓存则零分配。</summary>
    private Brush AlphaBrush(Color baseColor, double alpha)
    {
        int step = (int)Math.Round(Math.Clamp(alpha, 0, 1) * 24);
        long key = ColorKey(baseColor) | (uint)step;
        if (_alphaBrushCache.TryGetValue(key, out var brush)) return brush;

        brush = new SolidColorBrush(Color.FromArgb((byte)(step * 255 / 24), baseColor.R, baseColor.G, baseColor.B));
        brush.Freeze();
        _alphaBrushCache[key] = brush;
        return brush;
    }

    private Pen AlphaPen(Color baseColor, double alpha, double thickness)
    {
        int step = (int)Math.Round(Math.Clamp(alpha, 0, 1) * 24);
        int thick = (int)Math.Round(thickness * 2);
        long key = ColorKey(baseColor) | (uint)step | ((long)thick << 40);
        if (_alphaPenCache.TryGetValue(key, out var pen)) return pen;

        pen = new Pen(AlphaBrush(baseColor, alpha), thick / 2.0);
        pen.Freeze();
        _alphaPenCache[key] = pen;
        return pen;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double scale = _box.Value.Scale;
        return new Size(BaseWidth * scale, BaseHeight * scale);
    }

    /// <summary>推进一帧，返回 true = 动画未结束，渲染循环应继续。</summary>
    public bool Advance(double dtMs)
    {
        bool more = _store.Advance(dtMs);
        InvalidateVisual();
        return more;
    }

    public void Touch() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

        var cfg = _box.Value;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (Math.Abs(dip - _pixelsPerDip) > 0.001)
        {
            _pixelsPerDip = dip;
            _textCache.Clear();
        }

        double scale = cfg.Scale;
        double alpha = cfg.IsTransparent ? cfg.Opacity : 1.0;

        dc.PushTransform(new ScaleTransform(scale, scale));
        bool fade = alpha < 0.999;
        if (fade) dc.PushOpacity(alpha);

        DrawPanel(dc);

        if (cfg.ShowModifiers) DrawModifiers(dc);

        DrawChips(dc);

        if (cfg.ShowMouse) DrawMouse(dc);
        if (cfg.ShowStats) DrawStats(dc);
        if (cfg.ShowLatencyHud) DrawHud(dc);

        RecordLatency();

        if (fade) dc.Pop();
        dc.Pop();

        Frames++;
        RenderMsTotal += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    // ---------------------------------------------------------------- 面板

    private void DrawPanel(DrawingContext dc)
    {
        var card = new Rect(CardInset, CardInset, BaseWidth - CardInset * 2, BaseHeight - CardInset * 2);

        // 外发光（比 DropShadowEffect 便宜两个数量级）
        dc.DrawRoundedRectangle(null, _panelGlowPen,
            new Rect(card.X - 3, card.Y - 2, card.Width + 6, card.Height + 6), CardRadius + 5, CardRadius + 5);

        dc.DrawRoundedRectangle(_theme.PanelFill, null, card, CardRadius, CardRadius);

        dc.PushClip(new RectangleGeometry(card, CardRadius, CardRadius));
        dc.DrawRectangle(_cardSheen, null, new Rect(card.X, card.Y, card.Width, 52));
        dc.Pop();

        // 双色描边：外层一点主题色，内层深色，让卡片从深色画面里“浮”出来
        dc.DrawRoundedRectangle(null, _theme.AccentGlow, card, CardRadius, CardRadius);
        dc.DrawRoundedRectangle(null, _theme.PanelBorder, new Rect(card.X + 0.6, card.Y + 0.6, card.Width - 1.2, card.Height - 1.2),
            CardRadius - 0.6, CardRadius - 0.6);
    }

    // ---------------------------------------------------------------- 修饰键

    private void DrawModifiers(DrawingContext dc)
    {
        (string Label, bool On)[] mods =
        {
            ("Ctrl", _store.Ctrl),
            ("Shift", _store.Shift),
            ("Alt", _store.Alt),
            ("Win", _store.Win)
        };

        double x = ContentLeft;
        for (int i = 0; i < mods.Length; i++)
        {
            var r = new Rect(x, ModTop, ModWidth, ModHeight);
            bool on = mods[i].On;

            dc.DrawRoundedRectangle(on ? _theme.ModOnFill : _theme.ModOffFill, null, r, 9, 9);

            var ft = Text(mods[i].Label, 13, _theme.LabelFace, on ? _theme.ModTextOn : _theme.ModTextOff);
            dc.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Y + (r.Height - ft.Height) / 2 - 0.5));

            x += ModWidth + ModGap;
        }
    }

    // ---------------------------------------------------------------- 键帽

    private void DrawChips(DrawingContext dc)
    {
        var chips = _store.Display;

        int cap = Math.Max(1, _box.Value.ChipCapacity);
        double chipW = Math.Min(ChipWidth, (ChipAreaWidth - (cap - 1) * ChipGap) / cap);

        // 空槽位画成极淡的“键位凹槽”：面板尺寸固定，画面不抖动，
        // 观众看到的是一块稳定的键盘区，而不是忽宽忽窄的黑条。
        for (int i = 0; i < cap; i++)
        {
            double x = ContentLeft + i * (chipW + ChipGap);
            var rect = new Rect(x, ChipTop, chipW, ChipHeight);

            if (i < chips.Count)
            {
                var chip = chips[i];
                DrawKeycap(dc, rect, chip.Label, chip.Press, chip.Pop, chip.Alpha);
            }
            else
            {
                dc.DrawRoundedRectangle(_ghostFill, _ghostBorder, rect, 14, 14);
            }
        }
    }

    private void DrawKeycap(DrawingContext dc, Rect r, string label, double press, double pop, double alpha)
    {
        double cx = r.X + r.Width / 2;
        double cy = r.Y + r.Height / 2;

        // 按下时轻微收缩 + 下沉：纯几何计算，不生成 Transform 对象
        double k = 1.0 - 0.035 * press + 0.004 * pop;
        double dy = 1.6 * press;
        var body = new Rect(
            cx - r.Width * k / 2,
            cy - r.Height * k / 2 + dy,
            r.Width * k,
            r.Height * k);

        bool fading = alpha < 0.999;
        if (fading) dc.PushOpacity(alpha);

        // 投影
        dc.DrawRoundedRectangle(_theme.KeyShadow, null,
            new Rect(body.X, body.Y + 3, body.Width, body.Height), 14, 14);

        // 按下光晕（量化 alpha 描边，不产生合成层）
        if (press > 0.04)
        {
            dc.DrawRoundedRectangle(null, AlphaPen(_theme.AccentTopColor, 0.30 * press, 7),
                new Rect(body.X - 2, body.Y - 2, body.Width + 4, body.Height + 4), 16, 16);
        }

        dc.DrawRoundedRectangle(_theme.KeyFill, null, body, 14, 14);

        if (press >= 0.5)
        {
            dc.DrawRoundedRectangle(_theme.AccentFill, null, body, 14, 14);
        }

        // 描边画在填充之上：按下时换成高光描边，避免出现一圈灰边
        dc.DrawRoundedRectangle(null, press > 0.5 ? _theme.KeyBorderOn : _theme.KeyBorder, body, 14, 14);

        // 顶部高光
        dc.DrawLine(_sheenPen, new Point(body.X + 14, body.Y + 1.6), new Point(body.Right - 14, body.Y + 1.6));

        var color = press > 0.5 ? _theme.AccentText : _theme.KeyText;
        var text = Text(label, FontSizeFor(label), _theme.LabelFace, color);
        dc.DrawText(text, new Point(cx - text.Width / 2, cy + dy - text.Height / 2));

        if (fading) dc.Pop();
    }

    private static double FontSizeFor(string label) => label.Length switch
    {
        <= 2 => 32,
        3 => 22,
        <= 5 => 18,
        _ => 14
    };

    // ---------------------------------------------------------------- 鼠标

    private void DrawMouse(DrawingContext dc)
    {
        var body = new Rect(MouseX, MouseY, MouseW, MouseH);
        var geom = new RectangleGeometry(body, 22, 22);

        double left = Math.Max(_store.MouseLeft ? 1 : 0, _store.LeftPulse);
        double right = Math.Max(_store.MouseRight ? 1 : 0, _store.RightPulse);
        double middle = Math.Max(_store.MouseMiddle ? 1 : 0, _store.MiddlePulse);

        dc.DrawGeometry(_theme.MouseFill, null, geom);

        dc.PushClip(geom);
        DrawMouseZone(dc, new Rect(body.X, body.Y, body.Width / 2, 40), left);
        DrawMouseZone(dc, new Rect(body.X + body.Width / 2, body.Y, body.Width / 2, 40), right);
        DrawMouseZone(dc, new Rect(body.X + 20, body.Y, 22, 40), middle);
        dc.Pop();

        dc.DrawGeometry(null, _theme.MouseBorder, geom);
        dc.DrawLine(_dividerPen, new Point(body.X + 2, body.Y + 40), new Point(body.Right - 2, body.Y + 40));

        // 滚轮：常态灰，滚动时亮起
        double wheelGlow = Math.Max(_store.WheelUp, _store.WheelDown);
        var wheelRect = new Rect(body.X + (body.Width - 13) / 2, body.Y + 10, 13, 22);
        dc.DrawRoundedRectangle(_theme.MouseDetail, null, wheelRect, 6, 6);
        if (wheelGlow > 0)
        {
            dc.DrawRoundedRectangle(AlphaBrush(_theme.MouseAccentColor, wheelGlow), null, wheelRect, 6, 6);
        }

        if (_store.WheelUp > 0)
        {
            dc.DrawGeometry(AlphaBrush(_theme.MouseAccentColor, _store.WheelUp), null, ChevronUp);
        }
        if (_store.WheelDown > 0)
        {
            dc.DrawGeometry(AlphaBrush(_theme.MouseAccentColor, _store.WheelDown), null, ChevronDown);
        }

        // L / M / R 胶囊
        (string Text, double Level, double Y)[] buttons =
        {
            ("L", left, 74),
            ("M", middle, 102),
            ("R", right, 130)
        };

        foreach (var b in buttons)
        {
            var r = new Rect(ButtonX, b.Y, ButtonW, ButtonH);
            dc.DrawRoundedRectangle(_theme.ModOffFill, null, r, 9, 9);

            if (b.Level > 0.02)
            {
                dc.DrawRoundedRectangle(AlphaBrush(_theme.MouseAccentColor, Math.Min(1.0, b.Level)), null, r, 9, 9);
            }

            var fg = b.Level > 0.55 ? _theme.ModTextOn : _theme.MouseDetail;
            var ft = Text(b.Text, 13, _theme.LabelFace, fg);
            dc.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Y + (r.Height - ft.Height) / 2 - 0.5));
        }

        if (_store.MouseX1 || _store.MouseX2)
        {
            string t = _store.MouseX1 && _store.MouseX2 ? "S4 S5" : _store.MouseX1 ? "S4" : "S5";
            var ft = Text(t, 10.5, _theme.NumFace, _theme.MouseDetail);
            dc.DrawText(ft, new Point(ButtonX + 2, 158));
        }
    }

    private const double ChevronYUp = 96;
    private const double ChevronYDown = 124;

    private void DrawMouseZone(DrawingContext dc, Rect zone, double level)
    {
        if (level <= 0.02) return;
        dc.DrawRectangle(AlphaBrush(_theme.MouseAccentColor, Math.Min(1.0, level) * 0.92), null, zone);
    }

    private static StreamGeometry MakeChevron(Point center, double size, bool up)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            double h = size;
            if (up)
            {
                ctx.BeginFigure(new Point(center.X, center.Y - h), true, true);
                ctx.LineTo(new Point(center.X + h, center.Y), true, false);
                ctx.LineTo(new Point(center.X - h, center.Y), true, false);
            }
            else
            {
                ctx.BeginFigure(new Point(center.X, center.Y + h), true, true);
                ctx.LineTo(new Point(center.X + h, center.Y), true, false);
                ctx.LineTo(new Point(center.X - h, center.Y), true, false);
            }
        }
        g.Freeze();
        return g;
    }

    // ---------------------------------------------------------------- 统计

    private void DrawStats(DrawingContext dc)
    {
        DrawStat(dc, StatX1, "KPS", _store.Kps);
        DrawStat(dc, StatX2, "CPS", _store.Cps);
    }

    private void DrawStat(DrawingContext dc, double x, string label, double value)
    {
        string v = value >= 10
            ? ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);

        var lab = Text(label, 10, _theme.NumFace, _theme.StatLabel);
        var val = Text(v, 28, _theme.NumFace, _theme.StatValue);

        double total = lab.Height + 2 + val.Height;
        double top = StatCenterY - total / 2;

        dc.DrawText(lab, new Point(x, top));
        dc.DrawText(val, new Point(x, top + lab.Height + 2));
    }

    private void DrawHud(DrawingContext dc)
    {
        _latency.Snapshot(out double p50, out double p95, out _, out _);
        string s = _latency.Count == 0 ? "LAT --" : $"LAT {p50:0.0} · p95 {p95:0.0} ms";
        var ft = Text(s, 10, _theme.NumFace, _theme.StatLabel);
        dc.DrawText(ft, new Point(ContentRight - ft.Width, 34));
    }

    // ---------------------------------------------------------------- 延迟采样

    private void RecordLatency()
    {
        long now = 0;
        foreach (var chip in _store.Display)
        {
            if (chip.Rendered) continue;
            if (now == 0) now = System.Diagnostics.Stopwatch.GetTimestamp();
            double ms = (now - chip.DownTs) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _latency.Add(ms);
            chip.Rendered = true;
        }
    }

    // ---------------------------------------------------------------- 文本

    private FormattedText Text(string text, double size, Typeface face, Brush brush)
    {
        double dip = Math.Round(_pixelsPerDip, 2);
        var key = (text, size, face, brush, dip);

        if (_textCache.TryGetValue(key, out var cached)) return cached;

        if (_textCache.Count > 256) _textCache.Clear();

        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            face, size, brush, dip);
        _textCache[key] = ft;
        return ft;
    }
}
