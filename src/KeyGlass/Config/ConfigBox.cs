namespace KeyGlass.Config;

/// <summary>
/// 配置持有盒：叠加层、状态机、窗口共用同一个可变引用，
/// 设置窗口改完直接换 Value，无需重建渲染管线。
/// </summary>
public sealed class ConfigBox
{
    public ConfigBox(AppConfig value) => Value = value;

    public AppConfig Value { get; set; }
}
