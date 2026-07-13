# Security

Read the authoritative [threat model](docs/core/THREAT_MODEL.md),
[security model](docs/core/SECURITY.md), and
[security hardening guide](docs/operations/SECURITY_HARDENING.md).

OWS is tamper-evident, not tamper-proof on a student-owned machine. Local
verification works without a server. Optional package signatures use RSA-SHA256
and a user-local private key (Windows current-user DPAPI; restrictive Unix file
mode); unsigned packages are explicit and weaker.
Remote verifier receipts remain a separate trust anchor.

On Windows, `Ows.Setup.exe` installs the silent Agent as a LocalSystem service.
The service watches only roots explicitly registered by `ows init` in the
machine-scoped `%ProgramData%\OpenWorkStandard` registry.

## Reporting

Do not publish credentials, private project files, or exploitable details in a
public issue. Report security vulnerabilities privately through the repository
maintainer contact or GitHub Security Advisories, including reproduction steps
with all secrets and personal data removed.
