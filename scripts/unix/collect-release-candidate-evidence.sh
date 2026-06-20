#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common-local-verifier.sh"

version="${1:-v0.1}"
repo_root="$(resolve_ows_repo_root)"
cd "$repo_root"

gate_summary_path="$repo_root/artifacts/release-gate/release-gate-summary.json"
dry_run_summary_path="$repo_root/artifacts/pilot-demo/live-dry-run-summary.json"

[[ -f "$gate_summary_path" ]] || { echo "Missing gate summary at '$gate_summary_path'." >&2; exit 1; }
[[ -f "$dry_run_summary_path" ]] || { echo "Missing dry run summary at '$dry_run_summary_path'." >&2; exit 1; }

bundle_root="$repo_root/artifacts/release-candidate/$version"
mkdir -p "$bundle_root"

cp "$gate_summary_path" "$bundle_root/release-gate-summary.json"
cp "$dry_run_summary_path" "$bundle_root/live-dry-run-summary.json"

VERSION="$version" REPO_ROOT="$repo_root" BUNDLE_ROOT="$bundle_root" GATE_SUMMARY_PATH="$gate_summary_path" DRY_RUN_SUMMARY_PATH="$dry_run_summary_path" \
run_python - <<'PY'
import json
import os
from datetime import datetime, timezone
from pathlib import Path

version = os.environ["VERSION"]
repo_root = Path(os.environ["REPO_ROOT"])
bundle_root = Path(os.environ["BUNDLE_ROOT"])
gate_summary_path = Path(os.environ["GATE_SUMMARY_PATH"])
dry_run_summary_path = Path(os.environ["DRY_RUN_SUMMARY_PATH"])

gate = json.loads(gate_summary_path.read_text(encoding="utf-8-sig"))
dry = json.loads(dry_run_summary_path.read_text(encoding="utf-8-sig"))

if gate["overallStatus"] != "Passed":
    raise SystemExit(f"Release gate is not green. Current status: '{gate['overallStatus']}'.")

if dry["packageStatus"] != "Completed":
    raise SystemExit(f"Latest dry run package status is '{dry['packageStatus']}', expected 'Completed'.")

if dry["trustStatus"] != "Verified":
    raise SystemExit(f"Latest dry run trust status is '{dry['trustStatus']}', expected 'Verified'.")

if int(dry["reviewerDeniedStatus"]) != 403:
    raise SystemExit(f"Latest dry run reviewer denial status is '{dry['reviewerDeniedStatus']}', expected 403.")

if bool(dry["rawKeyLeakDetected"]):
    raise SystemExit("Latest dry run detected a raw API key leak.")

manifest = {
    "version": version,
    "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
    "overallStatus": "ReadyForManualSignoff",
    "gateSummary": {
        "source": str(gate_summary_path),
        "copiedTo": str(bundle_root / "release-gate-summary.json"),
        "dateUtc": gate["dateUtc"],
        "overallStatus": gate["overallStatus"],
    },
    "dryRunSummary": {
        "source": str(dry_run_summary_path),
        "copiedTo": str(bundle_root / "live-dry-run-summary.json"),
        "dateUtc": dry["dateUtc"],
        "packageStatus": dry["packageStatus"],
        "trustStatus": dry["trustStatus"],
        "reviewerDeniedStatus": dry["reviewerDeniedStatus"],
        "rawKeyLeakDetected": dry["rawKeyLeakDetected"],
    },
    "manualChecksRemaining": [
        "VS Code trusted-workspace interactive smoke path if the extension changed.",
        "Operator release-candidate sign-off.",
    ],
}

(bundle_root / "evidence-manifest.json").write_text(json.dumps(manifest, indent=4), encoding="utf-8")

readme = f"""# Open Work Standard Release Candidate Evidence

Version: {version}

Generated UTC: {manifest['generatedAtUtc']}

Gate status: {gate['overallStatus']}
Dry run trust status: {dry['trustStatus']}
Dry run package status: {dry['packageStatus']}
Reviewer denial status: {dry['reviewerDeniedStatus']}
Raw key leak detected: {dry['rawKeyLeakDetected']}

Files:

- release-gate-summary.json
- live-dry-run-summary.json
- evidence-manifest.json

Remaining manual checks:

- VS Code trusted-workspace interactive smoke path if the extension changed.
- Operator release-candidate sign-off.
"""

(bundle_root / "README.md").write_text(readme, encoding="utf-8")
print(json.dumps(manifest, indent=4))
PY
