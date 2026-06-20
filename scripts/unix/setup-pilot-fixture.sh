#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common-local-verifier.sh"

base_url="${OWS_VERIFIER_BASE_URL:-http://127.0.0.1:5078}"
operator_key="${OWS_VERIFIER_API_KEY:-}"
prefix="pilot"
as_json=false

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
    -Prefix|--prefix)
      prefix="$2"
      shift 2
      ;;
    -AsJson|--as-json)
      as_json=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

[[ -n "$operator_key" ]] || { echo "Operator key required. Pass --operator-key or set OWS_VERIFIER_API_KEY." >&2; exit 1; }

repo_root="$(resolve_ows_repo_root)"
cd "$repo_root"
base_url="${base_url%/}"

new_id() {
  printf '%s-%s\n' "$prefix" "$1"
}

invoke_ows_json() {
  local method="$1"
  local path="$2"
  local body="$3"
  curl -fsS -X "$method" "$base_url$path" \
    -H "X-OWS-Verifier-Key: $operator_key" \
    -H "Content-Type: application/json" \
    -d "$body"
}

institution_id="$(new_id institution)"
course_id="$(new_id course)"
class_group_id="$(new_id class)"
course_offering_id="$(new_id offering)"
student_user_id="$(new_id student)"
enrollment_id="$(new_id enrollment)"
assessment_id="$(new_id assessment)"
now="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

invoke_ows_json POST "/education/institutions" "$(cat <<EOF
{"id":{"value":"$institution_id"},"name":"OWS Pilot Institution","slug":"$institution_id","createdAt":"$now"}
EOF
)" >/dev/null

invoke_ows_json POST "/education/courses" "$(cat <<EOF
{"id":{"value":"$course_id"},"institutionId":{"value":"$institution_id"},"code":"OWS101","title":"Open Work Standard Pilot","createdAt":"$now"}
EOF
)" >/dev/null

invoke_ows_json POST "/education/class-groups" "$(cat <<EOF
{"id":{"value":"$class_group_id"},"institutionId":{"value":"$institution_id"},"name":"Pilot Group A","createdAt":"$now"}
EOF
)" >/dev/null

invoke_ows_json POST "/education/users" "$(cat <<EOF
{"id":{"value":"$student_user_id"},"institutionId":{"value":"$institution_id"},"displayName":"Pilot Student","externalId":"$student_user_id","email":"pilot.student@example.edu","createdAt":"$now"}
EOF
)" >/dev/null

current_year="$(date -u +%Y)"
invoke_ows_json POST "/education/course-offerings" "$(cat <<EOF
{"id":{"value":"$course_offering_id"},"institutionId":{"value":"$institution_id"},"courseId":{"value":"$course_id"},"classGroupId":{"value":"$class_group_id"},"term":"Pilot","year":$current_year,"createdAt":"$now"}
EOF
)" >/dev/null

invoke_ows_json POST "/education/enrollments" "$(cat <<EOF
{"id":{"value":"$enrollment_id"},"courseOfferingId":{"value":"$course_offering_id"},"studentUserId":{"value":"$student_user_id"},"createdAt":"$now"}
EOF
)" >/dev/null

invoke_ows_json POST "/education/assessments" "$(cat <<EOF
{"id":{"value":"$assessment_id"},"institutionId":{"value":"$institution_id"},"courseOfferingId":{"value":"$course_offering_id"},"title":"Pilot Assignment","startsAt":null,"endsAt":null,"policyId":null,"createdAt":"$now"}
EOF
)" >/dev/null

student_key_result="$(invoke_ows_json POST "/auth/api-keys" "$(cat <<EOF
{"role":"StudentClient","institutionId":"$institution_id","studentUserId":"$student_user_id"}
EOF
)")"

reviewer_key_result="$(invoke_ows_json POST "/auth/api-keys" "$(cat <<EOF
{"role":"InstructorReviewer","institutionId":"$institution_id"}
EOF
)")"

artifact_dir="$repo_root/artifacts/pilot-demo"
mkdir -p "$artifact_dir"

STUDENT_KEY_RESULT="$student_key_result" REVIEWER_KEY_RESULT="$reviewer_key_result" ARTIFACT_DIR="$artifact_dir" BASE_URL="$base_url" INSTITUTION_ID="$institution_id" COURSE_ID="$course_id" CLASS_GROUP_ID="$class_group_id" COURSE_OFFERING_ID="$course_offering_id" ENROLLMENT_ID="$enrollment_id" ASSESSMENT_ID="$assessment_id" STUDENT_USER_ID="$student_user_id" AS_JSON="$as_json" \
run_python - <<'PY'
import json
import os
from pathlib import Path

student_key_result = json.loads(os.environ["STUDENT_KEY_RESULT"])
reviewer_key_result = json.loads(os.environ["REVIEWER_KEY_RESULT"])
artifact_dir = Path(os.environ["ARTIFACT_DIR"])
metadata = {
    "baseUrl": os.environ["BASE_URL"],
    "institutionId": os.environ["INSTITUTION_ID"],
    "courseId": os.environ["COURSE_ID"],
    "classGroupId": os.environ["CLASS_GROUP_ID"],
    "courseOfferingId": os.environ["COURSE_OFFERING_ID"],
    "enrollmentId": os.environ["ENROLLMENT_ID"],
    "assessmentId": os.environ["ASSESSMENT_ID"],
    "studentUserId": os.environ["STUDENT_USER_ID"],
    "studentClientKeyPrefix": student_key_result["metadata"]["keyPrefix"],
    "instructorReviewerKeyPrefix": reviewer_key_result["metadata"]["keyPrefix"],
}
metadata_path = artifact_dir / "fixture-metadata.json"
metadata_path.write_text(json.dumps(metadata, indent=4), encoding="utf-8")

result = {
    "metadataFile": str(metadata_path),
    **metadata,
    "studentClientKey": student_key_result["apiKey"],
    "instructorReviewerKey": reviewer_key_result["apiKey"],
}

if os.environ["AS_JSON"].lower() == "true":
    print(json.dumps(result, indent=4))
else:
    print("Pilot fixture created.")
    print(f"Metadata file: {metadata_path}")
    print()
    print(f"institutionId={metadata['institutionId']}")
    print(f"courseId={metadata['courseId']}")
    print(f"classGroupId={metadata['classGroupId']}")
    print(f"courseOfferingId={metadata['courseOfferingId']}")
    print(f"assessmentId={metadata['assessmentId']}")
    print(f"studentUserId={metadata['studentUserId']}")
    print()
    print(f"StudentClient key (shown once): {result['studentClientKey']}")
    print(f"InstructorReviewer key (shown once): {result['instructorReviewerKey']}")
PY
