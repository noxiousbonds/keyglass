using KeyGlass.Interop;
using System.Diagnostics;

namespace KeyGlass.Core;

/// <summary>
/// 混合输入源（默认）：Raw Input 与轮询同时工作，互为兜底。
///
/// 为什么需要它：在本机实测发现，<c>RIDEV_INPUTSINK</c> 只能在 KeyGlass 处于前台时
/// 拿到键盘输入 —— 设置窗口一关（应用退到后台）就一条事件都没有；而低级钩子又被
/// 安全软件静默拦截。轮询 <c>GetAsyncKeyState</c> 不依赖前台状态，因此把它和 Raw Input
/// 并联：Raw Input 负责滚轮与前台低延迟，轮询负责后台键鼠兜底。
///
/// 去重：两个源会同时报同一个按键。这里按 (键种, 键码, 按下/抬起) 记录最近一次来源，
/// 只有「不同来源 + 150ms 内」的重复才丢弃，同源的快速连按不受影响。
/// </summary>
public sealed class HybridEngine : IInputSource
{
    private static readonly long TicksPerMs = Math.Max(1, Stopwatch.Frequency / 1000);

    private readonly PollingEngine _poll = new();
    private readonly RawInputEngine _raw = new();
    private readonly object _gate = new();
    private readonly Dictionary<int, (string Source, long Ticks)> _last = new();

    private Action<RawInput>? _sink;

    public string Name => "Auto";
    public bool KeyboardOk => _poll.KeyboardOk || _raw.KeyboardOk;
    public bool MouseOk => _poll.MouseOk || _raw.MouseOk;

    public void Start(Action<RawInput> sink)
    {
        Diag.Write("hybrid: start");

        _sink = sink;

        _poll.Start(e => Forward("poll", e));

        try
        {
            _raw.Start(e => Forward("raw", e));
        }
        catch (Exception ex)
        {
            Diag.Write($"hybrid: raw input start failed: {ex.Message}");
        }

        Diag.Write($"hybrid: pollOk={_poll.KeyboardOk} rawOk={_raw.KeyboardOk}");
    }

    private void Forward(string source, in RawInput e)
    {
        if (e.Kind is InputKind.Key or InputKind.MouseButton)
        {
            int key = ((int)e.Kind << 24) | (e.Code & 0xFFFF) | (e.Down ? 0x0100_0000 : 0);

            lock (_gate)
            {
                if (_last.TryGetValue(key, out var prev)
                    && !string.Equals(prev.Source, source, StringComparison.Ordinal)
                    && e.Timestamp - prev.Ticks < 150 * TicksPerMs)
                {
                    return;   // 另一个源刚刚报过，去重
                }

                _last[key] = (source, e.Timestamp);
            }
        }

        _sink?.Invoke(e);
    }

    public void Dispose()
    {
        Diag.Write("hybrid: dispose");
        _poll.Dispose();
        _raw.Dispose();
        _sink = null;
    }
}
