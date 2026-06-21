#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common-local-verifier.sh"
repo_root="$(resolve_ows_repo_root)"

echo "Formatting code in $repo_root based on .editorconfig..."
cd "$repo_root"
dotnet format
