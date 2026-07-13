# Local Demo

This demo uses no server or account.

```powershell
dotnet build OWS.sln -nologo
dotnet test OWS.sln -nologo
New-Item -ItemType Directory ows-demo-project
Set-Location ows-demo-project
ows init
Set-Content draft.txt "Initial work"
ows package
ows verify ..\ows-demo-project.owspkg
ows inspect ..\ows-demo-project.owspkg
ows report ..\ows-demo-project.owspkg --format text
```

For stronger authenticity, use `ows package --sign` and verify the resulting package offline. A signed package with valid local evidence is `Verified`; an unsigned package remains usable but reports `Unverified`.
