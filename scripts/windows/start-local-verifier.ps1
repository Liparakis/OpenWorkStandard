Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "common-local-verifier.ps1")
$repoRoot = Resolve-OwsRepoRoot -StartDirectory $PSScriptRoot
Set-Location $repoRoot

$runtimeInfo = Get-OwsVerifierRuntimeInfo -RepoRoot $repoRoot
Ensure-OwsVerifierBuild -RuntimeInfo $runtimeInfo

$state = Get-VerifierState -RuntimeInfo $runtimeInfo
switch ($state.State) {
    "running" {
        Write-Host "Verifier is already running with PID $($state.Pid) at $($runtimeInfo.BaseUrl)."
        exit 0
    }
    "stale_pid" {
        Write-Host "Cleaning stale PID state."
        Remove-Item $runtimeInfo.PidFilePath -Force -ErrorAction SilentlyContinue
    }
    "crashed" {
        Write-Host "Cleaning crashed verifier PID state."
        Remove-Item $runtimeInfo.PidFilePath -Force -ErrorAction SilentlyContinue
    }
    "port_in_use" {
        throw "Verifier port $($runtimeInfo.Port) is already in use. Stop the other process or change OWS_VERIFIER_BASE_URL."
    }
    "unreachable" {
        throw "Verifier endpoint $($runtimeInfo.BaseUrl) is already bound outside the managed lifecycle. Use status/logs or stop the conflicting process first."
    }
}

New-Item -ItemType Directory -Force -Path $runtimeInfo.RuntimeDirectory | Out-Null

Write-Host "Starting local PostgreSQL..."
docker compose -f docker-compose.local.yml up -d
if ($LASTEXITCODE -ne 0 -and -not (Test-TcpPortOpen -HostName "127.0.0.1" -Port 5432)) {
    throw "docker compose failed and PostgreSQL is not reachable on localhost:5432. Start docker-compose.local.yml or point OWS_VERIFIER_CONNECTION_STRING at a reachable PostgreSQL instance."
}

Write-Host "Running verifier migrations..."
$env:VerifierStorage__Provider = "postgres"
$env:VerifierStorage__PostgresConnectionString = $runtimeInfo.ConnectionString
dotnet $runtimeInfo.VerifierDllPath migrate
if ($LASTEXITCODE -ne 0) {
    throw "Verifier migration failed. Check PostgreSQL availability, OWS_VERIFIER_CONNECTION_STRING, and the verifier logs helper."
}

@"
Set-Location '$($runtimeInfo.RepoRoot.Replace("'", "''"))'
`$env:VerifierStorage__Provider = 'postgres'
`$env:VerifierStorage__PostgresConnectionString = '$($runtimeInfo.ConnectionString.Replace("'", "''"))'
`$env:ASPNETCORE_URLS = '$($runtimeInfo.BaseUrl.Replace("'", "''"))'
& dotnet '$($runtimeInfo.VerifierDllPath.Replace("'", "''"))' 1>> '$($runtimeInfo.StdoutLogPath.Replace("'", "''"))' 2>> '$($runtimeInfo.StderrLogPath.Replace("'", "''"))'
exit `$LASTEXITCODE
"@ | Set-Content -Path $runtimeInfo.ChildScriptPath -NoNewline

Write-Host "Starting verifier server in background on $($runtimeInfo.BaseUrl) ..."
$processStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
$processStartInfo.FileName = "powershell.exe"
$escapedChildScriptPath = $runtimeInfo.ChildScriptPath.Replace('"', '""')
$processStartInfo.Arguments =
    "-NoProfile -ExecutionPolicy Bypass -File ""$escapedChildScriptPath"""
$processStartInfo.WorkingDirectory = $runtimeInfo.RepoRoot
$processStartInfo.UseShellExecute = $false
$processStartInfo.CreateNoWindow = $true

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $processStartInfo
if (-not $process.Start()) {
    throw "Verifier background process could not be started."
}

$process.Id | Set-Content -Path $runtimeInfo.PidFilePath -NoNewline

$ready = $false
for ($attempt = 0; $attempt -lt 20; $attempt++) {
    Start-Sleep -Milliseconds 500

    if ($process.HasExited) {
        Remove-Item $runtimeInfo.PidFilePath -Force -ErrorAction SilentlyContinue
        Write-Host "Verifier crashed after background start. Recent logs:"
        Show-VerifierLogs -RuntimeInfo $runtimeInfo
        throw "Verifier exited before becoming ready."
    }

    if (Test-VerifierHttpReady -BaseUrl $runtimeInfo.BaseUrl) {
        $ready = $true
        break
    }
}

if (-not $ready) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    Remove-Item $runtimeInfo.PidFilePath -Force -ErrorAction SilentlyContinue
    Write-Host "Verifier did not become reachable after background start. Recent logs:"
    Show-VerifierLogs -RuntimeInfo $runtimeInfo
    throw "Verifier did not become ready at $($runtimeInfo.BaseUrl)."
}

Write-Host "Verifier started with PID $($process.Id)."
