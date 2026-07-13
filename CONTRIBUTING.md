# Contributing

1. Read `SPEC.md` and the architecture documentation.
2. Keep OWS local-first, text-first, IDE-agnostic, and project-scoped.
3. Add or update tests for public behavior.
4. Run:

~~~text
dotnet build OWS.sln -nologo
dotnet test OWS.sln -nologo
git diff --check
~~~

Keep changes scoped, preserve unrelated user work, and update roadmap/security
docs when capabilities or data boundaries change.
