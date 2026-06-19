#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common-local-verifier.sh"

all=false
if [[ "${1:-}" == "--all" ]]; then
  all=true
fi

repo_root="$(resolve_ows_repo_root)"
show_verifier_logs "$repo_root" "$all"
