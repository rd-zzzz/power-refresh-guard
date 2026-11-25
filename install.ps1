# RefreshGuard 安装脚本
#
# 做三件事：
#   1) 注册计划任务 RefreshGuard —— 电源事件触发（Kernel-Power 105 / Power-Troubleshooter 1）
#      → 弹窗倒计时 10 秒，可取消，到期切换
#   2) 注册计划任务 RefreshGuard-Logon —— 登录事件触发（Winlogon 7001）
#      → 延迟 15 秒静默对齐一次，覆盖"断电降到 60Hz → 关机 → 来电开机仍是 60Hz"
#   3) 写 HKCU Run 键 —— 登录时再执行一次静默对齐（第二条独立路径，双保险）
#
# 全程不需要管理员权限。
#
# 三个必须注意的坑（都是实测出来的，别改）：
#   * 不要用 schtasks /create /sc ONEVENT：它把 DisallowStartIfOnBatteries 默认设成 True，
#     断电那一刻正好是电池供电，任务会被系统拒绝启动，现象是"什么都没发生"。
#   * 不要用 AtLogOn 触发器注册计划任务：标准用户注册会"拒绝访问"，所以登录对齐走
#     Winlogon 7001 事件订阅 + HKCU 的 Run 键两条路。
#   * Run 键会被 Windows「设置 > 应用 > 启动」或第三方优化工具写进 StartupApproved\Run
#     禁用标记（首字节 03）。此时键还在，但登录时不会执行，且没有任何提示 —— 安装脚本
#     必须清掉这个标记，否则"登录对齐"是假的。
#
# 安装目录默认为脚本所在目录（便携：跟着文件夹走）。想换位置加 -InstallDir 'X:\某处\RefreshGuard'。
#
# 用法：
#   pwsh -File install.ps1                          安装 / 重装
#   pwsh -File install.ps1 -LogonDelaySeconds 20    登录任务延迟改成 20 秒（默认 15）
#   pwsh -File install.ps1 -Uninstall               卸载任务和自启项（保留配置和日志）

param([switch]$Uninstall, [string]$InstallDir = '', [int]$LogonDelaySeconds = 15)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $InstallDir) { $InstallDir = $root }
$TaskEvent = 'RefreshGuard'
$TaskLogon = 'RefreshGuard-Logon'
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$StartupApprovedRun = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$RunName = 'RefreshGuard'

function Clear-StartupApproved {
    # 被「启动应用」开关或优化工具关过之后，Run 值还在，但 StartupApproved 里有一条
    # 03 开头的禁用记录，登录时不会执行。删掉记录（等价于重新启用）才能恢复。
    if (-not (Test-Path -LiteralPath $StartupApprovedRun)) { return }
    $v = Get-ItemProperty -Path $StartupApprovedRun -Name $RunName -ErrorAction SilentlyContinue
    if ($null -ne $v) {
        Remove-ItemProperty -Path $StartupApprovedRun -Name $RunName
        Write-Host "已清除启动项禁用标记: $RunName"
    }
}

function Remove-All {
    foreach ($n in @($TaskEvent, $TaskLogon)) {
        if (Get-ScheduledTask -TaskName $n -ErrorAction SilentlyContinue) {
            Unregister-ScheduledTask -TaskName $n -Confirm:$false
            Write-Host "已移除计划任务: $n"
        }
    }
    if (Get-ItemProperty -Path $RunKey -Name $RunName -ErrorAction SilentlyContinue) {
        Remove-ItemProperty -Path $RunKey -Name $RunName
        Write-Host "已移除登录自启项: $RunName"
    }
    Clear-StartupApproved
}

if ($Uninstall) {
    Remove-All
    Write-Host ''
    Write-Host "运行目录保留（含配置和日志）：$InstallDir"
    Write-Host '如需彻底清理，手动删除该目录即可。'
    exit 0
}

$built = Join-Path $root 'dist\RefreshGuard.exe'
if (-not (Test-Path -LiteralPath $built)) {
    # 搬移目录后 dist 可能不存在，回退用根目录已有的 exe，不必强制先重编
    $built = Join-Path $root 'RefreshGuard.exe'
    if (-not (Test-Path -LiteralPath $built)) { throw "找不到 dist\RefreshGuard.exe 或 RefreshGuard.exe，请先运行 build.ps1" }
    Write-Host "未找到 dist\，改用 $built"
}

$target = $InstallDir
if (-not (Test-Path -LiteralPath $target)) { New-Item -ItemType Directory -Path $target | Out-Null }
$exe = Join-Path $target 'RefreshGuard.exe'
if ([System.IO.Path]::GetFullPath($built) -ne [System.IO.Path]::GetFullPath($exe)) {
    Copy-Item -LiteralPath $built -Destination $exe -Force
    Write-Host "已安装到: $exe"
} else {
    Write-Host "源与目标为同一文件，跳过复制: $exe"
}

function New-EventTrigger([string]$subscription, [string]$delay = '') {
    $cls = Get-CimClass -ClassName MSFT_TaskEventTrigger -Namespace Root/Microsoft/Windows/TaskScheduler
    $t = New-CimInstance -CimClass $cls -ClientOnly
    $t.Enabled = $true
    $t.Subscription = $subscription
    if ($delay) { $t.Delay = $delay }
    return $t
}

# 事件查询里的源名用单引号，PowerShell 单引号字符串内写成两个单引号
$q1 = '<QueryList><Query Id="0" Path="System"><Select Path="System">*[System[Provider[@Name=''Microsoft-Windows-Kernel-Power''] and (EventID=105)]]</Select></Query></QueryList>'
$q2 = '<QueryList><Query Id="0" Path="System"><Select Path="System">*[System[Provider[@Name=''Microsoft-Windows-Power-Troubleshooter''] and (EventID=1)]]</Select></Query></QueryList>'
# Winlogon 7001 = 用户登录完成通知。用它代替 AtLogOn 触发器：标准用户也能注册，
# 且不受 HKCU Run 键被「启动应用」开关禁用影响。
$q3 = '<QueryList><Query Id="0" Path="System"><Select Path="System">*[System[Provider[@Name=''Microsoft-Windows-Winlogon''] and (EventID=7001)]]</Select></Query></QueryList>'

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew `
    -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 2)

Remove-All

Register-ScheduledTask -TaskName $TaskEvent `
    -Trigger @((New-EventTrigger $q1), (New-EventTrigger $q2)) `
    -Action (New-ScheduledTaskAction -Execute $exe -Argument '--auto') `
    -Settings $settings `
    -Description '电源状态变化时自动切换屏幕刷新率' `
    -Force | Out-Null

# 登录对齐任务：延迟一点等桌面/显示驱动就绪，静默执行（--silent 走 logon 来源，不受去重限制）
$logonDelay = 'PT' + $LogonDelaySeconds + 'S'
$logonTrigger = New-EventTrigger $q3 $logonDelay
Register-ScheduledTask -TaskName $TaskLogon `
    -Trigger @($logonTrigger) `
    -Action (New-ScheduledTaskAction -Execute $exe -Argument '--auto --silent') `
    -Settings $settings `
    -Description '登录后静默对齐屏幕刷新率（Winlogon 7001 事件触发，覆盖关电再开机）' `
    -Force | Out-Null

Set-ItemProperty -Path $RunKey -Name $RunName -Value ('"' + $exe + '" --auto --silent')
Clear-StartupApproved
Write-Host "已设置登录自启: $RunName"

Write-Host ''
$t = Get-ScheduledTask -TaskName $TaskEvent
$tl = Get-ScheduledTask -TaskName $TaskLogon
Write-Host "计划任务 $TaskEvent"
Write-Host "  触发器数          : $($t.Triggers.Count)"
Write-Host "  电池供电也启动    : $(-not $t.Settings.DisallowStartIfOnBatteries)      <-- 必须 True"
Write-Host "  电池供电不中止    : $(-not $t.Settings.StopIfGoingOnBatteries)"
Write-Host "  多实例策略        : $($t.Settings.MultipleInstances)"
Write-Host "  动作              : $($t.Actions[0].Execute) $($t.Actions[0].Arguments)"
Write-Host "计划任务 $TaskLogon（登录对齐）"
Write-Host "  触发器数          : $($tl.Triggers.Count)"
Write-Host "  触发延迟          : $($tl.Triggers[0].Delay)"
Write-Host "  动作              : $($tl.Actions[0].Execute) $($tl.Actions[0].Arguments)"
Write-Host "登录自启项"
Write-Host "  $RunName          : $((Get-ItemProperty -Path $RunKey -Name $RunName).$RunName)"
$sa = (Get-ItemProperty -Path $StartupApprovedRun -Name $RunName -ErrorAction SilentlyContinue).$RunName
if ($null -eq $sa) { $saText = '无（Run 键已启用）' }
elseif (([byte[]]$sa)[0] -eq 0x02) { $saText = '02（已启用）' }
else { $saText = "$([BitConverter]::ToString([byte[]]$sa))（被启动项管理软件重新禁用，登录任务仍会兜底）" }
Write-Host "  启动禁用标记      : $saText"
Write-Host ''
Write-Host '完成。手动触发一次验证：'
Write-Host '  Start-ScheduledTask -TaskName RefreshGuard'
Write-Host '  Start-ScheduledTask -TaskName RefreshGuard-Logon'
