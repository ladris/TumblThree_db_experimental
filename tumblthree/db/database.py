"""SQLite connection management.

A thin layer over :mod:`sqlite3` that applies the schema and the performance
PRAGMAs (WAL, etc.). One :class:`Database` owns a connection; it is **not**
shared across threads (sqlite3 connections aren't thread-safe) — the crawl
worker uses its own connection. The Flask request path is single-threaded for a
personal/localhost deployment.
"""
from __future__ import annotations

import sqlite3
import threading
from pathlib import Path

_SCHEMA = (Path(__file__).parent / "schema.sql").read_text(encoding="utf-8")


def connect(db_path: str | Path, *, apply_schema: bool = True) -> sqlite3.Connection:
    conn = sqlite3.connect(
        str(db_path),
        isolation_level=None,          # autocommit off via explicit BEGIN; see Repository
        check_same_thread=False,
        timeout=10.0,
    )
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA journal_mode=WAL")
    conn.execute("PRAGMA synchronous=NORMAL")
    conn.execute("PRAGMA foreign_keys=ON")
    conn.execute("PRAGMA temp_store=MEMORY")
    conn.execute("PRAGMA mmap_size=268435456")
    conn.execute("PRAGMA cache_size=-16384")
    conn.execute("PRAGMA busy_timeout=10000")
    if apply_schema:
        conn.executescript(_SCHEMA)
    return conn


class Database:
    """Owns a connection and serializes access with a lock for safety."""

    def __init__(self, db_path: str | Path, *, apply_schema: bool = True):
        self.path = Path(db_path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.conn = connect(self.path, apply_schema=apply_schema)
        self.lock = threading.RLock()

    def close(self) -> None:
        try:
            self.conn.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        except sqlite3.Error:
            pass
        self.conn.close()

    def __enter__(self) -> "Database":
        return self

    def __exit__(self, *exc) -> None:
        self.close()
