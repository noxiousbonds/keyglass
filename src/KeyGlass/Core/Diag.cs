using System.IO;
using System.Text;

namespace KeyGlass.Core;

/// <summary>
/// 轻量诊断日志（滚动，单文件上限 ~512KB）。
/// 记录：启动参数/输入源、每 5 秒的心跳（输入计数、帧率、窗口可见性、前台进程）、
/// 以及热键/设置窗/窗口重建等关键动作。排查「按键突然不生效」时先看这个文件。
/// </summary>
public static class Diag
{
    private static readonly object Gate = new();
    private static string? _path;
    private static long _written;

    public static string FilePath => _path ??= Path.Combine(Config.AppPaths.LogDir, "diag.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                if (_written > 512 * 1024)
                {
                    try
                    {
                        var bak = FilePath + ".1";
                        if (File.Exists(bak)) File.Delete(bak);
                        File.Move(FilePath, bak);
                        _written = 0;
                    }
                    catch
                    {
                        // 滚动失败就继续追加
                    }
                }

                var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}";
                File.AppendAllText(FilePath, line, Encoding.UTF8);
                _written += line.Length;
            }
        }
        catch
        {
            // 日志失败绝不能影响主流程
        }
    }
}
