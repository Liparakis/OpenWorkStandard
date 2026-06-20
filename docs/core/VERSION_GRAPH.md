# Work Version Graph

## Overview

The Work Version Graph is a forward-compatible placeholder in v0.1. OWS currently packages an empty graph object so the package format can support future graph semantics without changing the archive structure. Real graph nodes, edges, validation, and graph-derived trust signals are deferred.

In practice today:

- `version_graph.json` may be packaged inside `.owspkg`
- the packaged graph is currently scaffolded and empty
- it is not used as meaningful trust evidence yet
- real version-graph implementation is deferred

## Work version nodes

Future graph nodes should carry:

- a version identifier
- a content hash
- a creation timestamp
- parent hashes or identifiers
- optional metadata

## Work version edges

Future graph edges should describe transitions between version nodes. An edge may refer to the event that triggered the transition and include a short description of the change.

## Parent hashes

Parent hashes are part of the deferred design. In a linear sequence each node would have one parent. In a merge-oriented future flow a node may reference multiple parents.

## Version reconstruction

Future reconstruction should combine:

- the graph structure
- timeline ordering
- stored deltas or snapshots
- hash validation at each step

OWS v0.1 does not claim graph reconstruction is implemented.

## DAG rationale

A DAG remains the intended long-term model because real work does not always evolve as a perfect line. Tool changes, imports, and merges can produce provenance that is meaningful but not strictly linear. That semantic model is not implemented in v0.1.
