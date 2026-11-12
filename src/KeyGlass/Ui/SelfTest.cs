using System.IO;
using System.Text.Json;
using System.Windows;
using KeyGlass.Config;
using KeyGlass.Core;

namespace KeyGlass.Ui;

/// <summary>
/// 自检：不需要人看屏幕，用一组确定性检查跑通「配置 → 钩子 → 状态机 → 渲染」全链路，
/// 并把结果与各主题截图写到 artifacts/。验收脚本据此判定。
/// </summary>
public static class SelfTest
{
    public static int Run(AppConfig cfg, string outDir)
    {
        Directory.CreateDirectory(outDir);

        var report = new Dictionary<string, object>();
        bool allOk = true;

        // 1) 配置往返：保存 → 读回 → 关键字段一致
        bool configOk;
        try
        {
            var tmp = Path.Combine(outDir, "settings.roundtrip.json");
            bool saved = ConfigStore.TrySave(cfg, tmp);
            var loaded = ConfigStore.Load(tmp, out _);
            configOk = saved
                       && string.Equals(loaded.Theme, cfg.Theme, StringComparison.Ordinal)
                       && loaded.ChipCapacity == cfg.ChipCapacity
                       && Math.Abs(loaded.Scale - cfg.Scale) < 1e-9
                       && string.Equals(loaded.Anchor, cfg.Anchor, StringComparison.Ordinal);
        }
        catch
        {
            configOk = false;
        }
        report["configRoundtrip"] = configOk;
        allOk &= configOk;

        // 2) 键位映射覆盖
        int mapped = 0;
        for (int vk = 0; vk <= 0xFF; vk++)
        {
            if (Vk.Label(vk) is not null) mapped++;
        }
        report["vkLabelsMapped"] = mapped;
        report["vkLabelsOk"] = mapped >= 90;
        allOk &= mapped >= 90;

        // 3) 输入源初始化（Raw Input 为主，低级钩子为兼容备选）
        bool rawOk = false, hookOk = false;
        try
        {
            using var raw = new RawInputEngine();
            raw.Start(_ => { });
            rawOk = raw.KeyboardOk && raw.MouseOk;
            report["rawInputError"] = raw.LastError;
        }
        catch
        {
            // 下面如实记录
        }

        try
        {
            using var hook = new InputEngine(_ => { });
            hook.Start();
            Thread.Sleep(300);
            hookOk = hook.KeyboardOk && hook.MouseOk;
            report["hookError"] = hook.KeyboardHookError;
        }
        catch
        {
            // 忽略
        }

        report["rawInputOk"] = rawOk;
        report["lowLevelHookInstallOk"] = hookOk;

        bool pollOk = false;
        try
        {
            using var poll = new PollingEngine();
            poll.Start(_ => { });
            pollOk = poll.KeyboardOk && poll.MouseOk;
        }
        catch
        {
            // 记录失败即可
        }
        report["pollingEngineOk"] = pollOk;

        bool hybridOk = false;
        try
        {
            using var hybrid = new HybridEngine();
            hybrid.Start(_ => { });
            hybridOk = hybrid.KeyboardOk && hybrid.MouseOk;
        }
        catch
        {
            // 记录失败即可
        }
        report["hybridEngineOk"] = hybridOk;

        // 低级钩子「装得上但收不到事件」是安全软件拦截的典型表现，
        // 所以这里只把 Raw Input 作为硬性通过条件。
        allOk &= rawOk;

        // 4) 状态机：一条按键事件应产生一个键帽，松键后 linger 到期被回收
        var box = new ConfigBox(cfg.Clone());
        var store = new StateStore(box);
        var latency = new LatencyTracker();
        bool stateOk;
        try
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            store.Apply(new RawInput(InputKind.Key, 0x41, true, 0, now));
            bool created = store.Display.Count == 1;
            for (int i = 0; i < 6; i++) store.Advance(16);
            store.Apply(new RawInput(InputKind.Key, 0x41, false, 0, System.Diagnostics.Stopwatch.GetTimestamp()));
            for (int i = 0; i < 120; i++) store.Advance(16);
            bool expired = store.Display.Count == 0;
            stateOk = created && expired;
        }
        catch
        {
            stateOk = false;
        }
        report["stateMachineOk"] = stateOk;
        allOk &= stateOk;

        // 5) 渲染：每个主题各出一张 PNG，检查文件真的写出且不是空图
        var shots = new List<string>();
        try
        {
            string[] themes = ThemePalette.Names;
            foreach (var name in themes)
            {
                var c = cfg.Clone();
                c.Theme = name;
                c.ShowLatencyHud = true;
                c.Normalize();

                var shotBox = new ConfigBox(c);
                var shotStore = new StateStore(shotBox);
                shotStore.SeedDemo();

                var surface = new OverlaySurface(shotBox, shotStore, latency, ThemePalette.Create(name, false));
                surface.Measure(new Size(OverlaySurface.BaseWidth, OverlaySurface.BaseHeight));
                surface.Arrange(new Rect(0, 0, OverlaySurface.BaseWidth, OverlaySurface.BaseHeight));
                for (int i = 0; i < 20; i++) surface.Advance(16);

                var path = Path.Combine(outDir, $"preview-{name.ToLowerInvariant()}.png");
                Preview.Save(surface, path, withBackdrop: true);
                shots.Add(path);
            }
        }
        catch
        {
            allOk = false;
        }

        report["previews"] = shots;
        bool previewsOk = shots.Count == ThemePalette.Names.Length
                          && shots.All(p => File.Exists(p) && new FileInfo(p).Length > 8 * 1024);
        report["previewsOk"] = previewsOk;
        allOk &= previewsOk;

        report["ok"] = allOk;
        report["generatedAt"] = DateTime.Now.ToString("O");
        report["machine"] = Environment.MachineName;

        File.WriteAllText(Path.Combine(outDir, "selftest.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        return allOk ? 0 : 1;
    }
}
