# Security Channels And Transport Roadmap

## Default Rule

Use standard secure web transports first.

Do not start with:

- custom TCP protocols
- custom DTLS protocols
- raw QUIC transport

Those add operational and security burden too early.

## Recommended Roadmap

1. HTTPS REST over TLS
2. gRPC over HTTP/2 TLS
3. HTTP/3 over QUIC where supported
4. raw QUIC only as future experimental work

## Why REST/TLS First

REST over TLS is the right MVP baseline because it is:

- easy to deploy
- easy to debug
- easy to proxy
- compatible with standard auth and certificate tooling
- acceptable in restrictive university networks

It is the lowest-risk way to get the trust boundary online.

## Why gRPC Next

gRPC is a reasonable next step for:

- streaming checkpoint submissions
- lease refresh flows
- lower-overhead typed client/server interaction

It still stays within standard HTTP/TLS infrastructure.

## Why HTTP/3 / QUIC Later

QUIC is a good future direction because it gives:

- TLS 1.3 by default
- multiplexed streams
- lower connection setup latency
- better connection migration behavior

But it should remain optional because:

- some campuses block or degrade UDP
- some proxies and middleboxes still behave poorly
- fallback to HTTP/2 or HTTP/1.1 must exist

Treat HTTP/3 as an optimization, not as the mandatory baseline.

## Transport Abstraction

When client/server transport code appears, keep it behind a narrow abstraction so the protocol can evolve without rewriting the client domain logic.

Example direction:

- `StartSessionAsync()`
- `SendCheckpointAsync()`
- `RefreshLeaseAsync()`
- `UploadPackageAsync()`
- `GetReceiptsAsync()`

Likely implementations:

- HTTPS transport first
- gRPC transport later
- HTTP/3-backed transport later

## Security Requirements

Transport and channel security should assume:

- TLS everywhere
- JWT/OIDC authentication
- server-enforced minimum client versions
- audit logging
- secret management outside source control

Later:

- mTLS for official clients or device-bound deployments
- institution SSO via SAML/OIDC
- signed client releases
- container signing and SBOMs

## Failure Scenarios

Transport design must tolerate:

- client network drops
- duplicated checkpoint submits
- duplicated package uploads
- API pod restarts
- Redis restarts
- HTTP/3/QUIC blocked by the network

That means:

- retry-safe APIs
- idempotency keys
- durable receipt/checkpoint persistence
- fallback transports

## Channel Philosophy

The transport is not the trust model by itself.

Even with TLS:

- the local client is still untrusted
- the verifier still has to compare checkpoint history and receipt chains
- verified status still depends on consistent durable server receipts

Secure channels protect communication. They do not remove the need for verification logic.
