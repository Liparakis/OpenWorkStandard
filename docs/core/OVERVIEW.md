# Open Work Standard Overview

Open Work Standard (OWS) is a local-first academic work provenance toolchain. It records project-scoped filesystem evidence, packages it into `.owspkg`, and provides offline verification and review reports.

OWS is intentionally not an LMS, surveillance tool, automated misconduct detector, hosted dashboard, or institutional management layer. Those concerns belong in separate projects.

The smallest useful workflow is:

```text
ows init → ordinary project work → ows package → offline verify / inspect / report
```

The Agent watches only explicitly initialized project roots. Event presence is evidence of recorded activity; event absence is not proof of misconduct.
