# OWS Rider / IntelliJ Integration Design

This document details the architecture, command mappings, and security policies for integrating Open Work Standard (OWS) into JetBrains Rider and IntelliJ IDEA.

---

## 1. Architectural Overview

Rider plugins are built in Kotlin/Java (running in the IntelliJ JVM process) and communicate with the Rider C# back-end (running in the ReSharper MSBuild host) via the **Reactive Protocol (RD)**.

```mermaid
graph TD
    subgraph JetBrains Rider Host (JVM)
        Plugin[IntelliJ Kotlin Plugin]
        UI[Rider Status Indicator / UI]
    end
    
    subgraph ReSharper Backend (.NET)
        RiderModel[RD Protocol Model]
    end
    
    subgraph Local Workspace
        CLI[OWS CLI process]
        Config[.ows/config.json]
    end

    Plugin --> UI
    Plugin <-->|RD protocol| RiderModel
    RiderModel <-->|spawn --json| CLI
    CLI <-->|read/write| Config
```

For this integration, the JVM-side plugin acts as the front-end rendering indicators and controls, while the ReSharper (C#) back-end spawns the OWS CLI or interacts with `IOwsWatchSessionManager` in-process.

---

## 2. Command Mapping Reference

The plugin should map IDE actions to the machine-readable OWS CLI subcommands:

| Action | Rider IDE Command | OWS CLI Command |
|---|---|---|
| **Initialize** | `OWS: Initialize Project` | `ows init --json` |
| **Configure** | `OWS: Configure Assessment Context` | (Prompts UI, then writes `.ows/config.json`) |
| **Start Watcher** | `OWS: Start Watcher` | `ows watch start --json` (run in background) |
| **Stop Watcher** | `OWS: Stop Watcher` | `ows watch stop --json` |
| **Check Session** | `OWS: Show Active Session` | `ows session status --json` |
| **Package** | `OWS: Package Project` | `ows package --json` |
| **Upload** | `OWS: Upload Submission` | `ows package upload --json` |
| **Check Status** | `OWS: Query Verification Status` | `ows package status --json` |

---

## 3. Status Bar State Transitions

Rider contributes a status bar widget representing the current workspace tracking status:

| Widget Text | Icon | ThemeColor | Meaning |
|---|---|---|---|
| **OWS: No Folder** | `shield` | Default | No active workspace / folder open |
| **OWS: Ready** | `shield` | Default | `.ows` folder exists; watcher is idle |
| **OWS: Watching** | `eye` | Default | Watcher process is running; no active session |
| **OWS: Session active** | `check` | Default | Session active but watcher is idle |
| **OWS: Watching & Session active** | `pulse` | Warning (Orange) | Active session with background filesystem watcher running |
| **OWS: Offline** | `warning` | Error (Red) | CLI status execution failed or server unreachable |

---

## 4. API Key Security & Custody

1. **Storage**:
   - Do NOT store the `StudentClient` API key in `.ows/config.json`.
   - Store it using the JetBrains **Credential Store** API (`com.intellij.credentialStore.CredentialStore`).
2. **Environment Injection**:
   - When launching the `ows` process, retrieve the key from the Credential Store and inject it as `OWS_VERIFIER_API_KEY` in the child process's environment.
3. **Log Redaction**:
   - Scan stdout/stderr lines captured from the CLI and replace occurrences of the API key with `[REDACTED_API_KEY]`.
4. **Key Warnings**:
   - Warn the student if no key is configured when starting a watch session.
   - Warn the student if the key starts with `op_` or `admin_`, as these roles are not intended for student workflows.
