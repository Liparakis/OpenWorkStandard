# OWS Regression Gate

The release regression gate is the boring path that must stay green before adding new UI surfaces or integrations.

## Gate Script

Primary command:

```powershell
.\scripts\run-release-regression-gate.ps1
```

What it automates:

- solution build
- solution tests
- VS Code extension compile
- Compose config validation when Docker is available
- full local PostgreSQL-backed live pilot dry run

What stays manual:

- interactive VS Code smoke validation in an Extension Development Host
- release-candidate sign-off

## Pass Criteria

Treat the gate as passed only when all of these are true:

- `artifacts/release-gate/release-gate-summary.json` says `overallStatus = Passed`
- `artifacts/pilot-demo/live-dry-run-summary.json` exists
- the latest dry run says `packageStatus = Completed`
- the latest dry run says `trustStatus = Verified`
- the latest dry run says `reviewerDeniedStatus = 403`
- the latest dry run says `rawKeyLeakDetected = false`

## Failure Policy

If the gate fails:

- fix the regression first
- rerun the gate
- do not mark the roadmap release-gate milestone complete off stale evidence
