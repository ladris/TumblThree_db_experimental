"""Tumblr public-blog crawler (v1 read API — no authentication required).

Ports the behaviour of the legacy ``TumblrBlogCrawler`` against the classic
``/api/read/json`` endpoint, which returns posts for a public blog without an
API key. Pagination is ``start``/``num``; the response is JSONP wrapped as
``var tumblr_api_read = { ... };``.

This is the no-auth path on purpose: authentication for hidden/liked/search
blogs and the modern API is a separate, later effort.
"""
from __future__ import annotations

import json
import re
from typing import Iterable, Optional

from .base import CrawlContext, Crawler
from ..db.models import Post
from ..download.downloader import DownloadItem

PAGE_SIZE = 50
MAX_PAGES = 10_000  # safety valve

_PHOTO_SIZES = ("1280", "500", "400", "250", "100", "75")
_MP4 = re.compile(r"https?://[^\s\"']+\.mp4")
_AUDIO = re.compile(r"https?://[^\s\"']+\.(?:mp3|m4a)")


def strip_jsonp(text: str) -> str:
    s = text.strip()
    i, j = s.find("{"), s.rfind("}")
    if i == -1 or j == -1:
        raise ValueError("not a Tumblr read-json response")
    return s[i : j + 1]


def parse_read_json(text: str) -> dict:
    data = json.loads(strip_jsonp(text))
    return {
        "name": (data.get("tumblelog") or {}).get("name", ""),
        "total": int(data.get("posts-total", 0) or 0),
        "posts": data.get("posts", []) or [],
    }


def _photo_url(d: dict) -> Optional[str]:
    for size in _PHOTO_SIZES:
        url = d.get(f"photo-url-{size}")
        if url:
            return url
    return None


def _filename_from_url(url: str) -> str:
    return url.split("?")[0].rstrip("/").split("/")[-1] or "file"


def _tags(post: dict) -> list[str]:
    tags = post.get("tags") or []
    return [t for t in tags if isinstance(t, str)]


class TumblrCrawler(Crawler):
    blog_type = "tumblr"

    def __init__(self, page_size: int = PAGE_SIZE):
        self.page_size = page_size

    # -- public API ----------------------------------------------------------
    def crawl(self, ctx: CrawlContext) -> None:
        base = ctx.blog.url.rstrip("/") + "/"
        ctx.report(f"Crawling {ctx.blog.name}…")

        start = 0
        total: Optional[int] = None
        pages = 0
        while pages < MAX_PAGES:
            if ctx.cancelled:
                ctx.report("Cancelled")
                break
            page = self._fetch_page(ctx, base, start)
            if page is None:
                break
            if total is None:
                total = page["total"]
                ctx.set_total(total)
            posts = page["posts"]
            if not posts:
                break
            for post in posts:
                if ctx.cancelled:
                    break
                self._handle_post(ctx, post)
            start += self.page_size
            pages += 1
            if total is not None and start >= total:
                break

    # -- internals -----------------------------------------------------------
    def _fetch_page(self, ctx: CrawlContext, base: str, start: int) -> Optional[dict]:
        url = f"{base}api/read/json?num={self.page_size}"
        if start:
            url += f"&start={start}"
        try:
            text = ctx.http.get_text(url)
            return parse_read_json(text)
        except Exception as exc:  # noqa: BLE001 - report and stop paging
            ctx.report(f"Failed to fetch page at {start}: {exc}")
            return None

    def _handle_post(self, ctx: CrawlContext, post: dict) -> None:
        ptype = post.get("type", "")
        if ptype == "photo":
            self._handle_photo(ctx, post)
        elif ptype == "video":
            self._handle_media(ctx, post, kind="video", toggle=ctx.blog.download_video,
                               extractor=_MP4)
        elif ptype == "audio":
            self._handle_media(ctx, post, kind="audio", toggle=ctx.blog.download_audio,
                               extractor=_AUDIO)
        elif ptype in ("regular", "quote", "link", "conversation", "answer"):
            self._handle_text(ctx, post, ptype)

    def _save_post(self, ctx: CrawlContext, post: dict, post_type: str,
                   *, title: str = "", body: str = "") -> tuple[Optional[int], bool]:
        tags = _tags(post)
        return ctx.repo.add_post_ex(
            Post(
                blog_id=ctx.blog.id,
                remote_id=str(post.get("id", "")),
                post_type=post_type,
                posted_utc=int(post.get("unix-timestamp", 0) or 0),
                url=post.get("url-with-slug") or post.get("url", ""),
                title=title,
                body=body,
                tags_text=", ".join(tags),
                raw_json=json.dumps(post, ensure_ascii=False),
            ),
            tags=tags,
        )

    def _handle_photo(self, ctx: CrawlContext, post: dict) -> None:
        if not ctx.blog.download_photo:
            return
        post_id, _ = self._save_post(ctx, post, "photo", body=post.get("photo-caption", ""))

        photos = post.get("photos") or []
        urls: Iterable[str]
        if len(photos) > 1:
            urls = [u for u in (_photo_url(p) for p in photos) if u]
        else:
            single = _photo_url(post)
            urls = [single] if single else []

        for url in urls:
            ctx.downloader.submit(
                DownloadItem(url=url, filename=_filename_from_url(url),
                             post_id=post_id, kind="photo")
            )

    def _handle_media(self, ctx: CrawlContext, post: dict, *, kind: str,
                      toggle: bool, extractor: re.Pattern) -> None:
        body = post.get(f"{'video' if kind == 'video' else 'audio'}-caption", "")
        post_id, _ = self._save_post(ctx, post, kind, body=body)
        if not toggle:
            return
        blob = " ".join(
            str(post.get(k, "")) for k in (
                f"{kind}-player", f"{kind}-source", f"{kind}-url", f"{kind}-player-500"
            )
        )
        match = extractor.search(blob)
        if not match:
            return
        url = match.group(0)
        ctx.downloader.submit(
            DownloadItem(url=url, filename=_filename_from_url(url), post_id=post_id, kind=kind)
        )

    def _handle_text(self, ctx: CrawlContext, post: dict, ptype: str) -> None:
        if not ctx.blog.download_text:
            return
        title, body = "", ""
        if ptype == "regular":
            title = post.get("regular-title", "") or ""
            body = post.get("regular-body", "") or ""
        elif ptype == "quote":
            title = post.get("quote-text", "") or ""
            body = post.get("quote-source", "") or ""
        elif ptype == "link":
            title = post.get("link-text", "") or ""
            body = (post.get("link-url", "") or "") + "\n" + (post.get("link-description", "") or "")
        elif ptype == "conversation":
            title = post.get("conversation-title", "") or ""
            body = post.get("conversation-text", "") or ""
        elif ptype == "answer":
            title = post.get("question", "") or ""
            body = post.get("answer", "") or ""
        post_type = "text" if ptype == "regular" else ptype
        _, created = self._save_post(ctx, post, post_type, title=title, body=body)
        if created:
            ctx.tally(posts=1, downloaded=1, message=f"Saved {post_type} post")
        else:
            ctx.tally(duplicates=1, message=f"Skipped duplicate {post_type} post")
