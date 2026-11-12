using System.Diagnostics;
using System.Windows.Threading;
using KeyGlass.Core;

namespace KeyGlass.Ui;

/// <summary>
/// 演示驱动器（--demo）：按固定节奏模拟打字、点击、滚轮，
/// 用来在真机上检查动画手感、截图取证，不需要人一直按键盘。
/// </summary>
public sealed class DemoDriver : IDisposable
{
    private static readonly int[] KeyCycle = { 0x51, 0x57, 0x45, 0x52, 0x41, 0x53, 0x44, 0x46, 0x20 };

    private readonly Action<RawInput> _inject;
    private readonly DispatcherTimer _timer;
    private readonly List<(long Until, InputKind Kind, int Code)> _held = new();

    private int _index;
    private int _tick;
    private long _lastPress;
    private bool _disposed;

    public DemoDriver(Action<RawInput> inject)
    {
        _inject = inject;
        _timer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _timer.Tick += Tick;
    }

    public void Start() => _timer.Start();

    private void Tick(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        long ticks120 = (long)(Stopwatch.Frequency * 0.12);

        for (int i = _held.Count - 1; i >= 0; i--)
        {
            if (now < _held[i].Until) continue;
            var h = _held[i];
            _inject(new RawInput(h.Kind, h.Code, false, 0, now));
            _held.RemoveAt(i);
        }

        double sinceMs = (now - _lastPress) * 1000.0 / Stopwatch.Frequency;
        if (sinceMs < 190) return;

        int vk = KeyCycle[_index++ % KeyCycle.Length];
        _inject(new RawInput(InputKind.Key, vk, true, 0, now));
        _held.Add((now + ticks120, InputKind.Key, vk));

        _tick++;

        if (_tick % 4 == 0)
        {
            _inject(new RawInput(InputKind.MouseButton, MouseCodes.Left, true, 0, now));
            _held.Add((now + (long)(Stopwatch.Frequency * 0.09), InputKind.MouseButton, MouseCodes.Left));
        }

        if (_tick % 7 == 0)
        {
            _inject(new RawInput(InputKind.Wheel, MouseCodes.WheelUp, true, 120, now));
        }

        _lastPress = now;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
    }
}
