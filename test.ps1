# RefreshGuard 手动测试脚本
#
# 用法示例：
#   pwsh -File test.ps1 -List                查看当前模式与可用刷新率
#   pwsh -File test.ps1 -Status              查看配置与状态
#   pwsh -File test.ps1 -DryRun -Battery     只打印决策，不动屏幕
#   pwsh -File test.ps1 -Demo                弹一次横幅演示，不会真的切换
#   pwsh -File test.ps1 -Battery -Now        立即切到 60Hz（会黑屏 1~2 秒）
#   pwsh -File test.ps1 -Ac -Now             立即切回 120Hz

param(
    [switch]$List,
    [switch]$Status,
    [switch]$Demo,
    [switch]$Battery,
    [switch]$Ac,
    [switch]$Now,
    [switch]$DryRun,
    [string]$InstallDir = ''
)

$ErrorActionPreference = 'Stop'
if (-not $InstallDir) { $InstallDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
$exe = Join-Path $InstallDir 'RefreshGuard.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "未安装：$exe（先运行 install.ps1）" }

$argList = @()
if ($List)    { $argList += '--list' }
if ($Status)  { $argList += '--status' }
if ($Demo)    { $argList += '--banner-demo' }
if ($Battery) { $argList += '--battery' }
if ($Ac)      { $argList += '--ac' }
if ($Now)     { $argList += '--now' }
if ($DryRun)  { $argList += '--dry-run' }

if ($argList.Count -eq 0) { $argList = @('--status') }

# 横幅路径要等倒计时走完才有结果，500ms 只能读到日志头部
$banner = $Demo -or (($Battery -or $Ac) -and -not $Now -and -not $DryRun)

& $exe @argList
if ($banner) {
    Write-Host '横幅路径：等待倒计时结束再读日志（20 秒，覆盖默认 10 秒倒计时；可提前点按钮，Ctrl+C 可中断等待）...'
    Start-Sleep -Seconds 20
} else {
    Start-Sleep -Milliseconds 500
}

# 界面上看不到输出时，直接读日志
$logDir = Join-Path $InstallDir 'logs'
$log = Get-ChildItem -LiteralPath $logDir -Filter '*.log' -ErrorAction SilentlyContinue |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($log) {
    Write-Host ''
    Write-Host "--- 最近日志 ($($log.Name)) ---"
    Get-Content -LiteralPath $log.FullName -Tail 25
}
