param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "artifacts\ows-setup"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$projectPath = Join-Path $repoRoot "src\Ows.Setup\Ows.Setup.csproj"
$cliProjectPath = Join-Path $repoRoot "src\Ows.Cli\Ows.Cli.csproj"
$outputRoot = Join-Path $repoRoot $OutputPath
$payloadRoot = Join-Path $env:TEMP "ows-setup-payload-$([guid]::NewGuid().ToString('N') )"
$payloadArchive = Join-Path $env:TEMP "ows-setup-payload-$([guid]::NewGuid().ToString('N') ).zip"

if (-not [System.Environment]::Is64BitOperatingSystem)
{
    throw "The OWS Setup executable requires a 64-bit Windows host."
}

try
{
    if (Test-Path -LiteralPath $outputRoot)
    {
        Remove-Item -LiteralPath $outputRoot -Recurse -Force
    }

    dotnet publish $projectPath --configuration $Configuration --runtime win-x64 --self-contained true `
        -p:OwsAgentPayload=true -p:PublishSingleFile=false --output $payloadRoot --nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw "OWS Agent payload publish failed."
    }

    $cliOutput = Join-Path $payloadRoot "cli"
    dotnet publish $cliProjectPath --configuration $Configuration --runtime win-x64 --self-contained true `
        -p:OwsCliExecutableName=true -p:PublishSingleFile=false --output $cliOutput --nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw "OWS CLI payload publish failed."
    }

    Compress-Archive -Path (Join-Path $payloadRoot '*') -DestinationPath $payloadArchive -CompressionLevel Optimal

    dotnet publish $projectPath --configuration $Configuration --runtime win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:PayloadArchivePath=$payloadArchive --output $outputRoot --nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw "OWS Setup publish failed."
    }

    $setupPath = Join-Path $outputRoot "Ows.Setup.exe"
    if (-not (Test-Path -LiteralPath $setupPath))
    {
        throw "OWS Setup publish did not produce '$setupPath'."
    }

    Write-Host "Built $setupPath with embedded payload"
}
finally
{
    if (Test-Path -LiteralPath $payloadRoot)
    {
        Remove-Item -LiteralPath $payloadRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $payloadArchive)
    {
        Remove-Item -LiteralPath $payloadArchive -Force
    }
}
