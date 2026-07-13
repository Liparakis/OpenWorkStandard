Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "common-local-verifier.ps1")
$repoRoot = Resolve-OwsRepoRoot -StartDirectory $PSScriptRoot
Set-Location $repoRoot

$runtimeInfo = Get-OwsVerifierRuntimeInfo -RepoRoot $repoRoot
Ensure-OwsVerifierBuild -RuntimeInfo $runtimeInfo

$state = Get-VerifierState -RuntimeInfo $runtimeInfo
if ($state.State -eq "running") {
    throw "Verifier is already running at $($runtimeInfo.BaseUrl). Use the status or stop helper first."
}

if ($state.State -eq "port_in_use" -or $state.State -eq "unreachable") {
    throw "Verifier port $($runtimeInfo.Port) is already in use or unmanaged. Resolve the conflict before running the verifier."
}

Write-Host "Starting local PostgreSQL..."
$composeErrorAction = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    $composeOutput = docker compose -f docker-compose.local.yml up -d 2>&1
}
finally {
    $ErrorActionPreference = $composeErrorAction
}
$composeExitCode = $LASTEXITCODE
$composeOutput | Out-Host
if (-not (Wait-OwsPostgresReady)) {
    throw "PostgreSQL did not become ready on localhost:5432 (docker compose exit code $composeExitCode). Start docker-compose.local.yml or point OWS_VERIFIER_CONNECTION_STRING at a reachable PostgreSQL instance."
}

Write-Host "Running verifier migrations..."
$env:VerifierStorage__Provider = "postgres"
$env:VerifierStorage__PostgresConnectionString = $runtimeInfo.ConnectionString
dotnet $runtimeInfo.VerifierDllPath migrate
if ($LASTEXITCODE -ne 0) {
    throw "Verifier migration failed. Check PostgreSQL availability, OWS_VERIFIER_CONNECTION_STRING, and the verifier logs helper."
}

Write-Host "Starting verifier server on $($runtimeInfo.BaseUrl) ..."
$env:ASPNETCORE_URLS = $runtimeInfo.BaseUrl
dotnet $runtimeInfo.VerifierDllPath
