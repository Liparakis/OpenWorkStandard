#!/usr/bin/env bash
set -euo pipefail

resolve_ows_repo_root() {
  local current_dir
  current_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

  while [[ "$current_dir" != "/" ]]; do
    if [[ -f "$current_dir/OWS.sln" ]]; then
      printf '%s\n' "$current_dir"
      return 0
    fi

    current_dir="$(dirname "$current_dir")"
  done

  echo "Could not resolve the OWS repository root." >&2
  return 1
}

test_tcp_port_open() {
  local host_name="$1"
  local port="$2"
  python3 - "$host_name" "$port" <<'PY'
import socket
import sys

host = sys.argv[1]
port = int(sys.argv[2])
sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
sock.settimeout(2)
try:
    sock.connect((host, port))
except OSError:
    sys.exit(1)
finally:
    sock.close()
PY
}

test_verifier_http_ready() {
  local base_url="$1"
  local status auth_headers=()
  if [[ -n "${OWS_VERIFIER_API_KEY:-}" ]]; then
    auth_headers=(-H "X-OWS-Verifier-Key: $OWS_VERIFIER_API_KEY")
  fi

  status="$(curl -s -o /dev/null -w "%{http_code}" "${auth_headers[@]}" "$base_url/sessions/not-a-real-session/head" || true)"
  [[ "$status" == "404" || "$status" == "401" ]]
}

get_verifier_runtime_value() {
  local repo_root="$1"
  local key="$2"
  local base_url="${OWS_VERIFIER_BASE_URL:-http://127.0.0.1:5078}"
  local connection_string="${OWS_VERIFIER_CONNECTION_STRING:-Host=localhost;Port=5432;Database=ows_verifier;Username=ows;Password=ows-dev}"
  local runtime_directory="$repo_root/artifacts/local-verifier"

  case "$key" in
    repo_root) printf '%s\n' "$repo_root" ;;
    base_url) printf '%s\n' "${base_url%/}" ;;
    host) python3 - "$base_url" <<'PY'
import sys
from urllib.parse import urlparse
print(urlparse(sys.argv[1]).hostname or "")
PY
    ;;
    port) python3 - "$base_url" <<'PY'
import sys
from urllib.parse import urlparse
parsed = urlparse(sys.argv[1])
print(parsed.port or (443 if parsed.scheme == "https" else 80))
PY
    ;;
    connection_string) printf '%s\n' "$connection_string" ;;
    verifier_dll_path) printf '%s\n' "$repo_root/src/Ows.Verifier.Server/bin/Debug/net9.0/Ows.Verifier.Server.dll" ;;
    runtime_directory) printf '%s\n' "$runtime_directory" ;;
    pid_file_path) printf '%s\n' "$runtime_directory/verifier.pid" ;;
    stdout_log_path) printf '%s\n' "$runtime_directory/verifier.stdout.log" ;;
    stderr_log_path) printf '%s\n' "$runtime_directory/verifier.stderr.log" ;;
    *) echo "Unknown runtime key: $key" >&2; return 1 ;;
  esac
}

ensure_ows_verifier_build() {
  local repo_root="$1"
  local verifier_dll_path
  verifier_dll_path="$(get_verifier_runtime_value "$repo_root" verifier_dll_path)"

  if [[ -f "$verifier_dll_path" ]]; then
    return 0
  fi

  echo "Verifier build output is missing. Running 'dotnet build OWS.sln -nologo'..."
  (
    cd "$repo_root"
    dotnet build OWS.sln -nologo
  )

  if [[ ! -f "$verifier_dll_path" ]]; then
    echo "Verifier server build output is still missing after build." >&2
    return 1
  fi
}

get_verifier_state() {
  local repo_root="$1"
  local base_url host port pid_file_path stdout_log_path stderr_log_path
  base_url="$(get_verifier_runtime_value "$repo_root" base_url)"
  host="$(get_verifier_runtime_value "$repo_root" host)"
  port="$(get_verifier_runtime_value "$repo_root" port)"
  pid_file_path="$(get_verifier_runtime_value "$repo_root" pid_file_path)"
  stdout_log_path="$(get_verifier_runtime_value "$repo_root" stdout_log_path)"
  stderr_log_path="$(get_verifier_runtime_value "$repo_root" stderr_log_path)"

  local pid_value="" state="not_started" message="Verifier is not started." process_running=false port_bound=false http_ready=false
  if test_tcp_port_open "$host" "$port" >/dev/null 2>&1; then
    port_bound=true
  fi
  if test_verifier_http_ready "$base_url"; then
    http_ready=true
  fi

  if [[ -f "$pid_file_path" ]]; then
    pid_value="$(tr -d '[:space:]' < "$pid_file_path")"
    if [[ ! "$pid_value" =~ ^[0-9]+$ ]]; then
      state="stale_pid"
      message="PID file is invalid."
    elif kill -0 "$pid_value" 2>/dev/null; then
      process_running=true
      if [[ "$http_ready" == true ]]; then
        state="running"
        message="Verifier is running."
      else
        state="unreachable"
        message="Verifier process is running but the HTTP endpoint is not reachable."
      fi
    elif [[ -f "$stdout_log_path" || -f "$stderr_log_path" ]]; then
      state="crashed"
      message="Verifier process is gone but log files exist."
    else
      state="stale_pid"
      message="PID file points to a process that no longer exists."
    fi
  elif [[ "$http_ready" == true ]]; then
    state="unreachable"
    message="Verifier HTTP endpoint is reachable but no managed PID file exists."
  elif [[ "$port_bound" == true ]]; then
    state="port_in_use"
    message="The verifier port is already in use by another process."
  fi

  printf 'STATE=%s\nMESSAGE=%s\nPID=%s\nPROCESS_RUNNING=%s\nPORT_BOUND=%s\nHTTP_READY=%s\n' \
    "$state" "$message" "$pid_value" "$process_running" "$port_bound" "$http_ready"
}

show_verifier_logs() {
  local repo_root="$1"
  local all="${2:-false}"
  local runtime_directory stdout_log_path stderr_log_path
  runtime_directory="$(get_verifier_runtime_value "$repo_root" runtime_directory)"
  stdout_log_path="$(get_verifier_runtime_value "$repo_root" stdout_log_path)"
  stderr_log_path="$(get_verifier_runtime_value "$repo_root" stderr_log_path)"

  if [[ ! -d "$runtime_directory" ]]; then
    echo "Verifier logs directory does not exist: $runtime_directory"
    return 0
  fi

  for entry in "stdout:$stdout_log_path" "stderr:$stderr_log_path"; do
    local label="${entry%%:*}"
    local path="${entry#*:}"
    if [[ ! -f "$path" ]]; then
      continue
    fi

    echo "=== $label ==="
    if [[ "$all" == "true" ]]; then
      cat "$path"
    else
      tail -n 50 "$path"
    fi
  done
}
