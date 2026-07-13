# OWS Release Checklist

Use this before publishing a build.

## Automated checks

- `dotnet build OWS.sln -nologo`
- `dotnet test OWS.sln -nologo`
- `git diff --check`
- PowerShell syntax checks for Windows scripts
- confirm `ows init` → `ows package` → `ows verify` works with a temporary project
- confirm signed packages verify and tampered packages fail

## Manual checks

- review the package format and privacy boundaries
- confirm no generated output, private key, credential, or personal path is tracked
- on Windows, validate setup, SCM service start/recovery, and Installed apps uninstall
- inspect the final history and MIT license
- obtain owner authorization before tagging, pushing, or publishing
