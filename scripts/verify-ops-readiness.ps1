#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

<#
.SYNOPSIS
    OWS Verifier Operations Readiness Check.

.DESCRIPTION
    A read-only diagnostic helper that checks the operational state of a self-hosted
    OWS verifier before and after a restore, or as a routine pilot health check.

    Checks:
      - Package storage path configured and accessible
      - PostgreSQL connectivity (via /ready endpoint)
      - /ready returns success
      - /diagnostics/summary reachable
      - Signing key fingerprint present
      - Package storage reported ready
      - Backup documentation present
      - No jobs permanently stuck in Running state

    Safe: this script makes only GET requests and one write-probe to the blob directory.
    It does NOT modify the database, remove files, or change configuration.

.PARAMETER BaseUrl
    Base URL of the OWS verifier. Defaults to http://localhost:5078.

.PARAMETER ApiKey
    Operator API key. If omitted, the script reads OWS_VERIFIER_API_KEY from the environment.

.PARAMETER SkipBlobProbe
    If set, skip the local package blob directory write probe.

.EXAMPLE
    .\scripts\verify-ops-readiness.ps1

.EXAMPLE
    .\scripts\verify-ops-readiness.ps1 -BaseUrl http://192.168.1.10:5078 -ApiKey "my-key"
#>

param(
    [string]$BaseUrl = "http://localhost:5078",
    [string]$ApiKey  = "",
    [switch]$SkipBlobProbe
)

# ── Helpers ───────────────────────────────────────────────────────────────────

function Write-Check {
    param([string]$Label, [bool]$Pass, [string]$Note = "")
    if ($Pass) {
        Write-Host "  [OK]  $Label" -ForegroundColor Green
    } else {
        Write-Host "  [!!]  $Label" -ForegroundColor Red
        if ($Note) { Write-Host "        $Note" -ForegroundColor Yellow }
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
    param([string]$Url, [hashtable]$Headers = @{}, [int]$TimeoutSec = 5)
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -Headers $Headers `
                    -TimeoutSec $TimeoutSec -ErrorAction Stop
        return @{ Ok = $true; Code = [int]$response.StatusCode; Body = $response.Content }
    } catch {
        $code = 0
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        return @{ Ok = $false; Code = $code; Body = "" }
    }
}

function ConvertFrom-SafeJson {
    param([string]$Json)
    try { return $Json | ConvertFrom-Json } catch { return $null }
}

# ── Resolve API key ───────────────────────────────────────────────────────────

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = $env:OWS_VERIFIER_API_KEY
}

$authHeaders = @{}
if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
    $authHeaders["X-OWS-Verifier-Key"] = $ApiKey
}

# ── Banner ────────────────────────────────────────────────────────────────────

$repoRoot = Split-Path -Parent $PSScriptRoot
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

Write-Host ""
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  OWS Verifier Operations Readiness Check" -ForegroundColor Cyan
Write-Host "  $timestamp" -ForegroundColor Cyan
Write-Host "  Target: $BaseUrl" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""

$allPassed = $true

# ── 1. /health ────────────────────────────────────────────────────────────────

Write-Host "--- 1. HTTP Reachability ---"
$health = Invoke-SafeGet -Url "$BaseUrl/health"
$ok = Write-Check -Label "/health returns 200" -Pass ($health.Code -eq 200) `
    -Note "Verifier is not reachable at $BaseUrl. Start the verifier and retry."
$allPassed = $allPassed -and $ok
Write-Host ""

# ── 2. /ready ─────────────────────────────────────────────────────────────────

Write-Host "--- 2. Readiness Dependencies ---"
$ready = Invoke-SafeGet -Url "$BaseUrl/ready"
$readyOk = ($ready.Code -eq 200)
$ok = Write-Check -Label "/ready returns 200" -Pass $readyOk `
    -Note "Storage or education store is unhealthy. Check container logs."
$allPassed = $allPassed -and $ok

if ($ready.Body) {
    $readyJson = ConvertFrom-SafeJson -Json $ready.Body
    if ($readyJson -and $readyJson.dependencies) {
        $deps = $readyJson.dependencies
        $ok = Write-Check -Label "storageReady (PostgreSQL)" `
            -Pass ($null -ne $deps.storageReady -and $deps.storageReady -eq $true) `
            -Note "PostgreSQL is not ready. Check the postgres container."
        $allPassed = $allPassed -and $ok

        $ok = Write-Check -Label "educationStoreReady" `
            -Pass ($null -ne $deps.educationStoreReady -and $deps.educationStoreReady -eq $true) `
            -Note "Education store is not ready."
        $allPassed = $allPassed -and $ok

        $ok = Write-Check -Label "packageStorageReady (blob volume)" `
            -Pass ($null -ne $deps.packageStorageReady -and $deps.packageStorageReady -eq $true) `
            -Note "Package blob storage is not accessible. Check the verifier-packages volume."
        $allPassed = $allPassed -and $ok

        $ok = Write-Check -Label "signingConfigured (receipt signing key present)" `
            -Pass ($null -ne $deps.signingConfigured -and $deps.signingConfigured -eq $true) `
            -Note "No receipt signing key configured. Receipts will not be signed."
        $allPassed = $allPassed -and $ok

        if ($deps.authMode) {
            Write-Info "Auth mode: $($deps.authMode)"
        }
    }
}
Write-Host ""

# ── 3. /diagnostics/summary ───────────────────────────────────────────────────

Write-Host "--- 3. Diagnostics Summary ---"

$diagUrl = "$BaseUrl/diagnostics/summary"
if ($authHeaders.Count -eq 0) {
    Write-Warn "No API key set (OWS_VERIFIER_API_KEY). Diagnostics check may return 401."
}

$diag = Invoke-SafeGet -Url $diagUrl -Headers $authHeaders
$diagOk = ($diag.Code -eq 200)
$ok = Write-Check -Label "/diagnostics/summary returns 200" -Pass $diagOk `
    -Note "Ensure an operator API key is set (OWS_VERIFIER_API_KEY or -ApiKey)."
$allPassed = $allPassed -and $ok

if ($diagOk -and $diag.Body) {
    $diagJson = ConvertFrom-SafeJson -Json $diag.Body
    if ($diagJson) {
        # Signing key fingerprint
        $fingerprintPresent = $false
        if ($diagJson.signingKeyFingerprintPresent -ne $null) {
            $fingerprintPresent = $diagJson.signingKeyFingerprintPresent -eq $true
        } elseif ($diagJson.packageStorageConfigured -ne $null) {
            # Fall back: if diagnostics returned at all with signing enabled in /ready, treat as present
            $fingerprintPresent = $readyJson -and $readyJson.dependencies -and
                                  $readyJson.dependencies.signingConfigured -eq $true
        }
        $ok = Write-Check -Label "Signing key fingerprint present" -Pass $fingerprintPresent `
            -Note "No signing key fingerprint. Configure VerifierStorage__ReceiptSigningKey."
        $allPassed = $allPassed -and $ok

        # Package storage configured
        if ($diagJson.packageStorageConfigured -ne $null) {
            $storageConfigured = $diagJson.packageStorageConfigured -eq $true
            $ok = Write-Check -Label "Package storage configured" -Pass $storageConfigured `
                -Note "VerifierStorage__LocalStoragePath is not configured."
            $allPassed = $allPassed -and $ok
        }

        # Package storage ready
        if ($diagJson.packageStorageReady -ne $null) {
            $ok = Write-Check -Label "Package storage accessible (diagnostics)" `
                -Pass ($diagJson.packageStorageReady -eq $true) `
                -Note "Blob storage is not accessible from within the verifier."
            $allPassed = $allPassed -and $ok
        }

        # Blob count (informational)
        if ($null -ne $diagJson.packageBlobCount) {
            Write-Info "Package blob count: $($diagJson.packageBlobCount)"
        }

        # Job summary (warn on stuck running jobs)
        if ($diagJson.packageVerificationJobs) {
            $jobs = $diagJson.packageVerificationJobs
            Write-Info "Verification jobs — pending: $($jobs.pending) | running: $($jobs.running) | completed: $($jobs.completed) | failed: $($jobs.failed)"
            if ($jobs.running -gt 0) {
                Write-Warn "There are $($jobs.running) job(s) in Running state. After a restore this may be normal (startup recovery resets them). If persisting, restart the verifier."
            }
            if ($jobs.failed -gt 0) {
                Write-Warn "$($jobs.failed) verification job(s) failed. Query audit events for details."
            }
        }

        # Auth mode
        if ($diagJson.authMode) {
            Write-Info "Auth mode: $($diagJson.authMode)"
        }
    }
}
Write-Host ""

# ── 4. Backup documentation present ───────────────────────────────────────────

Write-Host "--- 4. Backup Documentation ---"

$docsToCheck = @(
    "docs\BACKUP_RESTORE.md",
    "docs\OPERATIONS_RUNBOOK.md",
    "docs\SECURITY_HARDENING.md"
)

foreach ($doc in $docsToCheck) {
    $docPath = Join-Path $repoRoot $doc
    $ok = Write-Check -Label "$doc exists" -Pass (Test-Path $docPath) `
        -Note "Backup documentation is missing. Operators may not know restore procedures."
    $allPassed = $allPassed -and $ok
}
Write-Host ""

# ── 5. Local blob probe (optional) ─────────────────────────────────────────────

if (-not $SkipBlobProbe) {
    Write-Host "--- 5. Local Package Blob Directory Probe ---"

    # Try to read blob path from diagnostics
    $blobPathKnown = $false
    if ($diagJson -and $diagJson.packageStoragePath) {
        $blobPath = $diagJson.packageStoragePath
        Write-Info "Blob storage path from diagnostics: $blobPath"

        if (Test-Path $blobPath) {
            $probeFile = Join-Path $blobPath ".ows-readiness-probe"
            try {
                Set-Content -Path $probeFile -Value "readiness-check" -Force
                Remove-Item -Path $probeFile -Force
                $ok = Write-Check -Label "Blob directory is writable" -Pass $true
            } catch {
                $ok = Write-Check -Label "Blob directory is writable" -Pass $false `
                    -Note "Could not write to $blobPath. Check volume mount and permissions."
                $allPassed = $allPassed -and $ok
            }

            $blobFiles = Get-ChildItem -Path $blobPath -Filter "*.owspkg" -ErrorAction SilentlyContinue
            Write-Info "Local .owspkg blob count: $($blobFiles.Count)"
        } else {
            Write-Warn "Blob path '$blobPath' is not accessible from this machine. Skipping local probe."
            Write-Info "This is expected when running against a remote or containerized verifier."
        }
        $blobPathKnown = $true
    }

    if (-not $blobPathKnown) {
        Write-Warn "Package storage path not exposed in diagnostics. Skipping local blob probe."
        Write-Info "Use -SkipBlobProbe to suppress this section."
    }
    Write-Host ""
}

# ── Summary ────────────────────────────────────────────────────────────────────

Write-Host "========================================================" -ForegroundColor Cyan
if ($allPassed) {
    Write-Host "  RESULT: All checks passed. Verifier appears ready." -ForegroundColor Green
} else {
    Write-Host "  RESULT: One or more checks failed. See details above." -ForegroundColor Red
    Write-Host "  Review OPERATIONS_RUNBOOK.md and BACKUP_RESTORE.md." -ForegroundColor Yellow
}
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""

if (-not $allPassed) {
    exit 1
}
