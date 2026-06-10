"""HTTP client abstraction.

A small protocol so crawlers/downloaders depend on an interface, not on httpx
directly — which keeps them fully testable offline (see :class:`FakeHttpClient`).
The real :class:`HttpxClient` adds retries with backoff and per-host rate
limiting.
"""
from __future__ import annotations

import time
from pathlib import Path
from typing import Optional, Protocol

from .ratelimit import RateLimiter

DEFAULT_UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/124.0 Safari/537.36 TumblThree/0.1"
)


class HttpError(Exception):
    def __init__(self, message: str, status: Optional[int] = None):
        super().__init__(message)
        self.status = status


class HttpClient(Protocol):
    def get_text(self, url: str) -> str: ...
    def download_to(self, url: str, dest: Path) -> int: ...
    def post_json(self, url: str, payload: dict, headers: Optional[dict] = None) -> dict: ...


class HttpxClient:
    """Real client backed by httpx (imported lazily so tests need no network)."""

    def __init__(
        self,
        rate: Optional[RateLimiter] = None,
        *,
        cookies: Optional[dict] = None,
        headers: Optional[dict] = None,
        user_agent: str = DEFAULT_UA,
        timeout: float = 30.0,
        max_retries: int = 3,
    ):
        import httpx  # local import keeps httpx optional for unit tests

        self._httpx = httpx
        self.rate = rate or RateLimiter()
        self.max_retries = max_retries
        default_headers = {"User-Agent": user_agent}
        if headers:
            default_headers.update(headers)
        self.client = httpx.Client(
            headers=default_headers,
            timeout=timeout,
            follow_redirects=True,
            cookies=cookies or {},
        )

    def close(self) -> None:
        self.client.close()

    def _retry_sleep(self, attempt: int) -> None:
        time.sleep(min(16.0, 2 ** attempt))

    def get_text(self, url: str) -> str:
        last: Exception | None = None
        for attempt in range(self.max_retries + 1):
            self.rate.acquire(url)
            try:
                resp = self.client.get(url)
                if resp.status_code == 429 or resp.status_code >= 500:
                    raise HttpError(f"HTTP {resp.status_code}", resp.status_code)
                if resp.status_code >= 400:
                    raise HttpError(f"HTTP {resp.status_code}", resp.status_code)
                return resp.text
            except (self._httpx.TransportError, HttpError) as exc:
                last = exc
                status = getattr(exc, "status", None)
                if status and 400 <= status < 500 and status != 429:
                    raise
                if attempt < self.max_retries:
                    self._retry_sleep(attempt)
        raise HttpError(f"GET {url} failed: {last}")

    def post_json(self, url: str, payload: dict, headers: Optional[dict] = None) -> dict:
        last: Exception | None = None
        for attempt in range(self.max_retries + 1):
            self.rate.acquire(url)
            try:
                resp = self.client.post(url, json=payload, headers=headers or {})
                if resp.status_code == 429 or resp.status_code >= 500:
                    raise HttpError(f"HTTP {resp.status_code}", resp.status_code)
                if resp.status_code >= 400:
                    raise HttpError(f"HTTP {resp.status_code}: {resp.text[:200]}", resp.status_code)
                return resp.json()
            except (self._httpx.TransportError, HttpError) as exc:
                last = exc
                status = getattr(exc, "status", None)
                if status and 400 <= status < 500 and status != 429:
                    raise
                if attempt < self.max_retries:
                    self._retry_sleep(attempt)
        raise HttpError(f"POST {url} failed: {last}")

    def download_to(self, url: str, dest: Path) -> int:
        dest = Path(dest)
        dest.parent.mkdir(parents=True, exist_ok=True)
        tmp = dest.with_suffix(dest.suffix + ".part")
        last: Exception | None = None
        for attempt in range(self.max_retries + 1):
            self.rate.acquire(url)
            try:
                size = 0
                with self.client.stream("GET", url) as resp:
                    if resp.status_code >= 400:
                        raise HttpError(f"HTTP {resp.status_code}", resp.status_code)
                    with tmp.open("wb") as fh:
                        for chunk in resp.iter_bytes(65536):
                            fh.write(chunk)
                            size += len(chunk)
                tmp.replace(dest)
                return size
            except (self._httpx.TransportError, HttpError) as exc:
                last = exc
                status = getattr(exc, "status", None)
                if status and 400 <= status < 500 and status != 429:
                    raise
                if attempt < self.max_retries:
                    self._retry_sleep(attempt)
            finally:
                if tmp.exists():
                    try:
                        tmp.unlink()
                    except OSError:
                        pass
        raise HttpError(f"download {url} failed: {last}")


class FakeHttpClient:
    """In-memory client for tests. Maps URLs to text pages and byte blobs."""

    def __init__(self, pages: Optional[dict] = None, blobs: Optional[dict] = None,
                 posts: Optional[dict] = None):
        self.pages = pages or {}
        self.blobs = blobs or {}
        self.posts = posts or {}     # url -> response dict (or callable(payload)->dict)
        self.requested: list[str] = []
        self.posted: list[tuple] = []

    def get_text(self, url: str) -> str:
        self.requested.append(url)
        if url not in self.pages:
            raise HttpError(f"404 {url}", 404)
        return self.pages[url]

    def post_json(self, url: str, payload: dict, headers: Optional[dict] = None) -> dict:
        self.posted.append((url, payload))
        if url not in self.posts:
            raise HttpError(f"404 {url}", 404)
        resp = self.posts[url]
        return resp(payload) if callable(resp) else resp

    def download_to(self, url: str, dest: Path) -> int:
        self.requested.append(url)
        if url not in self.blobs:
            raise HttpError(f"404 {url}", 404)
        data = self.blobs[url]
        dest = Path(dest)
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(data)
        return len(data)
