#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common-local-verifier.sh"

repo_root="$(resolve_ows_repo_root)"
cd "$repo_root"

connection_string="$(get_verifier_runtime_value "$repo_root" connection_string)"
verifier_dll_path="$(get_verifier_runtime_value "$repo_root" verifier_dll_path)"
runtime_directory="$(get_verifier_runtime_value "$repo_root" runtime_directory)"
pid_file_path="$(get_verifier_runtime_value "$repo_root" pid_file_path)"
stdout_log_path="$(get_verifier_runtime_value "$repo_root" stdout_log_path)"
stderr_log_path="$(get_verifier_runtime_value "$repo_root" stderr_log_path)"
base_url="$(get_verifier_runtime_value "$repo_root" base_url)"
host="$(get_verifier_runtime_value "$repo_root" host)"
port="$(get_verifier_runtime_value "$repo_root" port)"
ensure_ows_verifier_build "$repo_root"

eval "$(get_verifier_state "$repo_root")"
case "$STATE" in
  running)
    echo "Verifier is already running with PID $PID at $base_url."
    exit 0
    ;;
  stale_pid|crashed)
    echo "Cleaning stale verifier PID state."
    rm -f "$pid_file_path"
    ;;
  port_in_use)
    echo "Verifier port $port is already in use. Stop the other process or change OWS_VERIFIER_BASE_URL." >&2
    exit 1
    ;;
  unreachable)
    echo "Verifier endpoint $base_url is already bound outside the managed lifecycle." >&2
    exit 1
    ;;
esac

mkdir -p "$runtime_directory"

echo "Starting local PostgreSQL..."
docker compose -f docker-compose.local.yml up -d || true
if ! test_tcp_port_open "127.0.0.1" 5432 >/dev/null 2>&1; then
  echo "PostgreSQL is not reachable on localhost:5432. Start docker-compose.local.yml or point OWS_VERIFIER_CONNECTION_STRING at a reachable PostgreSQL instance." >&2
  exit 1
fi

echo "Running verifier migrations..."
export VerifierStorage__Provider=postgres
export VerifierStorage__PostgresConnectionString="$connection_string"
if ! dotnet "$verifier_dll_path" migrate; then
  echo "Verifier migration failed. Check PostgreSQL availability, OWS_VERIFIER_CONNECTION_STRING, and the verifier logs helper." >&2
  exit 1
fi

echo "Starting verifier server in background on $base_url ..."
export ASPNETCORE_URLS="$base_url"
nohup dotnet "$verifier_dll_path" >"$stdout_log_path" 2>"$stderr_log_path" &
echo $! > "$pid_file_path"

for _ in $(seq 1 20); do
  sleep 0.5
  if ! kill -0 "$(cat "$pid_file_path")" 2>/dev/null; then
    rm -f "$pid_file_path"
    echo "Verifier crashed after background start. Recent logs:" >&2
    show_verifier_logs "$repo_root" false
    exit 1
  fi

  if test_verifier_http_ready "$base_url"; then
    echo "Verifier started with PID $(cat "$pid_file_path")."
    exit 0
  fi
done

kill "$(cat "$pid_file_path")" 2>/dev/null || true
rm -f "$pid_file_path"
echo "Verifier did not become ready at $base_url. Recent logs:" >&2
show_verifier_logs "$repo_root" false
exit 1
