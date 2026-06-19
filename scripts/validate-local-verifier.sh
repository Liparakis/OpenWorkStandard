#!/usr/bin/env bash
set -euo pipefail

# Resolve script directory and source common if exists
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMMON_FILE="$SCRIPT_DIR/common-local-verifier.sh"

if [[ -f "$COMMON_FILE" ]]; then
  source "$COMMON_FILE"
fi

# Find repo root
resolve_ows_repo_root() {
  local current_dir="$SCRIPT_DIR"
  while [[ "$current_dir" != "/" && -n "$current_dir" ]]; do
    if [[ -f "$current_dir/OWS.sln" ]]; then
      echo "$current_dir"
      return 0
    fi
    current_dir="$(dirname "$current_dir")"
  done
  echo "$SCRIPT_DIR" # Fallback
}

repo_root="$(resolve_ows_repo_root)"

# Define base URL and postgres connection string defaults
base_url="${OWS_VERIFIER_BASE_URL:-http://127.0.0.1:5078}"
verifier_dll_path="$repo_root/src/Ows.Verifier.Server/bin/Debug/net9.0/Ows.Verifier.Server.dll"

echo "========================================="
echo " OWS Environment Diagnostics (Shell)"
echo "========================================="
echo "Repository Root: $repo_root"

# 1. Path with spaces check
if [[ "$repo_root" == *" "* ]]; then
  echo -e "\033[33m[!] Warning: Repository path contains spaces: '$repo_root'\033[0m"
  echo -e "\033[33m    Make sure to quote paths when invoking commands manually.\033[0m"
else
  echo -e "\033[32m[x] Path is space-free.\033[0m"
fi

# 2. .NET 9 SDK Check
if command -v dotnet >/dev/null 2>&1; then
  dotnet_version="$(dotnet --version | tr -d '\r')"
  echo -e "\033[32m[x] .NET SDK is available (Version: $dotnet_version)\033[0m"
else
  echo -e "\033[31m[X] Error: .NET SDK not found.\033[0m"
  echo -e "\033[33m    Action: Install .NET 9 SDK and ensure 'dotnet' is in your PATH.\033[0m"
fi

# 3. Docker availability and daemon state
docker_available=false
docker_running=false
if command -v docker >/dev/null 2>&1; then
  docker_available=true
  if docker info >/dev/null 2>&1; then
    docker_running=true
  fi
fi

if [[ "$docker_available" == true ]]; then
  if [[ "$docker_running" == true ]]; then
    echo -e "\033[32m[x] Docker is running.\033[0m"
  else
    echo -e "\033[33m[!] Warning: Docker command exists, but the Docker daemon is NOT running.\033[0m"
    echo -e "\033[33m    Action: Start Docker Desktop or your local Docker daemon.\033[0m"
  fi
else
  echo -e "\033[33m[!] Warning: Docker command not found.\033[0m"
  echo -e "\033[33m    Action: If you wish to use PostgreSQL in a container, please install Docker.\033[0m"
fi

# 4. PostgreSQL Port Reachability
postgres_reachable=false
if command -v nc >/dev/null 2>&1; then
  if nc -z -w 1 127.0.0.1 5432 >/dev/null 2>&1; then
    postgres_reachable=true
  fi
elif bash -c 'exec 3<>/dev/tcp/127.0.0.1/5432' >/dev/null 2>&1; then
  postgres_reachable=true
fi

if [[ "$postgres_reachable" == true ]]; then
  echo -e "\033[32m[x] PostgreSQL is reachable on 127.0.0.1:5432.\033[0m"
else
  echo -e "\033[33m[!] Warning: PostgreSQL is NOT reachable on port 5432.\033[0m"
  if [[ "$docker_running" == true ]]; then
    echo -e "\033[33m    Action: Start Postgres using: docker compose -f docker-compose.local.yml up -d\033[0m"
  else
    echo -e "\033[33m    Action: Start your local PostgreSQL service on port 5432.\033[0m"
  fi
fi

# 5. Verifier Build Output Check
if [[ -f "$verifier_dll_path" ]]; then
  echo -e "\033[32m[x] Verifier DLL is built.\033[0m"
else
  echo -e "\033[33m[!] Warning: Verifier DLL not found at: $verifier_dll_path\033[0m"
  echo -e "\033[33m    Action: Run 'dotnet build OWS.sln -nologo' to compile.\033[0m"
fi

# 6. Verifier Server status & Port conflicts
verifier_port_reachable=false
if command -v nc >/dev/null 2>&1; then
  if nc -z -w 1 127.0.0.1 5078 >/dev/null 2>&1; then
    verifier_port_reachable=true
  fi
elif bash -c 'exec 3<>/dev/tcp/127.0.0.1/5078' >/dev/null 2>&1; then
  verifier_port_reachable=true
fi

if [[ "$verifier_port_reachable" == true ]]; then
  echo -e "\033[32m[x] Port 5078 is active.\033[0m"
else
  echo -e "\033[30;1m[ ] Verifier server is not running (Port 5078 is free).\033[0m"
  echo -e "\033[33m    Action: Start the verifier using: ./scripts/start-local-verifier.sh\033[0m"
fi

# 7. Health and Readiness Endpoints
if [[ "$verifier_port_reachable" == true && "$(command -v curl)" ]]; then
  health_code=$(curl -s -o /dev/null -w "%{http_code}" "$base_url/health" || echo "000")
  ready_code=$(curl -s -o /dev/null -w "%{http_code}" "$base_url/ready" || echo "000")
  
  if [[ "$health_code" == "200" ]]; then
    echo -e "\033[32m[x] Verifier /health check passed (OK).\033[0m"
  else
    echo -e "\033[31m[X] Error: Verifier /health returned status code: $health_code\033[0m"
  fi
  
  if [[ "$ready_code" == "200" ]]; then
    echo -e "\033[32m[x] Verifier /ready check passed (OK).\033[0m"
  else
    echo -e "\033[31m[X] Error: Verifier /ready returned status code: $ready_code (Check DB Connection / Configuration)\033[0m"
  fi
fi

# 8. API Key Alignment
if [[ "$verifier_port_reachable" == true && "$(command -v curl)" ]]; then
  no_key_code=$(curl -s -o /dev/null -w "%{http_code}" "$base_url/sessions/not-a-real-session/head" || echo "000")
  if [[ "$no_key_code" == "401" ]]; then
    echo -e "\033[32m[x] Verifier API key guard is ACTIVE.\033[0m"
    if [[ -z "${OWS_VERIFIER_API_KEY:-}" ]]; then
      echo -e "\033[31m[X] Error: Verifier expects API Key but OWS_VERIFIER_API_KEY is not set in shell.\033[0m"
      echo -e "\033[33m    Action: Run: export OWS_VERIFIER_API_KEY='<your-key>' before running CLI commands.\033[0m"
    else
      echo -e "\033[32m[x] OWS_VERIFIER_API_KEY is set in local environment.\033[0m"
    fi
  else
    echo -e "\033[30;1m[ ] Verifier API key guard is INACTIVE (No Auth).\033[0m"
  fi
fi

# 9. Generated scripts verification
generated_folder="$repo_root/artifacts/generated-scripts"
if [[ -d "$generated_folder" ]]; then
  scripts_count=$(find "$generated_folder" -name "*.sh" | wc -l)
  if [[ $scripts_count -gt 0 ]]; then
    echo -e "\033[32m[x] Generated launcher scripts are present in artifacts/generated-scripts/ ($scripts_count scripts).\033[0m"
  else
    echo -e "\033[33m[!] Warning: artifacts/generated-scripts/ exists but has no .sh scripts.\033[0m"
    echo -e "\033[33m    Action: Build OWS.sln to copy launcher scripts.\033[0m"
  fi
else
  echo -e "\033[33m[!] Warning: artifacts/generated-scripts/ folder is missing.\033[0m"
  echo -e "\033[33m    Action: Build OWS.sln to generate scripts.\033[0m"
fi

echo "========================================="
echo "Diagnostics complete."
echo "========================================="
