"""A network-free crawler used to exercise the full pipeline and the live UI.

It fabricates a handful of posts and files, writing them through the same
repository/de-dup paths a real crawler uses, and emitting progress events. This
lets the web UI, SSE progress, and persistence all be developed and tested
before the real platform crawlers are ported.
"""
from __future__ import annotations

import time

from .base import CrawlContext, Crawler
from ..db.models import Post


class DemoCrawler(Crawler):
    blog_type = "demo"

    def __init__(self, count: int = 12, delay: float = 0.05):
        self.count = count
        self.delay = delay

    def crawl(self, ctx: CrawlContext) -> None:
        ctx.set_total(self.count)
        ctx.report(f"Starting crawl of {ctx.blog.name}")
        for i in range(1, self.count + 1):
            if ctx.cancelled:
                ctx.report("Cancelled")
                return
            time.sleep(self.delay)

            remote_id = f"{ctx.blog.id}-{i}"
            link = f"https://example/{ctx.blog.name}/{i}.jpg"

            if ctx.files.exists(link):
                ctx.tally(duplicates=1, message=f"Skipped duplicate {i}")
                continue

            ctx.repo.add_post(
                Post(
                    blog_id=ctx.blog.id,
                    post_type="photo" if i % 3 else "text",
                    remote_id=remote_id,
                    posted_utc=int(time.time()) - (self.count - i) * 3600,
                    url=f"https://example/{ctx.blog.name}/post/{i}",
                    title=f"Post {i}",
                    body=f"Demo post {i} for {ctx.blog.name} — sample searchable text.",
                    tags_text="demo, sample",
                ),
                tags=["demo", "sample"],
            )
            ctx.files.add(link, filename=f"{i}.jpg", size_bytes=1024 * i)
            ctx.tally(downloaded=1, posts=1, message=f"Processed post {i}/{self.count}")
        # Final blog counters are persisted once by the crawl worker.
