# OWS Desktop UI Design Spec

This spec outlines the design for the OWS Desktop Tray Application (`Ows.Desktop`) using the Avalonia UI framework.

---

## 1. Application Overview

The desktop UI is a lightweight, cross-platform system tray icon/menu application that operates in the background, sharing the same `IOwsWatchSessionManager` foundation as the CLI and VS Code extension.

---

## 2. Core Features v0.1

1. **System Tray Integration**:
   - Resides in the Windows taskbar tray or macOS status bar.
   - Icon changes state based on watcher activity (Idle, Watching, Session Active, Error).
2. **Project Selection**:
   - Allows the student to select a project root directory.
   - Automatically checks if `.ows` exists and reads `.ows/config.json`.
3. **Assessment Configuration**:
   - Input fields for Verifier URL, Institution ID, Assessment ID, Student User ID.
   - Text boxes for entering the StudentClient API Key (stored in the system keychain/credentials store).
4. **Watcher Control**:
   - "Start Watcher" / "Stop Watcher" buttons which trigger `IOwsWatchSessionManager.StartWatcherAsync` / `StopWatcherAsync` in the background.
5. **Session Control**:
   - "Start Session" button triggers remote session establishment.
   - Displays last heartbeat and checkpoint timestamps.
6. **Submission Controls**:
   - "Package & Submit" button runs packaging and uploads to the verifier, displaying the resulting Submission ID and verification job progress.

---

## 3. Security Guidelines

- **Keychain Storage**:
   - The desktop app must store API keys in the native operating system keychain:
     - **Windows**: Credential Manager (via DPAPI or Credential Management APIs).
     - **macOS**: Keychain Services.
- **API Key Redaction**:
   - All logs written to disk or shown in the UI text blocks must redact the API key value.
- **Privilege Separation**:
   - The application does not require administrator/root privileges to run or watch files.
- **Local-First Model**:
   - All file index database states and timeline entries remain strictly in the local project's `.ows/` folder. No telemetry is transmitted outside of the configured verifier server.
