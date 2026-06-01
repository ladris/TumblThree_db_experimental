# TumblThree → Python + Flask rewrite

The project is being rewritten from C#/WPF (Windows-only desktop GUI) to a
cross-platform **Python** application with a **Flask** browser UI backed by a
single local **SQLite** database. The legacy C# app is frozen and serves only
as the behavioural spec to port from.

Decisions (locked):
- **Python only** — the C#/WPF app is dropped.
- **Personal / localhost** — Flask bound to `127.0.0.1`, single user, no auth.
- **Jinja2 + HTMX** server-rendered UI; live updates via Server-Sent Events.
- **Full import** of existing libraries, via a first-run wizard (fresh start by
  default, or point at an existing TumblThree directory to convert).

## How to run

```bash
python -m venv .venv && . .venv/bin/activate
pip install -r requirements.txt
python -m tumblthree            # opens http://127.0.0.1:5000
pytest                          # 21 tests, all green
```

Data lives in `~/.local/share/tumblthree/` (override with `TUMBLTHREE_DATA_DIR`):
`tumblthree.db` + a `media/` tree.

## What works today (foundation — built and tested)

| Area | Status |
|---|---|
| SQLite schema (blogs, posts, files, tags, queue, settings) + **FTS5** | ✅ |
| Data layer (`Database`, `Repository`) with WAL + tuned PRAGMAs | ✅ |
| **De-dup engine** (`FileIndex`): in-RAM FNV-1a negative cache + confirm-on-hit; per-blog and cross-blog | ✅ |
| **Legacy importer**: converts existing libraries — blog metadata + de-dup records, from **both** legacy JSON *and* the phase-1 SQLite `_files` format | ✅ |
| Flask app: onboarding wizard, dashboard, blogs CRUD, blog detail, settings | ✅ |
| Background **crawl worker** + in-process event bus | ✅ |
| **Live progress** via SSE (`/events`, `EventSource`) | ✅ |
| Full-text **search** UI | ✅ |
| Network-free **demo crawler** exercising the whole pipeline | ✅ |
| 21 automated tests (db, dedup, importer, web incl. a live crawl) | ✅ |

The architecture is proven end-to-end: onboarding → add blog → crawl with live
progress → persisted posts/files → dashboard stats → full-text search.

## Project layout

```
tumblthree/
  config.py            # paths, tuning
  db/
    schema.sql         # the relational + FTS schema
    database.py        # connection, PRAGMAs
    models.py          # Blog / Post / FileEntry dataclasses, BlogType
    repository.py      # blogs, posts, search, stats, settings
    dedup.py           # FileIndex — the fast de-dup engine
  importer/legacy.py   # convert an existing C# TumblThree library
  crawl/
    base.py            # Crawler ABC + CrawlContext
    worker.py          # CrawlManager (threads) + EventBus (SSE fan-out)
    demo.py            # DemoCrawler (no network) — replace per platform
  web/
    app.py             # Flask factory + Services
    routes.py          # all endpoints
    templates/  static/
tests/                 # pytest
```

## Roadmap (the rest of the work, in suggested order)

1. **Real platform crawlers** — the bulk of the effort. Port, one at a time,
   behind the `Crawler` interface, using `httpx` + `asyncio` (or a thread pool)
   with per-host rate limiting:
   Tumblr (public API + svc-JSON), Tumblr hidden/liked-by/search/tagsearch,
   Twitter/X (GraphQL), Bluesky, newTumbl. Reference: the C# `Crawler/*` classes.
2. **Download engine** — concurrent file downloads with resume, the de-dup
   check on the hot path, filename templating, and media stored under
   `media/<blog>/`. Reference: `AbstractDownloader`.
3. **Auth / login** — the hard part. The C# app used an embedded WebView2 to
   harvest cookies/OAuth. Python options: a **Playwright**-driven interactive
   login, or a **cookie-import** flow. Per-platform.
4. **Media gallery** — a DB-backed lightbox/grid served from `media/` + `file`,
   with filtering by blog/type/tag/date, infinite scroll, inline text posts.
5. **Queue & scheduling** — a global crawl queue, concurrency caps, and
   cron-like auto-crawl; a small REST/JSON API.
6. **Parity sweep** — the long tail of per-blog settings, metadata formats,
   size/skip rules, and an **export** command that regenerates the legacy
   `.txt`/`.json` on demand (so power-user workflows survive).
7. **Curation features the flat files never allowed** — tags/favourites/notes,
   saved searches, duplicate & near-duplicate finder (content/perceptual hash),
   storage/orphan reports, "what's new since last crawl".
8. **Packaging** — `pipx install`, a Docker image for self-hosting, and an
   optional `pywebview` desktop wrapper so existing users still get a
   double-click app. Vendor `htmx.min.js` for fully-offline use.

## Notes

- `htmx` is currently loaded from a CDN for convenience; vendor it locally
  before shipping so a personal/offline install needs no network for the UI.
- sqlite3 connections are not shared across threads: the crawl worker uses its
  own connection; WAL lets the web connection read concurrently.
