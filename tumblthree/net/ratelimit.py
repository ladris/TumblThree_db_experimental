"""A simple thread-safe token-bucket rate limiter, keyed per host.

Crawling politely matters: each host gets its own bucket so one busy blog can't
starve another, and we never exceed the configured requests/second.
"""
from __future__ import annotations

import threading
import time
from urllib.parse import urlparse


class _Bucket:
    __slots__ = ("rate", "capacity", "tokens", "last")

    def __init__(self, rate: float, capacity: float):
        self.rate = rate
        self.capacity = capacity
        self.tokens = capacity
        self.last = time.monotonic()

    def take(self) -> float:
        """Consume one token, returning how long the caller should sleep first."""
        now = time.monotonic()
        self.tokens = min(self.capacity, self.tokens + (now - self.last) * self.rate)
        self.last = now
        if self.tokens >= 1:
            self.tokens -= 1
            return 0.0
        wait = (1 - self.tokens) / self.rate
        self.tokens = 0
        self.last = now + wait
        return wait


class RateLimiter:
    def __init__(self, rate_per_sec: float = 4.0, burst: float | None = None):
        self.rate = max(0.1, rate_per_sec)
        self.burst = burst if burst is not None else max(1.0, rate_per_sec)
        self._buckets: dict[str, _Bucket] = {}
        self._lock = threading.Lock()

    def acquire(self, url: str) -> None:
        host = urlparse(url).netloc or url
        with self._lock:
            bucket = self._buckets.get(host)
            if bucket is None:
                bucket = _Bucket(self.rate, self.burst)
                self._buckets[host] = bucket
            wait = bucket.take()
        if wait > 0:
            time.sleep(wait)
