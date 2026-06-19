Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-OwsRepoRoot {
    param(
        [string]$StartDirectory = $PSScriptRoot
    )

    $currentDirectory = Get-Item -LiteralPath (Resolve-Path $StartDirectory)
    while ($null -ne $currentDirectory) {
        if (Test-Path (Join-Path $currentDirectory.FullName "OWS.sln")) {
            return $currentDirectory.FullName
        }

        $currentDirectory = $currentDirectory.Parent
    }

    throw "Could not resolve the OWS repository root from '$StartDirectory'."
}

function Get-OwsVerifierRuntimeInfo {
    param(
        [string]$RepoRoot
    )

    $baseUrl = if ([string]::IsNullOrWhiteSpace($env:OWS_VERIFIER_BASE_URL)) {
        "http://127.0.0.1:5078"
    }
    else {
        $env:OWS_VERIFIER_BASE_URL
    }

    $connectionString = if ([string]::IsNullOrWhiteSpace($env:OWS_VERIFIER_CONNECTION_STRING)) {
        "Host=localhost;Port=5432;Database=ows_verifier;Username=ows;Password=ows-dev"
    }
    else {
        $env:OWS_VERIFIER_CONNECTION_STRING
    }

    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        throw "Verifier connection string is missing. Set OWS_VERIFIER_CONNECTION_STRING or use the local default."
    }

    $baseUri = [Uri]$baseUrl
    $runtimeDirectory = Join-Path $RepoRoot "artifacts\local-verifier"
    [pscustomobject]@{
        RepoRoot = $RepoRoot
        BaseUrl = $baseUrl.TrimEnd('/')
        Host = $baseUri.Host
        Port = $baseUri.Port
        ConnectionString = $connectionString
        VerifierDllPath = Join-Path $RepoRoot "src\Ows.Verifier.Server\bin\Debug\net9.0\Ows.Verifier.Server.dll"
        RuntimeDirectory = $runtimeDirectory
        PidFilePath = Join-Path $runtimeDirectory "verifier.pid"
        StdoutLogPath = Join-Path $runtimeDirectory "verifier.stdout.log"
        StderrLogPath = Join-Path $runtimeDirectory "verifier.stderr.log"
        ChildScriptPath = Join-Path $runtimeDirectory "run-verifier-child.ps1"
    }
}

function Ensure-OwsVerifierBuild {
    param(
        [pscustomobject]$RuntimeInfo
    )

    if (Test-Path $RuntimeInfo.VerifierDllPath) {
        return
    }

    Write-Host "Verifier build output is missing. Running 'dotnet build OWS.sln -nologo'..."
    Push-Location $RuntimeInfo.RepoRoot
    try {
        dotnet build OWS.sln -nologo
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $RuntimeInfo.VerifierDllPath)) {
            throw "Verifier server build output is still missing after build."
        }
    }
    finally {
        Pop-Location
    }
}

function Test-TcpPortOpen {
    param(
        [string]$HostName,
        [int]$Port
    )

    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $asyncResult = $client.BeginConnect($HostName, $Port, $null, $null)
        if (-not $asyncResult.AsyncWaitHandle.WaitOne(2000)) {
            return $false
        }

        $client.EndConnect($asyncResult)
        return $true
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Test-VerifierHttpReady {
    param(
        [string]$BaseUrl
    )

    try {
        Invoke-WebRequest -UseBasicParsing "$BaseUrl/sessions/not-a-real-session/head" | Out-Null
        return $true
    }
    catch {
        if ($_.Exception.Response) {
            return [int]$_.Exception.Response.StatusCode -eq 404
        }

        return $false
    }
}

function Get-VerifierState {
    param(
        [pscustomobject]$RuntimeInfo
    )

    $pidValue = $null
    $pidFileExists = Test-Path $RuntimeInfo.PidFilePath
    $processRunning = $false
    $state = "not_started"
    $message = "Verifier is not started."
    $portBound = Test-TcpPortOpen -HostName $RuntimeInfo.Host -Port $RuntimeInfo.Port
    $httpReady = Test-VerifierHttpReady -BaseUrl $RuntimeInfo.BaseUrl

    if ($pidFileExists) {
        $pidValue = (Get-Content $RuntimeInfo.PidFilePath -Raw).Trim()
        if ($pidValue -notmatch '^\d+$') {
            $state = "stale_pid"
            $message = "PID file is invalid."
        }
        else {
            $processRunning = $null -ne (Get-Process -Id ([int]$pidValue) -ErrorAction SilentlyContinue)
            if ($processRunning -and $httpReady) {
                $state = "running"
                $message = "Verifier is running."
            }
            elseif ($processRunning) {
                $state = "unreachable"
                $message = "Verifier process is running but the HTTP endpoint is not reachable."
            }
            elseif ((Test-Path $RuntimeInfo.StdoutLogPath) -or (Test-Path $RuntimeInfo.StderrLogPath)) {
                $state = "crashed"
                $message = "Verifier process is gone but log files exist."
            }
            else {
                $state = "stale_pid"
                $message = "PID file points to a process that no longer exists."
            }
        }
    }
    elseif ($httpReady) {
        $state = "unreachable"
        $message = "Verifier HTTP endpoint is reachable but no managed PID file exists."
    }
    elseif ($portBound) {
        $state = "port_in_use"
        $message = "The verifier port is already in use by another process."
    }

    [pscustomobject]@{
        State = $state
        Message = $message
        Pid = $pidValue
        PidFileExists = $pidFileExists
        ProcessRunning = $processRunning
        PortBound = $portBound
        HttpReady = $httpReady
    }
}

function Show-VerifierLogs {
    param(
        [pscustomobject]$RuntimeInfo,
        [switch]$All
    )

    if (-not (Test-Path $RuntimeInfo.RuntimeDirectory)) {
        Write-Host "Verifier logs directory does not exist: $($RuntimeInfo.RuntimeDirectory)"
        return
    }

    foreach ($entry in @(
        @{ Label = "stdout"; Path = $RuntimeInfo.StdoutLogPath },
        @{ Label = "stderr"; Path = $RuntimeInfo.StderrLogPath }
    )) {
        if (-not (Test-Path $entry.Path)) {
            continue
        }

        Write-Host "=== $($entry.Label) ==="
        if ($All) {
            Get-Content $entry.Path
        }
        else {
            Get-Content $entry.Path -Tail 50
        }
    }
}
