using System.Runtime.InteropServices;
using System.Windows.Interop;
using KeyGlass.Interop;

namespace KeyGlass.Core;

/// <summary>
/// 原始输入（Raw Input）引擎 —— 本项目的主输入源。
///
/// 为什么不用低级钩子做主方案（实测结论）：
///  - 装 360 / 火绒 / 部分 EDR 的机器上，SetWindowsHookEx 会返回成功，
///    但回调永远不来（安全软件静默拦截键盘钩子）。本机实测：钩子装得上、事件 0 条；
///    同一时刻 RegisterHotKey 能收到键，说明输入是通的，被拦的只是钩子。
///  - 低级钩子有 300ms 超时摘钩风险，且容易被反作弊游戏判为可疑。
///  - Raw Input 是普通设备注册，不拦截、不注入、不提权，安全软件不拦，反作弊也友好。
///
/// 实现要点：
///  - 隐藏窗口 + RIDEV_INPUTSINK：即使 KeyGlass 不在前台也能收到全局输入。
///  - 直接在 WM_INPUT 里解析，不进钩子链，所以没有系统级超时约束。
///  - 只订阅键盘(0x01/0x06)与鼠标(0x01/0x02)两个使用页。
/// </summary>
public sealed class RawInputEngine : IInputSource
{
    private const int UsagePageGenericDesktop = 0x01;
    private const int UsageKeyboard = 0x06;
    private const int UsageMouse = 0x02;

    private readonly IntPtr _nativeBuffer = Marshal.AllocHGlobal(64 * 1024);
    private IntPtr _sourceHandle;
    private HwndSource? _source;
    private Action<RawInput>? _sink;
    private byte[] _buffer = new byte[512];

    public string Name => "RawInput";
    public bool KeyboardOk { get; private set; }
    public bool MouseOk { get; private set; }
    public int LastError { get; private set; }

    public void Start(Action<RawInput> sink)
    {
        _sink = sink;

        var parameters = new HwndSourceParameters("KeyGlassRawInputSink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _sourceHandle = _source.Handle;
        _source.AddHook(WndProc);

        var devices = new[]
        {
            new Native.RAWINPUTDEVICE
            {
                usUsagePage = UsagePageGenericDesktop,
                usUsage = UsageKeyboard,
                dwFlags = Native.RIDEV_INPUTSINK,
                hwndTarget = _source.Handle
            },
            new Native.RAWINPUTDEVICE
            {
                usUsagePage = UsagePageGenericDesktop,
                usUsage = UsageMouse,
                dwFlags = Native.RIDEV_INPUTSINK,
                hwndTarget = _source.Handle
            }
        };

        bool ok = Native.RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<Native.RAWINPUTDEVICE>());
        LastError = ok ? 0 : Marshal.GetLastWin32Error();

        KeyboardOk = ok;
        MouseOk = ok;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)Native.WM_INPUT)
        {
            handled = true;
            ReadAndDispatch(lParam);
        }

        return IntPtr.Zero;
    }

    private void ReadAndDispatch(IntPtr rawInputHandle)
    {
        uint headerSize = (uint)Marshal.SizeOf<Native.RAWINPUTHEADER>();
        uint size = 0;

        // 第一遍取长度（pData = NULL 时返回值固定为 0，靠 size 带出所需长度）
        Native.GetRawInputData(rawInputHandle, Native.RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0 || size > 64 * 1024) return;

        if (_buffer.Length < size) _buffer = new byte[size];

        long ts = System.Diagnostics.Stopwatch.GetTimestamp();

        uint copied = Native.GetRawInputData(rawInputHandle, Native.RID_INPUT, _nativeBuffer, ref size, headerSize);
        if (copied != size) return;

        Marshal.Copy(_nativeBuffer, _buffer, 0, (int)size);

        int dataOffset = (int)headerSize;
        if (_buffer.Length < dataOffset + 16) return;

        uint type = BitConverter.ToUInt32(_buffer, 0);
        if (type == Native.RIM_TYPEKEYBOARD)
        {
            DispatchKeyboard(dataOffset, ts);
        }
        else if (type == Native.RIM_TYPEMOUSE)
        {
            DispatchMouse(dataOffset, ts);
        }
    }

    private void DispatchKeyboard(int offset, long ts)
    {
        ushort makeCode = BitConverter.ToUInt16(_buffer, offset);
        ushort flags = BitConverter.ToUInt16(_buffer, offset + 2);
        ushort vk = BitConverter.ToUInt16(_buffer, offset + 6);
        uint message = BitConverter.ToUInt32(_buffer, offset + 8);

        bool down = message is 0x0100 or 0x0104        // WM_KEYDOWN / WM_SYSKEYDOWN
                    || (flags & Native.RI_KEY_BREAK) == 0;

        // 多媒体键常给出 VK 0xFF，用扫描码反查
        if (vk == 0 || vk == 0xFF)
        {
            uint scan = makeCode;
            if ((flags & Native.RI_KEY_E0) != 0) scan |= 0xE000;
            uint mapped = Native.MapVirtualKey(scan, Native.MAPVK_VSC_TO_VK_EX);
            if (mapped != 0) vk = (ushort)mapped;
        }

        if (vk == 0) return;

        _sink?.Invoke(new RawInput(InputKind.Key, vk, down, 0, ts));
    }

    private void DispatchMouse(int offset, long ts)
    {
        ushort buttonFlags = BitConverter.ToUInt16(_buffer, offset + 4);
        short buttonData = BitConverter.ToInt16(_buffer, offset + 6);

        if ((buttonFlags & Native.RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
            _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.Left, true, 0, ts));
        if ((buttonFlags & Native.RI_MOUSE_LEFT_BUTTON_UP) != 0)
            _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.Left, false, 0, ts));
        if ((buttonFlags & Native.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
            _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.Right, true, 0, ts));
        if ((buttonFlags & Native.RI_MOUSE_RIGHT_BUTTON_UP) != 0)
            _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.Right, false, 0, ts));
        if ((buttonFlags & Native.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
            _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.Middle, true, 0, ts));
        if ((buttonFlags & Native.RI_MOUSE_MIDDLE_BUTTON_UP) != 0)
            _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.Middle, false, 0, ts));
        if ((buttonFlags & Native.RI_MOUSE_BUTTON_4_DOWN) != 0)
            _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.X1, true, 0, ts));
        if ((buttonFlags & Native.RI_MOUSE_BUTTON_4_UP) != 0)
            _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.X1, false, 0, ts));
        if ((buttonFlags & Native.RI_MOUSE_BUTTON_5_DOWN) != 0)
            _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.X2, true, 0, ts));
        if ((buttonFlags & Native.RI_MOUSE_BUTTON_5_UP) != 0)
            _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.X2, false, 0, ts));

        if ((buttonFlags & Native.RI_MOUSE_WHEEL) != 0)
        {
            int code = buttonData > 0 ? MouseCodes.WheelUp : MouseCodes.WheelDown;
            _sink?.Invoke(new RawInput(InputKind.Wheel, code, true, buttonData, ts));
        }

        if ((buttonFlags & Native.RI_MOUSE_HWHEEL) != 0)
        {
            int code = buttonData > 0 ? MouseCodes.WheelRight : MouseCodes.WheelLeft;
            _sink?.Invoke(new RawInput(InputKind.HWheel, code, true, buttonData, ts));
        }
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }

        _sink = null;
    }
}
