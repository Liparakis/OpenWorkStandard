# OWS v0.1 Release Checklist

Use this before calling an Open Work Standard v0.1 build a release candidate.

## Automated Gate

Run:

```powershell
$env:VerifierSecurity__ApiKey = "pilot-operator-key-12345"
$env:OWS_VERIFIER_API_KEY = "pilot-operator-key-12345"
$env:VerifierStorage__ReceiptSigningKey = "pilot-signing-key-12345"
.\scripts\run-release-regression-gate.ps1
```

Expected automated checks:

- `dotnet build OWS.sln -nologo`
- `dotnet test OWS.sln -nologo`
- VS Code extension compile via `src/ows-vscode/node_modules/.bin/tsc.cmd`
- `docker compose -f docker-compose.local.yml config` when Docker is reachable
- local verifier startup
- `/health` and `/ready`
- pilot fixture setup
- `StudentClient` session start
- heartbeat advancement while watcher is alive
- checkpoint issuance
- package creation
- package upload
- worker verification
- reviewer report read
- reviewer write rejection
- operator diagnostics and audit query
- request-id presence in logs
- raw API key redaction sanity check

Artifacts:

- gate summary: `artifacts/release-gate/release-gate-summary.json`
- latest dry run summary: `artifacts/pilot-demo/live-dry-run-summary.json`
- release-candidate evidence bundle: `.\scripts\collect-release-candidate-evidence.ps1` writes `artifacts/release-candidate/v0.1/`

## Manual Checks

- VS Code trusted-workspace smoke path if the extension changed
- operator sign-off that the latest dry run summary still matches expected trust and scope behavior
- doc review for any changed user-facing setup steps

## Fixture Reset And Cleanup

`setup-pilot-fixture.ps1` is safe for repeated automation when you pass a unique `-Prefix`. That is what the dry-run and regression-gate scripts do.

If you manually reuse the default `pilot` prefix, do one of these first:

- choose a new `-Prefix`
- reset the local dev database / package storage
- recreate the local PostgreSQL data volume

This is deliberate. Unique fixture ids are cheaper and safer than destructive cleanup logic.
