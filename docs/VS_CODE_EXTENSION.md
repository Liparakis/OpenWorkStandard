# OWS VS Code Extension

This extension integrates the Open Work Standard (OWS) file watcher, remote verifier sessions, and package submission lifecycle directly into Visual Studio Code.

> [!IMPORTANT]
> **Event presence is evidence of recorded activity. Event absence is not proof of misconduct.**
>
> PackageCreated records local packaging after the artifact is written and may appear in the next timeline/package state.

---

## 1. Quick Start

### Build and Run locally
1. Install node dependencies:
   ```bash
   cd src/ows-vscode
   npm install
   ```
2. Compile the TypeScript files:
   ```bash
   npm run compile
   ```
3. Open `src/ows-vscode` in VS Code and press `F5` to launch an Extension Development Host window.
4. Open your project folder inside the new window.

### Pilot Smoke Test

For a full pilot smoke test, use the fixture from [PILOT_DEMO.md](PILOT_DEMO.md), then:

1. Configure `ows.cliPath` so the extension can run the local CLI.
2. Configure `ows.verifierUrl`, `ows.institutionId`, `ows.assessmentId`, `ows.studentUserId`, and `ows.courseOfferingId`.
3. Open a trusted workspace.
4. Run `OWS: Configure Assessment Context` and enter the `StudentClient` key.
5. Run `OWS: Start Watch Session`.
6. Confirm the status bar reaches active tracking.
7. Run package, upload, and verification status commands.
8. Confirm output and error messages redact the raw API key.

---

## 2. Host Integration

When spawning the OWS watch process or executing CLI commands (like `init`, `status`, `package`, `upload`, etc.), the VS Code extension sets the `OWS_HOST` environment variable to `vscode`. 

This guarantees that any event logged to the local `.ows/timeline.jsonl` (e.g., `ProjectOpened`, `ProjectClosed`, `PackageCreated`) correctly specifies `vscode` as the originating host rather than generic `cli`.

---

## 3. Configuration Settings

Access settings via `Ctrl+,` (or `Cmd+,` on macOS) and search for `Open Work Standard`:

| Setting | Default | Description |
|---|---|---|
| `ows.cliPath` | `"ows"` | Command/path to invoke OWS CLI (e.g. `ows` or `dotnet /path/to/Ows.Cli.dll`). |
| `ows.verifierUrl` | `"http://localhost:5078"` | Remote verifier server base URL. |
| `ows.institutionId` | `""` | School/institution identifier. |
| `ows.assessmentId` | `""` | Current assessment task identifier. |
| `ows.studentUserId` | `""` | Student username or identifier. |
| `ows.courseOfferingId`| `""` | Active course section identifier. |

---

## 4. Contributed Commands

Open the command palette (`Ctrl+Shift+P` / `Cmd+Shift+P`) and search for `OWS:`:

- **OWS: Initialize Project**: Creates `.ows/` tracking directory.
- **OWS: Configure Assessment Context**: Prompts for remote verifier settings and stores the API key securely.
- **OWS: Start Watch Session**: Launches background file system tracking.
- **OWS: Stop Watch Session**: Stops tracking filesystem changes.
- **OWS: Show Status**: Displays active session information.
- **OWS: Package Submission**: Packages evidence files into a `.owspkg` archive.
- **OWS: Upload Package**: Submits `.owspkg` to the verifier.
- **OWS: Check Verification Status**: Queries verification success and receipt validation.

---

## 5. Security & API Key Storage

- **Storage**: The API key is stored securely in the VS Code **SecretStorage** API, which maps to OS keychain storage (like macOS Keychain or Windows Credential Manager).
- **No Workspace Leaks**: The API key is never written to workspace configuration files (`.vscode/settings.json` or `.ows/config.json`).
- **Log Redaction**: Standard output and error logs printed to the output channel automatically filter out the raw API key value.
- **Warnings**: Warns if using an Operator or Admin key instead of a `StudentClient` key for student activities.
