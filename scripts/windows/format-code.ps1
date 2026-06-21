Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common-local-verifier.ps1")
$repoRoot = Resolve-OwsRepoRoot -StartDirectory $PSScriptRoot

Write-Host "Formatting code in $repoRoot based on .editorconfig..."
Push-Location $repoRoot
try {
    dotnet format
}
finally {
    Pop-Location
}
