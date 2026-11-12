using System.Diagnostics;
using KeyGlass.Config;

namespace KeyGlass.Core;

/// <summary>
/// 叠加层的全部可视状态。只在 UI 线程读写，不加锁；动画推进在渲染帧里完成。
///
/// 设计取舍：鼠标按键不进「键帽流」——它有自己的鼠标图形 + L/M/R 胶囊，
/// 再在键帽行里重复一遍只会稀释信息层级。键帽行专属于键盘。
/// </summary>
public sealed class StateStore
{
    private static readonly long TicksPerMs = Math.Max(1, Stopwatch.Frequency / 1000);

    private readonly ConfigBox _cfg;
    private readonly List<Chip> _display = new();
    private readonly Dictionary<int, Chip> _byId = new();
    private readonly Queue<long> _keyTimes = new();
    private readonly Queue<long> _clickTimes = new();

    private long _maxSeq;
    private long _lastActivity;
    private int _statsCooldown;

    public StateStore(ConfigBox cfg)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
    }

    public IReadOnlyList<Chip> Display => _display;

    public bool Ctrl { get; private set; }
    public bool Shift { get; private set; }
    public bool Alt { get; private set; }
    public bool Win { get; private set; }

    public bool MouseLeft { get; private set; }
    public bool MouseRight { get; private set; }
    public bool MouseMiddle { get; private set; }
    public bool MouseX1 { get; private set; }
    public bool MouseX2 { get; private set; }

    /// <summary>点击后 260ms 内衰减的亮度脉冲，保证一次 40ms 的快速点击也看得见。</summary>
    public double LeftPulse { get; private set; }
    public double RightPulse { get; private set; }
    public double MiddlePulse { get; private set; }

    /// <summary>滚轮脉冲 0..1（轮盘 / 箭头闪烁）。</summary>
    public double WheelUp { get; private set; }
    public double WheelDown { get; private set; }

    public double Kps { get; private set; }
    public double Cps { get; private set; }
    public long TotalKeys { get; private set; }
    public long TotalClicks { get; private set; }

    public long LastActivityTicks => _lastActivity;

    public bool AnyMouseDown => MouseLeft || MouseRight || MouseMiddle || MouseX1 || MouseX2;

    /// <summary>把一条原始输入并入状态。返回 true 表示画面需要重绘。</summary>
    public bool Apply(in RawInput e)
    {
        _lastActivity = e.Timestamp;

        switch (e.Kind)
        {
            case InputKind.Key:
                return ApplyKey(e);
            case InputKind.MouseButton:
                return ApplyMouseButton(e);
            case InputKind.Wheel:
                if (e.Code == MouseCodes.WheelUp)
                {
                    WheelUp = 1.0;
                    _clickTimes.Enqueue(e.Timestamp);
                }
                else
                {
                    WheelDown = 1.0;
                    _clickTimes.Enqueue(e.Timestamp);
                }
                return true;
            default:
                return false;
        }
    }

    private bool ApplyKey(in RawInput e)
    {
        int vk = e.Code;

        if (Vk.IsModifier(vk))
        {
            SetModifier(vk, e.Down);
            return true;
        }

        string? label = Vk.Label(vk);
        if (label is null) return false;

        if (e.Down)
        {
            if (_byId.TryGetValue(vk, out var held) && held.Down)
            {
                // 按住不放的自动重复：只加脉冲，不再刷一个键帽
                held.Pop = 1.0;
                return true;
            }

            var chip = GetOrCreate(vk, label);
            chip.Down = true;
            chip.DownTs = e.Timestamp;
            chip.UpTs = 0;
            chip.AgeMs = 0;
            chip.Pop = 1.0;
            chip.Press = 0;
            chip.Alpha = 1;
            chip.Rendered = false;
            Touch(chip);

            _keyTimes.Enqueue(e.Timestamp);
            TotalKeys++;
            return true;
        }

        if (_byId.TryGetValue(vk, out var upChip))
        {
            upChip.Down = false;
            upChip.UpTs = e.Timestamp;
            upChip.AgeMs = 0;
            upChip.Pop = 0;
            return true;
        }

        return false;
    }

    private bool ApplyMouseButton(in RawInput e)
    {
        switch (e.Code)
        {
            case MouseCodes.Left:
                MouseLeft = e.Down;
                if (e.Down) LeftPulse = 1.0;
                break;
            case MouseCodes.Right:
                MouseRight = e.Down;
                if (e.Down) RightPulse = 1.0;
                break;
            case MouseCodes.Middle:
                MouseMiddle = e.Down;
                if (e.Down) MiddlePulse = 1.0;
                break;
            case MouseCodes.X1:
                MouseX1 = e.Down;
                break;
            case MouseCodes.X2:
                MouseX2 = e.Down;
                break;
        }

        if (e.Down)
        {
            _clickTimes.Enqueue(e.Timestamp);
            TotalClicks++;
        }

        return true;
    }

    private void SetModifier(int vk, bool down)
    {
        switch (vk)
        {
            case Vk.VK_SHIFT or Vk.VK_LSHIFT or Vk.VK_RSHIFT:
                Shift = down;
                break;
            case Vk.VK_CONTROL or Vk.VK_LCONTROL or Vk.VK_RCONTROL:
                Ctrl = down;
                break;
            case Vk.VK_MENU or Vk.VK_LMENU or Vk.VK_RMENU:
                Alt = down;
                break;
            case Vk.VK_LWIN or Vk.VK_RWIN:
                Win = down;
                break;
        }
    }

    private Chip GetOrCreate(int id, string label)
    {
        if (_byId.TryGetValue(id, out var chip)) return chip;

        chip = new Chip { Id = id, Label = label };
        _byId[id] = chip;
        _display.Add(chip);
        return chip;
    }

    private void Touch(Chip chip)
    {
        chip.Seq = ++_maxSeq;

        int index = _display.IndexOf(chip);
        if (index >= 0 && index != _display.Count - 1)
        {
            _display.RemoveAt(index);
            _display.Add(chip);
        }

        while (_display.Count > _cfg.Value.ChipCapacity)
        {
            var victim = _display[0];
            _display.RemoveAt(0);
            _byId.Remove(victim.Id);
        }
    }

    private void Remove(Chip chip)
    {
        _display.Remove(chip);
        if (_byId.TryGetValue(chip.Id, out var current) && ReferenceEquals(current, chip))
        {
            _byId.Remove(chip.Id);
        }
    }

    /// <summary>
    /// 推进一帧动画。返回 true = 动画未结束，渲染循环应继续。
    /// 计时完全基于累计 dtMs（而不是墙钟），因此自检与截图可以确定性地快进。
    /// </summary>
    public bool Advance(double dtMs)
    {
        if (dtMs <= 0) dtMs = 0;
        if (dtMs > 120) dtMs = 120;

        int linger = _cfg.Value.ChipLingerMs;

        for (int i = _display.Count - 1; i >= 0; i--)
        {
            var chip = _display[i];

            // 按压形变：约 70ms 收敛
            double target = chip.Down ? 1.0 : 0.0;
            chip.Press += (target - chip.Press) * Math.Min(1.0, dtMs / 70.0);

            if (chip.Pop > 0)
            {
                chip.Pop = Math.Max(0, chip.Pop - dtMs / 150.0);
            }

            if (!chip.Down && chip.UpTs > 0)
            {
                chip.AgeMs += dtMs;
                if (chip.AgeMs > linger)
                {
                    chip.Alpha -= dtMs / 150.0;
                    if (chip.Alpha <= 0.02)
                    {
                        Remove(chip);
                    }
                }
            }
        }

        if (LeftPulse > 0) LeftPulse = Math.Max(0, LeftPulse - dtMs / 260.0);
        if (RightPulse > 0) RightPulse = Math.Max(0, RightPulse - dtMs / 260.0);
        if (MiddlePulse > 0) MiddlePulse = Math.Max(0, MiddlePulse - dtMs / 260.0);
        if (WheelUp > 0) WheelUp = Math.Max(0, WheelUp - dtMs / 280.0);
        if (WheelDown > 0) WheelDown = Math.Max(0, WheelDown - dtMs / 280.0);

        _statsCooldown++;
        if (_statsCooldown >= 8)
        {
            _statsCooldown = 0;
            RecalcStats(Stopwatch.GetTimestamp());
        }

        return _display.Count > 0
               || LeftPulse > 0 || RightPulse > 0 || MiddlePulse > 0
               || WheelUp > 0 || WheelDown > 0;
    }

    private void RecalcStats(long now)
    {
        long cutoff = now - Stopwatch.Frequency;   // 1 秒滑动窗口
        while (_keyTimes.Count > 0 && _keyTimes.Peek() < cutoff) _keyTimes.Dequeue();
        while (_clickTimes.Count > 0 && _clickTimes.Peek() < cutoff) _clickTimes.Dequeue();

        Kps = _keyTimes.Count;
        Cps = _clickTimes.Count;
    }

    public void RecalcStatsNow() => RecalcStats(Stopwatch.GetTimestamp());

    public void Clear()
    {
        _display.Clear();
        _byId.Clear();
        _keyTimes.Clear();
        _clickTimes.Clear();
        Ctrl = Shift = Alt = Win = false;
        MouseLeft = MouseRight = MouseMiddle = MouseX1 = MouseX2 = false;
        LeftPulse = RightPulse = MiddlePulse = 0;
        WheelUp = WheelDown = 0;
        Kps = Cps = 0;
    }

    /// <summary>
    /// 确定性演示状态：直接喂一组带时间戳的事件，用于截图 / 样式回归。
    /// 6 个键帽里 3 个按住（高亮）、3 个已松开（暗态），并带一次点击与滚轮脉冲。
    /// </summary>
    public void SeedDemo()
    {
        long now = Stopwatch.GetTimestamp();

        void Press(int vk, int msAgo) => Apply(new RawInput(InputKind.Key, vk, true, 0, now - msAgo * TicksPerMs));
        void Release(int vk, int msAgo) => Apply(new RawInput(InputKind.Key, vk, false, 0, now - msAgo * TicksPerMs));

        Apply(new RawInput(InputKind.Key, Vk.VK_CONTROL, true, 0, now - 960 * TicksPerMs));  // Ctrl 亮起

        Press(0x51, 900);        // Q
        Release(0x51, 780);
        Press(0x57, 700);        // W
        Release(0x57, 600);
        Press(0x45, 520);        // E（按住）
        Press(0x41, 460);        // A
        Release(0x41, 380);
        Press(0x53, 300);        // S（按住）
        Press(0x44, 200);        // D（按住）

        Apply(new RawInput(InputKind.MouseButton, MouseCodes.Left, true, 0, now - 120 * TicksPerMs));
        LeftPulse = 0.85;
        Apply(new RawInput(InputKind.Wheel, MouseCodes.WheelUp, true, 120, now - 90 * TicksPerMs));
        WheelUp = 0.7;

        RecalcStatsNow();

        // 种子事件不参与延迟统计（否则 HUD 里会出现假的几百毫秒）
        foreach (var chip in _display) chip.Rendered = true;
    }
}

public sealed class Chip
{
    public int Id { get; init; }
    public string Label { get; init; } = "";
    public bool Down { get; set; }
    public long DownTs { get; set; }
    public long UpTs { get; set; }
    public long Seq { get; set; }
    public double Pop { get; set; }
    public double Press { get; set; }
    public double Alpha { get; set; } = 1.0;

    /// <summary>松开后累计的毫秒数（由 Advance 的 dtMs 累加，确定性）。</summary>
    public double AgeMs { get; set; }

    public bool Rendered { get; set; }
}
