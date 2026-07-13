# Getting Started

Open Work Standard (OWS) is a local-first proof-of-work protocol and
toolchain, not an LMS, plagiarism detector, or AI judge.

~~~text
dotnet build OWS.sln -nologo
dotnet test OWS.sln -nologo
~~~

For a project:

~~~text
ows init
# work normally
ows package
ows verify <package.owspkg>
ows inspect <package.owspkg>
ows report <package.owspkg>
~~~

On Windows, build and double-click the setup executable:

~~~powershell
.\scripts\windows\build-ows-setup.ps1
# Then double-click artifacts\ows-setup\Ows.Setup.exe
~~~

It requests UAC Administrator approval, installs the self-contained Agent under
`C:\Program Files\Open Work Standard`, registers `OWS Agent` in Services.msc,
and starts it silently. It also registers `Open Work Standard` in Windows
Installed apps / Control Panel Programs and Features. To remove the service
and installed files:

~~~powershell
.\artifacts\ows-setup\Ows.Setup.exe --uninstall
~~~

The uninstall prompt lets you preserve or delete the shared Agent registry;
`--uninstall --purge-data` selects deletion directly. Project `.ows` folders are
never removed. On Linux/macOS, the
installable service adapter remains deferred; `ows agent run` is the foreground
diagnostic fallback.

The verifier server is optional for local package creation and verification.
Start with [docs/START_HERE.md](docs/START_HERE.md), then read the
[student workflow](docs/workflows/STUDENT_WORKFLOW.md) or
[CLI reference](docs/development/CLI.md).

The tracked smoke fixture is [samples/minimal-project](samples/minimal-project).
