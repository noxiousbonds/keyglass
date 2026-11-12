using System.IO;
using System.Text.Json.Serialization;

namespace KeyGlass.Config;

/// <summary>
/// 全部可调项集中在此，序列化为 JSON（%APPDATA%\KeyGlass\settings.json）。
/// 新增字段时给默认值即可向下兼容旧配置。
/// </summary>
public sealed class AppConfig
{
    /// <summary>停靠锚点：TL TC TR / ML MC MR / BL BC BR。</summary>
    public string Anchor { get; set; } = "BR";

    /// <summary>距显示器边缘的水平内边距（DIP）。</summary>
    public int MarginX { get; set; } = 48;

    /// <summary>距显示器边缘的垂直内边距（DIP）。</summary>
    public int MarginY { get; set; } = 48;

    /// <summary>整体缩放 0.6 – 2.0。</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>采集模式：Chroma（绿幕，直播姬首选）/ Transparent（透明叠加）。</summary>
    public string CaptureMode { get; set; } = "Chroma";

    /// <summary>绿幕颜色。与直播姬「色度键」默认绿色一致。</summary>
    public string ChromaColor { get; set; } = "#00B140";

    /// <summary>主题：Midnight / Carbon / Sakura / Lime / BiliPink。</summary>
    public string Theme { get; set; } = "Midnight";

    /// <summary>整体不透明度 0.4 – 1.0。</summary>
    public double Opacity { get; set; } = 1.0;

    public bool ShowMouse { get; set; } = true;

    /// <summary>
    /// 不在本机屏幕上显示叠加层（挪到屏幕可见区域之外），只给直播姬的窗口捕获用。
    /// 窗口仍在渲染、仍可被窗口捕获；切回 false 立刻回到屏幕右下角。
    /// </summary>
    public bool HideOnScreen { get; set; } = false;

    /// <summary>
    /// 隐藏模式的实现方式：
    ///   peek（默认）= 窗口只留 16px 在屏幕内、且被任务栏盖住：DWM 仍在合成，
    ///                  WGC 系的窗口捕获（直播姬/OBS）能抓到完整画面，人眼看不到。
    ///   offscreen  = 整个窗口挪到屏幕可见区域之外（只适合本来就能抓屏幕外窗口的采集端）。
    /// </summary>
    public string HideMode { get; set; } = "peek";
    public bool ShowStats { get; set; } = true;
    public bool ShowModifiers { get; set; } = true;
    public bool ShowLatencyHud { get; set; } = false;

    /// <summary>从 Alt+Tab / 任务栏隐藏（工具窗口样式）。直播姬窗口列表里找不到时请关闭此项。</summary>
    public bool HideFromAltTab { get; set; } = true;

    /// <summary>0 起，目标显示器索引。</summary>
    public int MonitorIndex { get; set; } = 0;

    /// <summary>同时显示的最大键帽数（1 – 8）。</summary>
    public int ChipCapacity { get; set; } = 6;

    /// <summary>松键后键帽保留时长（毫秒），0 = 立即淡出。</summary>
    public int ChipLingerMs { get; set; } = 900;

    public bool StartHidden { get; set; } = false;

    /// <summary>全局快捷键：显示 / 隐藏叠加层。</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+K";

    /// <summary>输入源：Auto（原始输入+轮询，默认）/ RawInput / LowLevelHook / Polling。</summary>
    public string InputEngine { get; set; } = "Auto";

    public AppConfig Clone() => (AppConfig)MemberwiseClone();

    public void Normalize()
    {
        Scale = Math.Clamp(Scale, 0.6, 2.0);
        Opacity = Math.Clamp(Opacity, 0.4, 1.0);
        ChipCapacity = Math.Clamp(ChipCapacity, 1, 8);
        ChipLingerMs = Math.Clamp(ChipLingerMs, 0, 4000);
        MarginX = Math.Clamp(MarginX, 0, 2000);
        MarginY = Math.Clamp(MarginY, 0, 2000);
        if (MonitorIndex < 0) MonitorIndex = 0;

        if (CaptureMode is not ("Chroma" or "Transparent")) CaptureMode = "Chroma";
        if (InputEngine is not ("Auto" or "RawInput" or "LowLevelHook" or "Polling")) InputEngine = "Auto";
        if (!IsValidColor(ChromaColor)) ChromaColor = "#00B140";
        if (string.IsNullOrWhiteSpace(Theme)) Theme = "Midnight";
        if (string.IsNullOrWhiteSpace(Anchor)) Anchor = "BR";
        if (HideMode is not ("peek" or "offscreen")) HideMode = "peek";
    }

    private static bool IsValidColor(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.Length != 7 || s[0] != '#') return false;
        for (int i = 1; i < 7; i++)
        {
            if (!Uri.IsHexDigit(s[i])) return false;
        }
        return true;
    }

    [JsonIgnore]
    public bool IsTransparent => string.Equals(CaptureMode, "Transparent", StringComparison.OrdinalIgnoreCase);
}

public enum CaptureModeKind
{
    Chroma,
    Transparent
}
