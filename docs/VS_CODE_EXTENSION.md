# OWS VS Code Extension

This extension integrates the Open Work Standard (OWS) file watcher, remote verifier sessions, and package submission lifecycle directly into Visual Studio Code.

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

---

## 2. Configuration Settings

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

## 3. Contributed Commands

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

## 4. Security & API Key Storage

- **Storage**: The API key is stored securely in the VS Code **SecretStorage** API, which maps to OS keychain storage (like macOS Keychain or Windows Credential Manager).
- **No Workspace Leaks**: The API key is never written to workspace configuration files (`.vscode/settings.json` or `.ows/config.json`).
- **Log Redaction**: Standard output and error logs printed to the output channel automatically filter out the raw API key value.
- **Warnings**: Warns if using an Operator or Admin key instead of a `StudentClient` key for student activities.
