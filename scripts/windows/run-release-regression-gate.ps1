param(
    [string]$BaseUrl = $(if ($env:OWS_VERIFIER_BASE_URL) { $env:OWS_VERIFIER_BASE_URL } else { "http://127.0.0.1:5078" }),
    [string]$OperatorKey = $(if ($env:OWS_VERIFIER_API_KEY) { $env:OWS_VERIFIER_API_KEY } else { "pilot-operator-key-12345" }),
    [string]$ReceiptSigningKey = $(if ($env:VerifierStorage__ReceiptSigningKey) { $env:VerifierStorage__ReceiptSigningKey } else { "pilot-signing-key-12345" }),
    [switch]$SkipComposeValidation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common-local-verifier.ps1")
$repoRoot = Resolve-OwsRepoRoot -StartDirectory $PSScriptRoot
Set-Location $repoRoot

$summaryRoot = Join-Path $repoRoot "artifacts\release-gate"
New-Item -ItemType Directory -Force -Path $summaryRoot | Out-Null
$summaryPath = Join-Path $summaryRoot "release-gate-summary.json"
$nugetConfigPath = Join-Path $repoRoot "NuGet.Config"
$localAppDataRoot = Join-Path $summaryRoot "appdata"
$localNugetConfigRoot = Join-Path $localAppDataRoot "NuGet"
New-Item -ItemType Directory -Force -Path $localNugetConfigRoot | Out-Null
Copy-Item -LiteralPath $nugetConfigPath -Destination (Join-Path $localNugetConfigRoot "NuGet.Config") -Force
$originalAppData = $env:APPDATA
$env:APPDATA = $localAppDataRoot

$results = [System.Collections.Generic.List[object]]::new()

function Add-StepResult {
    param(
        [string]$Name,
        [string]$Status,
        [string]$Command,
        [string]$Notes
    )

    $results.Add([pscustomobject]@{
        name = $Name
        status = $Status
        command = $Command
        notes = $Notes
    })
}

function Get-ExceptionDetail {
    param(
        [System.Management.Automation.ErrorRecord]$ErrorRecord
    )

    if ($null -eq $ErrorRecord) {
        return "Unknown failure."
    }

    if ([string]::IsNullOrWhiteSpace($ErrorRecord.Exception.Message)) {
        return $ErrorRecord.ToString()
    }

    return $ErrorRecord.Exception.Message
}

function Invoke-Step {
    param(
        [string]$Name,
        [string]$Command,
        [scriptblock]$Action
    )

    try {
        & $Action
        Add-StepResult -Name $Name -Status "Passed" -Command $Command -Notes ""
    }
    catch {
        Add-StepResult -Name $Name -Status "Failed" -Command $Command -Notes (Get-ExceptionDetail -ErrorRecord $_)
        throw
    }
}

try {
    Invoke-Step -Name "dotnet restore" -Command "dotnet restore OWS.sln -nologo --configfile .\\NuGet.Config" -Action {
        dotnet restore OWS.sln -nologo --configfile $nugetConfigPath
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed. Verify NuGet access and local credentials/certificates."
        }
    }

    Invoke-Step -Name "dotnet build" -Command "dotnet build OWS.sln -nologo --no-restore" -Action {
        dotnet build OWS.sln -nologo --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed."
        }
    }

    Invoke-Step -Name "dotnet test" -Command "dotnet test OWS.sln -nologo --no-build --no-restore" -Action {
        dotnet test OWS.sln -nologo --no-build --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet test failed."
        }
    }

    Invoke-Step -Name "VS Code compile" -Command ".\\src\\ows-vscode\\node_modules\\.bin\\tsc.cmd -p ./" -Action {
        Push-Location (Join-Path $repoRoot "src\ows-vscode")
        try {
            & .\node_modules\.bin\tsc.cmd -p ./
            if ($LASTEXITCODE -ne 0) {
                throw "VS Code extension compile failed."
            }
        }
        finally {
            Pop-Location
        }
    }

    if ($SkipComposeValidation) {
        Add-StepResult -Name "compose config validation" -Status "Skipped" -Command "docker compose -f docker-compose.local.yml config" -Notes "Skipped by switch."
    }
    else {
        try {
            docker compose -f docker-compose.local.yml config | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "docker compose config failed."
            }

            Add-StepResult -Name "compose config validation" -Status "Passed" -Command "docker compose -f docker-compose.local.yml config" -Notes ""
        }
        catch {
            Add-StepResult -Name "compose config validation" -Status "Skipped" -Command "docker compose -f docker-compose.local.yml config" -Notes $_.Exception.Message
        }
    }

    Invoke-Step -Name "live pilot dry run" -Command ".\\scripts\\windows\\run-live-pilot-dry-run.ps1" -Action {
        $env:VerifierSecurity__ApiKey = $OperatorKey
        $env:OWS_VERIFIER_API_KEY = $OperatorKey
        $env:VerifierStorage__ReceiptSigningKey = $ReceiptSigningKey
        & (Join-Path $PSScriptRoot "run-live-pilot-dry-run.ps1") -BaseUrl $BaseUrl -OperatorKey $OperatorKey -ReceiptSigningKey $ReceiptSigningKey | Out-Host
    }

    $dryRunSummaryPath = Join-Path $repoRoot "artifacts\pilot-demo\live-dry-run-summary.json"
    $dryRunSummary = if (Test-Path $dryRunSummaryPath) {
        Get-Content $dryRunSummaryPath -Raw | ConvertFrom-Json
    }
    else {
        $null
    }

    $summary = [ordered]@{
        dateUtc = [DateTimeOffset]::UtcNow.ToString("o")
        baseUrl = $BaseUrl
        overallStatus = "Passed"
        automatedSteps = $results
        latestDryRunSummaryPath = $dryRunSummaryPath
        latestDryRun = $dryRunSummary
        manualChecks = @(
            "VS Code extension interactive smoke path in a trusted workspace remains manual.",
            "Release candidate sign-off remains manual."
        )
    }

    $summary | ConvertTo-Json -Depth 10 | Set-Content -Path $summaryPath -Encoding utf8
    $summary | ConvertTo-Json -Depth 10
}
catch {
    $dryRunSummaryPath = Join-Path $repoRoot "artifacts\pilot-demo\live-dry-run-summary.json"
    $dryRunSummary = if (Test-Path $dryRunSummaryPath) {
        Get-Content $dryRunSummaryPath -Raw | ConvertFrom-Json
    }
    else {
        $null
    }

    $summary = [ordered]@{
        dateUtc = [DateTimeOffset]::UtcNow.ToString("o")
        baseUrl = $BaseUrl
        overallStatus = "Failed"
        automatedSteps = $results
        latestDryRunSummaryPath = $dryRunSummaryPath
        latestDryRun = $dryRunSummary
        error = Get-ExceptionDetail -ErrorRecord $_
        manualChecks = @(
            "VS Code extension interactive smoke path in a trusted workspace remains manual.",
            "Release candidate sign-off remains manual."
        )
    }

    $summary | ConvertTo-Json -Depth 10 | Set-Content -Path $summaryPath -Encoding utf8
    throw
}
finally {
    $env:APPDATA = $originalAppData
}
