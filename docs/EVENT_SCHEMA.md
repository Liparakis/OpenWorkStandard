# OWS Event Schema

## Core fields

Each `OwsEvent` should be serializable with these core fields:

- `eventId`: unique event identifier
- `timestampUtc`: UTC timestamp
- `eventType`: normalized OWS event type
- `projectId`: logical project identifier
- `relativePath`: file path relative to the tracked root when applicable
- `toolName`: source tool or observer when known
- `hashBefore`: prior content hash when applicable
- `hashAfter`: resulting content hash when applicable
- `bytesChanged`: approximate size delta when available
- `metadata`: event-specific key/value metadata

## Event types

- `FileCreated`
- `FileModified`
- `FileDeleted`
- `ProjectOpened`
- `ProjectClosed`
- `BuildStarted`
- `BuildSucceeded`
- `BuildFailed`
- `ProgramExecuted`
- `TestExecuted`
- `LargeInsert`
- `PackageCreated`

## Required fields

- `eventId`
- `timestampUtc`
- `eventType`
- `projectId`

## Optional metadata

Optional metadata may include:

- language or runtime
- command name
- build target
- test suite name
- editor or IDE name
- line or byte thresholds for `LargeInsert`

Optional metadata must remain project-scoped and must not introduce surveillance data.

## Example event

```json
{
  "eventId": "6c2bc64b-4134-4f14-a679-7cc3f3a8e85b",
  "timestampUtc": "2026-06-18T17:45:00+00:00",
  "eventType": "FileModified",
  "projectId": "sample-project",
  "relativePath": "src/Program.cs",
  "toolName": "rider",
  "hashAfter": "abc123",
  "metadata": {
    "reason": "save"
  }
}
```
