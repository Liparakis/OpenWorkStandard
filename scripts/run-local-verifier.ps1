Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "common-local-verifier.ps1")
$repoRoot = Resolve-OwsRepoRoot -StartDirectory $PSScriptRoot
Set-Location $repoRoot

$runtimeInfo = Get-OwsVerifierRuntimeInfo -RepoRoot $repoRoot

if (-not (Test-Path $runtimeInfo.VerifierDllPath)) {
    throw "Verifier server build output is missing. Run 'dotnet build OWS.sln -nologo' first."
}

$state = Get-VerifierState -RuntimeInfo $runtimeInfo
if ($state.State -eq "running") {
    throw "Verifier is already running at $($runtimeInfo.BaseUrl). Use the status or stop helper first."
}

if ($state.State -eq "port_in_use" -or $state.State -eq "unreachable") {
    throw "Verifier port $($runtimeInfo.Port) is already in use or unmanaged. Resolve the conflict before running the verifier."
}

Write-Host "Starting local PostgreSQL..."
docker compose -f docker-compose.local.yml up -d
if ($LASTEXITCODE -ne 0 -and -not (Test-TcpPortOpen -HostName "127.0.0.1" -Port 5432)) {
    throw "docker compose failed and PostgreSQL is not reachable on localhost:5432."
}

Write-Host "Running verifier migrations..."
$env:VerifierStorage__Provider = "postgres"
$env:VerifierStorage__PostgresConnectionString = $runtimeInfo.ConnectionString
dotnet $runtimeInfo.VerifierDllPath migrate
if ($LASTEXITCODE -ne 0) {
    throw "Verifier migration failed. Check PostgreSQL availability and connection settings."
}

Write-Host "Starting verifier server on $($runtimeInfo.BaseUrl) ..."
$env:ASPNETCORE_URLS = $runtimeInfo.BaseUrl
dotnet $runtimeInfo.VerifierDllPath
