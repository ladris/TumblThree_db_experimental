"""Crawler abstraction.

A :class:`Crawler` turns a blog into posts + files. It is handed a
:class:`CrawlContext` giving it database access (its own connection, owned by
the worker thread), the per-blog de-dup index, a progress callback, and a
cancellation flag. Real platform crawlers (Tumblr, Twitter, Bluesky, …) will
subclass this; the network/auth/rate-limit machinery slots in behind it.
"""
from __future__ import annotations

import threading
from dataclasses import dataclass, field
from typing import Callable, Optional

from ..db import Database, FileIndex, Repository
from ..db.models import Blog


@dataclass
class CrawlEvent:
    blog_id: int
    type: str           # "progress" | "done" | "error"
    message: str = ""
    downloaded: int = 0
    duplicates: int = 0
    total: Optional[int] = None

    def as_dict(self) -> dict:
        return {
            "blog_id": self.blog_id,
            "type": self.type,
            "message": self.message,
            "downloaded": self.downloaded,
            "duplicates": self.duplicates,
            "total": self.total,
        }


@dataclass
class CrawlContext:
    db: Database
    repo: Repository
    blog: Blog
    files: FileIndex
    cancel: threading.Event
    progress: Callable[[CrawlEvent], None]
    counts: dict = field(default_factory=lambda: {"downloaded": 0, "duplicates": 0})

    def report(self, message: str = "", total: Optional[int] = None) -> None:
        self.progress(
            CrawlEvent(
                blog_id=self.blog.id,
                type="progress",
                message=message,
                downloaded=self.counts["downloaded"],
                duplicates=self.counts["duplicates"],
                total=total,
            )
        )

    @property
    def cancelled(self) -> bool:
        return self.cancel.is_set()


class Crawler:
    """Base class. Subclasses implement :meth:`crawl`."""

    blog_type: str = ""

    def crawl(self, ctx: CrawlContext) -> None:  # pragma: no cover - abstract
        raise NotImplementedError
