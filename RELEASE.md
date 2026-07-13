# Release

The release checklist is [docs/development/RELEASE_CHECKLIST.md](docs/development/RELEASE_CHECKLIST.md).
The automated gate is documented in
[docs/development/REGRESSION_GATE.md](docs/development/REGRESSION_GATE.md).

On Windows, run:

~~~powershell
.\scripts\windows\run-release-regression-gate.ps1
.\scripts\windows\collect-release-candidate-evidence.ps1 -Version v0.1
~~~

Review the generated evidence and complete the manual sign-off separately.
