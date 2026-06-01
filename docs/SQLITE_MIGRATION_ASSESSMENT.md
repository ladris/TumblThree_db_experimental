# TumblThree → Local SQLite Database: Workload Assessment

> Scope: assess the full engineering effort to replace TumblThree's current
> "mass dump to flat files" persistence (per‑blog JSON index files, per‑blog
> JSON dedup databases, and per‑blog `.txt` content dumps) with a single local
> SQLite database file. This is an **assessment only** — no production code is
> changed by this document.

---

## 1. Executive summary

TumblThree currently persists *everything* except the downloaded media bytes
themselves as flat files: two JSON files per blog (a metadata "index" and a
"files" dedup database), several aggregated `.txt` files per blog for text
content and URL lists, optional per‑post `.json` crawler dumps, and a handful
of global JSON config files. There is **no relational/database layer today** —
no SQLite, EF, Dapper, or `System.Data.*` anywhere in the solution.

Converting to a local SQLite file is **feasible and architecturally clean** —
the codebase already funnels nearly all persistence through two interfaces
(`IFiles` and `IBlog`) plus one text‑append path (`AbstractDownloader`), so the
blast radius is contained. But it is a **substantial** piece of work because of:

- **.NET Framework 4.7.2** (old‑style `.csproj`, not SDK‑style) → native SQLite
  deployment and packaging/installer changes are required.
- **8+ platforms / blog types**, 16 crawlers, 11 downloaders, 14 post models,
  ~48 k LOC — each platform has its own text/metadata formatting that must be
  re‑pointed at the DB.
- **Backward compatibility**: thousands of existing users have libraries with
  the current files. A one‑way importer/migrator is effectively mandatory.
- **Concurrency model change**: today every blog has its own in‑memory
  `HashSet` + its own files; SQLite changes the locking/transaction story.

**Rough order‑of‑magnitude effort:** a careful, production‑quality migration
that preserves all features and migrates existing libraries is **~6–12 focused
engineering‑weeks** for one developer (see §9 for the breakdown). A
"dedup‑DB‑only" minimal version (the highest‑value slice) is **~2–3 weeks**.

---

## 2. What gets "mass dumped" today

### 2.1 Per‑blog JSON: the blog index (metadata + counters)
- File: `{DownloadLocation}/Index/{BlogName}.{BlogType}` (e.g. `myblog.tumblr`).
- Format: JSON via `DataContractJsonSerializer`.
- Class: `TumblThree.Domain/Models/Blogs/Blog.cs` (**94 `[DataMember]` fields**),
  with per‑platform subclasses (`TumblrBlog`, `TwitterBlog`, `BlueskyBlog`,
  `NewTumblBlog`, `TumblrHiddenBlog`, `TumblrLikedByBlog`, `TumblrSearchBlog`,
  `TumblrTagSearchBlog`).
- Load/Save: `Blog.cs:934–999` (load), `Blog.cs:1001–1047` (save), atomic
  `.new`/`.bak` replace.
- Holds: URL, settings (what to download, sizes, templates, page ranges),
  progress counters (`DownloadedPhotos`, `DownloadedVideos`, …), timestamps,
  rating/tags/notes, collection id.

### 2.2 Per‑blog JSON: the "files" dedup database
- File: `{DownloadLocation}/Index/{BlogName}_files.{BlogType}`.
- Format: JSON via `DataContractJsonSerializer`.
- Class: `TumblThree.Domain/Models/Files/Files.cs` (386 LOC) implementing
  `IFiles`, holding `HashSet<FileEntry>` where `FileEntry` = `{ L:Link,
  O:OriginalLink, F:Filename }` (`FileEntry.cs`).
- **Loaded fully into memory** per blog, guarded by `ReaderWriterLockSlim`.
- Has a hand‑rolled **schema version system** (`MAX_SUPPORTED_DB_VERSION = 6`)
  with migrations 1→6 inline in `LoadCore` (`Files.cs:172–275`).
- Saved atomically (`.new`/`.bak`) and on a **120‑second timer**
  (`AbstractDownloader.cs:39,60` `SAVE_TIMESPAN_SECS`).
- A **copy** is kept under `Index/Archive/` for *offline cross‑blog duplicate
  checking* (`ManagerService.CacheLibraries`), and all archive DBs are loaded
  into memory too.

### 2.3 Per‑blog flat `.txt` dumps (the "tons of txt files")
Written by `AbstractDownloader.AppendToTextFile` (`AbstractDownloader.cs:139`)
through `StreamWriterWithInfo`, into the blog's download folder:

| File | Content | Toggle |
|---|---|---|
| `texts.txt` | text‑post bodies | `DownloadText` |
| `quotes.txt` | quote posts | `DownloadQuote` |
| `links.txt` | link posts | `DownloadLink` |
| `conversations.txt` | chat/conversation posts | `DownloadConversation` |
| `answers.txt` | Q&A posts | `DownloadAnswer` |
| `images_url.txt` | photo URLs (URL‑list mode) | `DownloadUrlList` |
| `videos_url.txt` | video URLs (URL‑list mode) | `DownloadUrlList` |
| `audios_url.txt` | audio URLs (URL‑list mode) | `DownloadUrlList` |
| `images.txt` / `videos.txt` / `audios.txt` | photo/video/audio **metadata** | `CreatePhotoMeta` / `CreateVideoMeta` / `CreateAudioMeta` |
| `{postId}.txt` | one file per text post | `SaveTextsIndividualFiles` |
| `*.json` (+ optional `.zip`) | raw per‑post crawler JSON | `DumpCrawlerData` / `ZipCrawlerData` (`JsonDownloader.cs`) |

These can be emitted as **plain text or as a JSON array** depending on
`blog.MetadataFormat` (`MetadataType.Text|Json`).

> ⚠️ **Fragility worth highlighting to justify the migration:**
> `StreamWriterWithInfo` (`Downloader/StreamWriterWithInfo.cs`) maintains a JSON
> array by **seeking backwards a hard‑coded number of bytes** (`Seek(-5…)`,
> `Seek(-3…)`) to splice in commas and the closing `]`. Any crash mid‑append,
> encoding hiccup, or manual edit corrupts the file. This is exactly the class
> of bug SQLite eliminates.

### 2.4 Global JSON config (out of scope for media, in scope for completeness)
`Settings.json`, `Manager.json`, `Queuelist.json`, `Cookies.json`
(via `SettingsProvider`, `DataContractJsonSerializer`), stored in
`%LocalAppData%\TumblThree\` or app folder (portable mode).

### 2.5 Multi‑location / Collections
Settings carry a list of **Collections**, each with its own `DownloadLocation`
and `Index/` folder, plus an `OfflineDuplicateCheck` flag. Any DB design must
keep working across multiple library roots, not just one.

---

## 3. Why the current design is the pain point

- **Memory**: every blog's *entire* dedup set is held in RAM; for a large
  archive a single `_files` JSON is 10–20 MB, and offline dup‑check loads
  *every* blog's archive copy simultaneously. With hundreds of blogs this is
  real memory pressure and slow startup.
- **No queryability**: "show me every video over 5 MB downloaded in March from
  blogs tagged X" is impossible without loading and scanning everything.
- **Duplicated truth**: URL‑list `.txt` files and the `_files` DB track
  overlapping information in two formats.
- **Write amplification**: the whole files DB is re‑serialized every 120 s and
  at end of crawl, regardless of how few entries changed.
- **Brittle text writer** (see §2.3) and **hand‑rolled migrations**.
- **Crash safety** relies on `.new`/`.bak` shuffles instead of transactions.

---

## 4. Target architecture options

### Decision A — One database vs. per‑blog databases
| Option | Pros | Cons |
|---|---|---|
| **A1. Single `TumblThree.sqlite` per library root** (recommended) | True cross‑blog dedup via SQL; one connection pool; global queries/stats; simplest archive story | Single writer ⇒ must serialize writes across concurrently crawling blogs (mitigated by WAL + short transactions / a write queue) |
| **A2. One `.sqlite` per blog** (drop‑in for today's per‑blog `_files`) | Minimal concurrency change; mirrors current model; easy partial rollout | Loses the main upside (cross‑blog queries/dedup still needs fan‑out); many DB files; archive/offline check still awkward |

A1 is the better long‑term target; A2 is a lower‑risk stepping stone. A phased
plan can ship A2's `IFiles` provider first, then converge on A1.

### Decision B — Data access library (all support net472)
| Option | Notes |
|---|---|
| **Microsoft.Data.Sqlite + Dapper** (recommended) | Lightweight, actively maintained, netstandard2.0 ⇒ works on net472 via SQLitePCLRaw; explicit SQL keeps control over the hot dedup path |
| `System.Data.SQLite` (SQLite.org ADO.NET) | Mature, but heavier native‑interop deployment (`SQLite.Interop.dll` per arch) |
| EF Core 3.1 | Last EF Core supporting net472; **EOL/unsupported**, heavier, migrations engine is nice but overkill for this schema |
| Raw ADO.NET only | No extra deps but more boilerplate |

All require shipping a **native SQLite binary per architecture** — this is the
single biggest *non‑code* task (installer/packaging, see §7).

### Proposed schema (single‑DB / A1 variant)
```
blog(
  id INTEGER PK, name TEXT, blog_type TEXT, url TEXT, collection_id INTEGER,
  -- the ~94 current Blog DataMembers become typed columns
  -- (settings, counters, timestamps, rating, notes, …)
  version INTEGER, UNIQUE(name, blog_type, collection_id))

file_entry(                     -- replaces *_files.{type} dedup DB
  id INTEGER PK, blog_id INTEGER REFERENCES blog(id),
  link TEXT NOT NULL,           -- "L"
  original_link TEXT,           -- "O"
  filename TEXT,                -- "F"
  added_utc INTEGER)
CREATE INDEX ix_file_entry_blog_link ON file_entry(blog_id, link);
CREATE INDEX ix_file_entry_orig     ON file_entry(blog_id, original_link);
-- for cross-blog (offline) dedup: index on link alone

post(                           -- replaces texts/quotes/links/…txt + meta + crawler json
  id INTEGER PK, blog_id INTEGER REFERENCES blog(id),
  remote_post_id TEXT, post_type TEXT,      -- text|quote|link|conversation|answer|photo_meta|…
  posted_utc INTEGER, url TEXT, body TEXT,  -- rendered text / metadata
  raw_json TEXT,                            -- optional, replaces DumpCrawlerData
  tags TEXT)
CREATE INDEX ix_post_blog_type ON post(blog_id, post_type);

schema_meta(key TEXT PK, value TEXT)        -- replaces the v1..v6 version logic
```
`PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;`

---

## 5. Code surface to change (the blast radius)

The good news: persistence is funneled through narrow interfaces. The work is
"implement these interfaces against SQLite + re‑point the text writers," not a
rewrite of crawlers.

**Core seams (must change):**
- `Domain/Models/Files/IFiles.cs` + `Files.cs` (+ 8 subclass stubs) → new
  `SqliteFiles : IFiles`. This alone replaces §2.2 and is the highest‑value
  slice. `CheckIfFileExistsInDB`, `AddFileToDb`, `UpdateOriginalLink`, `Save`.
- `Domain/Models/Blogs/Blog.cs` (`Load`/`Save`/`UpdatePostCount`
  /`UpdateProgress`) + 8 subclasses → DB‑backed metadata + counters (or keep
  blog JSON in phase 1 to reduce risk — see §9).
- `Applications/Downloader/AbstractDownloader.cs` — the text‑append path
  (`AppendToTextFile`/`WriteToTextFile`/`DownloadTextPost`, lines 139–244,
  473–493) and the dedup calls (435–469). Re‑point `.txt` writes to `post`
  rows; remove/replace `StreamWriterWithInfo`.
- `Applications/Downloader/JsonDownloader.cs` + `TumblrXmlDownloader.cs` —
  `DumpCrawlerData`/`ZipCrawlerData` → `post.raw_json`.
- `Applications/Services/ManagerService.cs` + `BlogService.cs` — `Databases`,
  `archiveDatabases`, `CacheLibraries`, cross‑blog dedup. Offline/archive dedup
  becomes a SQL query instead of loading archive JSONs.
- `Applications/Controllers/ManagerController.cs` — startup discovery
  (`LoadLibraryAsync`/`GetIBlogsCore`/`LoadAllDatabasesAsync`/`LoadArchiveAsync`)
  switches from "scan `Index/` for files" to "open DB and query".

**Per‑platform formatting (must re‑point, 8× similar work):**
- 16 crawlers in `Applications/Crawler/*` build the actual text/metadata strings
  and call `AddToDownloadList`. The strings they produce now land in `.txt`;
  they must instead become `post` rows. Each platform (Tumblr, TumblrHidden,
  TumblrLikedBy, TumblrSearch, TumblrTagSearch, Twitter, Bluesky, NewTumbl) has
  its own meta formatting and individual‑file logic to update.
- 14 post models in `DataModels/TumblrPosts/*` map cleanly to `post_type`.

**Largely unaffected (reusable as‑is):**
- The producer/consumer pipeline: `IPostQueue`/`PostQueue` (TPL Dataflow),
  `AbstractCrawler.AddToDownloadList`, the semaphores. SQLite slots in at the
  consumer's persistence step.
- HTTP/auth/rate‑limiting, the WPF UI/ViewModels (bind to the same `IBlog`
  counters), media file downloading itself.

**Call sites touching the persistence APIs today** (≈25 files): controllers,
crawler factory, all downloaders, blog/files models + interfaces, services,
and the design‑time mock. All are listed; none are huge.

---

## 6. Backward compatibility & data migration (mandatory)

Existing users have populated `Index/*.{type}`, `Index/*_files.{type}`,
`Index/Archive/**`, and per‑blog `.txt` files. A **one‑time importer** is
required and is non‑trivial:

1. On first run with the new build, detect legacy `Index/` per library root.
2. For each blog: deserialize the legacy JSON (reuse the *existing* `Blog.Load`
   / `Files.Load` + their v1→v6 migration code as the *reader*) and bulk‑insert
   into SQLite inside a transaction.
3. Optionally parse existing `.txt` dumps into `post` rows (or leave them in
   place and only adopt DB going forward — a product decision).
4. Move/rename legacy files to a `Index/legacy_backup/` so nothing is destroyed
   and rollback is possible.
5. Idempotent + resumable (large libraries; crash‑safe).

This is the riskiest area: it must be lossless across **every** platform and
**every** historical DB version, and well tested against real libraries.

---

## 7. Packaging / build / non‑code work

- **Native SQLite binaries**: ship `x86` + `x64` (and pick AnyCPU strategy).
  Update the project, `appveyor.yml` build, and the **Inno Setup installer** to
  include and place the native interop DLL(s). This is fiddly on net472.
- **NuGet additions**: `Microsoft.Data.Sqlite` (+ `SQLitePCLRaw.bundle_e_sqlite3`)
  and optionally `Dapper`. Update `LICENSE-3RD-PARTY`.
- **Portable mode**: DB file must live alongside the portable app, mirroring the
  current portable `Settings.json` logic.
- **CI**: the test project gains a SQLite dependency; ensure it runs on the
  AppVeyor image.

---

## 8. Risks & hard problems

1. **Write concurrency (A1).** Multiple blogs crawl in parallel; SQLite allows
   one writer. Needs WAL + short transactions, a serialized write queue, and
   `busy_timeout`. Get this wrong → `SQLITE_BUSY` errors or throughput loss.
2. **Dedup hot path.** `CheckIfFileExistsInDB` is called per candidate file.
   Today it's an in‑RAM `HashSet.Contains` (O(1)). Must stay fast: indexed
   queries + possibly an in‑memory cache layer per blog to match current speed.
3. **Lossless migration** across 8 platforms × 6 DB versions × text dumps.
4. **Atomicity vs. the 120 s timer model.** Replace timer‑flush semantics with
   transaction boundaries without changing crawl‑resume behavior.
5. **Cross‑blog / archive dedup** semantics (`OfflineDuplicateCheck`,
   collections) must be preserved exactly.
6. **Corruption recovery / `vacuum` / backups** — give users a way to back up
   one file (a migration *upside*, but needs a UI affordance).
7. **Feature‑parity for power users** who currently grep the `.txt`/`.json`
   dumps with external tools — consider an "export to txt/json" command so the
   workflow isn't lost.
8. **No existing DB infra** means new patterns, helpers, tests, and team
   familiarity from scratch.

---

## 9. Phased plan & effort estimate

Estimates assume one experienced .NET dev, production quality (tests + docs +
installer), not a throwaway prototype.

| Phase | Work | Effort |
|---|---|---|
| **0. Spikes & decisions** | Pick A1/A2, library, native packaging spike, schema sign‑off | 3–5 d |
| **1. `SqliteFiles : IFiles`** | DB bootstrap, schema, dedup CRUD, WAL/write‑queue, per‑blog cache, swap in via DI/MEF | 5–8 d |
| **2. Legacy importer (files DB)** | Read legacy `_files` (reuse existing readers) → SQLite, idempotent, backups, tests across platforms/versions | 5–8 d |
| **3. Text/metadata dumps → `post`** | Re‑point `AbstractDownloader` text path + 16 crawlers' meta/individual‑file logic + `JsonDownloader`/`Xml` raw dumps; retire `StreamWriterWithInfo` | 8–12 d |
| **4. Blog metadata/counters → DB** | `Blog.Load/Save/UpdatePostCount/UpdateProgress` + 8 subclasses + startup discovery in `ManagerController`/`ManagerService` | 6–10 d |
| **5. Archive/offline dedup & collections** | Replace `CacheLibraries`/archive loading with SQL; multi‑root | 3–5 d |
| **6. Packaging/installer/CI** | Native binaries, Inno Setup, AppVeyor, 3rd‑party license | 3–5 d |
| **7. Test, perf, migration QA** | Unit/integration, large‑library perf vs. baseline, real‑library migration runs, optional "export to txt" | 6–10 d |
| **Total (full migration)** | | **~6–12 weeks** |

**Minimal high‑value slice (recommended first deliverable):** Phases 0+1+2 only
— move just the dedup database to SQLite, keep blog JSON and the `.txt` dumps
untouched. Delivers the memory/startup/robustness wins on the most painful
artifact with the smallest blast radius: **~2–3 weeks**.

---

## 10. Recommended sequencing

1. **A2‑style `SqliteFiles` provider + importer** behind a feature flag (keeps
   `.txt`/blog JSON as‑is). Ship, gather feedback. *(highest value / lowest risk)*
2. **Migrate text & metadata dumps** into the `post` table; add an optional
   "export to legacy txt/json" so power‑user workflows survive.
3. **Migrate blog metadata/counters**, converge on a **single DB per library
   root (A1)**, and turn archive/offline dedup into SQL.
4. **Add user‑facing wins** the file model never allowed: search, stats,
   one‑file backup/restore, `VACUUM`.

---

### Appendix — key references
- Dedup DB: `Domain/Models/Files/Files.cs`, `FileEntry.cs`, `IFiles.cs`
- Blog metadata: `Domain/Models/Blogs/Blog.cs` (94 `[DataMember]`), subclasses
- Text dumping: `Applications/Downloader/AbstractDownloader.cs:139–244,473–493`,
  `StreamWriterWithInfo.cs` (byte‑seek JSON array), `JsonDownloader.cs`
- Pipeline (reuse): `DataModels/IPostQueue.cs`, `PostQueue.cs`,
  `Crawler/AbstractCrawler.cs:345`
- Services/startup: `Services/ManagerService.cs`, `BlogService.cs`,
  `Controllers/ManagerController.cs`
- Target framework: **.NET Framework 4.7.2**, old‑style `.csproj`, MEF,
  Newtonsoft.Json, `DataContractJsonSerializer`. **No existing SQLite/EF/Dapper.**
</content>
</invoke>
