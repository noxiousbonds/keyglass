namespace KeyGlass.Core;

public enum InputKind
{
    Key,
    MouseButton,
    Wheel,
    HWheel
}

/// <summary>
/// 钩子线程 → UI 线程传递的最小事件体。readonly struct 保证入队零分配压力。
/// </summary>
public readonly record struct RawInput(InputKind Kind, int Code, bool Down, int Data, long Timestamp);
