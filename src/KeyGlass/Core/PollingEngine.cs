using System.Diagnostics;
using KeyGlass.Interop;

namespace KeyGlass.Core;

/// <summary>
/// 轮询型输入源（兼容模式 2）。
///
/// 存在理由：某些机器上低级钩子被安全软件静默拦截、Raw Input 在后台可能收不到
/// 设备输入。轮询 <c>GetAsyncKeyState</c> 不依赖钩子、不依赖窗口焦点、也不需要任何
/// 驱动或权限，是最后一条兜底路径。
///
/// 代价（如实记录）：
///  - 采样间隔约 8ms：比事件驱动多 0~8ms 延迟，且短于 8ms 的极速点按可能漏掉；
///  - 看不到横向/纵向滚轮（滚轮没有「状态」，只有事件）；
///  - 需要轮询约 150 个常用 VK，实测代价见 docs/交付文档.md。
/// </summary>
public sealed class PollingEngine : IInputSource
{
    private static readonly int[] VirtualKeys = BuildKeyList();

    private Thread? _thread;
    private Action<RawInput>? _sink;
    private volatile bool _stop;

    public string Name => "Polling";
    public bool KeyboardOk { get; private set; }
    public bool MouseOk { get; private set; }
    public bool SupportsWheel => false;

    public void Start(Action<RawInput> sink)
    {
        _sink = sink;
        _stop = false;

        // 默认系统定时器精度 15.6ms，提到 1ms 才能真的 6ms 采样一次
        Native.TimeBeginPeriod(1);
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "KeyGlass.Poll",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();

        // 轮询没有「安装失败」这种状态，线程起来就算可用
        KeyboardOk = true;
        MouseOk = true;
        Diag.Write($"polling: thread started, keys={VirtualKeys.Length}");
    }

    private void Run()
    {
        try
        {
            RunCore();
        }
        catch (Exception ex)
        {
            Diag.Write($"polling: thread died: {ex}");
        }
    }

    private void RunCore()
    {
        // 关键：先建立线程消息队列。实测没有消息队列的线程里 GetAsyncKeyState 会一直返回 0
        Native.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);

        var previous = new bool[VirtualKeys.Length];

        // 启动瞬间已按住的键不算“按下”，避免开局刷一排键帽
        for (int i = 0; i < VirtualKeys.Length; i++)
        {
            previous[i] = IsDown(VirtualKeys[i]);
        }

        long ticks = 0;
        int emitted = 0;

        while (!_stop)
        {
            for (int i = 0; i < VirtualKeys.Length; i++)
            {
                bool down = IsDown(VirtualKeys[i]);
                if (down == previous[i]) continue;

                previous[i] = down;
                Emit(VirtualKeys[i], down);
                emitted++;
            }

            ticks++;
            if (ticks % 1000 == 0)
            {
                Diag.Write($"polling: ticks={ticks} emitted={emitted}");
            }

            Thread.Sleep(6);
        }
    }

    private static bool IsDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    private void Emit(int vk, bool down)
    {
        long ts = Stopwatch.GetTimestamp();

        switch (vk)
        {
            case 0x01: _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.Left, down, 0, ts)); return;
            case 0x02: _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.Right, down, 0, ts)); return;
            case 0x04: _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.Middle, down, 0, ts)); return;
            case 0x05: _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.X1, down, 0, ts)); return;
            case 0x06: _sink?.Invoke(new RawInput(InputKind.MouseButton, MouseCodes.X2, down, 0, ts)); return;
        }

        _sink?.Invoke(new RawInput(InputKind.Key, vk, down, 0, ts));
    }

    private static int[] BuildKeyList()
    {
        var list = new List<int>(160);

        // 鼠标五键
        list.AddRange(new[] { 0x01, 0x02, 0x04, 0x05, 0x06 });

        // 控制键
        list.AddRange(new[] { 0x08, 0x09, 0x0D, 0x13, 0x14, 0x1B, 0x20, 0x2C, 0x2D, 0x2E, 0x90, 0x91 });

        // 方向 / 导航
        for (int vk = 0x21; vk <= 0x28; vk++) list.Add(vk);

        // 字母、数字
        for (int vk = 0x30; vk <= 0x39; vk++) list.Add(vk);
        for (int vk = 0x41; vk <= 0x5A; vk++) list.Add(vk);

        // Win / Menu
        list.AddRange(new[] { 0x5B, 0x5C, 0x5D });

        // 小键盘
        for (int vk = 0x60; vk <= 0x6F; vk++) list.Add(vk);

        // F1–F24
        for (int vk = 0x70; vk <= 0x87; vk++) list.Add(vk);

        // 修饰键（左右分开，才能点亮对应胶囊）
        for (int vk = 0xA0; vk <= 0xA5; vk++) list.Add(vk);
        list.AddRange(new[] { 0x10, 0x11, 0x12 });

        // 媒体键
        for (int vk = 0xAD; vk <= 0xB3; vk++) list.Add(vk);

        // 标点区
        for (int vk = 0xBA; vk <= 0xDF; vk++) list.Add(vk);

        return list.Distinct().ToArray();
    }

    public void Dispose()
    {
        _stop = true;
        try
        {
            _thread?.Join(300);
        }
        catch
        {
            // ignore
        }
        try { Native.TimeEndPeriod(1); } catch { }


        _sink = null;
    }
}
