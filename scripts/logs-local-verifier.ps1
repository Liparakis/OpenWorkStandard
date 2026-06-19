param(
    [switch]$All
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common-local-verifier.ps1")
$repoRoot = Resolve-OwsRepoRoot -StartDirectory $PSScriptRoot
$runtimeInfo = Get-OwsVerifierRuntimeInfo -RepoRoot $repoRoot
Show-VerifierLogs -RuntimeInfo $runtimeInfo -All:$All
