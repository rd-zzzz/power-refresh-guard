# 编译 RefreshGuard
# 用法: pwsh -File build.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "找不到 csc.exe: $csc" }

$src  = Join-Path $root 'RefreshGuard.cs'
$dist = Join-Path $root 'dist'
if (-not (Test-Path -LiteralPath $src)) { throw "找不到源码: $src" }
if (-not (Test-Path -LiteralPath $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

$out = Join-Path $dist 'RefreshGuard.exe'

# 独立仓库里图标在 .\assets\；子目录并入上级项目时回退用 ..\assets\
$ico = Join-Path $root 'assets\refreshguard.ico'
if (-not (Test-Path -LiteralPath $ico)) {
    $ico = Join-Path (Split-Path -Parent $root) 'assets\refreshguard.ico'
}
$icoArg = @()
if (Test-Path -LiteralPath $ico) { $icoArg = @("/win32icon:$ico") }

# /codepage:65001 必须加：源码是 UTF-8 无 BOM，csc 默认按系统 ANSI 代码页读，中文界面串会变乱码
& $csc /nologo /target:winexe /platform:x64 /optimize /codepage:65001 `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    @icoArg /out:$out $src

if ($LASTEXITCODE -ne 0) { throw "编译失败，exit $LASTEXITCODE" }

# 根目录 exe 是便携副本（install.ps1 的回退源），编译后同步，避免仓库里两份产物不一致
Copy-Item -LiteralPath $out -Destination (Join-Path $root 'RefreshGuard.exe') -Force

$sizeKb = [math]::Round((Get-Item -LiteralPath $out).Length / 1KB, 1)
Write-Host "编译成功: $out ($sizeKb KB)，已同步根目录便携副本"
