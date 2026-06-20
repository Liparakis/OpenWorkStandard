#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common-local-verifier.sh"

repo_root="$(resolve_ows_repo_root)"
pid_file_path="$(get_verifier_runtime_value "$repo_root" pid_file_path)"
stdout_log_path="$(get_verifier_runtime_value "$repo_root" stdout_log_path)"
stderr_log_path="$(get_verifier_runtime_value "$repo_root" stderr_log_path)"
base_url="$(get_verifier_runtime_value "$repo_root" base_url)"

eval "$(get_verifier_state "$repo_root")"

printf 'State: %s\nMessage: %s\nPid: %s\nProcessRunning: %s\nPortBound: %s\nHttpReady: %s\nBaseUrl: %s\nPidFilePath: %s\nStdoutLogPath: %s\nStderrLogPath: %s\n' \
  "$STATE" "$MESSAGE" "$PID" "$PROCESS_RUNNING" "$PORT_BOUND" "$HTTP_READY" "$base_url" "$pid_file_path" "$stdout_log_path" "$stderr_log_path"
