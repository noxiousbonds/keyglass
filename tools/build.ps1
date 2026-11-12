# KeyGlass 一键构建
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -SkipTest
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -SingleFile
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -FrameworkDependent
#
# 产物：
#   默认（推荐）    dist\KeyGlass\KeyGlass.exe      文件夹版，自包含、不外解包、最稳
#   -SingleFile    dist-single\KeyGlass.exe        单文件版，启动时要往 %TEMP%\.net 解包
#   -FrameworkDependent 约 1MB，要求目标机装有 .NET 8 Desktop Runtime
#
# 为什么要默认文件夹版：单文件版在受限环境（%TEMP% 被策略/安全软件限制）会出现
# 「双击没反应」——进程在 CoreHost 解包阶段就退出（0x800080A0），界面根本不启动。

param(
    [switch]$SkipTest,
    [switch]$SingleFile,
    [switch]$FrameworkDependent,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path (Join-Path $root 'src\KeyGlass\KeyGlass.csproj'))) {
    $root = $PSScriptRoot
}

# 定位一个「真的带 SDK」的 dotnet：PATH -> 仓库内 -> 上级目录
$candidates = New-Object System.Collections.Generic.List[string]
$cmd = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -First 1
if ($cmd) { $candidates.Add($cmd.Source) }
$candidates.Add((Join-Path $root '.dotnet\dotnet.exe'))
$candidates.Add((Join-Path (Split-Path $root -Parent) '.dotnet\dotnet.exe'))

$dotnet = $null
foreach ($candidate in $candidates) {
    if (-not (Test-Path $candidate)) { continue }
    $sdks = @(& $candidate --list-sdks 2>$null)
    if ($sdks.Count -gt 0) { $dotnet = $candidate; break }
}
if (-not $dotnet) {
    throw '找不到带 SDK 的 dotnet。请安装 .NET 8 SDK，或直接使用已构建好的 dist\KeyGlass\KeyGlass.exe（运行不需要 SDK）。'
}

$proj = Join-Path $root 'src\KeyGlass\KeyGlass.csproj'

if ($SingleFile) {
    $out = Join-Path $root 'dist-single'
    Write-Host "模式    : 单文件版（启动需解包到 %TEMP%\.net）"
} else {
    $out = Join-Path $root 'dist\KeyGlass'
    Write-Host "模式    : 文件夹版（推荐，不外解包）"
}

Write-Host "dotnet  : $dotnet"
Write-Host "project : $proj"
Write-Host "output  : $out"

if ($SingleFile) {
    & $dotnet publish $proj -c $Configuration -r win-x64 --self-contained true -o $out `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false -v minimal
} elseif ($FrameworkDependent) {
    & $dotnet publish $proj -c $Configuration -r win-x64 --self-contained false -o $out `
        -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -v minimal
} else {
    & $dotnet publish $proj -c $Configuration -r win-x64 --self-contained true -o $out `
        -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -v minimal
}
if ($LASTEXITCODE -ne 0) { throw "构建失败（exit $LASTEXITCODE）" }

$exe = Join-Path $out 'KeyGlass.exe'
if (-not (Test-Path $exe)) { throw "构建完成但没有找到 $exe" }

$size = if ($SingleFile) {
    [math]::Round((Get-Item $exe).Length / 1MB, 1)
} else {
    [math]::Round((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
}
Write-Host "产物：$exe（共 $size MB）" -ForegroundColor Green

if (-not $SkipTest) {
    Write-Host ''
    Write-Host '== 自检（输入源注册 + 状态机 + 离屏渲染） =='
    $artifacts = Join-Path $root 'artifacts'
    $p = Start-Process -FilePath $exe -ArgumentList @('--selftest', '--out', $artifacts, '--config', (Join-Path $artifacts 'settings.selftest.json')) -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "自检失败，详见 $artifacts\selftest.json" }
    Get-Content (Join-Path $artifacts 'selftest.json') -Raw

    Write-Host ''
    Write-Host '== 冒烟测试（管线链路 + 全局快捷键） =='
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\smoke-test.ps1') -Exe $exe
    if ($LASTEXITCODE -ne 0) { throw '冒烟测试失败' }
}

Write-Host ''
Write-Host "完成。双击 $exe 即可运行（托盘常驻，Ctrl+Alt+K 显示/隐藏）。" -ForegroundColor Green
