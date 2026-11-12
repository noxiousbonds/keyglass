using System.Windows;
using System.Windows.Media;

namespace KeyGlass.Ui;

/// <summary>
/// 视觉主题。所有画刷/画笔在构造时冻结（Freeze），渲染循环里零分配。
/// 两套模式各取一套：绿幕模式必须完全不透明（否则抠像边缘会溢绿），
/// 透明模式允许卡片带轻微半透明。
/// </summary>
public sealed class ThemePalette
{
    public string Name { get; }

    public Brush PanelFill { get; }
    public Pen PanelBorder { get; }
    public Brush PanelSheen { get; }

    public Brush KeyFill { get; }
    public Pen KeyBorder { get; }
    public Pen KeyBorderOn { get; }
    public Brush KeyText { get; }
    public Brush KeyShadow { get; }

    public Brush AccentFill { get; }
    public Brush AccentText { get; }
    public Pen AccentGlow { get; }

    public Brush ModOffFill { get; }
    public Brush ModOnFill { get; }
    public Brush ModTextOff { get; }
    public Brush ModTextOn { get; }

    public Brush StatValue { get; }
    public Brush StatLabel { get; }

    public Brush MouseFill { get; }
    public Pen MouseBorder { get; }
    public Brush MouseDetail { get; }
    public Brush MouseAccent { get; }

    public Typeface LabelFace { get; }
    public Typeface NumFace { get; }

    // 供渲染面做“量化透明度”画刷缓存使用（避免每帧 PushOpacity 产生合成层）
    public Color AccentTopColor { get; }
    public Color AccentTextColor { get; }
    public Color KeyTextColor { get; }
    public Color MouseAccentColor { get; }

    private ThemePalette(
        string name,
        Color panelTop, Color panelBottom, Color border,
        Color keyTop, Color keyBottom, Color keyBorder, Color keyText,
        Color accentTop, Color accentBottom, Color accentText,
        Color modOff, Color modTextOff,
        Color statValue, Color statLabel,
        Color mouseFill, Color mouseBorder, Color mouseDetail,
        bool translucent)
    {
        Name = name;
        AccentTopColor = accentTop;
        AccentTextColor = accentText;
        KeyTextColor = keyText;
        MouseAccentColor = accentTop;

        byte panelAlpha = translucent ? (byte)230 : (byte)255;
        var pTop = WithAlpha(panelTop, panelAlpha);
        var pBottom = WithAlpha(panelBottom, panelAlpha);

        PanelFill = Frozen(new LinearGradientBrush(pTop, pBottom, 90));
        PanelBorder = FrozenPen(new Pen(new SolidColorBrush(WithAlpha(border, (byte)(translucent ? 180 : 255))), 1.2));
        PanelSheen = Frozen(new SolidColorBrush(WithAlpha(Colors.White, 26)));

        KeyFill = Frozen(new LinearGradientBrush(keyTop, keyBottom, 90));
        KeyBorder = FrozenPen(new Pen(new SolidColorBrush(keyBorder), 1.3));
        KeyBorderOn = FrozenPen(new Pen(new SolidColorBrush(WithAlpha(keyText, 120)), 1.3));
        KeyText = Frozen(new SolidColorBrush(keyText));
        KeyShadow = Frozen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)));

        AccentFill = Frozen(new LinearGradientBrush(accentTop, accentBottom, 90));
        AccentText = Frozen(new SolidColorBrush(accentText));
        AccentGlow = FrozenPen(new Pen(new SolidColorBrush(WithAlpha(accentTop, 70)), 6));

        ModOffFill = Frozen(new SolidColorBrush(WithAlpha(modOff, (byte)(translucent ? 200 : 255))));
        ModOnFill = Frozen(new SolidColorBrush(accentTop));
        ModTextOff = Frozen(new SolidColorBrush(modTextOff));
        ModTextOn = Frozen(new SolidColorBrush(accentText));

        StatValue = Frozen(new SolidColorBrush(statValue));
        StatLabel = Frozen(new SolidColorBrush(statLabel));

        MouseFill = Frozen(new LinearGradientBrush(keyTop, keyBottom, 90));
        MouseBorder = FrozenPen(new Pen(new SolidColorBrush(mouseBorder), 1.4));
        MouseDetail = Frozen(new SolidColorBrush(mouseDetail));
        MouseAccent = Frozen(new SolidColorBrush(accentTop));

        LabelFace = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        NumFace = new Typeface(new FontFamily("Cascadia Mono, Consolas, Segoe UI"),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    }

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static Color Hex(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return c;
    }

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Pen p)
    {
        p.Freeze();
        return p;
    }

    public static ThemePalette Create(string name, bool translucent)
    {
        return (name ?? "").ToLowerInvariant() switch
        {
            "carbon" => new ThemePalette(
                "Carbon",
                Hex("#17171B"), Hex("#0B0B0D"), Hex("#2E2E34"),
                Hex("#2A2A30"), Hex("#1A1A1E"), Hex("#40404A"), Hex("#F4F4F6"),
                Hex("#EDEDF1"), Hex("#C6C6CE"), Hex("#101013"),
                Hex("#1D1D22"), Hex("#8C8C96"),
                Hex("#F7F7F9"), Hex("#7E7E88"),
                Hex("#1C1C21"), Hex("#3A3A42"), Hex("#8C8C96"),
                translucent),

            "sakura" => new ThemePalette(
                "Sakura",
                Hex("#1B1218"), Hex("#120C10"), Hex("#3B2833"),
                Hex("#2E2030"), Hex("#1E1420"), Hex("#4C3548"), Hex("#F7EAF0"),
                Hex("#E8608F"), Hex("#C13F6B"), Hex("#FFFFFF"),
                Hex("#241820"), Hex("#B08C9C"),
                Hex("#FBEFF4"), Hex("#A98A99"),
                Hex("#241820"), Hex("#432C38"), Hex("#B08C9C"),
                translucent),

            "lime" => new ThemePalette(
                "Lime",
                Hex("#0D130F"), Hex("#070B09"), Hex("#25362C"),
                Hex("#222D26"), Hex("#141C17"), Hex("#39503F"), Hex("#EAF8EF"),
                Hex("#A6EF6E"), Hex("#69C336"), Hex("#0B1A07"),
                Hex("#18211C"), Hex("#89A394"),
                Hex("#F1FCF5"), Hex("#7E9A89"),
                Hex("#18211C"), Hex("#2E4136"), Hex("#89A394"),
                translucent),

            "bilipink" => new ThemePalette(
                "BiliPink",
                Hex("#18131B"), Hex("#0F0C12"), Hex("#37283C"),
                Hex("#2F2436"), Hex("#1E1726"), Hex("#4C3758"), Hex("#F8EFF6"),
                Hex("#F26088"), Hex("#D13A67"), Hex("#FFFFFF"),
                Hex("#251C2B"), Hex("#B192A4"),
                Hex("#FDF4F8"), Hex("#A98B9C"),
                Hex("#251C2B"), Hex("#433051"), Hex("#B192A4"),
                translucent),

            _ => new ThemePalette(
                "Midnight",
                Hex("#161A23"), Hex("#0C0E13"), Hex("#2B3242"),
                Hex("#2A3140"), Hex("#1B2029"), Hex("#4A5470"), Hex("#EEF2FA"),
                Hex("#5B84F5"), Hex("#2C50CC"), Hex("#FFFFFF"),
                Hex("#1C212C"), Hex("#8B94A7"),
                Hex("#F3F7FF"), Hex("#7F8899"),
                Hex("#1B202A"), Hex("#3B4353"), Hex("#8B94A7"),
                translucent)
        };
    }

    public static readonly string[] Names = { "Midnight", "Carbon", "Sakura", "Lime", "BiliPink" };
}
