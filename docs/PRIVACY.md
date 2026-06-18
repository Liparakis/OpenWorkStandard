# Privacy Boundaries

## What OWS collects

OWS is intended to collect only project-scoped provenance, such as:

- file creation, modification, and deletion events inside the tracked project
- project open and close events
- build, execution, and test events related to the tracked work
- hashes, deltas, snapshots, manifests, and package metadata
- tool names when they are relevant to the tracked project workflow

## What OWS must never collect

OWS must never collect:

- raw keystrokes
- passwords or secrets
- browser history
- browser page content
- private messages
- webcam data
- microphone data
- unrelated personal files outside the tracked project
- automated guilt scores presented as facts

## Design stance

Privacy is a product boundary. If a proposed feature requires broader device surveillance, it does not belong in OWS without a fundamental change to the project mission and documentation.
