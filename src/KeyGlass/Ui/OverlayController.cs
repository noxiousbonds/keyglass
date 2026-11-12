using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KeyGlass.Config;
using KeyGlass.Core;
using KeyGlass.Interop;

namespace KeyGlass.Ui;

/// <summary>
/// 总调度：钩子线程 → 队列 → UI 线程状态机 → 渲染面；外加托盘、快捷键、指标落盘。
/// </summary>
public sealed class OverlayController : IDisposable
{
    private const int HotkeyId = 0x4B47;

    /// <summary>第二个实例用它通知已经在跑的实例「把设置窗口打开」。</summary>
    private static readonly uint ShowSettingsMessage = Native.RegisterWindowMessage("KeyGlass.ShowSettings");

    public static void BroadcastShowSettings()
    {
        if (ShowSettingsMessage != 0)
        {
            Native.PostMessage(Native.HWND_BROADCAST, ShowSettingsMessage, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private readonly ConfigBox _box;
    private readonly string? _configPath;
    private readonly StateStore _store;
    private readonly LatencyTracker _latency = new();
    private IInputSource _engine;
    private readonly ConcurrentQueue<RawInput> _queue = new();

    private OverlayWindow? _window;
    private SettingsWindow? _settings;
    private TrayHost? _tray;
    private DemoDriver? _demo;
    private HwndSource? _hotkeySink;
    private DispatcherTimer? _metricsTimer;

    private int _wakeFlag;
    private bool _visible;
    private bool _disposed;
    private long _inputEvents;
    private long _lastLoggedInput;

    private TimeSpan _lastCpu;
    private DateTime _lastCpuAt;
    private DateTime _lastMetricsAt;
    private long _lastFrames;
    private readonly DateTime _startedAt = DateTime.Now;

    public AppConfig Config => _box.Value;
    public IInputSource Engine => _engine;
    public StateStore Store => _store;
    public LatencyTracker Latency => _latency;
    public bool IsVisible => _visible;

    public OverlayController(AppConfig config, string? configPath)
    {
        _box = new ConfigBox(config);
        _configPath = configPath;
        _store = new StateStore(_box);
        _engine = CreateEngine(config);
    }

    private static IInputSource CreateEngine(AppConfig cfg) => cfg.InputEngine switch
    {
        "RawInput" => new RawInputEngine(),
        "LowLevelHook" => new InputEngine(null, msg => Debug.WriteLine($"[KeyGlass] {msg}")),
        "Polling" => new PollingEngine(),
        _ => new HybridEngine()
    };

    private void RebuildEngine()
    {
        try
        {
            _engine.Dispose();
        }
        catch
        {
            // 切换输入源失败也不影响已运行的部分
        }

        _engine = CreateEngine(_box.Value);
        _engine.Start(OnRawInput);
    }

    // ------------------------------------------------------------ 生命周期

    public void Start(bool startHidden)
    {
        _engine.Start(OnRawInput);

        Diag.Write($"start v1.0.0 engine={_engine.Name} kbOk={_engine.KeyboardOk} msOk={_engine.MouseOk} " +
                   $"mode={_box.Value.CaptureMode} theme={_box.Value.Theme} scale={_box.Value.Scale} " +
                   $"anchor={_box.Value.Anchor} hideOnScreen={_box.Value.HideOnScreen} monitor={_box.Value.MonitorIndex} hitAltTab={_box.Value.HideFromAltTab} " +
                   $"logDir={AppPaths.LogDir}");

        Diag.Write($"process: exe={Environment.ProcessPath} integrity={Integrity.Name}");

        // 原始输入注册失败（极少见：老系统 / 服务会话）时自动退回低级钩子
        if (!_engine.KeyboardOk || !_engine.MouseOk)
        {
            Debug.WriteLine("[KeyGlass] raw input unavailable, falling back to low-level hooks");
            try { _engine.Dispose(); } catch { }
            _engine = new InputEngine(null, msg => Debug.WriteLine($"[KeyGlass] {msg}"));
            _engine.Start(OnRawInput);
        }

        CreateWindow();

        if (startHidden) Hide();
        else ShowOverlay();

        _tray = new TrayHost(
            () => _visible,
            ToggleVisible,
            OpenSettings,
            transparent => SetTransparent(transparent),
            () => _box.Value.IsTransparent,
            Exit,
            _box.Value.Hotkey);

        RegisterHotkey();
        StartMetrics();

        _tray.Notify("KeyGlass 已启动", $"{_box.Value.Hotkey} 显示 / 隐藏，双击托盘图标打开设置。");

        if (!_engine.KeyboardOk)
        {
            _tray.Notify("输入源未能启动", "请在安全软件中放行 KeyGlass，或到设置里切换输入引擎。");
        }
        if (Integrity.IsRestricted)
        {
            Diag.Write($"WARN: integrity={Integrity.Name}, input to other windows will be invisible");
            _tray.Notify("权限受限：可能收不到按键",
                $"KeyGlass 正以 {Integrity.Name} 完整性运行，只能看到自己窗口的输入。请把程序移到 C:\\Program Files\\KeyGlass 或桌面后运行。");
        }
    }

    private void CreateWindow()
    {
        var theme = ThemePalette.Create(_box.Value.Theme, _box.Value.IsTransparent);
        _window = new OverlayWindow(_box, _store, _latency, theme);
    }

    private void ShowOverlay()
    {
        if (_window is null) CreateWindow();
        _window!.Show();
        _window.PositionWindow();
        _visible = true;
        _window.Wake();
        Diag.Write("overlay shown");
    }

    public void Hide()
    {
        _window?.Hide();
        _visible = false;
        Diag.Write("overlay hidden");
    }

    public void ToggleVisible()
    {
        if (_visible) Hide();
        else ShowOverlay();
    }

    public void Exit()
    {
        ConfigStore.TrySave(_box.Value, _configPath);
        Dispose();
        Application.Current?.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _metricsTimer?.Stop();
        _metricsTimer = null;

        _demo?.Dispose();
        _demo = null;

        if (_hotkeySink is not null)
        {
            Native.UnregisterHotKey(_hotkeySink.Handle, HotkeyId);
            _hotkeySink.Dispose();
            _hotkeySink = null;
        }

        _settings?.Close();
        _settings = null;

        _window?.Close();
        _window = null;

        _tray?.Dispose();
        _tray = null;

        _engine.Dispose();
    }

    // ------------------------------------------------------------ 输入通路

    private void OnRawInput(RawInput e)
    {
        if (_disposed) return;
        Enqueue(e);
    }

    /// <summary>同一个入队口，钩子线程与演示驱动器都走这里。</summary>
    private void Enqueue(RawInput e)
    {
        Interlocked.Increment(ref _inputEvents);
        _queue.Enqueue(e);

        // 合并唤醒：1 万次按键也只排一次 Dispatcher 回调，杜绝消息队列被输入灌爆
        if (Interlocked.Exchange(ref _wakeFlag, 1) == 0)
        {
            var app = Application.Current;
            if (app is null) return;
            app.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(Drain));
        }
    }

    private void Drain()
    {
        bool changed = false;
        while (_queue.TryDequeue(out var e))
        {
            if (_store.Apply(e)) changed = true;
        }

        Interlocked.Exchange(ref _wakeFlag, 0);

        if (!_queue.IsEmpty && Interlocked.Exchange(ref _wakeFlag, 1) == 0)
        {
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(Drain));
        }

        if (changed) _window?.Wake();
    }

    // ------------------------------------------------------------ 配置

    public void ApplyConfig(AppConfig next, bool persist)
    {
        if (next is null) return;
        next.Normalize();

        var prev = _box.Value;
        bool needRebuild =
            !string.Equals(next.CaptureMode, prev.CaptureMode, StringComparison.OrdinalIgnoreCase) ||
            next.HideFromAltTab != prev.HideFromAltTab;
        bool engineChanged = !string.Equals(next.InputEngine, prev.InputEngine, StringComparison.OrdinalIgnoreCase);

        _box.Value = next;

        if (persist) ConfigStore.TrySave(next, _configPath);

        if (engineChanged) RebuildEngine();
        if (needRebuild) RebuildWindow();
        else _window?.UpdateChrome();
    }

    private void RebuildWindow()
    {
        Diag.Write($"rebuild window (mode={_box.Value.CaptureMode} hideAltTab={_box.Value.HideFromAltTab} visible={_visible})");
        bool wasVisible = _visible;
        if (_window is not null)
        {
            _window.Close();
            _window = null;
        }

        CreateWindow();

        if (wasVisible)
        {
            _window!.Show();
            _window.PositionWindow();
            _visible = true;
            _window.Wake();
        }
    }

    public void SetTransparent(bool transparent)
    {
        var cfg = _box.Value.Clone();
        cfg.CaptureMode = transparent ? "Transparent" : "Chroma";
        cfg.Normalize();
        ApplyConfig(cfg, persist: true);
    }

    public void OpenSettings()
    {
        Diag.Write("open settings");
        if (_settings is not null)
        {
            // 从后台/最小化拉回前台：Topmost 抖动一下可以突破前台窗口限制
            if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
            _settings.Topmost = true;
            _settings.Activate();
            _settings.Topmost = false;
            return;
        }

        _settings = new SettingsWindow(this);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    // ------------------------------------------------------------ 快捷键

    private void RegisterHotkey()
    {
        try
        {
            var parameters = new HwndSourceParameters("KeyGlassHotkeySink")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ExtendedWindowStyle = 0
            };

            _hotkeySink = new HwndSource(parameters);
            _hotkeySink.AddHook(HotkeyProc);

            if (!TryParseHotkey(_box.Value.Hotkey, out uint mods, out uint vk))
            {
                mods = Native.MOD_CONTROL | Native.MOD_ALT;
                vk = 0x4B; // K
            }

            Native.RegisterHotKey(_hotkeySink.Handle, HotkeyId, mods | Native.MOD_NOREPEAT, vk);
        }
        catch
        {
            // 快捷键注册失败不影响主流程（可继续用托盘）
        }
    }

    private IntPtr HotkeyProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            ToggleVisible();
            handled = true;
        }

        // 再次双击 exe（第二个实例）→ 直接把设置窗口带到前台
        if (ShowSettingsMessage != 0 && (uint)msg == ShowSettingsMessage)
        {
            OpenSettings();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static bool TryParseHotkey(string? text, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    mods |= Native.MOD_CONTROL;
                    break;
                case "alt":
                    mods |= Native.MOD_ALT;
                    break;
                case "shift":
                    mods |= Native.MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    mods |= Native.MOD_WIN;
                    break;
                default:
                    if (Enum.TryParse<Key>(raw, ignoreCase: true, out var key))
                    {
                        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    }
                    else if (raw.Length == 1 && char.IsLetterOrDigit(raw[0]))
                    {
                        vk = (uint)char.ToUpperInvariant(raw[0]);
                    }
                    break;
            }
        }

        return vk != 0 && mods != 0;
    }

    // ------------------------------------------------------------ 开发/验收辅助

    public void SeedDemo()
    {
        _store.SeedDemo();
        _window?.Wake();
    }

    /// <summary>--demo：持续模拟输入，便于真机看动画 / 截图。</summary>
    public void StartDemo()
    {
        _demo ??= new DemoDriver(Enqueue);
        _demo.Start();
    }

    public void Snapshot(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var stem = Path.GetFileNameWithoutExtension(full);
            _window?.SavePng(Path.Combine(dir ?? ".", stem + ".png"));
            if (_window is not null)
            {
                Preview.Save(_window.Surface, Path.Combine(dir ?? ".", stem + ".preview.png"));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KeyGlass] snapshot failed: {ex}");
        }

        WriteMetricsNow();
    }

    private void StartMetrics()
    {
        var proc = Process.GetCurrentProcess();
        _lastCpu = proc.TotalProcessorTime;
        _lastCpuAt = DateTime.UtcNow;

        _metricsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _metricsTimer.Tick += (_, _) => WriteMetricsNow();
        _metricsTimer.Start();

        WriteMetricsNow();
    }

    public void WriteMetricsNow()
    {
        try
        {
            var proc = Process.GetCurrentProcess();
            var now = DateTime.UtcNow;
            var cpuDelta = proc.TotalProcessorTime - _lastCpu;
            var wall = now - _lastCpuAt;
            double cpuPct = wall.TotalMilliseconds > 1e-3
                ? cpuDelta.TotalMilliseconds / wall.TotalMilliseconds / Environment.ProcessorCount * 100.0
                : 0;

            _lastCpu = proc.TotalProcessorTime;
            _lastCpuAt = now;

            _latency.Snapshot(out double p50, out double p95, out double max, out double avg);

            long frames = _window?.Surface.Frames ?? 0;
            double renderMs = _window?.Surface.AverageRenderMs ?? 0;
            var nowWall = DateTime.UtcNow;
            double fps = _lastMetricsAt == default
                ? 0
                : (frames - _lastFrames) / Math.Max(0.001, (nowWall - _lastMetricsAt).TotalSeconds);
            _lastFrames = frames;
            _lastMetricsAt = nowWall;

            long inputs = Interlocked.Read(ref _inputEvents);
            long inputDelta = inputs - _lastLoggedInput;
            _lastLoggedInput = inputs;

            var dto = new
            {
                timestampUtc = now.ToString("O"),
                uptimeSeconds = Math.Round((DateTime.Now - _startedAt).TotalSeconds, 1),
                cpuPercentOfMachine = Math.Round(cpuPct, 2),
                workingSetMb = Math.Round(proc.WorkingSet64 / 1024.0 / 1024.0, 1),
                privateMb = Math.Round(proc.PrivateMemorySize64 / 1024.0 / 1024.0, 1),
                threads = proc.Threads.Count,
                handleCount = proc.HandleCount,
                latencyP50Ms = Math.Round(p50, 2),
                latencyP95Ms = Math.Round(p95, 2),
                latencyMaxMs = Math.Round(max, 2),
                latencyAvgMs = Math.Round(avg, 2),
                latencySamples = _latency.Count,
                kps = Math.Round(_store.Kps, 1),
                cps = Math.Round(_store.Cps, 1),
                totalKeys = _store.TotalKeys,
                totalClicks = _store.TotalClicks,
                captureMode = _box.Value.CaptureMode,
                theme = _box.Value.Theme,
                overlayVisible = _visible,
                inputEngine = _engine.Name,
                integrity = Integrity.Name,
                keyboardHookOk = _engine.KeyboardOk,
                mouseHookOk = _engine.MouseOk,
                monitorIndex = _box.Value.MonitorIndex,
                scale = _box.Value.Scale,
                renderTier = RenderCapability.Tier >> 16,
                animating = _window?.IsAnimating ?? false,
                framesPerSecond = Math.Round(fps, 1),
                surfaceRenderMsAvg = Math.Round(renderMs, 3),
                inputEventCount = inputs,
                foreground = ForegroundInfo(),
                logDir = AppPaths.LogDir
            };

            string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });

            // 1) 测试脚本用的位置：%TEMP%\KeyGlass\metrics.json（写不进去就跳过）
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "KeyGlass");
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(Path.Combine(tempDir, "metrics.json"), json);
            }
            catch
            {
                // 受限环境常见：继续写第二个位置
            }

            // 2) 可靠位置：logs\metrics.json（与 diag.log 同目录）
            try
            {
                var logDir = AppPaths.LogDir;
                Directory.CreateDirectory(logDir);
                File.WriteAllText(Path.Combine(logDir, "metrics.json"), json);
            }
            catch
            {
                // 都不行就只能靠 diag.log
            }

            Diag.Write($"hb keys={_store.TotalKeys} clicks={_store.TotalClicks} input={inputs}(+{inputDelta}) " +
                       $"fps={fps:0.0} render={renderMs:0.00}ms visible={_visible} win={_window?.IsVisible} " +
                       $"anim={_window?.IsAnimating} settings={_settings is not null} fg={ForegroundInfo()}");
        }
        catch
        {
            // 指标落盘失败绝不能影响叠加层
        }
    }

    /// <summary>当前前台窗口（进程名|标题），用于判断「输入到底是不是在后台丢的」。</summary>
    private static string ForegroundInfo()
    {
        try
        {
            var h = Native.GetForegroundWindow();
            if (h == IntPtr.Zero) return "none";

            int len = Native.GetWindowTextLength(h);
            var sb = new System.Text.StringBuilder(Math.Max(4, len + 1));
            Native.GetWindowText(h, sb, sb.Capacity);

            Native.GetWindowThreadProcessId(h, out uint pid);
            string proc;
            try
            {
                proc = Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                proc = "?";
            }

            var title = sb.ToString();
            if (title.Length > 36) title = title.Substring(0, 36) + "…";
            return $"{proc}|{title}";
        }
        catch
        {
            return "?";
        }
    }
}
