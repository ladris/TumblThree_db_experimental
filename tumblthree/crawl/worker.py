"""Background crawl manager + in-process event bus for live (SSE) progress.

Crawls run on worker threads, each with its **own** SQLite connection (sqlite3
connections aren't shareable across threads; WAL lets the web connection read
concurrently). Within a crawl, media downloads are fanned out across a thread
pool by the :class:`~tumblthree.download.downloader.Downloader`. Progress events
are published to any number of subscribers (e.g. SSE streams).
"""
from __future__ import annotations

import queue
import threading
import time
from pathlib import Path
from typing import Optional

from .base import CrawlContext, CrawlEvent, Crawler
from .demo import DemoCrawler
from .tumblr import TumblrCrawler
from ..db import Database, FileIndex, Repository
from ..download.downloader import Downloader
from ..net import HttpxClient, RateLimiter


def crawler_for(blog_type: str) -> Crawler:
    """Resolve a crawler for a blog type.

    Public Tumblr blogs use the real (no-auth) crawler; the other platforms and
    the auth-gated Tumblr variants fall back to the demo crawler until ported.
    """
    if blog_type == "tumblr":
        return TumblrCrawler()
    return DemoCrawler()


class EventBus:
    def __init__(self) -> None:
        self._subscribers: list[queue.Queue] = []
        self._lock = threading.Lock()

    def subscribe(self) -> queue.Queue:
        q: queue.Queue = queue.Queue()
        with self._lock:
            self._subscribers.append(q)
        return q

    def unsubscribe(self, q: queue.Queue) -> None:
        with self._lock:
            if q in self._subscribers:
                self._subscribers.remove(q)

    def publish(self, event: CrawlEvent) -> None:
        with self._lock:
            subs = list(self._subscribers)
        for q in subs:
            q.put(event)


class CrawlManager:
    def __init__(self, db_path: str | Path, media_dir: str | Path):
        self.db_path = str(db_path)
        self.media_dir = Path(media_dir)
        self.bus = EventBus()
        self._threads: dict[int, threading.Thread] = {}
        self._cancels: dict[int, threading.Event] = {}
        self._lock = threading.Lock()

    def is_running(self, blog_id: int) -> bool:
        with self._lock:
            t = self._threads.get(blog_id)
            return bool(t and t.is_alive())

    def running_ids(self) -> list[int]:
        with self._lock:
            return [bid for bid, t in self._threads.items() if t.is_alive()]

    def start(self, blog_id: int) -> bool:
        with self._lock:
            if blog_id in self._threads and self._threads[blog_id].is_alive():
                return False
            cancel = threading.Event()
            self._cancels[blog_id] = cancel
            t = threading.Thread(target=self._run, args=(blog_id, cancel), daemon=True)
            self._threads[blog_id] = t
            t.start()
            return True

    def stop(self, blog_id: int) -> None:
        with self._lock:
            ev = self._cancels.get(blog_id)
        if ev:
            ev.set()

    def _run(self, blog_id: int, cancel: threading.Event) -> None:
        db = Database(self.db_path, apply_schema=False)
        http: Optional[HttpxClient] = None
        try:
            repo = Repository(db)
            blog = repo.get_blog(blog_id)
            if blog is None:
                self.bus.publish(CrawlEvent(blog_id, "error", "Blog not found"))
                return

            rate = float(repo.get_setting("rate_limit_per_sec", "4") or 4)
            workers = int(repo.get_setting("concurrent_connections", "8") or 8)
            http = HttpxClient(RateLimiter(rate))

            ctx = CrawlContext(
                db=db, repo=repo, blog=blog,
                files=FileIndex(db, blog_id),
                cancel=cancel, progress=self.bus.publish,
                media_dir=self.media_dir, http=http,
            )
            ctx.downloader = Downloader(ctx, http, self.media_dir, max_workers=workers)

            crawler = crawler_for(blog.blog_type.value)
            crawler.crawl(ctx)
            ctx.downloader.join()

            repo.update_blog_progress(
                blog_id,
                downloaded=ctx.counts["downloaded"],
                duplicates=ctx.counts["duplicates"],
                total=ctx.total, last_crawl=int(time.time()),
            )
            self.bus.publish(
                CrawlEvent(
                    blog_id, "done",
                    f"Finished: {ctx.counts['downloaded']} new, {ctx.counts['duplicates']} duplicates",
                    downloaded=ctx.counts["downloaded"],
                    duplicates=ctx.counts["duplicates"],
                    total=ctx.total,
                )
            )
        except Exception as exc:  # noqa: BLE001
            self.bus.publish(CrawlEvent(blog_id, "error", str(exc)))
        finally:
            if http is not None:
                http.close()
            db.close()
