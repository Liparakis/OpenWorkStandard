#Requires -Version 5.1
param(
    [string]$BaseUrl = "http://localhost:5078",
    [string]$ApiKey = "",
    [switch]$SkipBlobProbe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common-local-verifier.ps1")

function Write-Check {
    param(
        [string]$Label,
        [bool]$Pass,
        [string]$Note = ""
    )

    if ($Pass) {
        Write-Host "  [OK]  $Label" -ForegroundColor Green
    }
    else {
        Write-Host "  [!!]  $Label" -ForegroundColor Red
        if ($Note) {
            Write-Host "        $Note" -ForegroundColor Yellow
        }
    }

    return $Pass
}

function Write-Warn {
    param([string]$Message)
    Write-Host "  [W]   $Message" -ForegroundColor Yellow
}

function Write-Info {
    param([string]$Message)
    Write-Host "        $Message" -ForegroundColor Gray
}

function Invoke-SafeGet {
    param(
        [string]$Url,
        [hashtable]$Headers = @{},
        [int]$TimeoutSec = 5
    )

    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -Headers $Headers -TimeoutSec $TimeoutSec -ErrorAction Stop
        return @{
            Ok = $true
            Code = [int]$response.StatusCode
            Body = $response.Content
        }
    }
    catch {
        $code = 0
        if ($_.Exception.Response) {
            $code = [int]$_.Exception.Response.StatusCode
        }

        return @{
            Ok = $false
            Code = $code
            Body = ""
        }
    }
}

function ConvertFrom-SafeJson {
    param([string]$Json)

    try {
        return $Json | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = $env:OWS_VERIFIER_API_KEY
}

$authHeaders = @{}
if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
    $authHeaders["X-OWS-Verifier-Key"] = $ApiKey
}

$repoRoot = Resolve-OwsRepoRoot -StartDirectory $PSScriptRoot
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$allPassed = $true
$BaseUrl = $BaseUrl.TrimEnd("/")

Write-Host ""
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  OWS Verifier Operations Readiness Check" -ForegroundColor Cyan
Write-Host "  $timestamp" -ForegroundColor Cyan
Write-Host "  Target: $BaseUrl" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "--- 1. HTTP Reachability ---"
$health = Invoke-SafeGet -Url "$BaseUrl/health"
$ok = Write-Check -Label "/health returns 200" -Pass ($health.Code -eq 200) -Note "Verifier is not reachable at $BaseUrl."
$allPassed = $allPassed -and $ok
Write-Host ""

Write-Host "--- 2. Readiness Dependencies ---"
$ready = Invoke-SafeGet -Url "$BaseUrl/ready"
$readyOk = $ready.Code -eq 200
$ok = Write-Check -Label "/ready returns 200" -Pass $readyOk -Note "Verifier storage or package storage is unhealthy."
$allPassed = $allPassed -and $ok

$readyJson = $null
if ($ready.Body) {
    $readyJson = ConvertFrom-SafeJson -Json $ready.Body
}

if ($readyJson -and $readyJson.dependencies) {
    $deps = $readyJson.dependencies

    $ok = Write-Check -Label "storageReady" -Pass ($deps.storageReady -eq $true) -Note "PostgreSQL is not ready."
    $allPassed = $allPassed -and $ok

    $ok = Write-Check -Label "packageStorageReady" -Pass ($deps.packageStorageReady -eq $true) -Note "Package blob storage is not accessible."
    $allPassed = $allPassed -and $ok

    $ok = Write-Check -Label "signingConfigured" -Pass ($deps.signingConfigured -eq $true) -Note "Receipt signing key is not configured."
    $allPassed = $allPassed -and $ok

    if ($deps.authMode) {
        Write-Info "Auth mode: $($deps.authMode)"
    }
}
Write-Host ""

Write-Host "--- 3. Diagnostics Summary ---"
if ($authHeaders.Count -eq 0) {
    Write-Warn "No API key set. Diagnostics may return 401."
}

$diag = Invoke-SafeGet -Url "$BaseUrl/diagnostics/summary" -Headers $authHeaders
$diagOk = $diag.Code -eq 200
$ok = Write-Check -Label "/diagnostics/summary returns 200" -Pass $diagOk -Note "Set OWS_VERIFIER_API_KEY or pass -ApiKey."
$allPassed = $allPassed -and $ok

$diagJson = $null
if ($diagOk -and $diag.Body) {
    $diagJson = ConvertFrom-SafeJson -Json $diag.Body
}

if ($diagJson) {
    if ($null -ne $diagJson.signingKeyFingerprintPresent) {
        $ok = Write-Check -Label "Signing key fingerprint present" -Pass ($diagJson.signingKeyFingerprintPresent -eq $true) -Note "No signing key fingerprint is exposed."
        $allPassed = $allPassed -and $ok
    }

    if ($null -ne $diagJson.packageStorageConfigured) {
        $ok = Write-Check -Label "Package storage configured" -Pass ($diagJson.packageStorageConfigured -eq $true) -Note "Package storage is not configured."
        $allPassed = $allPassed -and $ok
    }

    if ($null -ne $diagJson.packageStorageReady) {
        $ok = Write-Check -Label "Package storage accessible" -Pass ($diagJson.packageStorageReady -eq $true) -Note "Package storage is not accessible."
        $allPassed = $allPassed -and $ok
    }

    if ($null -ne $diagJson.packageBlobCount) {
        Write-Info "Package blob count: $($diagJson.packageBlobCount)"
    }

    if ($diagJson.packageVerificationJobs) {
        $jobs = $diagJson.packageVerificationJobs
        Write-Info "Verification jobs - pending: $($jobs.pending) | running: $($jobs.running) | succeeded: $($jobs.succeeded) | failed: $($jobs.failed)"
        if ($jobs.running -gt 0) {
            Write-Warn "There are running verification jobs. After a restart this may be temporary."
        }

        if ($jobs.failed -gt 0) {
            Write-Warn "$($jobs.failed) verification job(s) have failed."
        }
    }
}
Write-Host ""

Write-Host "--- 4. Backup Documentation ---"
$docsToCheck = @(
    "docs/operations/BACKUP_RESTORE.md",
    "docs/operations/OPERATIONS_RUNBOOK.md",
    "docs/operations/SECURITY_HARDENING.md"
)

foreach ($doc in $docsToCheck) {
    $docPath = Join-Path $repoRoot $doc
    $ok = Write-Check -Label "$doc exists" -Pass (Test-Path $docPath) -Note "Operator documentation is missing."
    $allPassed = $allPassed -and $ok
}
Write-Host ""

if (-not $SkipBlobProbe) {
    Write-Host "--- 5. Local Package Blob Directory Probe ---"

    if ($diagJson -and $null -ne $diagJson.packageStoragePath -and -not [string]::IsNullOrWhiteSpace($diagJson.packageStoragePath)) {
        $blobPath = [string]$diagJson.packageStoragePath
        Write-Info "Blob storage path: $blobPath"

        if (Test-Path $blobPath) {
            $probeFile = Join-Path $blobPath ".ows-readiness-probe"
            try {
                Set-Content -Path $probeFile -Value "readiness-check" -Force
                Remove-Item -Path $probeFile -Force
                $ok = Write-Check -Label "Blob directory is writable" -Pass $true
            }
            catch {
                $ok = Write-Check -Label "Blob directory is writable" -Pass $false -Note "Could not write to the blob directory."
                $allPassed = $allPassed -and $ok
            }
        }
        else {
            Write-Warn "Blob path '$blobPath' is not accessible from this machine."
        }
    }
    else {
        Write-Warn "Package storage path is not exposed in diagnostics. Skipping local blob probe."
    }

    Write-Host ""
}

Write-Host "========================================================" -ForegroundColor Cyan
if ($allPassed) {
    Write-Host "  RESULT: All checks passed. Verifier appears ready." -ForegroundColor Green
}
else {
    Write-Host "  RESULT: One or more checks failed. See details above." -ForegroundColor Red
}
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""

if (-not $allPassed) {
    exit 1
}
