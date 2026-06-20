Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common-local-verifier.ps1")
$repoRoot = Resolve-OwsRepoRoot -StartDirectory $PSScriptRoot
$runtimeInfo = Get-OwsVerifierRuntimeInfo -RepoRoot $repoRoot
$state = Get-VerifierState -RuntimeInfo $runtimeInfo

[pscustomobject]@{
    State = $state.State
    Message = $state.Message
    Pid = $state.Pid
    ProcessRunning = $state.ProcessRunning
    PortBound = $state.PortBound
    HttpReady = $state.HttpReady
    BaseUrl = $runtimeInfo.BaseUrl
    PidFilePath = $runtimeInfo.PidFilePath
    StdoutLogPath = $runtimeInfo.StdoutLogPath
    StderrLogPath = $runtimeInfo.StderrLogPath
} | Format-List
