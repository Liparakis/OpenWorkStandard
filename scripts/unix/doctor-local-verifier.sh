#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common-local-verifier.sh"

repo_root="$(resolve_ows_repo_root)"
base_url="$(get_verifier_runtime_value "$repo_root" base_url)"
port="$(get_verifier_runtime_value "$repo_root" port)"
verifier_dll_path="$(get_verifier_runtime_value "$repo_root" verifier_dll_path)"

dotnet_available=false
docker_available=false
compose_file_exists=false
verifier_built=false
postgres_reachable=false

command -v dotnet >/dev/null 2>&1 && dotnet_available=true
command -v docker >/dev/null 2>&1 && docker_available=true
[[ -f "$repo_root/docker-compose.local.yml" ]] && compose_file_exists=true
[[ -f "$verifier_dll_path" ]] && verifier_built=true
test_tcp_port_open "127.0.0.1" 5432 >/dev/null 2>&1 && postgres_reachable=true

eval "$(get_verifier_state "$repo_root")"

cat <<EOF
RepoRoot: $repo_root
DotnetAvailable: $dotnet_available
DockerAvailable: $docker_available
ComposeFileExists: $compose_file_exists
VerifierBuilt: $verifier_built
PostgresReachable: $postgres_reachable
VerifierState: $STATE
VerifierMessage: $MESSAGE
VerifierBaseUrl: $base_url
EOF

if [[ "$dotnet_available" != true ]]; then
  echo "Action: install .NET 9 SDK or put dotnet on PATH."
fi

if [[ "$compose_file_exists" != true ]]; then
  echo "Action: run this helper from an OWS checkout that contains docker-compose.local.yml."
fi

if [[ "$verifier_built" != true ]]; then
  echo "Action: run dotnet build OWS.sln -nologo, or use start-local-verifier to auto-build."
fi

if [[ "$postgres_reachable" != true ]]; then
  echo "Action: start PostgreSQL with docker compose -f docker-compose.local.yml up -d, or set OWS_VERIFIER_CONNECTION_STRING."
fi

case "$STATE" in
  stale_pid|crashed)
    echo "Action: run stop-local-verifier, then start-local-verifier."
    ;;
  port_in_use)
    echo "Action: free port $port or set OWS_VERIFIER_BASE_URL."
    ;;
  unreachable)
    echo "Action: inspect logs-local-verifier and confirm the process owns $base_url."
    ;;
esac
