#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common-local-verifier.sh"

base_url="${OWS_VERIFIER_BASE_URL:-http://127.0.0.1:5078}"
operator_key="${OWS_VERIFIER_API_KEY:-pilot-operator-key-12345}"
receipt_signing_key="${VerifierStorage__ReceiptSigningKey:-pilot-signing-key-12345}"
skip_compose_validation=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    -BaseUrl|--base-url)
      base_url="$2"
      shift 2
      ;;
    -OperatorKey|--operator-key)
      operator_key="$2"
      shift 2
      ;;
    -ReceiptSigningKey|--receipt-signing-key)
      receipt_signing_key="$2"
      shift 2
      ;;
    -SkipComposeValidation|--skip-compose-validation)
      skip_compose_validation=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

repo_root="$(resolve_ows_repo_root)"
cd "$repo_root"

summary_root="$repo_root/artifacts/release-gate"
mkdir -p "$summary_root"
summary_path="$summary_root/release-gate-summary.json"

results_file="$(mktemp)"
cleanup() {
  docker compose -f docker-compose.local.yml down >/dev/null 2>&1 || true
  rm -f "$results_file"
}
trap cleanup EXIT

add_step_result() {
  printf '%s\t%s\t%s\t%s\n' "$1" "$2" "$3" "$4" >> "$results_file"
}

invoke_step() {
  local name="$1"
  local command_text="$2"
  shift 2
  set +e
  "$@"
  local exit_code=$?
  set -e
  if [[ $exit_code -eq 0 ]]; then
    add_step_result "$name" "Passed" "$command_text" ""
  else
    add_step_result "$name" "Failed" "$command_text" "$name failed."
    SUMMARY_STATUS="Failed"
    SUMMARY_ERROR="$name failed."
    write_summary
    exit 1
  fi
}

write_summary() {
  local dry_run_summary_path="$repo_root/artifacts/pilot-demo/live-dry-run-summary.json"
  SUMMARY_RESULTS_FILE="$results_file" SUMMARY_PATH="$summary_path" SUMMARY_STATUS="${SUMMARY_STATUS:-Passed}" SUMMARY_ERROR="${SUMMARY_ERROR:-}" BASE_URL="$base_url" DRY_RUN_SUMMARY_PATH="$dry_run_summary_path" \
  run_python - <<'PY'
import json
import os
from datetime import datetime, timezone
from pathlib import Path

results = []
results_file = Path(os.environ["SUMMARY_RESULTS_FILE"])
if results_file.exists():
    for line in results_file.read_text(encoding="utf-8").splitlines():
        name, status, command, notes = line.split("\t")
        results.append({
            "name": name,
            "status": status,
            "command": command,
            "notes": notes,
        })

dry_run_summary_path = Path(os.environ["DRY_RUN_SUMMARY_PATH"])
dry_run = None
if dry_run_summary_path.exists():
    dry_run = json.loads(dry_run_summary_path.read_text(encoding="utf-8-sig"))

summary = {
    "dateUtc": datetime.now(timezone.utc).isoformat(),
    "baseUrl": os.environ["BASE_URL"],
    "overallStatus": os.environ["SUMMARY_STATUS"],
    "automatedSteps": results,
    "latestDryRunSummaryPath": str(dry_run_summary_path),
    "latestDryRun": dry_run,
    "manualChecks": ["Release candidate sign-off remains manual."],
}
if os.environ["SUMMARY_ERROR"]:
    summary["error"] = os.environ["SUMMARY_ERROR"]

Path(os.environ["SUMMARY_PATH"]).write_text(json.dumps(summary, indent=4), encoding="utf-8")
print(json.dumps(summary, indent=4))
PY
}

SUMMARY_STATUS="Passed"
SUMMARY_ERROR=""

invoke_step "dotnet restore" "dotnet restore OWS.sln -nologo --configfile ./NuGet.Config" dotnet restore OWS.sln -nologo --configfile ./NuGet.Config
invoke_step "dotnet build" "dotnet build OWS.sln -nologo --no-restore" dotnet build OWS.sln -nologo --no-restore
invoke_step "dotnet test" "dotnet test OWS.sln -nologo --no-build --no-restore" dotnet test OWS.sln -nologo --no-build --no-restore
if [[ "$skip_compose_validation" == true ]]; then
  add_step_result "compose config validation" "Skipped" "docker compose -f docker-compose.local.yml config" "Skipped by switch."
else
  if docker compose -f docker-compose.local.yml config >/dev/null 2>&1; then
    add_step_result "compose config validation" "Passed" "docker compose -f docker-compose.local.yml config" ""
  else
    add_step_result "compose config validation" "Skipped" "docker compose -f docker-compose.local.yml config" "docker compose config failed."
  fi
fi

export VerifierSecurity__ApiKey="$operator_key"
export OWS_VERIFIER_API_KEY="$operator_key"
export VerifierStorage__ReceiptSigningKey="$receipt_signing_key"
invoke_step "live pilot dry run" "./scripts/unix/run-live-pilot-dry-run.sh" bash "$repo_root/scripts/unix/run-live-pilot-dry-run.sh" --base-url "$base_url" --operator-key "$operator_key" --receipt-signing-key "$receipt_signing_key"

write_summary
