using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyGlass.Config;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyGlass");

    public static string DefaultPath => Path.Combine(DefaultDirectory, "settings.json");

    public static AppConfig Load(string? path, out bool created)
    {
        created = false;
        path ??= DefaultPath;

        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
                if (cfg != null)
                {
                    cfg.Normalize();
                    return cfg;
                }
            }
            else
            {
                created = true;
            }
        }
        catch (Exception)
        {
            // 配置损坏不应阻止程序启动：回到默认值，下次保存时覆盖。
        }

        var fresh = new AppConfig();
        fresh.Normalize();
        if (created) TrySave(fresh, path);
        return fresh;
    }

    public static bool TrySave(AppConfig cfg, string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(cfg, Options));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
