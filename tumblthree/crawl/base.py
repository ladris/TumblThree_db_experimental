"""Crawler abstraction.

A :class:`Crawler` turns a blog into posts + files. It is handed a
:class:`CrawlContext` giving it database access (its own connection, owned by
the worker thread), the per-blog de-dup index, an HTTP client, a concurrent
downloader, a thread-safe progress/counter API, and a cancellation flag.
"""
from __future__ import annotations

import threading
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Optional, TYPE_CHECKING

from ..db import Database, FileIndex, Repository
from ..db.models import Blog

if TYPE_CHECKING:  # avoid import cycles at runtime
    from ..net.http import HttpClient
    from ..download.downloader import Downloader


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
    media_dir: Optional[Path] = None
    http: Optional["HttpClient"] = None
    downloader: Optional["Downloader"] = None
    total: Optional[int] = None
    counts: dict = field(default_factory=lambda: {"downloaded": 0, "duplicates": 0, "posts": 0})
    _lock: threading.Lock = field(default_factory=threading.Lock, repr=False)

    @property
    def cancelled(self) -> bool:
        return self.cancel.is_set()

    def set_total(self, total: Optional[int]) -> None:
        with self._lock:
            self.total = total

    def report(self, message: str = "") -> None:
        with self._lock:
            self._emit(message)

    def tally(self, *, downloaded: int = 0, duplicates: int = 0, posts: int = 0,
              message: str = "") -> None:
        """Thread-safe counter update + progress emission (safe to call from
        multiple download threads concurrently)."""
        with self._lock:
            self.counts["downloaded"] += downloaded
            self.counts["duplicates"] += duplicates
            self.counts["posts"] += posts
            self._emit(message)

    def _emit(self, message: str) -> None:
        self.progress(
            CrawlEvent(
                blog_id=self.blog.id,
                type="progress",
                message=message,
                downloaded=self.counts["downloaded"],
                duplicates=self.counts["duplicates"],
                total=self.total,
            )
        )


class Crawler:
    """Base class. Subclasses implement :meth:`crawl`."""

    blog_type: str = ""

    def crawl(self, ctx: CrawlContext) -> None:  # pragma: no cover - abstract
        raise NotImplementedError
