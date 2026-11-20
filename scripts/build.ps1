param(
    [switch]$Release
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path -LiteralPath $csc)) {
    throw "C# compiler not found: $csc"
}

$sourceFiles = @(
    (Join-Path $root "src\AssemblyInfo.cs"),
    (Join-Path $root "src\Models.cs"),
    (Join-Path $root "src\AppSettings.cs"),
    (Join-Path $root "src\CombatModels.cs"),
    (Join-Path $root "src\CombatDemoTrainer.cs"),
    (Join-Path $root "src\AdaptiveCombatAgent.cs"),
    (Join-Path $root "src\PlaybackEventSelector.cs"),
    (Join-Path $root "src\ProjectStore.cs"),
    (Join-Path $root "src\InputEngine.cs"),
    (Join-Path $root "src\ScreenRecorder.cs"),
    (Join-Path $root "src\StrategyEngine.cs"),
    (Join-Path $root "src\UiKit.cs"),
    (Join-Path $root "src\SettingsForm.cs"),
    (Join-Path $root "src\StrategyRulesForm.cs"),
    (Join-Path $root "src\WukongAgentForm.cs"),
    (Join-Path $root "src\MainForm.cs"),
    (Join-Path $root "src\Program.cs")
)

foreach ($source in $sourceFiles) {
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Missing source file: $source"
    }
}

$binDir = Join-Path $root "bin"
$objDir = Join-Path $root "obj"
New-Item -ItemType Directory -Force -Path $binDir, $objDir | Out-Null

$output = Join-Path $binDir "GameInputLab.exe"
$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$references = @(
    (Join-Path $framework "System.dll"),
    (Join-Path $framework "System.Core.dll"),
    (Join-Path $framework "System.Drawing.dll"),
    (Join-Path $framework "System.Windows.Forms.dll"),
    (Join-Path $framework "System.Web.Extensions.dll")
)

$arguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/codepage:65001",
    "/langversion:5",
    "/warn:4",
    "/win32manifest:$(Join-Path $root 'src\app.manifest')",
    "/out:$output"
)

if ($Release) {
    $arguments += "/optimize+"
    $arguments += "/debug:pdbonly"
} else {
    $arguments += "/debug:full"
}

foreach ($reference in $references) {
    $arguments += "/reference:$reference"
}
$arguments += $sourceFiles

Write-Host "Compiling GameInputLab..." -ForegroundColor Cyan
& $csc $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Copy-Item `
    -LiteralPath (Join-Path $root "src\GameInputLab.exe.config") `
    -Destination (Join-Path $binDir "GameInputLab.exe.config") `
    -Force

Write-Host "Build complete: $output" -ForegroundColor Green
