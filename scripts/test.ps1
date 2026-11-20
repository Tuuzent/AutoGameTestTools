$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$outDir = Join-Path $root "obj\tests"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$testOutput = Join-Path $outDir "StorageTests.exe"
$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
& $csc `
    /nologo `
    /target:exe `
    /platform:anycpu `
    /codepage:65001 `
    /out:$testOutput `
    /reference:"$(Join-Path $framework 'System.dll')" `
    /reference:"$(Join-Path $framework 'System.Core.dll')" `
    /reference:"$(Join-Path $framework 'System.Web.Extensions.dll')" `
    "$(Join-Path $root 'src\Models.cs')" `
    "$(Join-Path $root 'src\PlaybackEventSelector.cs')" `
    "$(Join-Path $root 'src\ProjectStore.cs')" `
    "$(Join-Path $root 'tests\StorageTests.cs')"

if ($LASTEXITCODE -ne 0) {
    throw "Test executable compilation failed."
}

& $testOutput
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$settingsTestOutput = Join-Path $outDir "SettingsTests.exe"
& $csc `
    /nologo `
    /target:exe `
    /platform:anycpu `
    /codepage:65001 `
    /out:$settingsTestOutput `
    /reference:"$(Join-Path $framework 'System.dll')" `
    /reference:"$(Join-Path $framework 'System.Drawing.dll')" `
    /reference:"$(Join-Path $framework 'System.Web.Extensions.dll')" `
    "$(Join-Path $root 'src\AppSettings.cs')" `
    "$(Join-Path $root 'tests\SettingsTests.cs')"

if ($LASTEXITCODE -ne 0) {
    throw "Settings test compilation failed."
}

& $settingsTestOutput
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$inputTestOutput = Join-Path $outDir "InputEngineTests.exe"
& $csc `
    /nologo `
    /target:exe `
    /platform:anycpu `
    /codepage:65001 `
    /out:$inputTestOutput `
    /reference:"$(Join-Path $framework 'System.dll')" `
    /reference:"$(Join-Path $framework 'System.Core.dll')" `
    /reference:"$(Join-Path $framework 'System.Drawing.dll')" `
    /reference:"$(Join-Path $framework 'System.Windows.Forms.dll')" `
    "$(Join-Path $root 'src\Models.cs')" `
    "$(Join-Path $root 'src\InputEngine.cs')" `
    "$(Join-Path $root 'tests\InputEngineTests.cs')"

if ($LASTEXITCODE -ne 0) {
    throw "Input engine test compilation failed."
}

& $inputTestOutput
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$screenTestOutput = Join-Path $outDir "ScreenRecorderTests.exe"
& $csc `
    /nologo `
    /target:exe `
    /platform:anycpu `
    /codepage:65001 `
    /out:$screenTestOutput `
    /reference:"$(Join-Path $framework 'System.dll')" `
    /reference:"$(Join-Path $framework 'System.Core.dll')" `
    /reference:"$(Join-Path $framework 'System.Drawing.dll')" `
    /reference:"$(Join-Path $framework 'System.Windows.Forms.dll')" `
    /reference:"$(Join-Path $framework 'WPF\PresentationCore.dll')" `
    /reference:"$(Join-Path $framework 'WPF\WindowsBase.dll')" `
    /reference:"$(Join-Path $framework 'System.Xaml.dll')" `
    "$(Join-Path $root 'src\Models.cs')" `
    "$(Join-Path $root 'src\ScreenRecorder.cs')" `
    "$(Join-Path $root 'tests\ScreenRecorderTests.cs')"

if ($LASTEXITCODE -ne 0) {
    throw "Screen recorder test compilation failed."
}

& $screenTestOutput
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$strategyTestOutput = Join-Path $outDir "StrategyEngineTests.exe"
& $csc `
    /nologo `
    /target:exe `
    /platform:anycpu `
    /codepage:65001 `
    /out:$strategyTestOutput `
    /reference:"$(Join-Path $framework 'System.dll')" `
    /reference:"$(Join-Path $framework 'System.Drawing.dll')" `
    /reference:"$(Join-Path $framework 'System.Windows.Forms.dll')" `
    "$(Join-Path $root 'src\Models.cs')" `
    "$(Join-Path $root 'src\InputEngine.cs')" `
    "$(Join-Path $root 'src\StrategyEngine.cs')" `
    "$(Join-Path $root 'tests\StrategyEngineTests.cs')"

if ($LASTEXITCODE -ne 0) {
    throw "Strategy engine test compilation failed."
}

& $strategyTestOutput
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$combatTestOutput = Join-Path $outDir "CombatTests.exe"
& $csc `
    /nologo `
    /target:exe `
    /platform:anycpu `
    /codepage:65001 `
    /out:$combatTestOutput `
    /reference:"$(Join-Path $framework 'System.dll')" `
    /reference:"$(Join-Path $framework 'System.Drawing.dll')" `
    /reference:"$(Join-Path $framework 'System.Windows.Forms.dll')" `
    /reference:"$(Join-Path $framework 'System.Web.Extensions.dll')" `
    "$(Join-Path $root 'src\Models.cs')" `
    "$(Join-Path $root 'src\CombatModels.cs')" `
    "$(Join-Path $root 'src\CombatDemoTrainer.cs')" `
    "$(Join-Path $root 'src\InputEngine.cs')" `
    "$(Join-Path $root 'src\AdaptiveCombatAgent.cs')" `
    "$(Join-Path $root 'tests\CombatTests.cs')"

if ($LASTEXITCODE -ne 0) {
    throw "Combat agent test compilation failed."
}

& $combatTestOutput
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
