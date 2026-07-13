# OWS Pilot Demo

This is the end-to-end pilot walkthrough for showing Open Work Standard to a professor or sysadmin. It validates the current CLI, verifier, opaque external context metadata, package upload, report, audit, and diagnostics flow.

> [!NOTE]
> OWS defines a broader event vocabulary for future host integrations. The current MVP emits file-system and session/package events only where explicitly documented. Reserved event types are not currently used as trust evidence. Additionally, OWS packages a `version_graph.json` placeholder today; real graph nodes, edges, validation, and graph-derived trust signals are deferred.

## Preconditions

- PostgreSQL-backed verifier is running.
- `OWS_VERIFIER_API_KEY` is set to an Operator key in the operator shell.
- `dotnet build OWS.sln -nologo` has completed.

Start the local verifier path:

```powershell
docker compose -f docker-compose.local.yml up -d
.\scripts\windows\start-local-verifier.ps1
.\scripts\windows\status-local-verifier.ps1
```

Fastest validated rehearsal:

```powershell
$env:VerifierSecurity__ApiKey = "pilot-operator-key-12345"
$env:OWS_VERIFIER_API_KEY = "pilot-operator-key-12345"
$env:VerifierStorage__ReceiptSigningKey = "pilot-signing-key-12345"
.\scripts\windows\run-live-pilot-dry-run.ps1
```

This script starts the verifier, creates a unique fixture, runs the student CLI flow, waits for heartbeats, uploads a package, waits for worker verification, checks reviewer/operator flows, writes `artifacts/pilot-demo/live-dry-run-summary.json`, and then stops the verifier.

## Known-Good Local Pilot Path

Use this when you want the shortest path that is already known to pass locally:

1. Run `dotnet build OWS.sln -nologo`.
2. Run `dotnet test OWS.sln -nologo`.
3. Run `./scripts/windows/run-release-regression-gate.ps1` or `./scripts/windows/run-live-pilot-dry-run.ps1`.
4. Check `artifacts/pilot-demo/live-dry-run-summary.json`.

The current known-good summary must show:

- `verifierHealth = Healthy`
- `verifierReady = Ready`
- `activeStatus = SessionActive`
- `packageStatus = Completed`
- `trustStatus = Verified`
- `reviewerDeniedStatus = 403`
- `rawKeyLeakDetected = false`

## 1. Operator Fixture Setup

Create the minimal pilot fixture:

```powershell
.\scripts\windows\setup-pilot-fixture.ps1 -BaseUrl http://127.0.0.1:5078
```

The script creates:

- opaque institution, assessment, student, and course-offering identifiers
- bound `StudentClient` API key
- institution-scoped `InstructorReviewer` API key

It does not create institutions, courses, rosters, enrollments, students, or
assessments. Those records belong to a separate management system.

It prints the raw delegated keys once. It writes non-secret metadata to `artifacts/pilot-demo/fixture-metadata.json`.

Repeatability note:

- automated runs already pass a unique `-Prefix`, so they do not collide with previous fixtures
- manual reruns should also pass a unique `-Prefix`
- if you insist on reusing the default `pilot` prefix, reset the local dev database/package storage first

## 2. Student CLI Flow

In a clean assignment folder:

```powershell
$repo = "C:\path\to\Open Work Standard"
dotnet run --project "$repo\src\Ows.Cli" -- init
```

Edit `.ows/config.json` with the fixture values:

```json
{
  "verifierUrl": "http://127.0.0.1:5078",
  "institutionId": "pilot-institution",
  "assessmentId": "pilot-assessment",
  "studentUserId": "pilot-student",
  "courseOfferingId": "pilot-offering",
  "uploadEnabled": true
}
```

The documented config shape is camelCase. `ows session start` now reads that shape directly.

Store the student key only in the shell environment:

```powershell
$env:OWS_VERIFIER_API_KEY="<StudentClient key>"
```

Start the session and watcher for this remote-verifier rehearsal:

```powershell
dotnet run --project "$repo\src\Ows.Cli" -- session start
dotnet run --project "$repo\src\Ows.Cli" -- watch start
```

In a second terminal, confirm status:

```powershell
dotnet run --project "$repo\src\Ows.Cli" -- status --json
```

Expected state: watcher running, session active, no raw API key printed.

## 3. Heartbeat Lifecycle Validation

With the watcher still running:

1. Record `lastHeartbeatAt` from `ows status --json`.
2. Wait 60-90 seconds.
3. Run `ows status --json` again.
4. Confirm `lastHeartbeatAt` advanced.
5. Run `ows watch stop`.
6. Wait another 60-90 seconds.
7. Confirm `lastHeartbeatAt` no longer advances.

The heartbeat loop belongs to the long-running watcher/session manager process. A short-lived `ows session heartbeat` command is only a manual probe.

## 4. Student Package Upload

Make a small file change, then package and upload:

```powershell
dotnet run --project "$repo\src\Ows.Cli" -- session checkpoint
dotnet run --project "$repo\src\Ows.Cli" -- package
dotnet run --project "$repo\src\Ows.Cli" -- package upload
dotnet run --project "$repo\src\Ows.Cli" -- package status --json
```

Expected state: package submission ID returned, verification status eventually reaches `Completed`, and trust status is clear.

## 5. Reviewer Flow

Set the reviewer key:

```powershell
$env:REVIEWER_KEY="<InstructorReviewer key>"
```

Query the package and report:

```powershell
curl -H "X-OWS-Verifier-Key: $env:REVIEWER_KEY" http://127.0.0.1:5078/packages/<packageId>
curl -H "X-OWS-Verifier-Key: $env:REVIEWER_KEY" http://127.0.0.1:5078/packages/<packageId>/report
```

Confirm:

- report includes `External Context Metadata`
- report includes a top-level `Status:` line
- report is scoped to the reviewer institution
- reviewer cannot mutate verifier state or API-key management

Negative mutation check:

```powershell
curl -X POST -H "X-OWS-Verifier-Key: $env:REVIEWER_KEY" -H "Content-Type: application/json" -d '{"role":"Operator"}' http://127.0.0.1:5078/auth/api-keys
```

Expected result: `403 Forbidden` or a rejected request.

## 6. Operator Audit And Diagnostics

Use the Operator key:

```powershell
curl -H "X-OWS-Verifier-Key: $env:OWS_VERIFIER_API_KEY" http://127.0.0.1:5078/diagnostics/summary
curl -H "X-OWS-Verifier-Key: $env:OWS_VERIFIER_API_KEY" "http://127.0.0.1:5078/audit/events?limit=50"
.\scripts\windows\logs-local-verifier.ps1 -All
```

Confirm:

- diagnostics include package storage readiness and verification job counts
- audit events include session, heartbeat, checkpoint, package upload, and package verification activity
- request IDs appear in logs
- raw API keys do not appear in logs or diagnostics
- package blob count increases after upload

## 7. Negative-Path Checklist

Validate or document evidence for:

- expired `StudentClient` key rejects
- wrong institution rejects
- wrong `studentUserId` rejects
- missing external context metadata warns before pilot use
- verifier offline maps to `VerifierOffline`
- package too large rejects clearly
- invalid package rejects clearly
- reviewer cannot mutate verifier state or API-key management
- student cannot read another student report
- duplicate watcher start is blocked
- stale PID recovery works

## Pilot Exit Criteria

- fixture setup succeeds
- student CLI flow reaches uploaded package verification
- heartbeat continues while watcher runs
- heartbeat stops after watcher stop
- reviewer can read the report and cannot mutate verifier state
- diagnostics and audit evidence are available without leaking raw keys

## Live Dry Run Result

Validated on 2026-06-20 against the local PostgreSQL-backed verifier flow with:

- `scripts/windows/run-live-pilot-dry-run.ps1`
- `BaseUrl=http://127.0.0.1:5078`
- verifier storage provider `postgres`

Observed result from `artifacts/pilot-demo/live-dry-run-summary.json`:

- verifier `/health` = `Healthy`
- verifier `/ready` = `Ready`
- fixture creation succeeded with opaque external context metadata and delegated student/reviewer keys; no management records were created
- student CLI flow reached `SessionActive`
- heartbeat advanced while the watcher process stayed alive
- checkpoint issuance succeeded
- package upload succeeded
- worker verification completed with `trustStatus = Verified`
- reviewer report read succeeded and reviewer write attempts were rejected with `403`
- diagnostics showed `packageBlobCount` increase after upload
- audit events included package upload, verification queue/start/complete, and report read entries
- verifier logs included request ids and did not leak raw API keys

Known environment caveat from the dry run:

- `start-local-verifier.ps1` may print Docker client access-denied warnings in restricted shells. If PostgreSQL is already reachable on `localhost:5432`, the verifier can still start and the dry run can still pass.
