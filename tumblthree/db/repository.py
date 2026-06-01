"""Data access for blogs, posts, settings, search and stats."""
from __future__ import annotations

import json
import sqlite3
import time
from typing import Optional

from .database import Database
from .models import Blog, BlogType, Post

_BLOG_COLS = (
    "id, name, blog_type, url, download_location, download_photo, download_video,"
    " download_audio, download_text, total_posts, downloaded_items, duplicates,"
    " date_added, last_crawl, last_id, rating, notes, settings_json"
)


def _row_to_blog(r: sqlite3.Row) -> Blog:
    return Blog(
        id=r["id"],
        name=r["name"],
        blog_type=BlogType.parse(r["blog_type"]),
        url=r["url"] or "",
        download_location=r["download_location"] or "",
        download_photo=bool(r["download_photo"]),
        download_video=bool(r["download_video"]),
        download_audio=bool(r["download_audio"]),
        download_text=bool(r["download_text"]),
        total_posts=r["total_posts"],
        downloaded_items=r["downloaded_items"],
        duplicates=r["duplicates"],
        date_added=r["date_added"],
        last_crawl=r["last_crawl"],
        last_id=r["last_id"],
        rating=r["rating"],
        notes=r["notes"] or "",
        settings=json.loads(r["settings_json"]) if r["settings_json"] else {},
    )


class Repository:
    def __init__(self, db: Database):
        self.db = db

    # ----- settings ---------------------------------------------------------
    def get_setting(self, key: str, default: Optional[str] = None) -> Optional[str]:
        with self.db.lock:
            row = self.db.conn.execute(
                "SELECT value FROM setting WHERE key=?", (key,)
            ).fetchone()
        return row["value"] if row else default

    def set_setting(self, key: str, value: str) -> None:
        with self.db.lock:
            self.db.conn.execute(
                "INSERT INTO setting(key, value) VALUES(?,?)"
                " ON CONFLICT(key) DO UPDATE SET value=excluded.value",
                (key, value),
            )

    # ----- blogs ------------------------------------------------------------
    def add_blog(self, blog: Blog) -> Blog:
        if blog.date_added is None:
            blog.date_added = int(time.time())
        with self.db.lock:
            cur = self.db.conn.execute(
                "INSERT INTO blog(name, blog_type, url, download_location, download_photo,"
                " download_video, download_audio, download_text, total_posts, downloaded_items,"
                " duplicates, date_added, last_crawl, last_id, rating, notes, settings_json)"
                " VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)"
                " ON CONFLICT(name, blog_type) DO NOTHING",
                (
                    blog.name, blog.blog_type.value, blog.url, blog.download_location,
                    int(blog.download_photo), int(blog.download_video),
                    int(blog.download_audio), int(blog.download_text),
                    blog.total_posts, blog.downloaded_items, blog.duplicates,
                    blog.date_added, blog.last_crawl, blog.last_id, blog.rating, blog.notes,
                    json.dumps(blog.settings) if blog.settings else None,
                ),
            )
            if cur.lastrowid and cur.rowcount:
                blog.id = cur.lastrowid
                return blog
        existing = self.get_blog_by_name(blog.name, blog.blog_type)
        return existing or blog

    def get_blog(self, blog_id: int) -> Optional[Blog]:
        with self.db.lock:
            row = self.db.conn.execute(
                f"SELECT {_BLOG_COLS} FROM blog WHERE id=?", (blog_id,)
            ).fetchone()
        return _row_to_blog(row) if row else None

    def get_blog_by_name(self, name: str, blog_type: BlogType) -> Optional[Blog]:
        with self.db.lock:
            row = self.db.conn.execute(
                f"SELECT {_BLOG_COLS} FROM blog WHERE name=? AND blog_type=?",
                (name, blog_type.value),
            ).fetchone()
        return _row_to_blog(row) if row else None

    def list_blogs(self) -> list[Blog]:
        with self.db.lock:
            rows = self.db.conn.execute(
                f"SELECT {_BLOG_COLS} FROM blog ORDER BY name COLLATE NOCASE"
            ).fetchall()
        return [_row_to_blog(r) for r in rows]

    def update_blog_progress(self, blog_id: int, *, downloaded: int = 0,
                             duplicates: int = 0, total: Optional[int] = None,
                             last_crawl: Optional[int] = None) -> None:
        sets = ["downloaded_items = downloaded_items + ?", "duplicates = duplicates + ?"]
        params: list = [downloaded, duplicates]
        if total is not None:
            sets.append("total_posts = ?")
            params.append(total)
        if last_crawl is not None:
            sets.append("last_crawl = ?")
            params.append(last_crawl)
        params.append(blog_id)
        with self.db.lock:
            self.db.conn.execute(
                f"UPDATE blog SET {', '.join(sets)} WHERE id=?", params
            )

    def delete_blog(self, blog_id: int) -> None:
        with self.db.lock:
            self.db.conn.execute("DELETE FROM blog WHERE id=?", (blog_id,))

    # ----- posts ------------------------------------------------------------
    def add_post(self, post: Post, tags: Optional[list[str]] = None) -> int:
        with self.db.lock:
            cur = self.db.conn.execute(
                "INSERT INTO post(blog_id, remote_id, post_type, posted_utc, url, title,"
                " body, tags_text, raw_json)"
                " VALUES(?,?,?,?,?,?,?,?,?)"
                " ON CONFLICT(blog_id, remote_id, post_type) DO NOTHING",
                (
                    post.blog_id, post.remote_id, post.post_type, post.posted_utc,
                    post.url, post.title, post.body, post.tags_text, post.raw_json,
                ),
            )
            post_id = cur.lastrowid if cur.rowcount else None
            if post_id is None:
                row = self.db.conn.execute(
                    "SELECT id FROM post WHERE blog_id=? AND remote_id=? AND post_type=?",
                    (post.blog_id, post.remote_id, post.post_type),
                ).fetchone()
                post_id = row["id"] if row else None
            if post_id and tags:
                self._link_tags(post_id, tags)
        return post_id

    def _link_tags(self, post_id: int, tags: list[str]) -> None:
        for name in tags:
            name = name.strip()
            if not name:
                continue
            self.db.conn.execute(
                "INSERT INTO tag(name) VALUES(?) ON CONFLICT(name) DO NOTHING", (name,)
            )
            row = self.db.conn.execute("SELECT id FROM tag WHERE name=?", (name,)).fetchone()
            if row:
                self.db.conn.execute(
                    "INSERT OR IGNORE INTO post_tag(post_id, tag_id) VALUES(?,?)",
                    (post_id, row["id"]),
                )

    def recent_posts(self, blog_id: Optional[int] = None, limit: int = 50) -> list[sqlite3.Row]:
        sql = (
            "SELECT p.*, b.name AS blog_name, b.blog_type AS blog_type"
            " FROM post p JOIN blog b ON b.id = p.blog_id"
        )
        params: list = []
        if blog_id is not None:
            sql += " WHERE p.blog_id=?"
            params.append(blog_id)
        sql += " ORDER BY p.posted_utc DESC NULLS LAST, p.id DESC LIMIT ?"
        params.append(limit)
        with self.db.lock:
            return self.db.conn.execute(sql, params).fetchall()

    def search_posts(self, query: str, limit: int = 100) -> list[sqlite3.Row]:
        """Full-text search across post title/body/tags."""
        if not query.strip():
            return []
        with self.db.lock:
            return self.db.conn.execute(
                "SELECT p.*, b.name AS blog_name, b.blog_type AS blog_type,"
                " snippet(post_fts, 1, '<mark>', '</mark>', ' … ', 12) AS snippet"
                " FROM post_fts"
                " JOIN post p ON p.id = post_fts.rowid"
                " JOIN blog b ON b.id = p.blog_id"
                " WHERE post_fts MATCH ?"
                " ORDER BY rank LIMIT ?",
                (query, limit),
            ).fetchall()

    # ----- media / files ----------------------------------------------------
    def list_media(self, blog_id: Optional[int] = None, *, limit: int = 60,
                   offset: int = 0) -> list[sqlite3.Row]:
        sql = (
            "SELECT f.id, f.blog_id, f.filename, f.link, f.size_bytes, f.post_id,"
            " b.name AS blog_name, p.url AS post_url, p.body AS post_body"
            " FROM file f JOIN blog b ON b.id = f.blog_id"
            " LEFT JOIN post p ON p.id = f.post_id"
            " WHERE f.filename IS NOT NULL"
        )
        params: list = []
        if blog_id is not None:
            sql += " AND f.blog_id = ?"
            params.append(blog_id)
        sql += " ORDER BY f.downloaded_utc DESC, f.id DESC LIMIT ? OFFSET ?"
        params += [limit, offset]
        with self.db.lock:
            return self.db.conn.execute(sql, params).fetchall()

    def count_media(self, blog_id: Optional[int] = None) -> int:
        sql = "SELECT COUNT(*) AS n FROM file WHERE filename IS NOT NULL"
        params: list = []
        if blog_id is not None:
            sql += " AND blog_id = ?"
            params.append(blog_id)
        with self.db.lock:
            return self.db.conn.execute(sql, params).fetchone()["n"]

    # ----- stats ------------------------------------------------------------
    def stats(self) -> dict:
        with self.db.lock:
            c = self.db.conn
            blogs = c.execute("SELECT COUNT(*) AS n FROM blog").fetchone()["n"]
            posts = c.execute("SELECT COUNT(*) AS n FROM post").fetchone()["n"]
            files = c.execute("SELECT COUNT(*) AS n FROM file").fetchone()["n"]
            size = c.execute("SELECT COALESCE(SUM(size_bytes),0) AS s FROM file").fetchone()["s"]
        return {"blogs": blogs, "posts": posts, "files": files, "bytes": size}
