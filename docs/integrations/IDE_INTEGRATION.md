# OWS IDE Integration Guide

This document describes how IDE plugins (such as VS Code or Rider) integrate with the Open Work Standard (OWS) CLI and local agent.

---

## 1. General Principles

IDE plugins delegate all core file watcher, checkpointing, and packaging operations to the OWS CLI or Core assembly. The IDE plugin should remain a thin visual layer that:
1. Spawns and manages the lifetime of `ows watch start`.
2. Reads the local `.ows/session.json` and `.ows/watcher.json` status properties.
3. Invokes CLI commands (such as `ows init`, `ows status --json`, `ows package`, `ows package upload`) and displays output/errors to the user.
4. Redacts verifier credentials from all logging channels and popup dialog alerts.

---

## 2. CLI Execution & JSON Protocol

All control commands executed by the IDE should supply the `--json` flag to return structured, machine-readable outcomes. The response is serialized as a JSON object carrying the `OwsCliResponse` schema:

```json
{
  "Success": true,
  "Status": "SessionActive",
  "WatcherRunning": true,
  "SessionId": "sess_12345",
  "ProjectRoot": "/path/to/project",
  "Message": "Command executed successfully.",
  "Errors": []
}
```

---

## 3. Status Mapping & Visuals

The IDE extension should poll `ows status --json` periodically (e.g. every 10–30 seconds) to refresh the status bar indicators:

| Status Value | Meaning | Recommended Visual / Color |
|---|---|---|
| `WatchingLocalOnly` | Watcher active, but local-only (no remote verifier) | Eye icon / Default color |
| `SessionActive` | Watcher active and connected to remote verifier | Pulse or Check icon / Warning or Warm color |
| `VerifierOffline` | Verifier server is unreachable | Warning icon / Error (Red) color |
| `HeartbeatFailing` | Heartbeats rejected (such as expired API keys) | Alert icon / Error (Red) color |
| `Degraded` | Verifier detected a lease gap (continuity gap) | Warning icon / Warning (Yellow) color |
| `Error` | Watcher process crashed or failed | Alert icon / Error (Red) color |

---

## 4. Security Boundaries

- **API Keys**: IDE plugins must never write verifier API keys (credentials) to project workspace configurations (such as `.ows/config.json`). Instead, store them in the secure platform credentials store (VS Code `SecretStorage` or OS Keychain).
- **Redaction**: All logs written to output windows or text logs must scrub the value of `OWS_VERIFIER_API_KEY` and replace it with `[REDACTED_API_KEY]`.
- **Workspace Trust**: Block OWS watcher execution in untrusted workspace folders to prevent execution of unverified binaries.
