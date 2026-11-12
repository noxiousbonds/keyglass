namespace KeyGlass.Core;

/// <summary>
/// 输入源抽象。两套实现：
///  - <see cref="RawInputEngine"/>：RegisterRawInputDevices + WM_INPUT（默认，抗安全软件拦截、无需提权）
///  - <see cref="InputEngine"/>：WH_KEYBOARD_LL / WH_MOUSE_LL 低级钩子（兼容模式）
/// </summary>
public interface IInputSource : IDisposable
{
    string Name { get; }

    bool KeyboardOk { get; }

    bool MouseOk { get; }

    void Start(Action<RawInput> sink);
}
