# Release

The release checklist and automated gate are documented in
[docs/development/RELEASE_CHECKLIST.md](docs/development/RELEASE_CHECKLIST.md).

On Windows, run:

~~~powershell
.\scripts\windows\run-release-regression-gate.ps1
.\scripts\windows\collect-release-candidate-evidence.ps1 -Version v0.1
~~~

Review the generated evidence and complete the manual sign-off separately.
