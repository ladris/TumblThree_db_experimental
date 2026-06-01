# Archived: original C#/WPF TumblThree

This directory holds the **original C#/WPF implementation** of TumblThree,
**archived for reference only**. The project has moved to a Python + Flask
rewrite — see the repository root and `docs/PYTHON_REWRITE.md`.

It is kept (not deleted) so we can consult the original, battle-tested behaviour
while porting — especially the per-platform crawlers, authentication/login
flows, rate limiting, filename templating, and metadata formats — in case the
Python port misses an edge case.

Contents:
- `src/` — the C# solution (Domain / Applications / Presentation projects).
- `lib/` — bundled C# dependencies.
- `scripts/`, `appveyor.yml` — the old Windows build/CI pipeline.
- `README.md`, `Contributing.md`, `LICENSE-3RD-PARTY` — original project docs.

> The last C# development (a SQLite migration of the de-dup database) lives on
> the `claude/sqlite-migration-phase1` branch, described in
> `docs/SQLITE_MIGRATION_PHASE1.md`. Do not develop new features here.
