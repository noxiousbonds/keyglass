using System.IO;

namespace KeyGlass.Config;

/// <summary>
/// 可写路径兜底：有些机器的 %TEMP% / %LOCALAPPDATA% 被策略或安全软件限制，
/// 日志与指标写不进去，排查就变成瞎子。这里按「exe 同目录 → LocalAppData → Temp」
/// 依次探测，取第一个真正可写的目录。
/// </summary>
public static class AppPaths
{
    private static string? _logDir;

    public static string LogDir => _logDir ??= Resolve();

    private static string Resolve()
    {
        var candidates = new List<string>();

        try
        {
            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir)) candidates.Add(Path.Combine(baseDir, "logs"));
        }
        catch
        {
            // ignore
        }

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyGlass", "logs"));
        candidates.Add(Path.Combine(Path.GetTempPath(), "KeyGlass", "logs"));

        foreach (var dir in candidates)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".write-probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return dir;
            }
            catch
            {
                // 换下一个
            }
        }

        return Path.GetTempPath();
    }
}
