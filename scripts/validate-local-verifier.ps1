Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common-local-verifier.ps1")

function Test-CommandAvailable {
    param(
        [string]$Name
    )
    $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Get-HttpStatus {
    param([string]$Url)
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 2
        return @{ Code = [int]$response.StatusCode; Body = $response.Content }
    }
    catch {
        if ($_.Exception.Response) {
            return @{ Code = [int]$_.Exception.Response.StatusCode; Body = "" }
        }
        return @{ Code = 0; Body = "" }
    }
}

$repoRoot = Resolve-OwsRepoRoot -StartDirectory $PSScriptRoot
$runtimeInfo = Get-OwsVerifierRuntimeInfo -RepoRoot $repoRoot
$state = Get-VerifierState -RuntimeInfo $runtimeInfo

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " OWS Environment Diagnostics" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Repository Root: $repoRoot"

# 1. Paths with spaces check
$pathHasSpaces = $repoRoot.Contains(" ")
if ($pathHasSpaces) {
    Write-Host "[!] Warning: Repository path contains spaces: '$repoRoot'" -ForegroundColor Yellow
    Write-Host "    Make sure to quote paths when invoking commands manually." -ForegroundColor Yellow
} else {
    Write-Host "[x] Path is space-free." -ForegroundColor Green
}

# 2. PowerShell execution policy
$policy = Get-ExecutionPolicy
if ($policy -eq "Restricted") {
    Write-Host "[!] Warning: PowerShell Execution Policy is 'Restricted'." -ForegroundColor Yellow
    Write-Host "    To run OWS scripts, bypass execution policy:" -ForegroundColor Yellow
    Write-Host "    powershell -ExecutionPolicy Bypass -File .\scripts\<script>.ps1" -ForegroundColor Yellow
} else {
    Write-Host "[x] PowerShell Execution Policy: $policy" -ForegroundColor Green
}

# 3. Non-admin shell check
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    Write-Host "[x] Running in Administrator shell." -ForegroundColor Green
} else {
    Write-Host "[x] Running in Non-Administrator shell. (Admin rights not required for OWS.)" -ForegroundColor Green
}

# 4. .NET 9 SDK Check
$dotnetAvailable = Test-CommandAvailable -Name "dotnet"
if ($dotnetAvailable) {
    $dotnetVersion = (dotnet --version).Trim()
    Write-Host "[x] .NET SDK is available (Version: $dotnetVersion)" -ForegroundColor Green
} else {
    Write-Host "[X] Error: .NET SDK not found." -ForegroundColor Red
    Write-Host "    Action: Install .NET 9 SDK and ensure 'dotnet' is in your PATH." -ForegroundColor Yellow
}

# 5. Docker availability and daemon state
$dockerAvailable = Test-CommandAvailable -Name "docker"
$dockerRunning = $false
if ($dockerAvailable) {
    try {
        $null = docker info 2>&1
        $dockerRunning = $LASTEXITCODE -eq 0
    } catch {
        $dockerRunning = $false
    }
    
    if ($dockerRunning) {
        Write-Host "[x] Docker is running." -ForegroundColor Green
    } else {
        Write-Host "[!] Warning: Docker command exists, but the Docker daemon is NOT running." -ForegroundColor Yellow
        Write-Host "    Action: Start Docker Desktop or your local Docker daemon." -ForegroundColor Yellow
    }
} else {
    Write-Host "[!] Warning: Docker command not found." -ForegroundColor Yellow
    Write-Host "    Action: If you wish to use PostgreSQL in a container, please install Docker." -ForegroundColor Yellow
}

# 6. PostgreSQL Port Reachability
$postgresReachable = Test-TcpPortOpen -HostName "127.0.0.1" -Port 5432
if ($postgresReachable) {
    Write-Host "[x] PostgreSQL is reachable on 127.0.0.1:5432." -ForegroundColor Green
} else {
    Write-Host "[!] Warning: PostgreSQL is NOT reachable on port 5432." -ForegroundColor Yellow
    if ($dockerRunning) {
        Write-Host "    Action: Start Postgres using: docker compose -f docker-compose.local.yml up -d" -ForegroundColor Yellow
    } else {
        Write-Host "    Action: Start your local PostgreSQL service on port 5432." -ForegroundColor Yellow
    }
}

# 7. Verifier Build Output Check
$verifierBuilt = Test-Path $runtimeInfo.VerifierDllPath
if ($verifierBuilt) {
    Write-Host "[x] Verifier DLL is built." -ForegroundColor Green
} else {
    Write-Host "[!] Warning: Verifier DLL not found at: $($runtimeInfo.VerifierDllPath)" -ForegroundColor Yellow
    Write-Host "    Action: Run 'dotnet build OWS.sln -nologo' to compile." -ForegroundColor Yellow
}

# 8. Verifier Server status & Port conflicts
$portBound = Test-TcpPortOpen -HostName $runtimeInfo.Host -Port $runtimeInfo.Port
if ($portBound) {
    if ($state.State -eq "running") {
        Write-Host "[x] Verifier server is running (State: $($state.State))." -ForegroundColor Green
    } else {
        Write-Host "[!] Warning: Verifier port $($runtimeInfo.Port) is in use, but verifier state is: $($state.State)." -ForegroundColor Yellow
        Write-Host "    Action: If another verifier instance is running, run Stop-Local-Verifier." -ForegroundColor Yellow
        Write-Host "    Otherwise, make sure port $($runtimeInfo.Port) is free." -ForegroundColor Yellow
    }
} else {
    Write-Host "[ ] Verifier server is not running (Port $($runtimeInfo.Port) is free)." -ForegroundColor Gray
    Write-Host "    Action: Start the verifier using: .\scripts\start-local-verifier.ps1" -ForegroundColor Yellow
}

# 9. Health and Readiness Endpoints
if ($portBound) {
    $health = Get-HttpStatus -Url "$($runtimeInfo.BaseUrl)/health"
    $ready = Get-HttpStatus -Url "$($runtimeInfo.BaseUrl)/ready"
    
    if ($health.Code -eq 200) {
        Write-Host "[x] Verifier /health check passed (OK)." -ForegroundColor Green
    } else {
        Write-Host "[X] Error: Verifier /health returned status code: $($health.Code)" -ForegroundColor Red
    }
    
    if ($ready.Code -eq 200) {
        Write-Host "[x] Verifier /ready check passed (OK)." -ForegroundColor Green
    } else {
        Write-Host "[X] Error: Verifier /ready returned status code: $($ready.Code) (Check DB Connection / Configuration)" -ForegroundColor Red
    }
}

# 10. API Key Alignment
$envApiKey = $env:OWS_VERIFIER_API_KEY
if ($portBound) {
    # Query with no key header
    $noKeyRequest = try {
        $r = Invoke-WebRequest -UseBasicParsing -Uri "$($runtimeInfo.BaseUrl)/sessions/not-a-real-session/head" -TimeoutSec 2 -ErrorAction SilentlyContinue
        [int]$r.StatusCode
    } catch {
        if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
    }
    
    if ($noKeyRequest -eq 401) {
        Write-Host "[x] Verifier API key guard is ACTIVE." -ForegroundColor Green
        if ([string]::IsNullOrWhiteSpace($envApiKey)) {
            Write-Host "[X] Error: Verifier expects API Key but OWS_VERIFIER_API_KEY is not set in shell." -ForegroundColor Red
            Write-Host "    Action: Set `$env:OWS_VERIFIER_API_KEY = '<your-key>' before running CLI commands." -ForegroundColor Yellow
        } else {
            Write-Host "[x] OWS_VERIFIER_API_KEY is set in local environment." -ForegroundColor Green
        }
    } else {
        Write-Host "[ ] Verifier API key guard is INACTIVE (No Auth)." -ForegroundColor Gray
    }
}

# 11. Generated scripts verification
$generatedFolder = Join-Path $repoRoot "artifacts\generated-scripts"
$generatedScriptsExist = Test-Path $generatedFolder
if ($generatedScriptsExist) {
    $scriptsCount = (Get-ChildItem $generatedFolder -Filter "*.ps1").Count
    if ($scriptsCount -gt 0) {
        Write-Host "[x] Generated launcher scripts are present in artifacts\generated-scripts\ ($scriptsCount scripts)." -ForegroundColor Green
    } else {
        Write-Host "[!] Warning: artifacts\generated-scripts\ exists but has no .ps1 scripts." -ForegroundColor Yellow
        Write-Host "    Action: Build OWS.sln to copy launcher scripts." -ForegroundColor Yellow
    }
} else {
    Write-Host "[!] Warning: artifacts\generated-scripts\ folder is missing." -ForegroundColor Yellow
    Write-Host "    Action: Build OWS.sln to generate scripts." -ForegroundColor Yellow
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Diagnostics complete." -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
