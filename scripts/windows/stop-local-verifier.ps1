Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common-local-verifier.ps1")
$repoRoot = Resolve-OwsRepoRoot -StartDirectory $PSScriptRoot
$runtimeInfo = Get-OwsVerifierRuntimeInfo -RepoRoot $repoRoot
$state = Get-VerifierState -RuntimeInfo $runtimeInfo

switch ($state.State) {
    "not_started" {
        Write-Host "Verifier is not running."
        exit 0
    }
    "stale_pid" {
        Remove-Item $runtimeInfo.PidFilePath -Force -ErrorAction SilentlyContinue
        Write-Host "Removed stale PID file."
        exit 0
    }
    "crashed" {
        Remove-Item $runtimeInfo.PidFilePath -Force -ErrorAction SilentlyContinue
        Write-Host "Removed crashed verifier PID file."
        exit 0
    }
    "unreachable" {
        if (-not $state.ProcessRunning) {
            $orphanedProcessIds = @(Get-OwsVerifierProcessIds -RuntimeInfo $runtimeInfo)
            if ($orphanedProcessIds.Count -eq 0) {
                Write-Host "Verifier is unreachable and not managed by the PID file."
                exit 1
            }

            foreach ($orphanedProcessId in $orphanedProcessIds) {
                Stop-OwsProcessTree -ProcessId ([int]$orphanedProcessId)
            }
            Write-Host "Stopped orphaned verifier process(es)."
            exit 0
        }
    }
    "port_in_use" {
        $orphanedProcessIds = @(Get-OwsVerifierProcessIds -RuntimeInfo $runtimeInfo)
        if ($orphanedProcessIds.Count -eq 0) {
            Write-Host "Verifier port is in use by another process and no managed PID file exists."
            exit 1
        }

        foreach ($orphanedProcessId in $orphanedProcessIds) {
            Stop-OwsProcessTree -ProcessId ([int]$orphanedProcessId)
        }
        Write-Host "Stopped orphaned verifier process(es)."
        exit 0
    }
}

Stop-OwsProcessTree -ProcessId ([int]$state.Pid)
Remove-Item $runtimeInfo.PidFilePath -Force -ErrorAction SilentlyContinue
Write-Host "Verifier stopped."
