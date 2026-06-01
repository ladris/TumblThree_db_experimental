-- TumblThree (Python) SQLite schema.
-- Everything the legacy app scattered across per-blog JSON index files, per-blog
-- de-duplication databases, and per-blog *.txt dumps becomes relational + FTS here.

CREATE TABLE IF NOT EXISTS setting (
    key   TEXT PRIMARY KEY,
    value TEXT
);

CREATE TABLE IF NOT EXISTS blog (
    id                INTEGER PRIMARY KEY,
    name              TEXT NOT NULL,
    blog_type         TEXT NOT NULL,
    url               TEXT,
    download_location TEXT,

    -- what to download (the long tail of toggles lives in settings_json)
    download_photo    INTEGER NOT NULL DEFAULT 1,
    download_video    INTEGER NOT NULL DEFAULT 1,
    download_audio    INTEGER NOT NULL DEFAULT 1,
    download_text     INTEGER NOT NULL DEFAULT 1,

    -- progress counters
    total_posts       INTEGER NOT NULL DEFAULT 0,
    downloaded_items  INTEGER NOT NULL DEFAULT 0,
    duplicates        INTEGER NOT NULL DEFAULT 0,

    -- bookkeeping / curation
    date_added        INTEGER,
    last_crawl        INTEGER,
    last_id           TEXT,
    rating            INTEGER NOT NULL DEFAULT 0,
    notes             TEXT,
    settings_json     TEXT,

    UNIQUE (name, blog_type)
);

CREATE TABLE IF NOT EXISTS post (
    id         INTEGER PRIMARY KEY,
    blog_id    INTEGER NOT NULL REFERENCES blog(id) ON DELETE CASCADE,
    remote_id  TEXT,
    post_type  TEXT,             -- photo|video|audio|text|quote|link|conversation|answer|...
    posted_utc INTEGER,
    url        TEXT,
    title      TEXT,
    body       TEXT,             -- rendered text / caption (replaces texts.txt, quotes.txt, ...)
    tags_text  TEXT,
    raw_json   TEXT,             -- replaces DumpCrawlerData
    UNIQUE (blog_id, remote_id, post_type)
);
CREATE INDEX IF NOT EXISTS ix_post_blog_type ON post(blog_id, post_type);
CREATE INDEX IF NOT EXISTS ix_post_posted    ON post(posted_utc);

CREATE TABLE IF NOT EXISTS file (
    id              INTEGER PRIMARY KEY,
    blog_id         INTEGER NOT NULL REFERENCES blog(id) ON DELETE CASCADE,
    post_id         INTEGER REFERENCES post(id) ON DELETE SET NULL,
    link            TEXT NOT NULL,   -- url key used for de-duplication
    original_link   TEXT,
    filename        TEXT,
    size_bytes      INTEGER,
    content_hash    TEXT,            -- reserved for content-level / near-duplicate dedup
    downloaded_utc  INTEGER
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_file_blog_link ON file(blog_id, link);
CREATE INDEX IF NOT EXISTS ix_file_blog_orig  ON file(blog_id, original_link);
CREATE INDEX IF NOT EXISTS ix_file_link_global ON file(link);
CREATE INDEX IF NOT EXISTS ix_file_hash        ON file(content_hash);

CREATE TABLE IF NOT EXISTS tag (
    id   INTEGER PRIMARY KEY,
    name TEXT UNIQUE
);
CREATE TABLE IF NOT EXISTS post_tag (
    post_id INTEGER NOT NULL REFERENCES post(id) ON DELETE CASCADE,
    tag_id  INTEGER NOT NULL REFERENCES tag(id) ON DELETE CASCADE,
    PRIMARY KEY (post_id, tag_id)
);

CREATE TABLE IF NOT EXISTS queue (
    id        INTEGER PRIMARY KEY,
    blog_id   INTEGER NOT NULL REFERENCES blog(id) ON DELETE CASCADE,
    position  INTEGER,
    added_utc INTEGER
);

-- Full-text search across post text. Externally-contented FTS5 mirrored from `post`.
CREATE VIRTUAL TABLE IF NOT EXISTS post_fts USING fts5(
    title, body, tags_text,
    content='post', content_rowid='id'
);

CREATE TRIGGER IF NOT EXISTS post_ai AFTER INSERT ON post BEGIN
    INSERT INTO post_fts(rowid, title, body, tags_text)
    VALUES (new.id, new.title, new.body, new.tags_text);
END;
CREATE TRIGGER IF NOT EXISTS post_ad AFTER DELETE ON post BEGIN
    INSERT INTO post_fts(post_fts, rowid, title, body, tags_text)
    VALUES ('delete', old.id, old.title, old.body, old.tags_text);
END;
CREATE TRIGGER IF NOT EXISTS post_au AFTER UPDATE ON post BEGIN
    INSERT INTO post_fts(post_fts, rowid, title, body, tags_text)
    VALUES ('delete', old.id, old.title, old.body, old.tags_text);
    INSERT INTO post_fts(rowid, title, body, tags_text)
    VALUES (new.id, new.title, new.body, new.tags_text);
END;
