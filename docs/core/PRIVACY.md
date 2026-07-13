# Privacy

OWS is local-first. The Agent watches only project roots explicitly initialized with `ows init`.

OWS may record project-scoped filesystem events, project lifecycle events, hashes, deltas, manifests, build/test/run metadata, and provenance metadata under the tracked project boundary. Binary files are opaque and hash-only.

OWS must never collect raw keystrokes, passwords, private messages, browser content or history, webcam data, microphone data, or unrelated personal files. OWS does not require a server or account for package verification.

Observation gaps are reported as uncertainty. Event absence is never proof of misconduct, and OWS does not make misconduct judgments.
