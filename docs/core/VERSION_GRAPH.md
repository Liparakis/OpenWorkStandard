# Work Version Graph

## Overview

The Work Version Graph is OWS's structural model for work evolution. It is a directed acyclic graph rather than a plain timeline so the model can eventually represent branching, merges, and imported states without redefining the core abstraction.

## Work version nodes

Each node represents a state of work and should carry:

- a version identifier
- a content hash
- a creation timestamp
- parent hashes or identifiers
- optional metadata

## Work version edges

Each edge describes a transition from one version node to another. The edge may refer to the event that triggered the transition and include a short description of the change.

## Parent hashes

Parent hashes support chain integrity and future reconstruction logic. In a linear sequence each node has one parent. In a merge-oriented future flow a node may reference multiple parents.

## Version reconstruction

Reconstruction should eventually combine:

- the graph structure
- timeline ordering
- stored deltas or snapshots
- hash validation at each step

The initial repository defines these concepts but does not claim reconstruction is implemented yet.

## DAG rationale

A DAG is more honest than a timeline-only model because real work does not always evolve as a perfect line. Tool changes, imports, and merges all produce provenance that is still meaningful but not strictly linear.
