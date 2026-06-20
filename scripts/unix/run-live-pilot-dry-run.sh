#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common-local-verifier.sh"

base_url="${OWS_VERIFIER_BASE_URL:-http://127.0.0.1:5078}"
operator_key="${OWS_VERIFIER_API_KEY:-pilot-operator-key-12345}"
receipt_signing_key="${VerifierStorage__ReceiptSigningKey:-pilot-signing-key-12345}"
heartbeat_wait_seconds=65

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
    -HeartbeatWaitSeconds|--heartbeat-wait-seconds)
      heartbeat_wait_seconds="$2"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

repo_root="$(resolve_ows_repo_root)"
cd "$repo_root"

export VerifierSecurity__ApiKey="$operator_key"
export OWS_VERIFIER_API_KEY="$operator_key"
export VerifierStorage__ReceiptSigningKey="$receipt_signing_key"

cli_dll="$repo_root/src/Ows.Cli/bin/Debug/net9.0/Ows.Cli.dll"
[[ -f "$cli_dll" ]] || { echo "CLI build output is missing at '$cli_dll'. Run 'dotnet build OWS.sln -nologo' first." >&2; exit 1; }

timestamp="$(date -u +"%Y%m%d%H%M%S")"
prefix="live-$timestamp"
assignment_root="$repo_root/artifacts/pilot-demo/live-assignment-$timestamp"
watcher_log_root="$repo_root/artifacts/pilot-demo/watcher-logs-$timestamp"
summary_path="$repo_root/artifacts/pilot-demo/live-dry-run-summary.json"
watcher_stdout_path="$watcher_log_root/watcher.stdout.log"
watcher_stderr_path="$watcher_log_root/watcher.stderr.log"
watcher_pid=""
student_key=""
reviewer_key=""

invoke_ows_cli_json() {
  local working_directory="$1"
  shift
  local output exit_code json
  set +e
  output="$(cd "$working_directory" && dotnet "$cli_dll" "$@" --json 2>&1)"
  exit_code=$?
  set -e
  json="$(printf '%s' "$output" | run_python - <<'PY'
import sys
text = sys.stdin.read()
start = text.find("{")
end = text.rfind("}")
if start < 0 or end < start:
    raise SystemExit(f"CLI JSON output not found.\n{text}")
print(text[start:end + 1])
PY
)"
  OWS_CLI_EXIT_CODE="$exit_code"
  OWS_CLI_OUTPUT="$output"
  OWS_CLI_JSON="$json"
}

json_field() {
  local json="$1"
  local field="$2"
  JSON_TEXT="$json" FIELD_NAME="$field" run_python - <<'PY'
import json
import os
data = json.loads(os.environ["JSON_TEXT"])
value = data
for part in os.environ["FIELD_NAME"].split("."):
    value = value.get(part)
    if value is None:
        break
if value is None:
    print("")
elif isinstance(value, bool):
    print("true" if value else "false")
else:
    print(value)
PY
}

wait_until() {
  local failure_message="$1"
  local attempts="$2"
  local delay_seconds="$3"
  shift 3
  for ((attempt=0; attempt<attempts; attempt++)); do
    if "$@"; then
      return 0
    fi
    sleep "$delay_seconds"
  done
  echo "$failure_message" >&2
  exit 1
}

cleanup() {
  export OWS_VERIFIER_API_KEY="$student_key"
  if [[ -n "$assignment_root" && -d "$assignment_root/.ows" ]]; then
    (cd "$assignment_root" && dotnet "$cli_dll" watch stop >/dev/null 2>&1) || true
  fi
  if [[ -n "$watcher_pid" ]] && kill -0 "$watcher_pid" 2>/dev/null; then
    kill "$watcher_pid" 2>/dev/null || true
  fi
  export OWS_VERIFIER_API_KEY="$operator_key"
  bash "$repo_root/scripts/unix/stop-local-verifier.sh" >/dev/null 2>&1 || true
}
trap cleanup EXIT

mkdir -p "$assignment_root" "$watcher_log_root"
printf 'Initial draft.\n' > "$assignment_root/draft.txt"

bash "$repo_root/scripts/unix/start-local-verifier.sh"

health="$(curl -fsS "$base_url/health")"
ready="$(curl -fsS "$base_url/ready")"
diagnostics_before="$(curl -fsS -H "X-OWS-Verifier-Key: $operator_key" "$base_url/diagnostics/summary")"

fixture="$(bash "$repo_root/scripts/unix/setup-pilot-fixture.sh" --base-url "$base_url" --operator-key "$operator_key" --prefix "$prefix" --as-json)"
student_key="$(json_field "$fixture" studentClientKey)"
reviewer_key="$(json_field "$fixture" instructorReviewerKey)"
metadata="$(cat "$repo_root/artifacts/pilot-demo/fixture-metadata.json")"

invoke_ows_cli_json "$assignment_root" init
[[ "$OWS_CLI_EXIT_CODE" -eq 0 ]] || { echo "$OWS_CLI_OUTPUT" >&2; exit 1; }

METADATA_JSON="$metadata" PROJECT_ROOT="$assignment_root" BASE_URL="$base_url" run_python - <<'PY' > "$assignment_root/.ows/config.json"
import json
import os
from datetime import datetime, timezone
metadata = json.loads(os.environ["METADATA_JSON"])
config = {
    "owsVersion": "0.1",
    "projectRoot": os.environ["PROJECT_ROOT"],
    "initializedAtUtc": datetime.now(timezone.utc).isoformat(),
    "verifierUrl": os.environ["BASE_URL"],
    "institutionId": metadata["institutionId"],
    "assessmentId": metadata["assessmentId"],
    "studentUserId": metadata["studentUserId"],
    "courseOfferingId": metadata["courseOfferingId"],
    "uploadEnabled": True,
}
print(json.dumps(config, indent=4))
PY

export OWS_VERIFIER_API_KEY="$student_key"
invoke_ows_cli_json "$assignment_root" session start
[[ "$OWS_CLI_EXIT_CODE" -eq 0 ]] || { echo "$OWS_CLI_OUTPUT" >&2; exit 1; }
session_start_json="$OWS_CLI_JSON"

(cd "$assignment_root" && dotnet "$cli_dll" watch start --poll >>"$watcher_stdout_path" 2>>"$watcher_stderr_path") &
watcher_pid="$!"

wait_until "Watcher did not create watcher.json." 20 1 test -f "$assignment_root/.ows/watcher.json"

invoke_ows_cli_json "$assignment_root" status
active_status_json="$OWS_CLI_JSON"
[[ "$(json_field "$active_status_json" Status)" == "SessionActive" ]] || { echo "Expected SessionActive after watcher start." >&2; exit 1; }

sleep "$heartbeat_wait_seconds"
invoke_ows_cli_json "$assignment_root" status
heartbeat_status_json="$OWS_CLI_JSON"
[[ -n "$(json_field "$heartbeat_status_json" LastHeartbeatAt)" ]] || { echo "Heartbeat timestamp did not advance while the watcher was running." >&2; exit 1; }

printf '\nSecond line at %s\n' "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" >> "$assignment_root/draft.txt"
sleep 3

invoke_ows_cli_json "$assignment_root" session checkpoint
[[ "$OWS_CLI_EXIT_CODE" -eq 0 ]] || { echo "$OWS_CLI_OUTPUT" >&2; exit 1; }
checkpoint_json="$OWS_CLI_JSON"

invoke_ows_cli_json "$assignment_root" package
[[ "$OWS_CLI_EXIT_CODE" -eq 0 ]] || { echo "$OWS_CLI_OUTPUT" >&2; exit 1; }

invoke_ows_cli_json "$assignment_root" package upload
[[ "$OWS_CLI_EXIT_CODE" -eq 0 ]] || { echo "$OWS_CLI_OUTPUT" >&2; exit 1; }
upload_json="$OWS_CLI_JSON"
package_id="$(json_field "$upload_json" PackageId)"
[[ -n "$package_id" ]] || { echo "Package upload did not return a package id." >&2; exit 1; }

last_package_status=""
for ((attempt=0; attempt<45; attempt++)); do
  invoke_ows_cli_json "$assignment_root" package status --package-id "$package_id"
  last_package_status="$OWS_CLI_JSON"
  if [[ "$(json_field "$last_package_status" Status)" == "Completed" ]]; then
    break
  fi
  sleep 2
done
[[ "$(json_field "$last_package_status" Status)" == "Completed" ]] || { echo "Package verification did not complete in time." >&2; exit 1; }

export OWS_VERIFIER_API_KEY="$reviewer_key"
reviewer_package="$(curl -fsS -H "X-OWS-Verifier-Key: $reviewer_key" "$base_url/packages/$package_id")"
reviewer_report="$(curl -fsS -H "X-OWS-Verifier-Key: $reviewer_key" "$base_url/packages/$package_id/report")"

reviewer_denied_status="$(curl -s -o /dev/null -w "%{http_code}" -X POST -H "X-OWS-Verifier-Key: $reviewer_key" -H "Content-Type: application/json" -d "{}" "$base_url/education/institutions" || true)"

export OWS_VERIFIER_API_KEY="$operator_key"
diagnostics_after="$(curl -fsS -H "X-OWS-Verifier-Key: $operator_key" "$base_url/diagnostics/summary")"
audit_events="$(curl -fsS -H "X-OWS-Verifier-Key: $operator_key" "$base_url/audit/events?limit=100&packageId=$package_id")"

stdout_log=""
stderr_log=""
[[ -f "$repo_root/artifacts/local-verifier/verifier.stdout.log" ]] && stdout_log="$(cat "$repo_root/artifacts/local-verifier/verifier.stdout.log")"
[[ -f "$repo_root/artifacts/local-verifier/verifier.stderr.log" ]] && stderr_log="$(cat "$repo_root/artifacts/local-verifier/verifier.stderr.log")"

HEALTH_JSON="$health" READY_JSON="$ready" FIXTURE_JSON="$fixture" METADATA_JSON="$metadata" SESSION_START_JSON="$session_start_json" ACTIVE_STATUS_JSON="$active_status_json" HEARTBEAT_STATUS_JSON="$heartbeat_status_json" CHECKPOINT_JSON="$checkpoint_json" LAST_PACKAGE_STATUS_JSON="$last_package_status" REVIEWER_PACKAGE_JSON="$reviewer_package" REVIEWER_REPORT_TEXT="$reviewer_report" DIAGNOSTICS_BEFORE_JSON="$diagnostics_before" DIAGNOSTICS_AFTER_JSON="$diagnostics_after" AUDIT_EVENTS_JSON="$audit_events" STDOUT_LOG_TEXT="$stdout_log" STDERR_LOG_TEXT="$stderr_log" SUMMARY_PATH="$summary_path" BASE_URL="$base_url" PREFIX="$prefix" ASSIGNMENT_ROOT="$assignment_root" PACKAGE_ID="$package_id" REVIEWER_DENIED_STATUS="$reviewer_denied_status" STUDENT_KEY="$student_key" REVIEWER_KEY="$reviewer_key" OPERATOR_KEY="$operator_key" \
run_python - <<'PY'
import json
import os
from datetime import datetime, timezone
from pathlib import Path

health = json.loads(os.environ["HEALTH_JSON"])
ready = json.loads(os.environ["READY_JSON"])
fixture = json.loads(os.environ["FIXTURE_JSON"])
metadata = json.loads(os.environ["METADATA_JSON"])
session_start = json.loads(os.environ["SESSION_START_JSON"])
active_status = json.loads(os.environ["ACTIVE_STATUS_JSON"])
heartbeat_status = json.loads(os.environ["HEARTBEAT_STATUS_JSON"])
checkpoint = json.loads(os.environ["CHECKPOINT_JSON"])
last_package_status = json.loads(os.environ["LAST_PACKAGE_STATUS_JSON"])
reviewer_package = json.loads(os.environ["REVIEWER_PACKAGE_JSON"])
reviewer_report = os.environ["REVIEWER_REPORT_TEXT"]
diagnostics_before = json.loads(os.environ["DIAGNOSTICS_BEFORE_JSON"])
diagnostics_after = json.loads(os.environ["DIAGNOSTICS_AFTER_JSON"])
audit_events = json.loads(os.environ["AUDIT_EVENTS_JSON"])
audit_event_list = audit_events if isinstance(audit_events, list) else audit_events.get("items", [])
combined_logs = os.environ["STDOUT_LOG_TEXT"] + "\n" + os.environ["STDERR_LOG_TEXT"]
summary = {
    "dateUtc": datetime.now(timezone.utc).isoformat(),
    "baseUrl": os.environ["BASE_URL"],
    "prefix": os.environ["PREFIX"],
    "assignmentRoot": os.environ["ASSIGNMENT_ROOT"],
    "verifierHealth": health["status"],
    "verifierReady": ready["status"],
    "fixture": {
        "institutionId": metadata["institutionId"],
        "courseId": metadata["courseId"],
        "classGroupId": metadata["classGroupId"],
        "courseOfferingId": metadata["courseOfferingId"],
        "assessmentId": metadata["assessmentId"],
        "studentUserId": metadata["studentUserId"],
        "studentClientKeyPrefix": metadata["studentClientKeyPrefix"],
        "instructorReviewerKeyPrefix": metadata["instructorReviewerKeyPrefix"],
    },
    "sessionId": session_start["SessionId"],
    "activeStatus": active_status["Status"],
    "lastHeartbeatAt": heartbeat_status.get("LastHeartbeatAt"),
    "checkpointAt": checkpoint.get("LastCheckpointAt"),
    "packageId": os.environ["PACKAGE_ID"],
    "packageStatus": last_package_status["Status"],
    "trustStatus": last_package_status["TrustStatus"],
    "reviewerDeniedStatus": int(os.environ["REVIEWER_DENIED_STATUS"]),
    "reviewerPackageInstitutionId": reviewer_package.get("institutionId"),
    "reviewerReportHasAssessmentContext": "Assessment Context" in reviewer_report,
    "reviewerReportHasStatusLine": "Status:" in reviewer_report,
    "diagnosticsBefore": diagnostics_before,
    "diagnosticsAfter": diagnostics_after,
    "auditEventTypes": [item.get("eventType") for item in audit_event_list],
    "requestIdSeenInLogs": "requestId=" in combined_logs,
    "rawKeyLeakDetected": any(key and key in combined_logs for key in (os.environ["STUDENT_KEY"], os.environ["REVIEWER_KEY"], os.environ["OPERATOR_KEY"])),
}
summary_path = Path(os.environ["SUMMARY_PATH"])
summary_path.write_text(json.dumps(summary, indent=4), encoding="utf-8")
print(json.dumps(summary, indent=4))
PY
