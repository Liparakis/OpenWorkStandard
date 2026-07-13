#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common-local-verifier.sh"

base_url="http://localhost:5078"
api_key="${OWS_VERIFIER_API_KEY:-}"
skip_blob_probe=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    -BaseUrl|--base-url)
      base_url="$2"
      shift 2
      ;;
    -ApiKey|--api-key)
      api_key="$2"
      shift 2
      ;;
    -SkipBlobProbe|--skip-blob-probe)
      skip_blob_probe=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

repo_root="$(resolve_ows_repo_root)"
timestamp="$(date +"%Y-%m-%d %H:%M:%S")"
base_url="${base_url%/}"
all_passed=true

write_check() {
  local label="$1"
  local pass="$2"
  local note="${3:-}"
  if [[ "$pass" == true ]]; then
    printf '  [OK]  %s\n' "$label"
  else
    printf '  [!!]  %s\n' "$label"
    [[ -n "$note" ]] && printf '        %s\n' "$note"
    all_passed=false
  fi
}

write_warn() {
  printf '  [W]   %s\n' "$1"
}

write_info() {
  printf '        %s\n' "$1"
}

invoke_safe_get() {
  local url="$1"
  local headers=()
  if [[ -n "$api_key" ]]; then
    headers=(-H "X-OWS-Verifier-Key: $api_key")
  fi
  local body_file code
  body_file="$(mktemp)"
  code="$(curl -sS -o "$body_file" -w "%{http_code}" "${headers[@]}" "$url" || true)"
  printf '%s\n' "$code"
  cat "$body_file"
  rm -f "$body_file"
}

echo
echo "========================================================"
echo "  OWS Verifier Operations Readiness Check"
echo "  $timestamp"
echo "  Target: $base_url"
echo "========================================================"
echo

echo "--- 1. HTTP Reachability ---"
mapfile -t health_response < <(invoke_safe_get "$base_url/health")
health_code="${health_response[0]}"
write_check "/health returns 200" "$([[ "$health_code" == "200" ]] && echo true || echo false)" "Verifier is not reachable at $base_url."
echo

echo "--- 2. Readiness Dependencies ---"
mapfile -t ready_response < <(invoke_safe_get "$base_url/ready")
ready_code="${ready_response[0]}"
ready_body="$(printf '%s\n' "${ready_response[@]:1}")"
write_check "/ready returns 200" "$([[ "$ready_code" == "200" ]] && echo true || echo false)" "Verifier storage or package storage is unhealthy."
if [[ -n "$ready_body" ]]; then
  READY_BODY="$ready_body" run_python - <<'PY'
import json
import os
try:
    data = json.loads(os.environ["READY_BODY"])
except Exception:
    raise SystemExit(0)
deps = data.get("dependencies", {})
for key in ("storageReady", "packageStorageReady", "signingConfigured"):
    print(f"{key}={str(deps.get(key)).lower()}")
if deps.get("authMode"):
    print(f"authMode={deps['authMode']}")
PY
fi | while IFS='=' read -r key value; do
  case "$key" in
    storageReady) write_check "storageReady" "$([[ "$value" == "true" ]] && echo true || echo false)" "PostgreSQL is not ready." ;;
    packageStorageReady) write_check "packageStorageReady" "$([[ "$value" == "true" ]] && echo true || echo false)" "Package storage is not accessible." ;;
    signingConfigured) write_check "signingConfigured" "$([[ "$value" == "true" ]] && echo true || echo false)" "Receipt signing key is not configured." ;;
    authMode) write_info "Auth mode: $value" ;;
  esac
done
echo

echo "--- 3. Diagnostics Summary ---"
[[ -n "$api_key" ]] || write_warn "No API key set. Diagnostics may return 401."
mapfile -t diag_response < <(invoke_safe_get "$base_url/diagnostics/summary")
diag_code="${diag_response[0]}"
diag_body="$(printf '%s\n' "${diag_response[@]:1}")"
write_check "/diagnostics/summary returns 200" "$([[ "$diag_code" == "200" ]] && echo true || echo false)" "Set OWS_VERIFIER_API_KEY or pass --api-key."
if [[ "$diag_code" == "200" && -n "$diag_body" ]]; then
  DIAG_BODY="$diag_body" run_python - <<'PY'
import json
import os
data = json.loads(os.environ["DIAG_BODY"])
for key in ("signingKeyFingerprintPresent", "packageStorageConfigured", "packageStorageReady", "packageBlobCount"):
    if key in data:
        print(f"{key}={data[key]}")
jobs = data.get("packageVerificationJobs")
if jobs:
    for key in ("pending", "running", "succeeded", "failed"):
        print(f"jobs.{key}={jobs.get(key, 0)}")
if data.get("packageStoragePath"):
    print(f"packageStoragePath={data['packageStoragePath']}")
PY
fi | while IFS='=' read -r key value; do
  case "$key" in
    signingKeyFingerprintPresent) write_check "Signing key fingerprint present" "$([[ "$value" == "True" || "$value" == "true" ]] && echo true || echo false)" "No signing key fingerprint is exposed." ;;
    packageStorageConfigured) write_check "Package storage configured" "$([[ "$value" == "True" || "$value" == "true" ]] && echo true || echo false)" "Package storage is not configured." ;;
    packageStorageReady) write_check "Package storage accessible" "$([[ "$value" == "True" || "$value" == "true" ]] && echo true || echo false)" "Package storage is not accessible." ;;
    packageBlobCount) write_info "Package blob count: $value" ;;
    jobs.pending) pending="$value" ;;
    jobs.running) running="$value" ;;
    jobs.succeeded) succeeded="$value" ;;
    jobs.failed) failed="$value" ;;
    packageStoragePath) package_storage_path="$value" ;;
  esac
done

if [[ -n "${pending:-}" ]]; then
  write_info "Verification jobs - pending: ${pending:-0} | running: ${running:-0} | succeeded: ${succeeded:-0} | failed: ${failed:-0}"
  [[ "${running:-0}" -gt 0 ]] && write_warn "There are running verification jobs. After a restart this may be temporary."
  [[ "${failed:-0}" -gt 0 ]] && write_warn "${failed:-0} verification job(s) have failed."
fi
echo

echo "--- 4. Backup Documentation ---"
for doc in "docs/operations/BACKUP_RESTORE.md" "docs/operations/OPERATIONS_RUNBOOK.md" "docs/operations/SECURITY_HARDENING.md"; do
  if [[ -f "$repo_root/$doc" ]]; then
    write_check "$doc exists" true
  else
    write_check "$doc exists" false "Operator documentation is missing."
  fi
done
echo

if [[ "$skip_blob_probe" != true ]]; then
  echo "--- 5. Local Package Blob Directory Probe ---"
  if [[ -n "${package_storage_path:-}" ]]; then
    write_info "Blob storage path: $package_storage_path"
    if [[ -d "$package_storage_path" ]]; then
      probe_file="$package_storage_path/.ows-readiness-probe"
      if printf 'readiness-check\n' > "$probe_file" 2>/dev/null; then
        rm -f "$probe_file"
        write_check "Blob directory is writable" true
      else
        write_check "Blob directory is writable" false "Could not write to the blob directory."
      fi
    else
      write_warn "Blob path '$package_storage_path' is not accessible from this machine."
    fi
  else
    write_warn "Package storage path is not exposed in diagnostics. Skipping local blob probe."
  fi
  echo
fi

echo "========================================================"
if [[ "$all_passed" == true ]]; then
  echo "  RESULT: All checks passed. Verifier appears ready."
else
  echo "  RESULT: One or more checks failed. See details above."
fi
echo "========================================================"
echo

[[ "$all_passed" == true ]]
