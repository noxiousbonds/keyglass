# KeyGlass 端到端冒烟测试
#
# 分两条链路验证，因为「合成输入」在 Windows 上走不到 Raw Input 通道：
#   A. 管线链路：--demo 让程序按节奏向输入队列投递事件，
#      验证 队列 → 状态机 → 渲染 → 指标落盘 全链路可用（含延迟、帧率、内存）。
#   B. 系统链路：用 RegisterHotKey 的 Ctrl+Alt+K 验证
#      「系统输入 → 窗口控制」这条真实通路（热键由系统投递，可被合成输入触发）。
#
# 唯一无法自动化的一段是「物理键 → HID → Raw Input」，
# 这一段由脚本末尾的手动检查项覆盖。
#
# 用法： powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke-test.ps1 -Exe .\dist\KeyGlass.exe

param(
    [string]$Exe = "$PSScriptRoot\..\dist\KeyGlass.exe"
)

$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]

function Assert($condition, $message) {
    if ($condition) {
        Write-Host "  [PASS] $message" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] $message" -ForegroundColor Red
        $script:failures.Add($message)
    }
}

function Read-Metrics($path) {
    # 单文件 exe 首次启动要解包原生库、可能被杀软扫描，这里给足 24 秒
    for ($i = 0; $i -lt 80; $i++) {
        if (Test-Path $path) {
            try { return Get-Content $path -Raw | ConvertFrom-Json } catch { }
        }
        Start-Sleep -Milliseconds 300
    }
    throw "没有读到 metrics.json：$path"
}

if (-not (Test-Path $Exe)) { throw "找不到 $Exe" }
$exePath = (Resolve-Path $Exe).Path
$metricsPath = Join-Path $env:TEMP 'KeyGlass\metrics.json'

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class KGInput
{
    [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    public static void KeyDown(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); }
    public static void KeyUp(byte vk) { keybd_event(vk, 0, 2, UIntPtr.Zero); }
    public static void Tap(byte vk) { KeyDown(vk); System.Threading.Thread.Sleep(40); KeyUp(vk); }
}
"@

Get-Process KeyGlass -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

# ---------------------------------------------------------------- A. 管线链路
Write-Host ''
Write-Host '== A. 管线链路（输入队列 -> 状态机 -> 渲染 -> 指标） ==' -ForegroundColor Cyan
if (Test-Path $metricsPath) { Remove-Item $metricsPath -Force }
$cfgA = Join-Path $env:TEMP 'kg-smoke-a.json'
Remove-Item $cfgA -Force -ErrorAction SilentlyContinue

$procA = Start-Process -FilePath $exePath -ArgumentList @('--demo', '--quiet', '--config', $cfgA) -PassThru
try {
    $m0 = Read-Metrics $metricsPath
    Assert ($m0.keyboardHookOk -eq $true -and $m0.mouseHookOk -eq $true) "输入源注册成功：$($m0.inputEngine)"
    Assert ($m0.overlayVisible -eq $true) '叠加层窗口已显示'
    Assert ($m0.captureMode -eq 'Chroma') '默认采集模式为色度键绿幕'

    Start-Sleep -Seconds 8
    $m1 = Read-Metrics $metricsPath
    Assert ($m1.totalKeys -ge 10) "按键事件进入状态机：totalKeys=$($m1.totalKeys)"
    Assert ($m1.totalClicks -ge 1) "鼠标点击进入状态机：totalClicks=$($m1.totalClicks)"
    Assert ($m1.latencySamples -gt 0) "记录到渲染延迟：$($m1.latencySamples) 个样本"
    Assert ([double]$m1.latencyP95Ms -lt 8) "p95 捕获->渲染提交延迟 < 8ms（实测 $($m1.latencyP95Ms)ms）"
    Assert ([double]$m1.framesPerSecond -gt 40) "渲染循环帧率 > 40fps（实测 $($m1.framesPerSecond)fps）"
    Assert ([double]$m1.workingSetMb -lt 400) "工作集 < 400MB（实测 $($m1.workingSetMb)MB）"
    Assert ($m1.renderTier -ge 1) "WPF 渲染层级 tier=$($m1.renderTier)"
}
finally {
    Stop-Process -Id $procA.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 600
}

# ---------------------------------------------------------------- B. 系统链路
Write-Host ''
Write-Host '== B. 系统链路（全局快捷键 Ctrl+Alt+K） ==' -ForegroundColor Cyan
if (Test-Path $metricsPath) { Remove-Item $metricsPath -Force }
$cfgB = Join-Path $env:TEMP 'kg-smoke-b.json'
Remove-Item $cfgB -Force -ErrorAction SilentlyContinue

$procB = Start-Process -FilePath $exePath -ArgumentList @('--quiet', '--config', $cfgB) -PassThru
try {
    $b0 = Read-Metrics $metricsPath
    Assert ($b0.overlayVisible -eq $true) '启动后叠加层可见'

    [KGInput]::KeyDown(0x11); [KGInput]::KeyDown(0x12)
    [KGInput]::Tap(0x4B)
    [KGInput]::KeyUp(0x12); [KGInput]::KeyUp(0x11)
    Start-Sleep -Seconds 6
    $b1 = Read-Metrics $metricsPath
    Assert ($b1.overlayVisible -eq $false) 'Ctrl+Alt+K 可以隐藏叠加层'

    [KGInput]::KeyDown(0x11); [KGInput]::KeyDown(0x12)
    [KGInput]::Tap(0x4B)
    [KGInput]::KeyUp(0x12); [KGInput]::KeyUp(0x11)
    Start-Sleep -Seconds 6
    $b2 = Read-Metrics $metricsPath
    Assert ($b2.overlayVisible -eq $true) 'Ctrl+Alt+K 可以重新显示叠加层'
}
finally {
    Stop-Process -Id $procB.Id -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------- 手动检查项
Write-Host ''
Write-Host '== 需要人工确认的一段（合成输入覆盖不到） ==' -ForegroundColor Yellow
Write-Host '  请手动运行 dist\KeyGlass.exe，然后：'
Write-Host '   1) 按 A / S / D 三个键 -> 叠加层应立刻出现对应键帽（按住时高亮）'
Write-Host '   2) 单击鼠标左键 -> 鼠标图形的左键区与 L 胶囊亮起'
Write-Host '   3) 滚轮上下 -> 鼠标滚轮亮起并出现方向箭头'
Write-Host '  若第 1 步无反应：设置里把「输入引擎」切到低级钩子再试，或在安全软件里放行 KeyGlass。'

Write-Host ''
if ($failures.Count -eq 0) {
    Write-Host 'SMOKE TEST: PASS' -ForegroundColor Green
    exit 0
} else {
    Write-Host "SMOKE TEST: FAIL ($($failures.Count) 项)" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
