namespace KeyGlass.Core;

/// <summary>
/// 虚拟键码 → 叠加层文案。只做展示映射，不参与任何键位拦截逻辑。
/// </summary>
public static class Vk
{
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;      // Alt
    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU = 0xA4;
    public const int VK_RMENU = 0xA5;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;

    public static bool IsModifier(int vk) => vk is
        VK_SHIFT or VK_CONTROL or VK_MENU or
        VK_LSHIFT or VK_RSHIFT or VK_LCONTROL or VK_RCONTROL or
        VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN;

    /// <summary>返回键帽文案；未知键返回 null（不显示，避免垃圾字符）。</summary>
    public static string? Label(int vk)
    {
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();        // 0-9
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();        // A-Z
        if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);          // 小键盘数字
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);        // F1-F24

        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x13 => "Pause",
            0x14 => "Caps",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PgUp",
            0x22 => "PgDn",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "←",
            0x26 => "↑",
            0x27 => "→",
            0x28 => "↓",
            0x2C => "PrtSc",
            0x2D => "Ins",
            0x2E => "Del",
            0x5D => "Menu",
            0x6A => "Num*",
            0x6B => "Num+",
            0x6D => "Num-",
            0x6E => "Num.",
            0x6F => "Num/",
            0x90 => "NumLock",
            0x91 => "ScrLock",
            0xA6 => "Browser←",
            0xA7 => "Browser→",
            0xA8 => "Browser↻",
            0xA9 => "Browser★",
            0xAD => "Mute",
            0xAE => "Vol-",
            0xAF => "Vol+",
            0xB0 => "Next",
            0xB1 => "Prev",
            0xB2 => "Stop",
            0xB3 => "Play",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            0xE2 => "\\",
            0xE5 => "IME",
            0xF2 => "OEM",
            _ => null
        };
    }

    /// <summary>修饰键在“修饰键胶囊”里的短标签。</summary>
    public static string ModifierLabel(int vk) => vk switch
    {
        VK_SHIFT or VK_LSHIFT or VK_RSHIFT => "Shift",
        VK_CONTROL or VK_LCONTROL or VK_RCONTROL => "Ctrl",
        VK_MENU or VK_LMENU or VK_RMENU => "Alt",
        VK_LWIN or VK_RWIN => "Win",
        _ => "?"
    };
}
