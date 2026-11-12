using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using KeyGlass.Config;

namespace KeyGlass.Ui;

/// <summary>
/// 设置窗口。所有控件都是「即时生效」——改一项立刻重建/刷新叠加层，
/// 主播可以一边看画面一边调位置和配色。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly OverlayController _controller;
    private bool _loading;

    public SettingsWindow(OverlayController controller)
    {
        _controller = controller;
        InitializeComponent();

        PopulateMonitors();
        LoadFrom(controller.Config);
        Hook();
    }

    private void PopulateMonitors()
    {
        cmbMonitor.Items.Clear();
        foreach (var m in MonitorHelper.GetMonitors())
        {
            cmbMonitor.Items.Add(new ComboBoxItem
            {
                Content = m.Primary ? $"{m.Index} · {m.Width}×{m.Height}（主屏）" : $"{m.Index} · {m.Width}×{m.Height}",
                Tag = m.Index.ToString()
            });
        }

        if (cmbMonitor.Items.Count == 0)
        {
            cmbMonitor.Items.Add(new ComboBoxItem { Content = "0 · 主屏", Tag = "0" });
        }
    }

    private void Hook()
    {
        cmbMode.SelectionChanged += OnControlChanged;
        cmbEngine.SelectionChanged += OnControlChanged;
        cmbChroma.SelectionChanged += OnControlChanged;
        cmbTheme.SelectionChanged += OnControlChanged;
        cmbAnchor.SelectionChanged += OnControlChanged;
        cmbChips.SelectionChanged += OnControlChanged;
        cmbMonitor.SelectionChanged += OnControlChanged;

        sldMarginX.ValueChanged += OnControlChanged;
        sldMarginY.ValueChanged += OnControlChanged;
        sldScale.ValueChanged += OnControlChanged;
        sldOpacity.ValueChanged += OnControlChanged;
        sldLinger.ValueChanged += OnControlChanged;

        chkMouse.Checked += OnControlChanged;
        chkMouse.Unchecked += OnControlChanged;
        chkStats.Checked += OnControlChanged;
        chkStats.Unchecked += OnControlChanged;
        chkMods.Checked += OnControlChanged;
        chkMods.Unchecked += OnControlChanged;
        chkHud.Checked += OnControlChanged;
        chkHud.Unchecked += OnControlChanged;
        chkHideOnScreen.Checked += OnControlChanged;
        chkHideOnScreen.Unchecked += OnControlChanged;
        chkHideAltTab.Checked += OnControlChanged;
        chkHideAltTab.Unchecked += OnControlChanged;
        chkStartHidden.Checked += OnControlChanged;
        chkStartHidden.Unchecked += OnControlChanged;

        btnDefaults.Click += OnDefaults;
        btnFolder.Click += OnOpenFolder;
        btnClose.Click += OnCloseSave;
        Closing += (_, _) => Save();
    }

    private void LoadFrom(AppConfig c)
    {
        _loading = true;

        SelectTag(cmbMode, c.CaptureMode);
        SelectTag(cmbEngine, c.InputEngine);
        SelectTag(cmbChroma, c.ChromaColor.ToUpperInvariant());
        SelectTag(cmbTheme, c.Theme);
        SelectTag(cmbAnchor, c.Anchor);
        SelectTag(cmbChips, c.ChipCapacity.ToString());

        cmbMonitor.SelectedIndex = Math.Clamp(c.MonitorIndex, 0, Math.Max(0, cmbMonitor.Items.Count - 1));

        sldMarginX.Value = c.MarginX;
        sldMarginY.Value = c.MarginY;
        sldScale.Value = c.Scale;
        sldOpacity.Value = c.Opacity;
        sldLinger.Value = c.ChipLingerMs;

        chkMouse.IsChecked = c.ShowMouse;
        chkStats.IsChecked = c.ShowStats;
        chkMods.IsChecked = c.ShowModifiers;
        chkHud.IsChecked = c.ShowLatencyHud;
        chkHideOnScreen.IsChecked = c.HideOnScreen;
        chkHideAltTab.IsChecked = c.HideFromAltTab;
        chkStartHidden.IsChecked = c.StartHidden;

        cmbChroma.IsEnabled = !c.IsTransparent;
        UpdateValueLabels();

        _loading = false;
    }

    private AppConfig ReadConfig()
    {
        var c = _controller.Config.Clone();

        c.CaptureMode = TagOf(cmbMode) ?? "Chroma";
        c.InputEngine = TagOf(cmbEngine) ?? "RawInput";
        c.ChromaColor = (TagOf(cmbChroma) ?? "#00B140").ToUpperInvariant();
        c.Theme = TagOf(cmbTheme) ?? "Midnight";
        c.Anchor = TagOf(cmbAnchor) ?? "BR";

        if (int.TryParse(TagOf(cmbChips), out int capacity)) c.ChipCapacity = capacity;

        c.MonitorIndex = cmbMonitor.SelectedIndex < 0 ? 0 : cmbMonitor.SelectedIndex;
        c.MarginX = (int)Math.Round(sldMarginX.Value);
        c.MarginY = (int)Math.Round(sldMarginY.Value);
        c.Scale = Math.Round(sldScale.Value, 2);
        c.Opacity = Math.Round(sldOpacity.Value, 2);
        c.ChipLingerMs = (int)Math.Round(sldLinger.Value);

        c.ShowMouse = chkMouse.IsChecked == true;
        c.ShowStats = chkStats.IsChecked == true;
        c.ShowModifiers = chkMods.IsChecked == true;
        c.ShowLatencyHud = chkHud.IsChecked == true;
        c.HideOnScreen = chkHideOnScreen.IsChecked == true;
        c.HideFromAltTab = chkHideAltTab.IsChecked == true;
        c.StartHidden = chkStartHidden.IsChecked == true;

        c.Normalize();
        return c;
    }

    private void OnControlChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        UpdateValueLabels();

        var cfg = ReadConfig();
        cmbChroma.IsEnabled = !cfg.IsTransparent;
        _controller.ApplyConfig(cfg, persist: false);
    }

    private void UpdateValueLabels()
    {
        txtMarginX.Text = $"{sldMarginX.Value:0}";
        txtMarginY.Text = $"{sldMarginY.Value:0}";
        txtScale.Text = $"{sldScale.Value:0.00}×";
        txtOpacity.Text = $"{sldOpacity.Value:P0}";
        txtLinger.Text = $"{sldLinger.Value:0}ms";
    }

    private void OnDefaults(object sender, RoutedEventArgs e)
    {
        var defaults = new AppConfig();
        defaults.Normalize();
        LoadFrom(defaults);
        _controller.ApplyConfig(ReadConfig(), persist: true);
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.DefaultDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = ConfigStore.DefaultDirectory,
                UseShellExecute = true
            });
        }
        catch
        {
            // 资源管理器打不开就忽略
        }
    }

    private void OnCloseSave(object sender, RoutedEventArgs e)
    {
        Save();
        Close();
    }

    private void Save() => _controller.ApplyConfig(ReadConfig(), persist: true);

    private static void SelectTag(ComboBox combo, string? tag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem ci && string.Equals(ci.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = ci;
                return;
            }
        }

        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string? TagOf(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;
}
