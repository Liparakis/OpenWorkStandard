Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common-local-verifier.ps1")
$repoRoot = Resolve-OwsRepoRoot -StartDirectory $PSScriptRoot
$runtimeInfo = Get-OwsVerifierRuntimeInfo -RepoRoot $repoRoot
$state = Get-VerifierState -RuntimeInfo $runtimeInfo

function Test-CommandAvailable {
    param(
        [string]$Name
    )

    $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

$dotnetAvailable = Test-CommandAvailable -Name "dotnet"
$dockerAvailable = Test-CommandAvailable -Name "docker"
$composeFileExists = Test-Path (Join-Path $repoRoot "docker-compose.local.yml")
$verifierBuilt = Test-Path $runtimeInfo.VerifierDllPath
$postgresReachable = Test-TcpPortOpen -HostName "127.0.0.1" -Port 5432

$checks = [ordered]@{
    RepoRoot = $repoRoot
    DotnetAvailable = $dotnetAvailable
    DockerAvailable = $dockerAvailable
    ComposeFileExists = $composeFileExists
    VerifierBuilt = $verifierBuilt
    PostgresReachable = $postgresReachable
    VerifierState = $state.State
    VerifierMessage = $state.Message
    VerifierBaseUrl = $runtimeInfo.BaseUrl
}

[pscustomobject]$checks | Format-List

if (-not $dotnetAvailable) {
    Write-Host "Action: install .NET 9 SDK or put dotnet on PATH."
}

if (-not $composeFileExists) {
    Write-Host "Action: run this helper from an OWS checkout that contains docker-compose.local.yml."
}

if (-not $verifierBuilt) {
    Write-Host "Action: run dotnet build OWS.sln -nologo, or use start-local-verifier to auto-build."
}

if (-not $postgresReachable) {
    Write-Host "Action: start PostgreSQL with docker compose -f docker-compose.local.yml up -d, or set OWS_VERIFIER_CONNECTION_STRING."
}

if ($state.State -in @("stale_pid", "crashed")) {
    Write-Host "Action: run stop-local-verifier, then start-local-verifier."
}
elseif ($state.State -eq "port_in_use") {
    Write-Host "Action: free port $($runtimeInfo.Port) or set OWS_VERIFIER_BASE_URL."
}
elseif ($state.State -eq "unreachable") {
    Write-Host "Action: inspect logs-local-verifier and confirm the process owns $($runtimeInfo.BaseUrl)."
}
