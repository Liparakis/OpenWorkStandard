#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common-local-verifier.sh"
base_url="${OWS_VERIFIER_BASE_URL:-http://127.0.0.1:5078}"
operator_key="${OWS_VERIFIER_API_KEY:-}"
prefix="pilot"
as_json=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    -BaseUrl|--base-url) base_url="$2"; shift 2 ;;
    -OperatorKey|--operator-key) operator_key="$2"; shift 2 ;;
    -Prefix|--prefix) prefix="$2"; shift 2 ;;
    -AsJson|--as-json) as_json=true; shift ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
done
[[ -n "$operator_key" ]] || { echo "Operator key required. Pass --operator-key or set OWS_VERIFIER_API_KEY." >&2; exit 1; }
base_url="${base_url%/}"
new_id() { printf '%s-%s\n' "$prefix" "$1"; }
create_key() {
  curl -fsS -X POST "$base_url/auth/api-keys" -H "X-OWS-Verifier-Key: $operator_key" -H "Content-Type: application/json" -d "$1"
}

institution_id="$(new_id institution)"
course_id="$(new_id course)"
class_group_id="$(new_id class)"
course_offering_id="$(new_id offering)"
student_user_id="$(new_id student)"
enrollment_id="$(new_id enrollment)"
assessment_id="$(new_id assessment)"
student_key_result="$(create_key "{\"role\":\"StudentClient\",\"institutionId\":\"$institution_id\",\"studentUserId\":\"$student_user_id\"}")"
reviewer_key_result="$(create_key "{\"role\":\"InstructorReviewer\",\"institutionId\":\"$institution_id\"}")"

artifact_dir="$(resolve_ows_repo_root)/artifacts/pilot-demo"
mkdir -p "$artifact_dir"
STUDENT_KEY_RESULT="$student_key_result" REVIEWER_KEY_RESULT="$reviewer_key_result" ARTIFACT_DIR="$artifact_dir" BASE_URL="$base_url" INSTITUTION_ID="$institution_id" COURSE_ID="$course_id" CLASS_GROUP_ID="$class_group_id" COURSE_OFFERING_ID="$course_offering_id" ENROLLMENT_ID="$enrollment_id" ASSESSMENT_ID="$assessment_id" STUDENT_USER_ID="$student_user_id" AS_JSON="$as_json" \
run_python - <<'PY'
import json
import os
from pathlib import Path

student = json.loads(os.environ["STUDENT_KEY_RESULT"])
reviewer = json.loads(os.environ["REVIEWER_KEY_RESULT"])
metadata = {
    "baseUrl": os.environ["BASE_URL"],
    "institutionId": os.environ["INSTITUTION_ID"],
    "courseId": os.environ["COURSE_ID"],
    "classGroupId": os.environ["CLASS_GROUP_ID"],
    "courseOfferingId": os.environ["COURSE_OFFERING_ID"],
    "enrollmentId": os.environ["ENROLLMENT_ID"],
    "assessmentId": os.environ["ASSESSMENT_ID"],
    "studentUserId": os.environ["STUDENT_USER_ID"],
    "studentClientKeyPrefix": student["metadata"]["keyPrefix"],
    "instructorReviewerKeyPrefix": reviewer["metadata"]["keyPrefix"],
}
metadata_path = Path(os.environ["ARTIFACT_DIR"]) / "fixture-metadata.json"
metadata_path.write_text(json.dumps(metadata, indent=4), encoding="utf-8")
result = {"metadataFile": str(metadata_path), **metadata, "studentClientKey": student["apiKey"], "instructorReviewerKey": reviewer["apiKey"]}
if os.environ["AS_JSON"].lower() == "true":
    print(json.dumps(result, indent=4))
else:
    print("Pilot metadata and verifier keys created (no management records are stored by OWS).")
    print(f"Metadata file: {metadata_path}")
    print(f"StudentClient key (shown once): {result['studentClientKey']}")
    print(f"InstructorReviewer key (shown once): {result['instructorReviewerKey']}")
PY
