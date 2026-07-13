Status: Active  
Audience: Student  
Last reviewed: 2026-07-13

# OWS Student Workflow Guide

The ordinary student workflow is intentionally small:

~~~text
ows init
# work normally in any editor
ows package
~~~

OWS records project-scoped work provenance. Event presence is evidence of
recorded activity; event absence is not proof of misconduct.

## Initialize

Run ows init from the project root. It creates .ows/, a starter .owsignore,
and an explicit registration for the project. The Agent watches only
explicitly initialized roots. If the Agent is unavailable, initialization and
registration are retained and the command reports how to start or install it.

On Windows, double-click the published `Ows.Setup.exe` once. It requests UAC
approval, installs `OWS Agent` in the Windows Services console, installs the
`ows` CLI, and starts the Agent silently. Open a new terminal after setup so
the updated PATH is loaded. No Windows account password is requested. On Linux/macOS, the
installable service adapter is deferred; packaging can continue from local
state while the Agent is unavailable.

Edit .owsignore only for project-specific exclusions. OWS does not collect raw
keystrokes, passwords, browser content, webcam or microphone data, or
unrelated personal files.

## Work normally

The local Agent observes filesystem changes through the project boundary and
recovers after restart where supported. No manual watcher start, stop, or
checkpoint ceremony is required. ows status is a diagnostic view.

## Package

Run:

~~~text
ows package
~~~

This flushes local Agent state when available, collects allowed artifacts, and
creates a .owspkg package. Use ows package --sign when a local RSA package
signature is desired. Unsigned packages remain structurally valid but have a
weaker explicit signature status.

Verify locally without a server:

~~~text
ows verify
ows inspect --json
~~~

ows inspect is reviewer-focused and reports the package root, signature
status, artifact count, timeline summary, and review findings.

## Troubleshooting

- AgentUnavailable: run or start the Windows Agent bootstrap; retrying
  `ows init` is safe.
- Package creation continues from local state when the Agent is unavailable.
- ows verify works without a verifier server.
- Missing events or continuity gaps require human interpretation and do not
  imply misconduct.
