using System.IO;
using System.Windows;
using System.Windows.Threading;
using KeyGlass.Config;
using KeyGlass.Ui;

namespace KeyGlass;

public partial class App : Application
{
    private OverlayController? _controller;
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var args = e.Args;
        string? configPath = ArgValue(args, "--config");
        var cfg = ConfigStore.Load(configPath, out bool created);

        if (HasFlag(args, "--selftest"))
        {
            string outDir = ArgValue(args, "--out") ?? Path.Combine(Environment.CurrentDirectory, "artifacts");
            Environment.ExitCode = SelfTest.Run(cfg, outDir);
            Shutdown(Environment.ExitCode);
            return;
        }

        string? snapshot = ArgValue(args, "--snapshot");
        bool demo = HasFlag(args, "--demo");

        // 正常启动时只允许一个实例；开发用参数（--demo/--snapshot）放行，方便脚本化验证
        if (snapshot is null && !demo)
        {
            _singleInstance = new Mutex(true, @"Local\KeyGlass.SingleInstance", out bool isNew);
            if (!isNew)
            {
                // 已经在跑：让那个实例把设置窗口打开，然后自己退出。
                // 这样即使托盘图标被 Windows 收进溢出区，双击 exe 也能找到设置。
                OverlayController.BroadcastShowSettings();
                Shutdown();
                return;
            }
        }

        bool hidden = HasFlag(args, "--hidden") || cfg.StartHidden;

        _controller = new OverlayController(cfg, configPath);
        _controller.Start(hidden);

        if (demo)
        {
            _controller.StartDemo();
        }

        if (snapshot is not null)
        {
            // 给布局/DPI 一点稳定时间再截图
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _controller?.Snapshot(snapshot);
                Shutdown();
            };
            timer.Start();
        }
        else if (created && !HasFlag(args, "--quiet"))
        {
            // 首次运行直接打开设置，降低上手成本
            _controller.OpenSettings();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _controller = null;
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

    /// <summary>
    /// 叠加层是「一直在跑的小程序」：单帧异常不应该让主播的画面直接消失。
    /// 这里记录日志后吞掉，交由下一次交互自愈。
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "KeyGlass");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"),
                $"{DateTime.Now:O}\t{e.Exception}\r\n");
        }
        catch
        {
            // 日志写不进去也不能再抛
        }

        e.Handled = true;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? ArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
