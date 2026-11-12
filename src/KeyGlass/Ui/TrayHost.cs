using System.Drawing;
using WinForms = System.Windows.Forms;

namespace KeyGlass.Ui;

/// <summary>
/// 托盘图标与快捷菜单。图标在运行时用 GDI+ 画出来，不携带任何二进制资源。
/// </summary>
public sealed class TrayHost : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _toggleItem;
    private readonly WinForms.ToolStripMenuItem _chromaItem;
    private readonly WinForms.ToolStripMenuItem _transparentItem;
    private bool _disposed;

    public TrayHost(
        Func<bool> isVisible,
        Action toggle,
        Action openSettings,
        Action<bool> setTransparent,
        Func<bool> isTransparent,
        Action exit,
        string hotkey)
    {
        var menu = new WinForms.ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Font("Microsoft YaHei UI", 9f)
        };

        var header = new WinForms.ToolStripMenuItem("KeyGlass 键影") { Enabled = false };
        menu.Items.Add(header);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        _toggleItem = new WinForms.ToolStripMenuItem($"显示 / 隐藏叠加层  ({hotkey})");
        _toggleItem.Click += (_, _) => toggle();
        menu.Items.Add(_toggleItem);

        var settingsItem = new WinForms.ToolStripMenuItem("打开设置…");
        settingsItem.Click += (_, _) => openSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var captureMenu = new WinForms.ToolStripMenuItem("采集模式");
        _chromaItem = new WinForms.ToolStripMenuItem("色度键绿幕（推荐）") { CheckOnClick = false };
        _transparentItem = new WinForms.ToolStripMenuItem("透明叠加（WGC）") { CheckOnClick = false };
        _chromaItem.Click += (_, _) => setTransparent(false);
        _transparentItem.Click += (_, _) => setTransparent(true);
        captureMenu.DropDownItems.Add(_chromaItem);
        captureMenu.DropDownItems.Add(_transparentItem);
        menu.Items.Add(captureMenu);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("退出 KeyGlass");
        exitItem.Click += (_, _) => exit();
        menu.Items.Add(exitItem);

        menu.Opening += (_, _) =>
        {
            _toggleItem.Text = (isVisible() ? "隐藏叠加层" : "显示叠加层") + $"  ({hotkey})";
            _chromaItem.Checked = !isTransparent();
            _transparentItem.Checked = isTransparent();
        };

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "KeyGlass 键影 · 输入可视化",
            Visible = true,
            ContextMenuStrip = menu
        };

        _icon.DoubleClick += (_, _) => openSettings();
    }

    public void Notify(string title, string body)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = body;
            _icon.ShowBalloonTip(2600);
        }
        catch
        {
            // 气泡提示失败不影响主流程
        }
    }

    /// <summary>
    /// 直接取 exe 内嵌的图标资源（csproj 的 ApplicationIcon）。
    /// 早期版本是运行时用 GDI+ 画图标再 GetHicon+Clone，实测在部分环境下
    /// Shell_NotifyIcon 会拒绝该图标（托盘里根本不出现），改用资源图标后稳定。
    /// </summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted is not null) return extracted;
            }
        }
        catch
        {
            // 取不到就退系统图标，托盘功能不能因为图标失败而丢
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
