#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common-local-verifier.sh"

base_url="${1:-http://127.0.0.1:5078}"
auth_headers=()
if [[ -n "${OWS_VERIFIER_API_KEY:-}" ]]; then
  auth_headers=(-H "X-OWS-Verifier-Key: $OWS_VERIFIER_API_KEY")
fi

if ! test_verifier_http_ready "$base_url"; then
  echo "Smoke test could not reach the verifier at $base_url. Run start-local-verifier first, then check status-local-verifier and logs-local-verifier." >&2
  exit 1
fi

session_id="$(curl -fsS -X POST "${auth_headers[@]}" "$base_url/sessions" | python -c "import json,sys; print(json.load(sys.stdin)['sessionId'])")"
checkpoint_body="$(printf '{"sessionId":"%s","sequenceNumber":1,"timelineHeadHash":"head-1"}' "$session_id")"
if ! receipt_hash="$(curl -fsS -X POST "${auth_headers[@]}" "$base_url/sessions/$session_id/checkpoints" -H "Content-Type: application/json" -H "Idempotency-Key: checkpoint-1" -d "$checkpoint_body" | python -c "import json,sys; print(json.load(sys.stdin)['receiptHash'])")"; then
  echo "Smoke test failed during checkpoint append. Check status-local-verifier, logs-local-verifier, and confirm migrations succeeded." >&2
  exit 1
fi
retry_receipt_hash="$(curl -fsS -X POST "${auth_headers[@]}" "$base_url/sessions/$session_id/checkpoints" -H "Content-Type: application/json" -H "Idempotency-Key: checkpoint-1" -d "$checkpoint_body" | python -c "import json,sys; print(json.load(sys.stdin)['receiptHash'])")"
receipt_count="$(curl -fsS "${auth_headers[@]}" "$base_url/sessions/$session_id/receipts" | python -c "import json,sys; print(len(json.load(sys.stdin)['receipts']))")"
head_sequence="$(curl -fsS "${auth_headers[@]}" "$base_url/sessions/$session_id/head" | python -c "import json,sys; print(json.load(sys.stdin)['lastSequenceNumber'])")"
head_hash="$(curl -fsS "${auth_headers[@]}" "$base_url/sessions/$session_id/head" | python -c "import json,sys; print(json.load(sys.stdin)['lastTimelineHeadHash'])")"

if [[ "$receipt_hash" != "$retry_receipt_hash" ]]; then
  echo "Idempotent retry did not return the same receipt hash." >&2
  exit 1
fi

if [[ "$receipt_count" != "1" ]]; then
  echo "Expected exactly one persisted receipt after retry, got $receipt_count." >&2
  exit 1
fi

if [[ "$head_sequence" != "1" ]]; then
  echo "Expected head sequence number 1, got $head_sequence." >&2
  exit 1
fi

if [[ "$head_hash" != "head-1" ]]; then
  echo "Expected head timeline hash 'head-1', got '$head_hash'." >&2
  exit 1
fi

printf 'SessionId: %s\nReceiptHash: %s\nReceiptCount: %s\nHeadSequence: %s\nHeadTimelineHeadHash: %s\nIdempotentRetryMatched: true\n' \
  "$session_id" "$receipt_hash" "$receipt_count" "$head_sequence" "$head_hash"
