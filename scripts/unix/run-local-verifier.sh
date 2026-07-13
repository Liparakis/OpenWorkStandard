#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common-local-verifier.sh"

repo_root="$(resolve_ows_repo_root)"
cd "$repo_root"

connection_string="$(get_verifier_runtime_value "$repo_root" connection_string)"
verifier_dll_path="$(get_verifier_runtime_value "$repo_root" verifier_dll_path)"
base_url="$(get_verifier_runtime_value "$repo_root" base_url)"
port="$(get_verifier_runtime_value "$repo_root" port)"
ensure_ows_verifier_build "$repo_root"

eval "$(get_verifier_state "$repo_root")"
if [[ "$STATE" == "running" ]]; then
  echo "Verifier is already running at $base_url. Use the status or stop helper first." >&2
  exit 1
fi

if [[ "$STATE" == "port_in_use" || "$STATE" == "unreachable" ]]; then
  echo "Verifier port $port is already in use or unmanaged. Resolve the conflict before running the verifier." >&2
  exit 1
fi

echo "Starting local PostgreSQL..."
compose_status=0
docker compose -f docker-compose.local.yml up -d || compose_status=$?
if ! wait_for_postgres_ready 60; then
  echo "PostgreSQL did not become ready on localhost:5432 (docker compose exit code $compose_status). Start docker-compose.local.yml or point OWS_VERIFIER_CONNECTION_STRING at a reachable PostgreSQL instance." >&2
  exit 1
fi

echo "Running verifier migrations..."
export VerifierStorage__Provider=postgres
export VerifierStorage__PostgresConnectionString="$connection_string"
if ! dotnet "$verifier_dll_path" migrate; then
  echo "Verifier migration failed. Check PostgreSQL availability, OWS_VERIFIER_CONNECTION_STRING, and the verifier logs helper." >&2
  exit 1
fi

echo "Starting verifier server on $base_url ..."
export ASPNETCORE_URLS="$base_url"
dotnet "$verifier_dll_path"
