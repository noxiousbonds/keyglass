using System.Diagnostics;
using System.Runtime.InteropServices;
using KeyGlass.Interop;

namespace KeyGlass.Core;

/// <summary>
/// 全局低级钩子引擎。
///
/// 设计要点：
/// 1. 钩子装在一条**独立的高优先级线程**上，并用 GetMessage 消息泵保活；
///    绝不在 UI 线程安装钩子，避免 UI 卡顿触发系统自动摘钩。
/// 2. 回调里只做「读时间戳 + 入队」，任何绘制/加锁/日志都不做，保证回调耗时 &lt; 50µs，
///    远低于 Windows 的 300ms LowLevelHooksTimeout 容忍线。
/// 3. **永远 CallNextHookEx，永不返回 1**：本程序不拦截、不改写、不丢弃任何系统输入。
/// 4. 钩子句柄与委托都保持强引用，防止 GC 回收导致进程崩溃。
/// </summary>
public sealed class InputEngine : IInputSource
{
    private readonly Action<string>? _log;
    private Action<RawInput> _sink;

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _kbHook;
    private IntPtr _msHook;
    private Native.HookProc? _kbProc;
    private Native.HookProc? _msProc;
    private volatile bool _disposed;

    public string Name => "LowLevelHook";

    public bool KeyboardOk { get; private set; }
    public bool MouseOk { get; private set; }
    public int KeyboardHookError { get; private set; }
    public int MouseHookError { get; private set; }
    public bool IsRunning => _thread is { IsAlive: true };

    public InputEngine(Action<RawInput>? sink = null, Action<string>? log = null)
    {
        _sink = sink ?? (_ => { });
        _log = log;
    }

    public void Start(Action<RawInput> sink)
    {
        _sink = sink;
        Start();
    }

    public void Start()
    {
        if (_thread != null) return;

        _thread = new Thread(RunHookThread)
        {
            IsBackground = true,
            Name = "KeyGlass.Hook",
            Priority = ThreadPriority.Highest
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void RunHookThread()
    {
        _threadId = Native.GetCurrentThreadId();

        // 低级钩子可以传 NULL 模块句柄（系统会绑定到当前进程的 user32）。
        _kbProc = KeyboardProc;
        _msProc = MouseProc;

        _kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc, Native.GetModuleHandle(null), 0);
        KeyboardOk = _kbHook != IntPtr.Zero;
        if (!KeyboardOk) KeyboardHookError = Marshal.GetLastWin32Error();

        _msHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _msProc, Native.GetModuleHandle(null), 0);
        MouseOk = _msHook != IntPtr.Zero;
        if (!MouseOk) MouseHookError = Marshal.GetLastWin32Error();

        _log?.Invoke($"hooks: keyboard={KeyboardOk}({KeyboardHookError}) mouse={MouseOk}({MouseHookError})");

        if (!KeyboardOk && !MouseOk) return;

        // 消息泵：低级钩子必须在装钩线程持续泵消息才会被回调。
        while (!_disposed && Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Native.TranslateMessage(ref msg);
            Native.DispatchMessage(ref msg);
        }
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            long ts = Stopwatch.GetTimestamp();
            var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;

            bool down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            _sink(new RawInput(InputKind.Key, (int)info.vkCode, down, 0, ts));
        }

        return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            long ts = Stopwatch.GetTimestamp();
            var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;

            switch (msg)
            {
                case Native.WM_LBUTTONDOWN:
                    _sink(new RawInput(InputKind.MouseButton, MouseCodes.Left, true, 0, ts));
                    break;
                case Native.WM_LBUTTONUP:
                    _sink(new RawInput(InputKind.MouseButton, MouseCodes.Left, false, 0, ts));
                    break;
                case Native.WM_RBUTTONDOWN:
                    _sink(new RawInput(InputKind.MouseButton, MouseCodes.Right, true, 0, ts));
                    break;
                case Native.WM_RBUTTONUP:
                    _sink(new RawInput(InputKind.MouseButton, MouseCodes.Right, false, 0, ts));
                    break;
                case Native.WM_MBUTTONDOWN:
                    _sink(new RawInput(InputKind.MouseButton, MouseCodes.Middle, true, 0, ts));
                    break;
                case Native.WM_MBUTTONUP:
                    _sink(new RawInput(InputKind.MouseButton, MouseCodes.Middle, false, 0, ts));
                    break;
                case Native.WM_XBUTTONDOWN:
                {
                    int btn = (int)((info.mouseData >> 16) & 0xFFFF) == 1 ? MouseCodes.X1 : MouseCodes.X2;
                    _sink(new RawInput(InputKind.MouseButton, btn, true, 0, ts));
                    break;
                }
                case Native.WM_XBUTTONUP:
                {
                    int btn = (int)((info.mouseData >> 16) & 0xFFFF) == 1 ? MouseCodes.X1 : MouseCodes.X2;
                    _sink(new RawInput(InputKind.MouseButton, btn, false, 0, ts));
                    break;
                }
                case Native.WM_MOUSEWHEEL:
                {
                    int delta = (short)((info.mouseData >> 16) & 0xFFFF);
                    _sink(new RawInput(InputKind.Wheel, delta > 0 ? MouseCodes.WheelUp : MouseCodes.WheelDown, true, delta, ts));
                    break;
                }
                case Native.WM_MOUSEHWHEEL:
                {
                    int delta = (short)((info.mouseData >> 16) & 0xFFFF);
                    _sink(new RawInput(InputKind.HWheel, delta > 0 ? MouseCodes.WheelRight : MouseCodes.WheelLeft, true, delta, ts));
                    break;
                }
            }
        }

        return Native.CallNextHookEx(_msHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_threadId != 0)
        {
            Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        try
        {
            _thread?.Join(1500);
        }
        catch
        {
            // 退出路径上的异常不外抛
        }

        if (_kbHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_kbHook);
        if (_msHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_msHook);
        _kbHook = IntPtr.Zero;
        _msHook = IntPtr.Zero;
    }
}

public static class MouseCodes
{
    public const int Left = 1;
    public const int Right = 2;
    public const int Middle = 3;
    public const int X1 = 4;
    public const int X2 = 5;
    public const int WheelUp = 11;
    public const int WheelDown = 12;
    public const int WheelLeft = 13;
    public const int WheelRight = 14;

    public static string Label(int code) => code switch
    {
        Left => "L",
        Right => "R",
        Middle => "M",
        X1 => "4",
        X2 => "5",
        _ => "?"
    };
}
