"""Concurrent media download engine.

Crawlers ``submit()`` :class:`DownloadItem`s; a thread pool downloads them
concurrently. Each job:

  1. checks the per-blog de-dup index (skips + counts a duplicate on a hit),
  2. streams the file to ``media/<blog>/<filename>`` (atomic via a .part file
     handled by the HTTP client),
  3. records it in the ``file`` table and updates the in-memory de-dup index,
  4. emits a thread-safe progress tally.

Text/metadata posts do **not** go through here — the crawler writes those
straight to the ``post`` table.
"""
from __future__ import annotations

import re
import threading
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from pathlib import Path
from typing import Optional, TYPE_CHECKING

from ..net.http import HttpError

if TYPE_CHECKING:
    from ..crawl.base import CrawlContext
    from ..net.http import HttpClient

_UNSAFE = re.compile(r'[<>:"/\\|?*\x00-\x1f]')


def safe_name(name: str) -> str:
    name = _UNSAFE.sub("_", name).strip().strip(".")
    return name or "file"


@dataclass
class DownloadItem:
    url: str
    filename: str
    post_id: Optional[int] = None
    original_link: Optional[str] = None
    kind: str = "photo"   # photo | video | audio | thumbnail


class Downloader:
    def __init__(self, ctx: "CrawlContext", http: "HttpClient", media_dir: Path,
                 max_workers: int = 8):
        self.ctx = ctx
        self.http = http
        self.blog_dir = Path(media_dir) / safe_name(ctx.blog.name)
        self.pool = ThreadPoolExecutor(max_workers=max(1, max_workers),
                                       thread_name_prefix="dl")
        self._futures: list = []
        self._submit_lock = threading.Lock()

    def submit(self, item: DownloadItem) -> None:
        with self._submit_lock:
            self._futures.append(self.pool.submit(self._run, item))

    def _run(self, item: DownloadItem) -> None:
        ctx = self.ctx
        if ctx.cancelled:
            return
        # de-dup: original link first (a different size of an already-grabbed image)
        if ctx.files.exists(item.url, check_original_first=bool(item.original_link)) or (
            item.original_link and ctx.files.exists(item.original_link, check_original_first=True)
        ):
            ctx.tally(duplicates=1, message=f"Skipped (duplicate) {item.filename}")
            return

        dest = self.blog_dir / safe_name(item.filename)
        try:
            size = self.http.download_to(item.url, dest)
        except HttpError as exc:
            ctx.report(f"Failed {item.filename}: {exc}")
            return
        except Exception as exc:  # noqa: BLE001 - one bad file shouldn't kill the crawl
            ctx.report(f"Error {item.filename}: {exc}")
            return

        ctx.files.add(
            item.url,
            original_link=item.original_link,
            filename=item.filename,
            post_id=item.post_id,
            size_bytes=size,
        )
        ctx.tally(downloaded=1, message=f"Downloaded {item.filename}")

    def join(self) -> None:
        """Wait for all submitted downloads to finish."""
        with self._submit_lock:
            futures = list(self._futures)
        for fut in futures:
            try:
                fut.result()
            except Exception:  # noqa: BLE001 - already reported in _run
                pass
        self.pool.shutdown(wait=True)
