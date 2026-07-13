param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "artifacts\ows-setup"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$projectPath = Join-Path $repoRoot "src\Ows.Setup\Ows.Setup.csproj"
$outputRoot = Join-Path $repoRoot $OutputPath

if (-not [System.Environment]::Is64BitOperatingSystem) {
    throw "The OWS Setup executable requires a 64-bit Windows host."
}

if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

dotnet publish $projectPath --configuration $Configuration --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $outputRoot --nologo
if ($LASTEXITCODE -ne 0) {
    throw "OWS Setup publish failed."
}

$setupPath = Join-Path $outputRoot "Ows.Setup.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "OWS Setup publish did not produce '$setupPath'."
}

Write-Host "Built $setupPath"
