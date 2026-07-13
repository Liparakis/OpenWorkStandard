# OWS Agent Design

The local Agent watches only project roots explicitly registered by ows init.
The filesystem is its primary observation source; OWS does not collect
keystrokes, passwords, browser data, webcam/microphone data, or unrelated files.

- Windows machine-scoped JSON registry under `%ProgramData%\OpenWorkStandard`; user-local registry on Unix
- Multi-project/restart host: OwsAgentHost
- Local IPC: current-user Windows named pipe or user-mode Unix socket
- Package coordination: ping/flush with local-state fallback
- Windows normal bootstrap: self-contained `Ows.Setup.exe` installs the `OWS Agent` SCM service
- Cross-platform foreground diagnostic host: ows agent run

The watcher, registry, and package logic remain in Ows.Core; Windows setup and
SCM hosting remain in the Windows-only setup executable. The service runs as
LocalSystem without a service-account password and watches only explicitly
initialized roots in the shared registry. Setup requires UAC Administrator
approval because Windows controls SCM installation.
SCM failure actions restart the Agent after unexpected exits with bounded
5/30/60-second delays.
