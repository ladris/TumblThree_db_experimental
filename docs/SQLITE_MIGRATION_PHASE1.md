# SQLite Migration — Phase 1: the per-blog de-duplication database

This phase replaces the per-blog **files de-duplication database** (the
`{name}_files.{type}` JSON documents) with a fast, embedded **SQLite** store,
behind the existing `IFiles` interface. It is the highest-value, lowest-risk
slice from `SQLITE_MIGRATION_ASSESSMENT.md §9`.

> The per-blog text/metadata dumps (`texts.txt`, `images.txt`, …) and the blog
> index metadata files are **not** touched in this phase — that is the
> "exhaustive additional work" we will design next.

## What changed

| File | Change |
|---|---|
| `Domain/Models/Files/SqliteFiles.cs` | **new** — SQLite-backed `IFiles` implementation (the engine) |
| `Domain/Models/Files/Files.cs` | `Load` now resolves to SQLite; legacy JSON reader kept as `LoadLegacyJson` for migration/archives |
| `Domain/Models/Blogs/*Blog.cs` (×8) | blog creation now calls `SqliteFiles.CreateNew(...)` |
| `Applications/Services/ManagerService.cs` | dispose SQLite connections on `RemoveDatabase` / `ClearDatabases` / `ClearArchive` |
| `Applications/Downloader/AbstractDownloader.cs` | dispose the downloader-owned DB connection on `Dispose` (when not the shared instance) |
| `Applications/Controllers/ManagerController.cs` | release the DB connection **before** moving/deleting the file; clean up `-wal`/`-shm` sidecars |
| `Domain/*.csproj`, `Presentation/*.csproj` | add `Microsoft.Data.Sqlite` 6.0.10 (last line that supports .NET Framework 4.7.2) |

## Design — "eye-bleeding" speed, low memory

The hot path is `CheckIfFileExistsInDB`, called once per candidate download.

1. **Same file path, no second file.** The SQLite database lives at the exact
   path the legacy JSON used (`{name}_files.{type}` — SQLite ignores the
   extension). Blog discovery globs, `blog.ChildId`, and the archive-copy
   mechanism keep working unchanged, and nothing gets loaded twice.

2. **In-RAM negative fast-path.** On open we load only a `HashSet<long>` of
   64-bit **FNV-1a** hashes of every link (and original-link), ~8 bytes/entry,
   no strings retained. The common "not downloaded yet" answer is a pure
   in-memory `HashSet.Contains` with **zero disk I/O**.

3. **Confirm-on-hit, never false-skip.** Only when the hash *is* present do we
   touch SQLite, confirming the exact string via a unique index
   (`SELECT 1 FROM entry WHERE link=?`). This rules out the astronomically rare
   hash collision, so we never skip a real download.

4. **Batched, journaled writes.** Inserts go into a single open transaction and
   are committed on `Save()` (the existing 120 s autosave cadence) — no more
   re-serialising an entire JSON document on every flush. The brittle legacy
   `.new`/`.bak` file shuffle is replaced by SQLite's own crash safety.

5. **Tuned PRAGMAs.** `journal_mode=WAL`, `synchronous=NORMAL`,
   `temp_store=MEMORY`, `mmap_size=256MB`, `cache_size≈16MB`,
   `busy_timeout=10s`. Prepared, reused commands for the hot insert/exists
   paths. `wal_checkpoint(TRUNCATE)` on save keeps the main db current so the
   archive copy stays consistent.

### Schema
```sql
CREATE TABLE meta(k TEXT PRIMARY KEY, v TEXT);                 -- name, blogtype, schema, version
CREATE TABLE entry(link TEXT NOT NULL, orig TEXT, fn TEXT);
CREATE UNIQUE INDEX ux_entry_link ON entry(link);
CREATE INDEX        ix_entry_orig ON entry(orig) WHERE orig IS NOT NULL;
```
`fn` and `orig` are stored `NULL` when they equal the link (mirrors the legacy
`FileEntry` space optimisation).

## Migration of existing libraries (automatic, lossless)

On first load of a legacy JSON database, `Files.Load` detects the format by file
header and:
1. reads it through the **existing** loader — reusing all the v1→v6 migration
   logic for free;
2. bulk-imports every entry into a new SQLite db (built as `*.sqlite.tmp`);
3. moves the original JSON to `Index/_legacy_json_backup/` (nothing is deleted);
4. promotes the SQLite db into the canonical path.

Idempotent and crash-safe: a half-finished migration just re-runs, and the
original JSON is always preserved as a backup.

## Scope / known limitations (to address next)

- **Offline-duplicate / archive cache** still runs through the legacy read path
  (`LoadLegacyJson` for JSON snapshots, read-only `SqliteFiles` for SQLite
  snapshots). It is functional but the snapshot-while-open story for that
  subsystem is Phase-2 work. Users with offline-duplicate-check **off** (the
  default) are unaffected.
- The unused legacy `*BlogFiles` subclasses are retained (harmless) rather than
  deleted, to keep the diff small.

## ⚠️ Build / packaging — must be verified on a Windows build

This repo targets **.NET Framework 4.7.2** with old-style (non-SDK) projects,
and the app cannot be compiled/run in this Linux environment. Before release,
verify on Windows:

1. **NuGet restore** pulls `Microsoft.Data.Sqlite` 6.0.10 +
   `SQLitePCLRaw.bundle_e_sqlite3` for both `TumblThree.Domain` and
   `TumblThree.Presentation`.
2. The native **`e_sqlite3.dll`** (`runtimes\win-x86|x64\native`) is copied into
   the application output and resolved at runtime (AnyCPU resolves the right
   sub-folder). The bundle auto-registers the provider — no manual
   `Batteries_V2.Init()` needed.
3. The **Inno Setup installer** and `appveyor.yml` package the native binaries.
4. Smoke test: add a blog, crawl, restart, re-crawl (dedup hits), delete a blog
   (file lock released), and confirm a legacy library migrates on first run.
