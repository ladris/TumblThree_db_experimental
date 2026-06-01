"""De-duplication index.

Ports the design proven in the C# SQLite work: the hot path is a membership
test ("have we already downloaded this URL?") asked once per candidate file.
The common answer is *no*, so we answer it from an in-memory set of 64-bit
FNV-1a hashes (small, no string retention, zero I/O). Only a positive hash hit
touches SQLite to confirm the exact string via the unique index, so a hash
collision can never cause a real download to be skipped.

Writes go through ``INSERT OR IGNORE`` on the ``(blog_id, link)`` unique index,
mirroring the legacy HashSet's "adding an existing link is a no-op" semantics.
"""
from __future__ import annotations

import time
from typing import Iterable, Optional

from .database import Database

_FNV_OFFSET = 0xCBF29CE484222325
_FNV_PRIME = 0x100000001B3
_MASK = 0xFFFFFFFFFFFFFFFF


def fnv1a64(s: str) -> int:
    h = _FNV_OFFSET
    for ch in s:
        c = ord(ch)
        h ^= c & 0xFF
        h = (h * _FNV_PRIME) & _MASK
        h ^= (c >> 8) & 0xFF
        h = (h * _FNV_PRIME) & _MASK
    return h


def _norm_orig(orig: Optional[str], link: str) -> Optional[str]:
    return None if (not orig or orig == link) else orig


class FileIndex:
    """Per-blog de-duplication index backed by the ``file`` table."""

    def __init__(self, db: Database, blog_id: int):
        self.db = db
        self.blog_id = blog_id
        self._links: set[int] = set()
        self._origs: set[int] = set()
        self._load()

    def _load(self) -> None:
        with self.db.lock:
            rows = self.db.conn.execute(
                "SELECT link, original_link FROM file WHERE blog_id=?", (self.blog_id,)
            ).fetchall()
        for r in rows:
            self._links.add(fnv1a64(r["link"]))
            if r["original_link"]:
                self._origs.add(fnv1a64(r["original_link"]))

    def exists(self, link: str, check_original_first: bool = False) -> bool:
        if not link:
            return False
        h = fnv1a64(link)
        with self.db.lock:
            if check_original_first and h in self._origs:
                row = self.db.conn.execute(
                    "SELECT 1 FROM file WHERE blog_id=? AND original_link=? LIMIT 1",
                    (self.blog_id, link),
                ).fetchone()
                if row:
                    return True
            if h in self._links:
                row = self.db.conn.execute(
                    "SELECT 1 FROM file WHERE blog_id=? AND link=? LIMIT 1",
                    (self.blog_id, link),
                ).fetchone()
                if row:
                    return True
        return False

    def exists_anywhere(self, link: str) -> bool:
        """Cross-blog duplicate check (uses the global link index)."""
        if not link:
            return False
        with self.db.lock:
            row = self.db.conn.execute(
                "SELECT 1 FROM file WHERE link=? LIMIT 1", (link,)
            ).fetchone()
        return row is not None

    def add(
        self,
        link: str,
        original_link: Optional[str] = None,
        filename: Optional[str] = None,
        post_id: Optional[int] = None,
        size_bytes: Optional[int] = None,
        content_hash: Optional[str] = None,
    ) -> None:
        if not link:
            return
        orig = _norm_orig(original_link, link)
        fn = None if filename == link else filename
        with self.db.lock:
            self.db.conn.execute(
                "INSERT OR IGNORE INTO file"
                " (blog_id, post_id, link, original_link, filename, size_bytes, content_hash, downloaded_utc)"
                " VALUES (?,?,?,?,?,?,?,?)",
                (self.blog_id, post_id, link, orig, fn, size_bytes, content_hash, int(time.time())),
            )
            self._links.add(fnv1a64(link))
            if orig:
                self._origs.add(fnv1a64(orig))

    def add_many(self, entries: Iterable[tuple]) -> int:
        """Bulk import. Each entry is (link, original_link, filename). Returns count."""
        rows = []
        now = int(time.time())
        new_links: list[int] = []
        new_origs: list[int] = []
        for link, original_link, filename in entries:
            if not link:
                continue
            orig = _norm_orig(original_link, link)
            fn = None if filename == link else filename
            rows.append((self.blog_id, link, orig, fn, now))
            new_links.append(fnv1a64(link))
            if orig:
                new_origs.append(fnv1a64(orig))
        if not rows:
            return 0
        with self.db.lock:
            self.db.conn.execute("BEGIN")
            try:
                self.db.conn.executemany(
                    "INSERT OR IGNORE INTO file"
                    " (blog_id, link, original_link, filename, downloaded_utc)"
                    " VALUES (?,?,?,?,?)",
                    rows,
                )
                self.db.conn.execute("COMMIT")
            except Exception:
                self.db.conn.execute("ROLLBACK")
                raise
            self._links.update(new_links)
            self._origs.update(new_origs)
        return len(rows)
