# OWS Student Workflow Guide

This guide walks through the student workflow using the Open Work Standard (OWS) CLI and VS Code extension to record, package, and upload evidence of academic work.

---

## 1. CLI-Only Workflow

Students can run all OWS operations using only the command line interface:

### Step 1: Initialize Project
```bash
ows init
```
This creates the `.ows` tracking folder, a starter `.ows/config.json`, and an empty `.ows/timeline.jsonl` file.

### Step 2: Configure Context
Edit `.ows/config.json` manually to set your verifier URL and academic context. For example:
```json
{
  "owsVersion": "0.1",
  "projectRoot": "/path/to/project",
  "initializedAtUtc": "2026-06-20T12:00:00Z",
  "verifierUrl": "http://localhost:5078",
  "institutionId": "university-a",
  "assessmentId": "assignment-1",
  "studentUserId": "student-123",
  "courseOfferingId": "csc-101-fall"
}
```

### Step 3: Provide API Key
Set the API key as an environment variable (do NOT write it to config.json):
```bash
# Windows (PowerShell)
$env:OWS_VERIFIER_API_KEY="std_your_key_here"

# macOS/Linux
export OWS_VERIFIER_API_KEY="std_your_key_here"
```

### Step 4: Start Watcher
To record filesystem changes in real-time:
```bash
ows watch start
```
This runs a persistent file system watcher. To stop it later, run `ows watch stop` or press `Ctrl+C`.

### Step 5: Start Remote Session
```bash
ows session start
```
This contacts the remote verifier to register the session and creates `.ows/session.json`.

### Step 6: Maintain Heartbeats & Checkpoints
When `ows watch start` is running, the watcher process sends verifier heartbeats every 30 seconds for remote sessions. The manual heartbeat command is only a probe:

```bash
ows session heartbeat
```

Use checkpoints to capture the current timeline head and receive a remote receipt:

```bash
ows session checkpoint
```

### Step 7: Show Status
Check current state at any time:
```bash
ows status
```
For machine-readable integrations, pass `--json` to any command (e.g. `ows status --json`).

### Step 8: Package & Upload
Create a `.owspkg` archive and upload it for verification:
```bash
# Package
ows package

# Upload
ows package upload

# Check Verification Status
ows package status
```

---

## 2. VS Code Extension Workflow

The VS Code extension handles the entire lifecycle visually:

1. **Initialize**: Run `OWS: Initialize Project` from the Command Palette (`Ctrl+Shift+P`).
2. **Configure**: Run `OWS: Configure Assessment Context`. Enter the verifier URL, IDs, and your API Key. The extension automatically saves the config and stores the key in the OS Keychain using VS Code's `SecretStorage`.
3. **Start Watcher**: Run `OWS: Start Watch Session`. The OWS status bar indicator in the bottom right will change to `OWS: Watching`.
4. **Active Session**: Run `OWS: Start Watch Session` (or starting a session) will show `OWS: Watching & Session active`.
5. **Heartbeats & Checkpoints**: The extension automatically routes session signals.
6. **Submit**: Run `OWS: Package Submission` and `OWS: Upload Package`. The Submission ID and verification job result will be displayed as notifications.

For the full pilot validation sequence, use [PILOT_DEMO.md](PILOT_DEMO.md).

---

## 3. Status Indicators & Meanings

- **Not Initialized**: The project root does not contain a `.ows` tracking folder. Run `init` first.
- **Ready**: Project is initialized, but the watcher and session are currently idle.
- **Watching**: Watcher is active locally, recording file events, but no remote session has been started.
- **Session active**: A remote session is registered on the verifier, but the file watcher is not active.
- **Watching & Session active**: Full active tracking (recommended state during assignments).
- **Degraded**: Heartbeats missed or timeline gaps detected by the verifier.
- **Offline / Error**: Verifier server is unreachable or an authentication/authorization error occurred.

---

## 4. Troubleshooting Local Errors

- **Missing Verifier URL / Context**: Check that `.ows/config.json` contains a valid `verifierUrl`.
- **401 Unauthorized**: Ensure `OWS_VERIFIER_API_KEY` is set correctly and starts with `std_`.
- **Duplicate Watcher**: If a watcher crashed, `.ows/watcher.json` might be stale. Run `ows watch stop` to force cleanup, then start it again.
- **API Key Redaction**: Errors printed to console or logs will show `[REDACTED_API_KEY]` instead of the raw key to protect credentials.
