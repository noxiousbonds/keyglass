using System.Runtime.InteropServices;
using System.Windows;
using KeyGlass.Interop;

namespace KeyGlass.Ui;

/// <summary>
/// 显示器枚举。全部返回**物理像素**（进程声明 PerMonitorV2，不会被系统 DPI 虚拟化）。
/// </summary>
public static class MonitorHelper
{
    public sealed record MonitorInfo(int Index, int Left, int Top, int Width, int Height, bool Primary)
    {
        public int Right => Left + Width;
        public int Bottom => Top + Height;
        public override string ToString() =>
            $"{Index}: {Width}×{Height} @({Left},{Top}){(Primary ? " 主屏" : "")}";
    }

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var rects = new List<(Native.RECT Rect, uint Flags)>();

        try
        {
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data) =>
                {
                    var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
                    if (Native.GetMonitorInfo(hMonitor, ref mi))
                    {
                        rects.Add((mi.rcMonitor, mi.dwFlags));
                    }
                    return true;
                },
                IntPtr.Zero);
        }
        catch
        {
            // 枚举失败时用 WPF 的主屏信息兜底
        }

        if (rects.Count == 0)
        {
            return new[]
            {
                new MonitorInfo(0, 0, 0,
                    (int)SystemParameters.PrimaryScreenWidth,
                    (int)SystemParameters.PrimaryScreenHeight,
                    true)
            };
        }

        var ordered = rects
            .OrderBy(r => r.Rect.Left)
            .ThenBy(r => r.Rect.Top)
            .ToList();

        var result = new List<MonitorInfo>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            var r = ordered[i].Rect;
            bool primary = (ordered[i].Flags & Native.MONITORINFOF_PRIMARY) != 0;
            result.Add(new MonitorInfo(i, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, primary));
        }

        return result;
    }

    public static MonitorInfo GetMonitor(int index)
    {
        var all = GetMonitors();
        return all[Math.Clamp(index, 0, all.Count - 1)];
    }

    public static int Count => GetMonitors().Count;
}
