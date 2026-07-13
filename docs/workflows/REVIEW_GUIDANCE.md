Status: Active  
Audience: Reviewer  
Last reviewed: 2026-06-20

# OWS Review Guidance

## Purpose

This document explains how a human reviewer should interpret Open Work Standard verification output.

OWS does not make the final academic decision. It provides evidence and trust signals for human review.

## Trust States

### `Verified`

Meaning:

- package structure is valid
- local integrity checks passed
- packaged or live remote receipts align with the local timeline head

Reviewer implication:

- the submitted package is internally consistent and anchored to known verifier receipt history
- this is stronger provenance evidence, not automatic proof of authorship

### `Unverified`

Meaning:

- the package is structurally valid
- local integrity checks passed
- remote trust anchors are missing, incomplete, or not packaged

Reviewer implication:

- the work may still be legitimate
- the package does not carry enough remote trust evidence for a stronger claim
- findings and review signals should be read before drawing conclusions

### `Invalid`

Meaning:

- package structure, hashes, event chain, receipt chain, or remote alignment failed validation

Reviewer implication:

- the package should not be treated as trustworthy provenance without additional explanation
- an invalid package is evidence of inconsistency, not automatic proof of misconduct

### `Degraded`

Meaning today:

- reserved for future policy work

Reviewer implication today:

- do not expect meaningful `Degraded` behavior from the current MVP
- use findings and review signals instead

## Findings

Findings explain why the current trust state was assigned.

Examples:

- `remote-receipts-missing`
- `remote-receipts-not-packaged`

Reviewer rule:

- findings are explanatory evidence, not accusations
- missing remote receipts usually mean weaker trust, not automatic wrongdoing

## Review Signals

Review signals are neutral prompts for extra attention.

Examples:

- large insert
- missing history
- timeline anomaly
- short duration

Reviewer rule:

- a review signal means "look closer"
- a review signal does not mean "misconduct happened"

## Recommended Review Flow

1. Check the trust state first.
2. Read the summary.
3. Read findings.
4. Read review signals.
5. Check whether missing trust comes from absent remote evidence, broken structure, or both.
6. Only then combine OWS output with the rest of the academic context.

## What Reviewers Should Not Do

- do not treat `Unverified` as guilt
- do not treat `Invalid` as enough by itself for disciplinary action
- do not ignore the distinction between missing evidence and broken evidence
- do not overclaim what filesystem observation can prove; event absence is not proof of misconduct

## Current MVP Limitation

The current repository still has major review limitations:

- local verification remains useful without a running Agent, but continuous observation requires an initialized project and an available Agent host
- `Degraded` is not a mature policy state
- there is no dedicated reviewer-facing UI
- reports are file outputs, not a case-management workflow

That means reviewers should use OWS as supporting provenance evidence, not as an automated verdict system.
