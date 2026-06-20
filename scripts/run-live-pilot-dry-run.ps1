param(
    [string]$BaseUrl = $(if ($env:OWS_VERIFIER_BASE_URL) { $env:OWS_VERIFIER_BASE_URL } else { "http://127.0.0.1:5078" }),
    [string]$OperatorKey = $(if ($env:OWS_VERIFIER_API_KEY) { $env:OWS_VERIFIER_API_KEY } else { "pilot-operator-key-12345" }),
    [string]$ReceiptSigningKey = $(if ($env:VerifierStorage__ReceiptSigningKey) { $env:VerifierStorage__ReceiptSigningKey } else { "pilot-signing-key-12345" }),
    [int]$HeartbeatWaitSeconds = 65
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$env:VerifierSecurity__ApiKey = $OperatorKey
$env:OWS_VERIFIER_API_KEY = $OperatorKey
$env:VerifierStorage__ReceiptSigningKey = $ReceiptSigningKey

$cliDll = Join-Path $repoRoot "src\Ows.Cli\bin\Debug\net9.0\Ows.Cli.dll"
if (-not (Test-Path $cliDll)) {
    throw "CLI build output is missing at '$cliDll'. Run 'dotnet build OWS.sln -nologo' first."
}

$timestamp = Get-Date -Format "yyyyMMddHHmmss"
$prefix = "live-$timestamp"
$assignmentRoot = Join-Path $repoRoot "artifacts\pilot-demo\live-assignment-$timestamp"
$watcherLogRoot = Join-Path $repoRoot "artifacts\pilot-demo\watcher-logs-$timestamp"
$summaryPath = Join-Path $repoRoot "artifacts\pilot-demo\live-dry-run-summary.json"
$watcherStdoutPath = Join-Path $watcherLogRoot "watcher.stdout.log"
$watcherStderrPath = Join-Path $watcherLogRoot "watcher.stderr.log"

$watcherProcess = $null
$studentKey = $null
$reviewerKey = $null
$watcherStopped = $false

function Invoke-OwsCliJson {
    param(
        [string]$WorkingDirectory,
        [string[]]$Arguments
    )

    Push-Location $WorkingDirectory
    try {
        $dotnetArguments = @($cliDll) + $Arguments + "--json"
        $output = & dotnet @dotnetArguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $text = ($output | Out-String)
    $jsonStart = $text.IndexOf("{")
    $jsonEnd = $text.LastIndexOf("}")
    if ($jsonStart -lt 0 -or $jsonEnd -lt $jsonStart) {
        throw "CLI JSON output not found for arguments '$($Arguments -join " ")'.`n$text"
    }

    $json = $text.Substring($jsonStart, $jsonEnd - $jsonStart + 1) | ConvertFrom-Json
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $text
        Json = $json
    }
}

function Wait-Until {
    param(
        [scriptblock]$Condition,
        [string]$FailureMessage,
        [int]$Attempts = 30,
        [int]$DelaySeconds = 2
    )

    for ($attempt = 0; $attempt -lt $Attempts; $attempt++) {
        if (& $Condition) {
            return
        }

        Start-Sleep -Seconds $DelaySeconds
    }

    throw $FailureMessage
}

try {
    New-Item -ItemType Directory -Force -Path $assignmentRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $watcherLogRoot | Out-Null
    Set-Content -Path (Join-Path $assignmentRoot "draft.txt") -Value "Initial draft." -Encoding utf8

    & (Join-Path $PSScriptRoot "start-local-verifier.ps1")

    $health = Invoke-RestMethod -Uri "$BaseUrl/health"
    $ready = Invoke-RestMethod -Uri "$BaseUrl/ready"
    $diagnosticsBefore = Invoke-RestMethod -Headers @{ "X-OWS-Verifier-Key" = $OperatorKey } -Uri "$BaseUrl/diagnostics/summary"

    $fixture = & (Join-Path $PSScriptRoot "setup-pilot-fixture.ps1") -BaseUrl $BaseUrl -OperatorKey $OperatorKey -Prefix $prefix -AsJson | ConvertFrom-Json
    $studentKey = $fixture.studentClientKey
    $reviewerKey = $fixture.instructorReviewerKey

    $metadata = Get-Content (Join-Path $repoRoot "artifacts\pilot-demo\fixture-metadata.json") -Raw | ConvertFrom-Json

    $init = Invoke-OwsCliJson -WorkingDirectory $assignmentRoot -Arguments @("init")
    if ($init.ExitCode -ne 0) {
        throw $init.Output
    }

    $config = @{
        owsVersion = "0.1"
        projectRoot = $assignmentRoot
        initializedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        verifierUrl = $BaseUrl
        institutionId = $metadata.institutionId
        assessmentId = $metadata.assessmentId
        studentUserId = $metadata.studentUserId
        courseOfferingId = $metadata.courseOfferingId
        uploadEnabled = $true
    } | ConvertTo-Json -Depth 10
    Set-Content -Path (Join-Path $assignmentRoot ".ows\config.json") -Value $config -Encoding utf8

    $env:OWS_VERIFIER_API_KEY = $studentKey
    $sessionStart = Invoke-OwsCliJson -WorkingDirectory $assignmentRoot -Arguments @("session", "start")
    if ($sessionStart.ExitCode -ne 0) {
        throw $sessionStart.Output
    }

    $watcherCommand = "Set-Location '$($assignmentRoot.Replace("'", "''"))'; & dotnet '$($cliDll.Replace("'", "''"))' watch start --poll 1>> '$($watcherStdoutPath.Replace("'", "''"))' 2>> '$($watcherStderrPath.Replace("'", "''"))'"
    $watcherProcess = Start-Process -FilePath "powershell.exe" -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $watcherCommand) -PassThru -WindowStyle Hidden

    Wait-Until -FailureMessage "Watcher did not create watcher.json." -Attempts 20 -DelaySeconds 1 -Condition {
        Test-Path (Join-Path $assignmentRoot ".ows\watcher.json")
    }

    $activeStatus = Invoke-OwsCliJson -WorkingDirectory $assignmentRoot -Arguments @("status")
    if ($activeStatus.Json.Status -ne "SessionActive") {
        throw "Expected SessionActive after watcher start, got '$($activeStatus.Json.Status)'."
    }

    Start-Sleep -Seconds $HeartbeatWaitSeconds
    $heartbeatStatus = Invoke-OwsCliJson -WorkingDirectory $assignmentRoot -Arguments @("status")
    if (-not $heartbeatStatus.Json.LastHeartbeatAt) {
        throw "Heartbeat timestamp did not advance while the watcher was running."
    }

    Add-Content -Path (Join-Path $assignmentRoot "draft.txt") -Value "`nSecond line at $(Get-Date -Format o)"
    Start-Sleep -Seconds 3

    $checkpoint = Invoke-OwsCliJson -WorkingDirectory $assignmentRoot -Arguments @("session", "checkpoint")
    if ($checkpoint.ExitCode -ne 0) {
        throw $checkpoint.Output
    }

    $package = Invoke-OwsCliJson -WorkingDirectory $assignmentRoot -Arguments @("package")
    if ($package.ExitCode -ne 0) {
        throw $package.Output
    }

    $upload = Invoke-OwsCliJson -WorkingDirectory $assignmentRoot -Arguments @("package", "upload")
    if ($upload.ExitCode -ne 0) {
        throw $upload.Output
    }

    $packageId = $upload.Json.PackageId
    if ([string]::IsNullOrWhiteSpace($packageId)) {
        throw "Package upload did not return a package id."
    }

    $lastPackageStatus = $null
    Wait-Until -FailureMessage "Package verification did not complete in time." -Attempts 45 -DelaySeconds 2 -Condition {
        $statusResult = Invoke-OwsCliJson -WorkingDirectory $assignmentRoot -Arguments @("package", "status", "--package-id", $packageId)
        $script:lastPackageStatus = $statusResult.Json
        return $statusResult.Json.Status -eq "Completed"
    }
    $lastPackageStatus = $script:lastPackageStatus

    $env:OWS_VERIFIER_API_KEY = $reviewerKey
    $reviewerHeaders = @{ "X-OWS-Verifier-Key" = $reviewerKey }
    $reviewerPackage = Invoke-RestMethod -Headers $reviewerHeaders -Uri "$BaseUrl/packages/$packageId"
    $reviewerReport = (Invoke-WebRequest -UseBasicParsing -Headers $reviewerHeaders -Uri "$BaseUrl/packages/$packageId/report").Content

    $reviewerDeniedStatus = $null
    try {
        Invoke-WebRequest -UseBasicParsing -Method Post -Headers $reviewerHeaders -ContentType "application/json" -Body "{}" -Uri "$BaseUrl/education/institutions" | Out-Null
        throw "Reviewer mutation unexpectedly succeeded."
    }
    catch {
        if (-not $_.Exception.Response) {
            throw
        }

        $reviewerDeniedStatus = [int]$_.Exception.Response.StatusCode
    }

    $env:OWS_VERIFIER_API_KEY = $OperatorKey
    $diagnosticsAfter = Invoke-RestMethod -Headers @{ "X-OWS-Verifier-Key" = $OperatorKey } -Uri "$BaseUrl/diagnostics/summary"
    $auditEvents = Invoke-RestMethod -Headers @{ "X-OWS-Verifier-Key" = $OperatorKey } -Uri "$BaseUrl/audit/events?limit=100&packageId=$packageId"
    $auditEventList = if ($auditEvents -is [System.Array]) { $auditEvents } elseif ($auditEvents.items) { $auditEvents.items } else { @() }

    $stdoutLog = if (Test-Path (Join-Path $repoRoot "artifacts\local-verifier\verifier.stdout.log")) {
        Get-Content (Join-Path $repoRoot "artifacts\local-verifier\verifier.stdout.log") -Raw
    }
    else {
        ""
    }
    $stderrLog = if (Test-Path (Join-Path $repoRoot "artifacts\local-verifier\verifier.stderr.log")) {
        Get-Content (Join-Path $repoRoot "artifacts\local-verifier\verifier.stderr.log") -Raw
    }
    else {
        ""
    }
    $combinedLogs = $stdoutLog + "`n" + $stderrLog

    $summary = [ordered]@{
        dateUtc = [DateTimeOffset]::UtcNow.ToString("o")
        baseUrl = $BaseUrl
        prefix = $prefix
        assignmentRoot = $assignmentRoot
        verifierHealth = $health.status
        verifierReady = $ready.status
        fixture = [ordered]@{
            institutionId = $metadata.institutionId
            courseId = $metadata.courseId
            classGroupId = $metadata.classGroupId
            courseOfferingId = $metadata.courseOfferingId
            assessmentId = $metadata.assessmentId
            studentUserId = $metadata.studentUserId
            studentClientKeyPrefix = $metadata.studentClientKeyPrefix
            instructorReviewerKeyPrefix = $metadata.instructorReviewerKeyPrefix
        }
        sessionId = $sessionStart.Json.SessionId
        activeStatus = $activeStatus.Json.Status
        lastHeartbeatAt = $heartbeatStatus.Json.LastHeartbeatAt
        checkpointAt = $checkpoint.Json.LastCheckpointAt
        packageId = $packageId
        packageStatus = $lastPackageStatus.Status
        trustStatus = $lastPackageStatus.TrustStatus
        reviewerDeniedStatus = $reviewerDeniedStatus
        reviewerPackageInstitutionId = $reviewerPackage.institutionId
        reviewerReportHasAssessmentContext = $reviewerReport -match "Assessment Context"
        reviewerReportHasStatusLine = $reviewerReport -match "Status:"
        diagnosticsBefore = $diagnosticsBefore
        diagnosticsAfter = $diagnosticsAfter
        auditEventTypes = @($auditEventList | ForEach-Object { $_.eventType })
        requestIdSeenInLogs = $combinedLogs -match "requestId="
        rawKeyLeakDetected = (
            $combinedLogs.Contains($studentKey) -or
            $combinedLogs.Contains($reviewerKey) -or
            $combinedLogs.Contains($OperatorKey)
        )
    }

    $summary | ConvertTo-Json -Depth 10 | Set-Content -Path $summaryPath -Encoding utf8
    $summary | ConvertTo-Json -Depth 10
}
finally {
    $env:OWS_VERIFIER_API_KEY = $studentKey

    if (-not $watcherStopped -and (Test-Path (Join-Path $assignmentRoot ".ows"))) {
        try {
            Push-Location $assignmentRoot
            try {
                & dotnet $cliDll watch stop | Out-Null
            }
            finally {
                Pop-Location
            }
            $watcherStopped = $true
        }
        catch {
        }
    }

    if ($watcherProcess -and -not $watcherProcess.HasExited) {
        try {
            Stop-Process -Id $watcherProcess.Id -Force -ErrorAction SilentlyContinue
        }
        catch {
        }
    }

    $env:OWS_VERIFIER_API_KEY = $OperatorKey
    try {
        & (Join-Path $PSScriptRoot "stop-local-verifier.ps1") | Out-Null
    }
    catch {
    }
}
