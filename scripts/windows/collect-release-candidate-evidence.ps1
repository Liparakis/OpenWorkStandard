param(
    [string]$Version = "v0.1"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common-local-verifier.ps1")
$repoRoot = Resolve-OwsRepoRoot -StartDirectory $PSScriptRoot
Set-Location $repoRoot

$gateSummaryPath = Join-Path $repoRoot "artifacts\release-gate\release-gate-summary.json"
$dryRunSummaryPath = Join-Path $repoRoot "artifacts\pilot-demo\live-dry-run-summary.json"

if (-not (Test-Path $gateSummaryPath)) {
    throw "Missing gate summary at '$gateSummaryPath'. Run .\scripts\windows\run-release-regression-gate.ps1 first."
}

if (-not (Test-Path $dryRunSummaryPath)) {
    throw "Missing dry run summary at '$dryRunSummaryPath'. Run .\scripts\windows\run-live-pilot-dry-run.ps1 first."
}

$gateSummary = Get-Content $gateSummaryPath -Raw | ConvertFrom-Json
$dryRunSummary = Get-Content $dryRunSummaryPath -Raw | ConvertFrom-Json

if ($gateSummary.overallStatus -ne "Passed") {
    throw "Release gate is not green. Current status: '$($gateSummary.overallStatus)'."
}

if ($dryRunSummary.packageStatus -ne "Completed") {
    throw "Latest dry run package status is '$($dryRunSummary.packageStatus)', expected 'Completed'."
}

if ($dryRunSummary.trustStatus -ne "Verified") {
    throw "Latest dry run trust status is '$($dryRunSummary.trustStatus)', expected 'Verified'."
}

if ([int]$dryRunSummary.reviewerDeniedStatus -ne 403) {
    throw "Latest dry run reviewer denial status is '$($dryRunSummary.reviewerDeniedStatus)', expected 403."
}

if ([bool]$dryRunSummary.rawKeyLeakDetected) {
    throw "Latest dry run detected a raw API key leak."
}

$bundleRoot = Join-Path $repoRoot "artifacts\release-candidate\$Version"
New-Item -ItemType Directory -Force -Path $bundleRoot | Out-Null

Copy-Item -LiteralPath $gateSummaryPath -Destination (Join-Path $bundleRoot "release-gate-summary.json") -Force
Copy-Item -LiteralPath $dryRunSummaryPath -Destination (Join-Path $bundleRoot "live-dry-run-summary.json") -Force

$manifest = [ordered]@{
    version = $Version
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    overallStatus = "ReadyForManualSignoff"
    gateSummary = @{
        source = $gateSummaryPath
        copiedTo = (Join-Path $bundleRoot "release-gate-summary.json")
        dateUtc = $gateSummary.dateUtc
        overallStatus = $gateSummary.overallStatus
    }
    dryRunSummary = @{
        source = $dryRunSummaryPath
        copiedTo = (Join-Path $bundleRoot "live-dry-run-summary.json")
        dateUtc = $dryRunSummary.dateUtc
        packageStatus = $dryRunSummary.packageStatus
        trustStatus = $dryRunSummary.trustStatus
        reviewerDeniedStatus = $dryRunSummary.reviewerDeniedStatus
        rawKeyLeakDetected = $dryRunSummary.rawKeyLeakDetected
    }
    manualChecksRemaining = @(
        "VS Code trusted-workspace interactive smoke path if the extension changed.",
        "Operator release-candidate sign-off."
    )
}

$manifestPath = Join-Path $bundleRoot "evidence-manifest.json"
$manifest | ConvertTo-Json -Depth 10 | Set-Content -Path $manifestPath -Encoding utf8

$readme = @"
# Open Work Standard Release Candidate Evidence

Version: $Version

Generated UTC: $($manifest.generatedAtUtc)

Gate status: $($gateSummary.overallStatus)
Dry run trust status: $($dryRunSummary.trustStatus)
Dry run package status: $($dryRunSummary.packageStatus)
Reviewer denial status: $($dryRunSummary.reviewerDeniedStatus)
Raw key leak detected: $($dryRunSummary.rawKeyLeakDetected)

Files:

- release-gate-summary.json
- live-dry-run-summary.json
- evidence-manifest.json

Remaining manual checks:

- VS Code trusted-workspace interactive smoke path if the extension changed.
- Operator release-candidate sign-off.
"@

$readmePath = Join-Path $bundleRoot "README.md"
$readme | Set-Content -Path $readmePath -Encoding utf8

$manifest | ConvertTo-Json -Depth 10
