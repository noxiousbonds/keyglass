# KeyGlass 性能实测脚本
#
# 用法（在没有 --demo 的真实场景里跑，或加 -Demo 让程序自己模拟输入）：
#   pwsh -File tools\measure-perf.ps1
#   pwsh -File tools\measure-perf.ps1 -Exe .\dist\KeyGlass.exe -Demo
#
# 输出：空闲 CPU、动画 CPU、工作集、以及程序自报的延迟分位数（metrics.json）。
# 测量口径：CPU% = 进程 CPU 时间增量 / 墙钟增量 / 逻辑核心数，与任务管理器一致。

param(
    [string]$Exe = "$PSScriptRoot\..\dist\KeyGlass.exe",
    [switch]$Demo,
    [int]$IdleSampleSeconds = 8,
    [int]$ActiveSampleSeconds = 15
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Exe)) {
    throw "找不到 $Exe，请先运行 build.ps1 或指定 -Exe"
}

$exePath = (Resolve-Path $Exe).Path
$cores = [Environment]::ProcessorCount
$metricsPath = Join-Path $env:TEMP 'KeyGlass\metrics.json'

$args = @('--quiet')
if ($Demo) { $args += '--demo' }

$proc = Start-Process -FilePath $exePath -ArgumentList $args -PassThru
Start-Sleep -Seconds 3   # 等 WPF 预 JIT / 托盘就绪

function Get-Sample {
    param([int]$Seconds, [string]$Label)
    $p = Get-Process -Id $proc.Id -ErrorAction Stop
    $t0 = $p.TotalProcessorTime.TotalMilliseconds
    $w0 = Get-Date
    Start-Sleep -Seconds $Seconds
    $p = Get-Process -Id $proc.Id -ErrorAction Stop
    $dt = $p.TotalProcessorTime.TotalMilliseconds - $t0
    $dw = ((Get-Date) - $w0).TotalMilliseconds
    $cpu = [math]::Round($dt / $dw / $cores * 100, 2)
    [pscustomobject]@{
        Phase        = $Label
        CpuPercent   = $cpu
        CpuOfOneCore = [math]::Round($dt / $dw * 100, 1)
        WorkingSetMb = [math]::Round($p.WorkingSet64 / 1MB, 1)
        PrivateMb    = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
        Threads      = $p.Threads.Count
        Handles      = $p.HandleCount
    }
}

$results = @()

$idle = Get-Sample -Seconds $IdleSampleSeconds -Label 'idle'
$results += $idle

if (Test-Path $metricsPath) {
    $m = Get-Content $metricsPath -Raw | ConvertFrom-Json
    "renderTier=$($m.renderTier)  animating=$($m.animating)  capture=$($m.captureMode)"
}

if ($Demo) {
    $active = Get-Sample -Seconds $ActiveSampleSeconds -Label 'typing (demo)'
    $results += $active
}

$results | Format-Table -AutoSize

if (Test-Path $metricsPath) {
    $m = Get-Content $metricsPath -Raw | ConvertFrom-Json
    "latency: p50=$($m.latencyP50Ms)ms p95=$($m.latencyP95Ms)ms max=$($m.latencyMaxMs)ms samples=$($m.latencySamples)"
    "hooks: keyboard=$($m.keyboardHookOk) mouse=$($m.mouseHookOk)  kps=$($m.kps) cps=$($m.cps)"
}

Stop-Process -Id $proc.Id -Force
Write-Output 'done.'
