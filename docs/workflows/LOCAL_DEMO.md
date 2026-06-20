# Local End-to-End Demo Guide

This guide walks you through the step-by-step flow to validate the entire Open Work Standard (OWS) verification loop locally. 

Following this path ensures that there are no hidden local assumptions and that all components (watcher, verifier, database, report generator) are operating correctly.

---

## Step 1: Clone and Build

1. Clone the OWS repository to a local directory.
2. Build the solution using the .NET CLI:
   ```bash
   dotnet build OWS.sln -nologo
   ```
   *Note: This step also triggers target task `GenerateVerifierScripts` which populates the platform-specific launcher scripts in `artifacts/generated-scripts/`.*

---

## Step 2: Start PostgreSQL Dependency

OWS verifier storage defaults to PostgreSQL for durable local notarization tracking. Start the container in the background:

```bash
docker compose -f docker-compose.local.yml up -d
```

---

## Step 3: Run Environment Diagnostics

Before launching the verifier server, run the read-only environment validation script to confirm your setup:

### Windows (PowerShell):
```powershell
.\scripts\validate-local-verifier.ps1
```

### Unix/macOS:
```bash
./scripts/validate-local-verifier.sh
```

The script checks:
- .NET 9 SDK version
- Docker engine and daemon status
- Port availability for PostgreSQL (5432) and Verifier (5078)
- Verifier build outputs and status
- PowerShell Execution Policy restrictions
- Shell privileges (Admin is NOT required)

Ensure all checks are green (or yellow warnings are understood) before moving forward.

---

## Step 4: Start the Verifier Server

Launch the verifier server in the background:

### Windows:
```powershell
.\scripts\start-local-verifier.ps1
```

### Unix/macOS:
```bash
./scripts/start-local-verifier.sh
```

Confirm that the verifier is active and listening by running:
- **Status check**: `.\scripts\status-local-verifier.ps1` (or `./scripts/status-local-verifier.sh`)
- **Ready endpoint**: Querying `http://127.0.0.1:5078/ready` (should return HTTP 200 `{ "status": "Healthy" }`)

---

## Step 5: Initialize a Sample Project

Open a new shell window or terminal (where you will run the watcher) and create a directory for your test project:

```bash
mkdir ows-demo-project
cd ows-demo-project
```

Initialize OWS inside this project folder:
```bash
ows init
```
This generates the local `.ows` metadata structure.

---

## Step 6: Start the Persistent File Watcher

Launch the watcher to monitor local project file changes:

```bash
ows watch
```
*Note: Keep this terminal window open. If your environment does not support native filesystem signals, the watcher will automatically fall back to folder polling.*

---

## Step 7: Create Files and Checkpoints

Open another shell/terminal in the `ows-demo-project` directory. Let's write some files and record events.

1. **Create/edit files**:
   ```bash
   echo "Initial research" > draft.txt
   ```
   *The watcher terminal will log file event capture and update the timeline.*

2. **Start a remote session** linked to the verifier:
   ```bash
   ows session start --server http://127.0.0.1:5078
   ```

3. **Record checkpoints**:
   Let's modify files and capture checkpoints:
   ```bash
   echo "Adding chapter 1" >> draft.txt
   ows session checkpoint
   ```

---

## Step 8: Create the Package

Bundle the timeline events, version graph, and file hashes into a secure `.owspkg` archive:

```bash
ows package
```
This creates a file named `ows-demo-project.owspkg` in the parent directory (or the path printed in the output console).

---

## Step 9: Submit & Verify Server-Side

Submit the package to the running local verifier to match it against durable server checkpoints:

```bash
ows verify --server http://127.0.0.1:5078 ../ows-demo-project.owspkg
```

---

## Step 10: Generate Review Reports

Once verified, generate the instructor-facing review reports:

### Generate Text Review Report:
```bash
ows report
```
Inspect the output file (e.g. `ows-demo-project.report.txt`). It contains 12 dedicated sections showing:
- Overall trust status (`Status: Verified` / `Status: Unverified` / `Status: Degraded`)
- Actionable reviewer recommendations
- Plain-English trust explanation
- Timeline integrity details
- Lease continuity status (heartbeats, gaps formatted as `Xm Ys`)
- Suggested manual actions

### Generate JSON Review Report:
```bash
ows report --format json
```
Inspect the resulting `.json` report file to see the structured, nested camelCase review schema.

---

## Step 11: Stop the Verifier

Once your demo runs are complete, stop the verifier server and tear down Postgres:

### Stop Verifier Server:
```bash
# Windows
.\scripts\stop-local-verifier.ps1

# Unix/macOS
./scripts/stop-local-verifier.sh
```

### Stop Postgres Container:
```bash
docker compose -f docker-compose.local.yml down
```
