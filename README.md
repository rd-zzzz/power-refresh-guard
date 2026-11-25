<p align="center">
  <img src="assets/refreshguard-128.png" alt="RefreshGuard" width="96">
</p>

# RefreshGuard（断电刷新率切换）

宿舍 23:30 断电后自动把内屏从 120Hz 降到 60Hz，来电后自动升回 120Hz。
**零常驻**：平时没有任何进程，只在电源状态变化或登录时被计划任务拉起，活几秒到十几秒自行退出。
"断电降频 → 关机 → 来电开机" 也有兜底：登录后 15 秒由 `RefreshGuard-Logon` 任务静默对齐一次，
因为开机时已插电不会产生电源事件。另有一条 HKCU Run 键作为第二路径，但它可能被启动项管理软件禁用（见坑 4）。

## 环境要求

- Windows 10 / 11 x64
- 安装与运行不需要管理员权限
- 运行时依赖系统自带的 .NET Framework 4.x（Win10/11 预装），无需单独安装
- 重新编译只需系统自带的 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`

## 已经装好了吗

装好了就是这三条：

```
计划任务  RefreshGuard         2 个事件触发器（Kernel-Power 105 / Power-Troubleshooter 1）
计划任务  RefreshGuard-Logon   1 个事件触发器（Winlogon 7001），延迟 15 秒静默对齐
注册表项  HKCU\...\Run\RefreshGuard   登录时静默对齐（第二路径）
```

确认命令：

```powershell
Get-ScheduledTask -TaskName RefreshGuard, RefreshGuard-Logon |
    Select-Object TaskName, State, Triggers
Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Run -Name RefreshGuard
# 03 开头的值 = Run 键被启动项管理软件禁用（见坑 4），不影响 RefreshGuard-Logon
Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run `
    -Name RefreshGuard -ErrorAction SilentlyContinue
```

## 文件

| 文件 | 作用 |
|---|---|
| `RefreshGuard.cs` | 主程序，单文件，C# 5（系统自带 csc.exe 编译，无运行时依赖） |
| `RefreshGuard.exe` | 编译产物，便携副本（`install.ps1` 的回退源），克隆后可直接用 |
| `build.ps1` | 编译 → `dist\RefreshGuard.exe`，并同步根目录便携副本 |
| `install.ps1` | 安装 / `-Uninstall` 卸载 |
| `test.ps1` | 手动测试入口 |
| `启动RefreshGuard.bat` | 双击：注册自启动 + 立即对齐一次 |
| `结束RefreshGuard.bat` | 双击：确认后停用自启动并结束实例 |
| `config.ini` | 配置（`state.ini` / `logs\` 运行时生成，不入库） |
| `assets\refreshguard.ico` | 应用图标，`build.ps1` 以 `/win32icon` 嵌入 exe |
| `assets\refreshguard-*.png` | README 图标（16–256 多尺寸） |
| `assets\icon-refreshguard*.svg` | 图标矢量源（`logo.svg` 的琥珀闪电变体） |

运行时目录（配置、状态、日志都在这里，重装不丢配置）：

```
<运行目录>\
├─ RefreshGuard.exe
├─ config.ini
├─ state.ini        运行时生成，不入库
└─ logs\yyyy-MM.log 运行时生成，不入库
```

> 目录是**便携**的：配置、状态、日志都跟着 exe 走。整个文件夹可以随意搬到别的盘或别的机器，
> 搬完只要重跑一次 `install.ps1`（或用 `-InstallDir '新路径'`）把计划任务和登录自启指向新位置。
> 搬移后 `dist\` 通常不存在，脚本会自动回退用根目录已有的 `RefreshGuard.exe`，不必先重编。

## 快速开始

方式一（不想开终端）：双击 `启动RefreshGuard.bat`，注册自启动并立即对齐一次刷新率。

方式二（命令行）：

```powershell
pwsh -File install.ps1          # 安装 / 重装（自动注册两个计划任务和 Run 键）
pwsh -File test.ps1 -Status     # 确认配置与运行状态
```

安装后不需要做别的，等待下一次电源事件或登录即可。想立刻验证：

```powershell
Start-ScheduledTask -TaskName RefreshGuard
Start-ScheduledTask -TaskName RefreshGuard-Logon
Get-Content '.\logs\2026-11.log' -Tail 10   # 在运行目录下执行
```

## 双击脚本

不想开终端时直接双击这两个，都在 exe 同目录，路径自动跟随文件夹搬移：

| 脚本 | 作用 |
|---|---|
| `启动RefreshGuard.bat` | 注册计划任务 + 登录自启项（会先清掉旧路径残留），然后立即静默对齐一次刷新率 |
| `结束RefreshGuard.bat` | 确认后结束运行中的实例、移除计划任务与登录自启项 |

两者都只是 `install.ps1` 的包装，不重复实现注册逻辑。结束后再点一次「启动」即可完全恢复，
配置、状态、日志全程保留。脚本优先用 `pwsh`，没有则回退 `powershell`。

## 常用命令

```powershell
pwsh -File test.ps1 -List             # 当前模式 + 可用刷新率
pwsh -File test.ps1 -Status           # 配置 + 状态 + 运行目录
pwsh -File test.ps1 -DryRun -Battery  # 只算决策，不动屏幕
pwsh -File test.ps1 -Demo             # 弹一次横幅演示，不会真的切换
pwsh -File test.ps1 -Battery -Now     # 立即切 60Hz（会黑屏 1~2 秒）
pwsh -File test.ps1 -Ac -Now          # 立即切回 120Hz
Start-ScheduledTask -TaskName RefreshGuard          # 手动模拟一次"事件触发"
Start-ScheduledTask -TaskName RefreshGuard-Logon    # 手动模拟一次"登录触发"
```

> 这是个 `winexe`，从终端直接运行看不到输出（它只在有父控制台时才会写回终端）。
> 看结果请读运行目录 `logs\` 里最新的日志，`test.ps1` 会自动附上日志尾巴。

## 命令行标志

`test.ps1` 只是这些标志的包装，直接调 exe 时可用全集：

| 标志 | 作用 |
|---|---|
| `--list` | 当前模式、可用刷新率、供电 / 锁屏 / 全屏状态 |
| `--status` | 配置 + state + 运行目录 |
| `--battery` / `--ac` | 手动模拟供电状态，**全程不写 state** |
| `--dry-run` | 只算决策并打印，不动屏幕 |
| `--now` | 跳过倒计时立即切换，**同时绕过锁屏与全屏检查** |
| `--silent` | 静默直接切，不弹横幅 |
| `--banner-demo` | 弹一次横幅演示，任何情况都不真切换 |
| `--auto` | 计划任务与 Run 键传入的占位标志，无独立分支；配 `--silent` 时落到登录路径，否则落到事件路径 |

两点容易误判的行为：

- `--list` / `--status` 在抢单实例互斥体之前就返回，所以横幅挂着时也能查状态
- `--silent` 受 `silentOnLogon` 反向控制：该配置为 `false` 时，登录触发也会照常弹横幅

## 配置 `config.ini`

```ini
batteryHz=60
acHz=120
delaySeconds=10
notifications=true
silentOnLogon=true
skipWhenFullscreen=true
```

改完不用重启任何东西，下次事件触发时生效。

## 登录对齐（关机再开机）

`RefreshGuard-Logon` 订阅 System 日志的 Winlogon 7001（用户登录完成），延迟 15 秒后执行
`RefreshGuard.exe --auto --silent`（延迟可在安装时用 `-LogonDelaySeconds` 调整）。它专门覆盖这条链路：

断电 → 切到 60Hz（`CDS_UPDATEREGISTRY` 会写进注册表）→ 正常关机 → 来电 → 开机 → 登录
→ 15 秒后重新读供电与刷新率 → 若已接通电源但仍是 60Hz，静默切回 120Hz。

为什么不用 AtLogOn：见坑 2。为什么还留 Run 键：两条独立路径，任何一条能跑就够。
这台机器上荣耀电脑管家会在几秒内把新写入的 Run 项重新标成禁用，所以实际起作用的是计划任务（见坑 4）。

验证（不会重启，只手动跑一次任务）：

```powershell
Start-ScheduledTask -TaskName RefreshGuard-Logon
Get-Content '.\logs\2026-11.log' -Tail 10   # 在运行目录下执行
```

## 怎么判断"要不要动手"

每次被拉起时，按这个顺序判断（`--list` / `--status` 在此之前就返回，不参与）。
登录来源有两个入口：Run 键在登录时立即执行，`RefreshGuard-Logon` 在 Winlogon 7001 之后延迟 15 秒执行；
两者都传 `--silent`（仍受 `silentOnLogon` 控制）。登录路径在开始判断前会先等显示模式可读，最多 10 秒。

1. 定触发来源：`--battery` / `--ac` 为 manual，`--silent` 为 logon，其余为 event
2. 抢单实例互斥体，已有实例在跑就退出（返回码 2）
3. 去重：**仅 event 来源**且 state 非空时比对供电模式，模式与刷新率都没变才退出。这一条消化掉了事件连发（实测有过 04:47、04:48、04:50 三连发）。模式没变但刷新率漂移（驱动唤醒后重置、用户手动改过）会继续走校正链。manual 与 logon 不受此限，所以登录对齐每次都会真的校准
4. 当前刷新率已经等于目标 → 写 `already` 退出，不弹窗打扰
5. 目标刷新率在当前分辨率不可用 → 回退到最接近的可用值并记警告
6. `--dry-run` → 打印决策后退出
7. `--now` → 立即切换，绕过后面所有闸门
8. 静默模式或锁屏 → 直接切，不弹窗（弹了也看不见）
9. 全屏独占（`SHQueryUserNotificationState` 返回忙 / D3D 全屏 / 演示）→ 跳过本次并记日志，**不写 state**
10. `notifications=false` → 直接切
11. 否则弹右下角横幅，10 秒倒计时，点「取消」保留原刷新率，点「立即切换」当场切
12. 倒计时期间电源又变回去 → 本次作废，不写 state
13. 横幅被 Alt+F4 / 注销意外关闭 → 本次不作数，不写 state，不会被当成用户取消

落地前复核：倒计时到期或点「立即切换」时，会再读一次真实供电状态，与本次决策的模式不一致就作废。这一步兜住了事件通知的竞态窗口（`BeginInvoke` 投递可能晚于最后一次 tick）。手动模拟不复核，因为它的模式是假的。

切换本身：`CDS_TEST` 校验 → `ChangeDisplaySettingsEx` → 回读确认。失败 300ms 后重试，
普通路径共 2 次尝试，登录路径共 4 次。

### state.ini 的 reason 语义

| reason | 含义 |
|---|---|
| `already` | 当前已是目标刷新率 |
| `applied` | 切换成功 |
| `cancelled` | 用户点了取消 |
| `failed` | 切换尝试全部失败（普通 2 次，登录最多 4 次） |

四处有意为之的不对称，改代码前注意：

- **取消会写 state**，所以同模式的后续事件会被第 3 步去重，不再重复弹窗（用户已表态）
- **全屏跳过、倒计时作废、意外关闭都不写 state**，所以下次事件仍会重试
- **手动模拟全程不写 state**，避免污染去重基准
- 横幅 `Result` 默认值是 `Dismissed`（不动作）而非 `Cancelled`，将来新增关闭路径会默认继承「不写 state」，不会误记成用户表态

## 四个踩过的坑（改代码前务必看）

**1. 不能用 `schtasks /create /sc ONEVENT` 建任务。**
它把 `DisallowStartIfOnBatteries` 默认设成 `True`。断电那一刻正好是电池供电，任务会被系统拒绝启动，
现象是"什么都没发生"，而且事件日志里看不到异常。必须走 `Register-ScheduledTask` 并显式
`-AllowStartIfOnBatteries -DontStopIfGoingOnBatteries`。

**2. 不能用 `AtLogOn` 触发器。**
标准用户注册带 AtLogOn 的计划任务会直接"拒绝访问"。所以登录对齐走两条替代路径：
Winlogon 7001 事件订阅的 `RefreshGuard-Logon` 任务 + HKCU 的 Run 键。

**3. 编码有三个落点：`.bat` 纯 ASCII、`.ps1` 带 UTF-8 BOM、exe 跟随控制台代码页。**
cmd.exe 按系统 OEM 代码页（本机 936）逐字节预读批处理，文件里放 UTF-8 中文会破坏行解析，
表现为「'xxx' 不是内部或外部命令」，而且在文件开头写 `chcp 65001` 也救不回来。
所以两个 bat 只用 ASCII，中文输出全部交给 `install.ps1`。

Windows PowerShell 5.1 读**无 BOM** 的 ps1 时按 ANSI 解码，中文会吃掉引号，
报「字符串缺少终止符」「`<` 运算符保留为将来使用」等解析错误；pwsh 7 默认 UTF-8，所以这个坑只在 5.1 暴露。
三个 ps1 都已加 UTF-8 BOM，5.1 与 pwsh 7 实测 tokenize 均 0 错误。

exe 自己也曾硬编码 `UTF8Encoding` 写控制台，在 cp936 的真实双击窗口里整片乱码
（git bash 恰好按 UTF-8 解，掩盖了问题）。`Con.Init` 已改为跟随 `Console.OutputEncoding`，
控制台是什么代码页就输出什么编码。日志文件仍固定 UTF-8，不受影响。
`RefreshGuard.cs` 源码仍是 UTF-8 无 BOM，由 `build.ps1` 的 `/codepage:65001` 处理。

**4. 只写 HKCU Run 键，不等于登录时会执行。**
Windows 会把启动项的启用状态另存在
`HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run`：
首字节 `02` 是启用，`03` 是禁用。被「设置 > 应用 > 启动」关过、或被电脑管家/优化工具
禁过之后，Run 值还在，但登录时根本不会跑，而且没有任何提示。

这台机器上的荣耀电脑管家（PCManager）会在几秒内把新写入的 `RefreshGuard` 记录重新改成
`03-00-00-00-...`，所以 Run 键这条路实际是死的。`install.ps1` 仍会尽量清掉标记
（换机器或关掉管家时有用），但真正保证登录对齐的是 `RefreshGuard-Logon` 任务，
它不经过 StartupApproved。判断方法见上面"确认命令"的第三条：返回 `03-...` 就是被禁用了，
不是程序坏了；只要 `RefreshGuard-Logon` 是 Ready，开机登录仍会切回 120Hz。

## 已验证 / 未验证

已验证：

- 枚举与切换：3120×2080 下只有 60 / 120 两档，120→60 与 60→120 双向真实切换成功
- 横幅渲染与倒计时（截图确认版式、中文不糊、不抢焦点）
- 计划任务能正常拉起程序，程序执行后自行退出，无残留进程
- 任务设置：`DisallowStartIfOnBatteries=False`、`StopIfGoingOnBatteries=False`、`IgnoreNew`
- `RefreshGuard-Logon` 手动触发：AC + 60Hz → 120Hz，state 写 `applied`，任务返回 0
- Winlogon 7001 订阅已注册，触发器 XML 带 `Delay=PT15S`

未验证（需要真实断电 / 重启才能测）：

- 真实"断电 → 60Hz → 关机 → 来电 → 开机登录"全链路：需要一次真实重启，
  确认 7001 触发后 15 秒确实自动切回 120Hz
- 真实拔电时的事件→任务→切换全链路耗时
- 日志显示 105 事件比 23:30 晚 2~3 分钟，到底是断电实际时间就在 23:32，还是 Windows 记录延迟
  测法：半夜拔电后对比下面三个时间戳
  ```powershell
  Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Microsoft-Windows-Kernel-Power'; Id=105} -MaxEvents 1 | Select-Object TimeCreated
  # 与日志里的 invoked / applied 行时间比较
  ```

## 卸载

```powershell
pwsh -File install.ps1 -Uninstall
```

然后删除本工具所在文件夹即可（配置、状态、日志都在里面）。
卸载只移除两个计划任务和 HKCU Run 键，不删除文件。

## 已知限制

- 全屏游戏时拔电：本次跳过，不补做（要"等退出全屏再切"就必须常驻，和零常驻前提冲突）
- 锁屏 / 未登录会话：只能静默切换，没有交互
- 断电瞬间若系统直接休眠，任务不会跑，靠下次登录的 `RefreshGuard-Logon` 任务对齐
- 切换到 60Hz 时画面黑 1~2 秒，是显示器重新协商链路，正常现象
- 横幅与切换都只作用于**主显示器**（`Screen.PrimaryScreen` / `ChangeDisplaySettingsEx(null, ...)`）。外接屏被设为主屏时，动的就不是内屏
- 读不到显示模式（`EnumDisplaySettings` 失败）时记日志并以退出码 1 结束，不会拿全零结构去切换
- 未处理异常统一记 WARN 后退出，不弹 WinForms 默认错误对话框（零常驻前提）
- 供电状态读不到时按"已接通电源"处理（`Session.GetMode()` 的兜底），不会降频
- 任务被第三方"优化工具"禁用后完全不工作，且不会提示；此时若 Run 键也被 StartupApproved 挡住，登录对齐就失效
- 荣耀电脑管家会实时禁用 HKCU Run 键；目前不影响计划任务，但换机器/换管家版本时需要复查 `RefreshGuard-Logon` 是否仍是 Ready