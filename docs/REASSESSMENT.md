# Project reassessment — TumblThree Python rewrite

_State at this writing: branch `claude/python-flask-rewrite`, 40 tests green,
working tree clean and pushed._

## 1. Decisions ledger (what we've locked in)

| # | Decision | Status |
|---|---|---|
| D1 | Move from per-blog flat files → local SQLite | done (Python schema) |
| D2 | Rewrite C#/WPF → **Python + Flask**; drop C# | done; C# archived in `legacy-csharp/` |
| D3 | **Personal/localhost**, single user, no auth wall on the app itself | done |
| D4 | UI = **Jinja2 + HTMX**, live updates via SSE | done |
| D5 | **Full import** of legacy libraries via a first-run wizard (fresh default, or point at an existing dir) | done |
| D6 | Auth = pluggable **Session** layer; support **both** cookie import and Playwright; start with cookie + Bluesky app-password | session layer done; consent bootstrap + authed crawlers pending |
| D7 | Commit/push autonomously when appropriate | active |

## 2. What exists and is genuinely solid

- **Data layer**: schema (blog/post/file/tag/queue/session/settings + FTS5),
  WAL + tuned PRAGMAs, `Repository`, and the `FileIndex` de-dup engine
  (in-RAM FNV negative cache + confirm-on-hit). Well covered by tests.
- **Importer**: converts legacy libraries (blog metadata + de-dup records) from
  both the old JSON and the phase-1 SQLite `_files` formats; idempotent.
- **Crawl pipeline**: `Crawler`/`CrawlContext`/`Downloader`/`HttpClient` cleanly
  separated and injectable, which is what makes everything testable offline.
- **Tumblr public crawler** (v1 read-JSON), **concurrent downloader** (rate
  limit, retries, atomic writes, hot-path dedup), **SSE live progress**, **FTS
  search**, **media gallery**, **auth/session layer**.
- 40 automated tests; the web app boots and runs end-to-end.

## 3. Pitfalls & issues (ranked)

### Fixed during this reassessment
- **[FIXED] Search crashed on ordinary input.** FTS5 parsed `OR`, `*`, quotes,
  parens as syntax → 500. Now user input is tokenized into quoted AND-ed terms.
- **[FIXED] Text posts re-counted as "downloaded" every re-crawl.** `add_post_ex`
  now reports real creation; re-seen text posts count as duplicates.

### Open — HIGH
- **Filename collisions overwrite files on disk.** Two distinct URLs whose last
  path segment matches write to the same `media/<blog>/<name>`; the second
  overwrites the first while both get `file` rows (DB says 2, disk has 1 → data
  loss). The legacy app solved this with an append-template (`_1`, `_2`). *Fix:*
  port that template / detect collisions and suffix.
- **Tumblr GDPR-consent / NSFW reality not handled.** The legacy app ran a
  consent handshake before crawling and required login for NSFW blogs. The
  Python crawler does neither, so against *live* Tumblr many public blogs will
  redirect `api/read/json` to a consent page and the parse fails. **Public
  crawling is effectively auth-adjacent.** *Fix:* port the consent bootstrap
  into the session layer (next task).

### Open — MEDIUM
- **Unported platforms silently fall back to the demo crawler.** Adding a
  Twitter/Bluesky/newTumbl blog today writes *fake* demo posts into the real DB.
  *Fix:* `crawler_for` should refuse unsupported types with a clear message.
- **Download de-dup TOCTOU.** Two concurrent jobs for the same URL both pass the
  `exists()` check, both download, both tally (the unique index dedups the row,
  but bytes are fetched twice and the count inflates). Low frequency; *fix:*
  claim the URL in-memory before downloading.
- **FTS search has no error envelope beyond the tokenizer fix.** Now safe, but
  worth a regression guard if query handling changes.
- **Secrets at rest in plaintext.** Cookies/tokens live unencrypted in the
  `session` table. Acceptable for localhost; document it and consider OS
  keyring / at-rest encryption if this ever leaves localhost.

### Open — LOW
- **Flask dev server** (`app.run`) is not a production server — fine for
  localhost; switch to waitress/gunicorn if ever hosted.
- **Fixed dev `secret_key`** — fine for localhost only.
- **`_fetch_page` aborts the whole crawl on one transient page error** (after
  the client's own retries). Could skip-and-continue instead.
- **`parse_blog_url`** doesn't handle `tumblr.com/blog/<name>` style or custom
  domains; the type dropdown is the fallback.

## 4. Known parity gaps vs. the legacy app

- **Video/audio**: best-effort `*.mp4/*.mp3` regex only; the C# app
  reconstructed `vtt.tumblr.com` URLs, handled `_480` sizes, and concatenated
  HLS playlist parts. Most real Tumblr video won't download yet.
- **Photo options unused**: imported `ImageSize`, skip-gif, pnj→png, raw-size,
  force-rescan toggles sit in `settings_json` but aren't consumed.
- **Importer doesn't parse legacy `.txt` dumps into `post`** — only blog
  metadata + de-dup records convert; historical text isn't searchable yet.
- **Not yet built**: filename templating, date/page-range limits, queue logic
  (table exists, no runtime), collections, bytes/sec bandwidth cap, auto-crawl
  scheduling, legacy-format export, REST API, the other 5+ platform crawlers.

## 5. Efficiency opportunities

- **Bulk post inserts**: the crawler inserts posts one-by-one under the lock;
  batching per page in a transaction would cut lock churn on large blogs.
- **`exists_anywhere` cross-blog dedup** hits SQLite per call; if used heavily,
  a shared in-RAM global hash (like per-blog) would remove the query.
- **FTS rebuild cost on import**: importing huge libraries fires per-row FTS
  triggers; an `INSERT ... ` with deferred `post_fts` rebuild (or
  `'rebuild'`) would import faster.
- **Connection model**: the web uses one lock-guarded connection; if the UI ever
  feels serialized under load, a small read-only connection pool would help
  (not needed at localhost scale).
- **Rate limiter** reserves future time per host — verify pacing is what we want
  under many concurrent download threads (currently serializes per host, which
  is polite but may under-utilize allowed throughput).

## 6. Caveats & addendums

- **No live-network validation is possible in this build environment** (outbound
  hosts are allowlisted). All crawler/downloader testing is offline + fixtures;
  the first real-world smoke test must happen on your machine, and the
  consent/NSFW reality (HIGH item above) is the most likely first surprise.
- **The C# phase-1 SQLite branch was never compiled** (no .NET here). It's
  reference-only now; the design carried into Python and the importer reads its
  format.
- **Playwright is the more capable login** (2FA/CAPTCHA/redirects, captures
  cookies + localStorage, one-click re-login); cookie import is lighter and uses
  your organic browser session (less bot-flagged). Both produce the same
  `Session`, so they're interchangeable — Playwright is wired as optional.

## 7. Recommended next steps (in order)

1. **Port the Tumblr consent / anonymous-session bootstrap** into the session
   layer — unblocks *public* crawling for real (HIGH).
2. **Fix filename-collision overwrites** (append-template) (HIGH).
3. **Make `crawler_for` refuse unported platforms** instead of demo fallback
   (MEDIUM, cheap).
4. **Bluesky crawler** (real) — the stable, app-password-authed quick win.
5. Then: authed Tumblr (hidden/likes), video/audio parity, importer `.txt`→post,
   queue logic, the remaining platforms.
