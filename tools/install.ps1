# KeyGlass 安装脚本
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\install.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\install.ps1 -Target "D:\Apps\KeyGlass"
#
# 为什么需要装到 Program Files：
#   本机实测，把程序放在「被策略限制的目录」（某些沙盘 / 被接管的目录）时，系统会强制它以 **Low 完整性**
#   运行；Low 进程看不到发往普通（Medium/High）窗口的输入，表现就是“设置窗口一关，
#   按键全部失效”。放到 Program Files 或桌面后，程序以 High 完整性运行，输入正常。
#
# 本脚本做的事：复制文件 → 在桌面创建快捷方式 → 提示直接运行。

param(
    [string]$Source = "$PSScriptRoot\..\dist\KeyGlass",
    [string]$Target = "C:\Program Files\KeyGlass",
    [switch]$NoShortcut
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $Source 'KeyGlass.exe'))) {
    throw "找不到源目录里的 KeyGlass.exe：$Source（先运行 build.ps1 或使用已构建的 dist\KeyGlass）"
}

Write-Host "源目录 : $Source"
Write-Host "目标目录: $Target"

Get-Process KeyGlass -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Force -Path $Target | Out-Null
Copy-Item (Join-Path $Source '*') $Target -Recurse -Force

$exe = Join-Path $Target 'KeyGlass.exe'
if (-not (Test-Path $exe)) { throw "复制完成但找不到 $exe" }
Write-Host "已安装：$exe" -ForegroundColor Green

if (-not $NoShortcut) {
    try {
        $desktop = [Environment]::GetFolderPath('Desktop')
        $link = Join-Path $desktop 'KeyGlass 键影.lnk'
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($link)
        $shortcut.TargetPath = $exe
        $shortcut.WorkingDirectory = $Target
        $shortcut.Description = 'KeyGlass 键影 · 键盘鼠标输入可视化叠加层'
        $shortcut.Save()
        Write-Host "桌面快捷方式：$link" -ForegroundColor Green
    } catch {
        Write-Host "创建快捷方式失败（不影响使用）：$($_.Exception.Message)" -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host "启动方式：双击 $exe" -ForegroundColor Green
Write-Host '直播姬采集：添加来源 → 窗口捕获 → 选「KeyGlass 叠加层（直播姬采集这个）」→ 加色度键 #00B140、相似度 15%~25%'
