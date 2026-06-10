"""Session model + persistence.

A :class:`Session` is the single artifact every acquisition method produces —
whether cookies were pasted, fetched by a Bluesky login, or captured by an
interactive Playwright browser. Crawlers consume it uniformly.
"""
from __future__ import annotations

import json
import time
from dataclasses import dataclass, field
from typing import Optional

from ..db.database import Database


@dataclass
class Session:
    platform: str
    cookies: dict = field(default_factory=dict)
    headers: dict = field(default_factory=dict)
    data: dict = field(default_factory=dict)
    label: str = ""
    updated_utc: Optional[int] = None

    @property
    def is_empty(self) -> bool:
        return not self.cookies and not self.headers and not self.data

    def merged_headers(self, base: Optional[dict] = None) -> dict:
        out = dict(base or {})
        out.update(self.headers)
        return out


class SessionStore:
    """Loads/saves :class:`Session` rows. Treat contents as secrets."""

    def __init__(self, db: Database):
        self.db = db

    def get(self, platform: str) -> Optional[Session]:
        with self.db.lock:
            row = self.db.conn.execute(
                "SELECT platform, label, cookies, headers, data, updated_utc"
                " FROM session WHERE platform=?",
                (platform,),
            ).fetchone()
        if not row:
            return None
        return Session(
            platform=row["platform"],
            label=row["label"] or "",
            cookies=json.loads(row["cookies"]) if row["cookies"] else {},
            headers=json.loads(row["headers"]) if row["headers"] else {},
            data=json.loads(row["data"]) if row["data"] else {},
            updated_utc=row["updated_utc"],
        )

    def save(self, session: Session) -> None:
        session.updated_utc = int(time.time())
        with self.db.lock:
            self.db.conn.execute(
                "INSERT INTO session(platform, label, cookies, headers, data, updated_utc)"
                " VALUES(?,?,?,?,?,?)"
                " ON CONFLICT(platform) DO UPDATE SET"
                " label=excluded.label, cookies=excluded.cookies, headers=excluded.headers,"
                " data=excluded.data, updated_utc=excluded.updated_utc",
                (
                    session.platform, session.label,
                    json.dumps(session.cookies), json.dumps(session.headers),
                    json.dumps(session.data), session.updated_utc,
                ),
            )

    def delete(self, platform: str) -> None:
        with self.db.lock:
            self.db.conn.execute("DELETE FROM session WHERE platform=?", (platform,))

    def status(self) -> dict[str, dict]:
        """Non-secret summary for the settings UI (no cookie/token values)."""
        with self.db.lock:
            rows = self.db.conn.execute(
                "SELECT platform, label, updated_utc,"
                " (cookies IS NOT NULL AND cookies != '{}') AS has_cookies,"
                " (headers IS NOT NULL AND headers != '{}') AS has_headers"
                " FROM session"
            ).fetchall()
        return {
            r["platform"]: {
                "label": r["label"] or "",
                "updated_utc": r["updated_utc"],
                "has_cookies": bool(r["has_cookies"]),
                "has_headers": bool(r["has_headers"]),
            }
            for r in rows
        }
