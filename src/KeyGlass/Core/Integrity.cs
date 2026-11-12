using System.Runtime.InteropServices;
using KeyGlass.Interop;

namespace KeyGlass.Core;

/// <summary>
/// 进程完整性级别自检。
///
/// 为什么需要它：实测发现，把 KeyGlass 放在被策略限制的目录（例如被沙盘接管的目录）时，
/// 系统会强制它以 Low 完整性运行；而 Low 进程**看不到**发往普通（Medium/High）窗口的输入，
/// 表现就是「只有 KeyGlass 自己的窗口有焦点时才有按键，切走就全失效」。
/// 这类问题肉眼不可见，所以启动时自检并明确提示。
/// </summary>
public static class Integrity
{
    private static int? _rid;

    /// <summary>Low = 0x1000，Medium = 0x2000，High = 0x3000。</summary>
    public static int Rid => _rid ??= Query();

    /// <summary>低于 Medium 就无法可靠观测其他程序的输入。</summary>
    public static bool IsRestricted => Rid < 0x2000;

    public static string Name => Rid switch
    {
        0x0000 => "Untrusted",
        0x1000 => "Low",
        0x2000 => "Medium",
        0x2100 => "Medium+",
        0x3000 => "High",
        0x4000 => "System",
        0x5000 => "Protected",
        _ => $"0x{Rid:X}"
    };

    private static int Query()
    {
        IntPtr token = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;

        try
        {
            if (!Native.OpenProcessToken(Native.GetCurrentProcess(), 0x0008, out token)) return -1;

            buffer = Marshal.AllocHGlobal(64);
            if (!Native.GetTokenInformation(token, 25, buffer, 64, out _)) return -1;

            IntPtr sid = Marshal.ReadIntPtr(buffer);
            byte count = Marshal.ReadByte(Native.GetSidSubAuthorityCount(sid));
            IntPtr sub = Native.GetSidSubAuthority(sid, count - 1);
            return Marshal.ReadInt32(sub);
        }
        catch
        {
            return -1;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (token != IntPtr.Zero) Native.CloseHandle(token);
        }
    }
}
