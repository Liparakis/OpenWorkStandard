#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common-local-verifier.sh"

repo_root="$(resolve_ows_repo_root)"
pid_file_path="$(get_verifier_runtime_value "$repo_root" pid_file_path)"

eval "$(get_verifier_state "$repo_root")"
case "$STATE" in
  not_started)
    echo "Verifier is not running."
    exit 0
    ;;
  stale_pid)
    rm -f "$pid_file_path"
    echo "Removed stale PID file."
    exit 0
    ;;
  crashed)
    rm -f "$pid_file_path"
    echo "Removed crashed verifier PID file."
    exit 0
    ;;
  port_in_use)
    echo "Verifier port is in use by another process and no managed PID file exists." >&2
    exit 1
    ;;
  unreachable)
    if [[ "$PROCESS_RUNNING" != "true" ]]; then
      echo "Verifier is unreachable and not managed by the PID file." >&2
      exit 1
    fi
    ;;
esac

kill "$PID"
rm -f "$pid_file_path"
echo "Verifier stopped."
