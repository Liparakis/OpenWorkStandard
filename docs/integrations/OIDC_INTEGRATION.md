# OIDC/JWT Bearer Foundation

This document describes the current Open Work Standard verifier OIDC/JWT bearer foundation. It is implemented for API bearer validation, and it is deliberately not full interactive SSO.

What this milestone does:

- keeps API keys as the primary auth mechanism for CLI, VS Code, watcher, and automation clients
- adds optional JWT bearer validation for verifier API access
- maps validated bearer claims into the same internal verifier access context used by API keys
- reuses the existing RBAC rules for `Operator`, `InstitutionAdmin`, `InstructorReviewer`, and `StudentClient`

What this milestone does not do:

- browser login screens
- browser session management
- cookies or session management
- callback routes
- logout flows
- IdP onboarding UI
- SAML
- dashboard UI

## Auth Modes

Current supported modes:

- `ApiKeyOnly`: default. OIDC/JWT bearer is disabled. Existing API-key behavior remains unchanged.
- `OidcOptional`: API keys still work. Bearer tokens are also accepted when `VerifierAuth:Oidc:Enabled=true`.
- `OidcRequiredForBrowserRoutes`: deferred. Not implemented in v0.1.

## Configuration

OIDC/JWT bearer is disabled by default.

Configure with:

```text
VerifierAuth__Oidc__Enabled=true
VerifierAuth__Oidc__Authority=https://your-idp.example
VerifierAuth__Oidc__Audience=ows-verifier
VerifierAuth__Oidc__ClientId=ows-verifier
VerifierAuth__Oidc__ClientSecret=<optional future use>
VerifierAuth__Oidc__RequireHttpsMetadata=true
VerifierAuth__Oidc__RoleClaim=role
VerifierAuth__Oidc__InstitutionClaim=institution
VerifierAuth__Oidc__UserIdClaim=sub
VerifierAuth__Oidc__EmailClaim=email
VerifierAuth__Oidc__DisplayNameClaim=name
```

Do not commit real client secrets.

## Claim Mapping

Validated bearer claims are converted into the verifier's internal access context before RBAC runs.

Supported role mapping:

- `Operator`
- `InstitutionAdmin`
- `InstructorReviewer`
- `StudentClient`

Rules:

- missing role claim: rejected
- invalid role claim: rejected
- `InstitutionAdmin`, `InstructorReviewer`, and `StudentClient` require the configured institution claim
- `StudentClient` requires the configured user id claim
- email and display name are accepted for future dashboard context but are not required for authorization

This keeps one RBAC system. Endpoints are not authorized directly from raw `ClaimsPrincipal` checks.

## Dual-Auth Requests

Requests must present exactly one actor.

If a request sends both:

- `X-OWS-Verifier-Key`
- `Authorization: Bearer`

the verifier rejects it with `400 Bad Request`:

```json
{
  "error": "ambiguous_authentication",
  "message": "Send either X-OWS-Verifier-Key or Authorization: Bearer, not both.",
  "requestId": "<x-request-id>"
}
```

The verifier also emits a safe audit event:

- `auth.ambiguous`

The verifier never logs raw API keys or bearer tokens.

## Diagnostics and Readiness

`GET /ready` and `GET /diagnostics/summary` expose a safe OIDC status block:

- `enabled`
- `authorityConfigured`
- `audienceConfigured`
- `roleClaimConfigured`

They do not expose:

- client secrets
- bearer token contents
- raw claim payloads

## Local Development

Local development does not require OIDC/JWT bearer.

Default local behavior:

- leave `VerifierAuth__Oidc__Enabled=false`
- continue using API keys or unguarded local bootstrap mode as before

Enable OIDC/JWT bearer only when you are explicitly validating future human-facing access patterns.

## Provider Examples

The verifier only needs validated JWT claims plus stable claim names. Exact IdP setup varies.

Typical mappings:

- Keycloak: realm/client role mapped into `role`, institution into a custom claim, user id from `sub`
- Auth0: custom namespaced role/institution claims mapped into the configured claim names
- Azure AD / Entra ID: app role claim and extension/custom institution claim mapped into the configured claim names

The important constraint is not the IdP brand. It is that the emitted claim set must map cleanly into OWS roles and institution scope without silent privilege upgrades.

## Security Notes

- API keys remain the boring, primary pilot mechanism for non-browser clients.
- Protect any future bearer-token-accepting routes with normal TLS and reverse-proxy controls.
- Do not treat this as full SSO. It is the backend auth foundation for future dashboard work.
- SAML is deferred until there is a real browser/dashboard requirement that justifies the extra complexity.
