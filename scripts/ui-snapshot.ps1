param(
    [string]$Output = "",
    [int]$Width = 1360,
    [int]$Height = 840,
    [switch]$Settings,
    [switch]$Strategy,
    [switch]$Wukong
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $framework "csc.exe"
$outDir = Join-Path $root "obj\tests"
$exe = Join-Path $outDir "UiSnapshot.exe"
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root "docs\界面预览.png"
}
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$OutputDir = Split-Path -Parent $Output
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

& $csc `
    /nologo `
    /target:exe `
    /platform:anycpu `
    /codepage:65001 `
    /out:$exe `
    /reference:"$(Join-Path $framework 'System.dll')" `
    /reference:"$(Join-Path $framework 'System.Core.dll')" `
    /reference:"$(Join-Path $framework 'System.Drawing.dll')" `
    /reference:"$(Join-Path $framework 'System.Windows.Forms.dll')" `
    /reference:"$(Join-Path $framework 'System.Web.Extensions.dll')" `
    "$(Join-Path $root 'src\Models.cs')" `
    "$(Join-Path $root 'src\AppSettings.cs')" `
    "$(Join-Path $root 'src\CombatModels.cs')" `
    "$(Join-Path $root 'src\CombatDemoTrainer.cs')" `
    "$(Join-Path $root 'src\AdaptiveCombatAgent.cs')" `
    "$(Join-Path $root 'src\PlaybackEventSelector.cs')" `
    "$(Join-Path $root 'src\ProjectStore.cs')" `
    "$(Join-Path $root 'src\InputEngine.cs')" `
    "$(Join-Path $root 'src\ScreenRecorder.cs')" `
    "$(Join-Path $root 'src\StrategyEngine.cs')" `
    "$(Join-Path $root 'src\UiKit.cs')" `
    "$(Join-Path $root 'src\SettingsForm.cs')" `
    "$(Join-Path $root 'src\StrategyRulesForm.cs')" `
    "$(Join-Path $root 'src\WukongAgentForm.cs')" `
    "$(Join-Path $root 'src\MainForm.cs')" `
    "$(Join-Path $root 'tests\UiSnapshot.cs')"

if ($LASTEXITCODE -ne 0) {
    throw "UI snapshot compilation failed."
}

if ($Settings) {
    & $exe settings $Output
} elseif ($Strategy) {
    & $exe strategy $Output
} elseif ($Wukong) {
    & $exe wukong $Output
} else {
    & $exe $Output $Width $Height
}
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
