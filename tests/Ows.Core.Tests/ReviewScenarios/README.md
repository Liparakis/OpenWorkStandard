# Review Scenario Tests

These tests group behavior that should be manually reviewed before release because it affects OWS trust semantics, evidence continuity, and reviewer-facing wording.

`ObservationGapReviewTests.cs` validates:

* observation gaps
* clean stops vs interruptions
* recovery scan behavior
* snapshot hash binding and recovery baseline mismatches
* large unobserved changes
* trust degradation rules
* non-accusatory report language

Core invariant:
Event presence is evidence of recorded activity. Event absence is not proof of misconduct.

Unobserved large changes are evidence continuity review signals, not accusations of misconduct or cheating.

`observed_snapshot.json` is operational recovery state. OWS now commits canonical snapshot hashes into the timeline with `SnapshotUpdated` so current package hash verification stays distinct from observed edit-history continuity.
