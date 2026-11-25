<p align="center">
  <img src="assets/refreshguard-128.png" alt="RefreshGuard" width="96">
</p>

# RefreshGuard（断电刷新率切换）

宿舍断电后把内屏从 120Hz 降到 60Hz，来电后升回。平时没有常驻进程，只有计划任务在电源状态变化或登录时拉起程序，执行完自行退出。

触发来源：

- `Kernel-Power` 105、`Power-Troubleshooter` 1：电源变化，弹横幅倒计时 10 秒，可取消。
- `Winlogon` 7001：登录后延迟 15 秒静默校准，处理“断电降到 60Hz → 关机 → 来电开机”。
- HKCU Run 键：登录时再执行一次，作为第二条路径。

## 环境要求

Windows 10/11 x64。安装和运行不需要管理员，运行时依赖系统自带的 .NET Framework 4.x。重新编译用系统自带的 `csc.exe`。

## 安装与卸载

```powershell
pwsh -File install.ps1              # 注册计划任务和 Run 键
pwsh -File install.ps1 -Uninstall   # 移除任务和 Run 键，保留配置和日志
```

也可以直接双击 `启动RefreshGuard.bat` / `结束RefreshGuard.bat`。登录对齐的延迟可用 `-LogonDelaySeconds` 调整，默认 15 秒。

安装后手动跑一次任务确认：

```powershell
Start-ScheduledTask -TaskName RefreshGuard
Start-ScheduledTask -TaskName RefreshGuard-Logon
Get-Content '.\logs\2026-11.log' -Tail 10
```

## 文件

| 文件 | 作用 |
|---|---|
| `RefreshGuard.cs` | 主程序，单文件 C# 5 |
| `RefreshGuard.exe` | 编译产物，便携副本 |
| `build.ps1` | 编译到 `dist\`，并同步根目录 exe |
| `install.ps1` | 安装 / 卸载 |
| `test.ps1` | 手动测试入口 |
| `启动RefreshGuard.bat` / `结束RefreshGuard.bat` | 双击启停 |
| `config.ini` | 配置 |
| `assets\refreshguard.ico` | exe 图标 |

`state.ini`、`logs\`、`dist\` 是运行时产物，已在 `.gitignore` 中排除。配置、状态、日志都跟着 exe 走，整个目录可以搬到别处，搬完重跑一次 `install.ps1` 即可。

## 配置

```ini
batteryHz=60
acHz=120
delaySeconds=10
notifications=true
silentOnLogon=true
skipWhenFullscreen=true
```

改完不需要重启，下次触发时生效。

## 命令行

`test.ps1` 常用参数：

```powershell
pwsh -File test.ps1 -List
pwsh -File test.ps1 -Status
pwsh -File test.ps1 -DryRun -Battery
pwsh -File test.ps1 -Demo
pwsh -File test.ps1 -Battery -Now
pwsh -File test.ps1 -Ac -Now
```

exe 的完整参数：

| 参数 | 作用 |
|---|---|
| `--list` | 当前模式、可用刷新率、供电/锁屏/全屏状态 |
| `--status` | 配置、状态、运行目录 |
| `--battery` / `--ac` | 模拟供电状态，不写 state |
| `--dry-run` | 只打印决策，不动屏幕 |
| `--now` | 立即切换，跳过锁屏和全屏检查 |
| `--silent` | 静默切换，不弹横幅 |
| `--banner-demo` | 横幅演示，不实际切换 |
| `--auto` | 计划任务和 Run 键使用的占位参数 |

`--list` / `--status` 不抢单实例互斥体，横幅显示时也能查。`silentOnLogon=false` 时，登录触发也会弹横幅。

## 判断流程

1. 确定来源：manual / logon / event。
2. 抢单实例互斥体，已有实例则退出。
3. event 来源且 state 存在时，比较供电模式和刷新率，都没变才退出。
4. 当前刷新率已是目标值，写 `already` 退出。
5. 目标刷新率不可用，取最接近的可用值并记警告。
6. `--dry-run` 到此退出。
7. `--now` 立即切换。
8. 静默或锁屏时直接切换。
9. 全屏独占时跳过，不写 state。
10. 否则弹横幅倒计时，期间供电变化、窗口被关闭或用户取消，按对应规则处理。

切换时先 `CDS_TEST` 校验，再 `ChangeDisplaySettingsEx`，最后回读确认。失败 300ms 后重试，普通路径 2 次，登录路径 4 次。

`state.ini` 的 `reason`：

| reason | 含义 |
|---|---|
| `already` | 已是目标刷新率 |
| `applied` | 切换成功 |
| `cancelled` | 用户取消 |
| `failed` | 全部尝试失败 |

取消会写 state；全屏跳过、倒计时作废、横幅意外关闭都不写；手动模拟不写。

## 实现注意

- 不用 `schtasks /create /sc ONEVENT`：它默认禁止电池供电时启动，断电时任务不会跑。必须用 `Register-ScheduledTask` 并加 `-AllowStartIfOnBatteries -DontStopIfGoingOnBatteries`。
- 不用 `AtLogOn`：标准用户注册会拒绝访问。登录对齐靠 Winlogon 7001 任务和 Run 键。
- 编码：`.bat` 只用 ASCII，`.ps1` 带 UTF-8 BOM，exe 控制台输出跟随父控制台代码页。
- Run 键可能被启动项管理软件写进 `StartupApproved\Run` 禁用标记（首字节 `03`），此时登录不会执行。真正兜底的是 `RefreshGuard-Logon` 任务。

## 已知限制

- 只处理主显示器。
- 全屏时跳过，不补做。
- 断电瞬间直接休眠时任务不会跑，靠下次登录校准。
- 切到 60Hz 时会黑屏 1~2 秒。
- 任务被第三方工具禁用后不会提示；Run 键被禁用不影响 `RefreshGuard-Logon`。