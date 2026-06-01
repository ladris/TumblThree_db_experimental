import json
import threading

import pytest

from tumblthree.crawl.base import CrawlContext
from tumblthree.crawl.tumblr import TumblrCrawler, parse_read_json, strip_jsonp
from tumblthree.download.downloader import Downloader
from tumblthree.db import Blog, BlogType, FileIndex, Repository
from tumblthree.net import FakeHttpClient


def _wrap(obj: dict) -> str:
    return "var tumblr_api_read = " + json.dumps(obj) + ";"


def test_strip_jsonp_and_parse():
    text = _wrap({"tumblelog": {"name": "b"}, "posts-total": 2, "posts": [{"id": "1"}]})
    assert strip_jsonp(text).startswith("{")
    parsed = parse_read_json(text)
    assert parsed["total"] == 2 and parsed["name"] == "b" and len(parsed["posts"]) == 1


@pytest.fixture
def tumblr_pages():
    base = "https://b.tumblr.com/api/read/json?num=2"
    page0 = _wrap({
        "tumblelog": {"name": "b"}, "posts-total": 3,
        "posts": [
            {"id": "1", "type": "photo", "unix-timestamp": 1000, "url": "https://b/p/1",
             "tags": ["art"], "photo-url-1280": "https://b/img1_1280.jpg", "photos": [],
             "photo-caption": "a caption"},
            {"id": "2", "type": "regular", "unix-timestamp": 1001,
             "regular-title": "Hello", "regular-body": "world searchable unicorn"},
        ],
    })
    page1 = _wrap({
        "tumblelog": {"name": "b"}, "posts-total": 3,
        "posts": [
            {"id": "3", "type": "photo", "unix-timestamp": 1002,
             "photo-url-1280": "https://b/set_a_1280.jpg",
             "photos": [{"photo-url-1280": "https://b/set_a_1280.jpg"},
                        {"photo-url-1280": "https://b/set_b_1280.jpg"}]},
        ],
    })
    pages = {base: page0, base + "&start=2": page1}
    blobs = {
        "https://b/img1_1280.jpg": b"img1",
        "https://b/set_a_1280.jpg": b"seta",
        "https://b/set_b_1280.jpg": b"setb",
    }
    return pages, blobs


def _run_crawl(db, tmp_path, http):
    repo = Repository(db)
    blog = repo.add_blog(Blog(name="b", blog_type=BlogType.tumblr, url="https://b.tumblr.com"))
    ctx = CrawlContext(
        db=db, repo=repo, blog=blog, files=FileIndex(db, blog.id),
        cancel=threading.Event(), progress=lambda e: None,
        media_dir=tmp_path / "media", http=http,
    )
    ctx.downloader = Downloader(ctx, http, tmp_path / "media", max_workers=4)
    TumblrCrawler(page_size=2).crawl(ctx)
    ctx.downloader.join()
    return ctx, repo, blog


def test_full_crawl(db, tmp_path, tumblr_pages):
    pages, blobs = tumblr_pages
    http = FakeHttpClient(pages=pages, blobs=blobs)
    ctx, repo, blog = _run_crawl(db, tmp_path, http)

    # 3 posts saved (photo, text, photoset-as-one-photo-post)
    assert repo.stats()["posts"] == 3
    # 3 image files downloaded (img1 + set_a + set_b)
    assert repo.stats()["files"] == 3
    for fn in ("img1_1280.jpg", "set_a_1280.jpg", "set_b_1280.jpg"):
        assert (tmp_path / "media" / "b" / fn).exists()

    # text post is full-text searchable
    hits = repo.search_posts("unicorn")
    assert hits and hits[0]["remote_id"] == "2"
    # tags captured
    assert "art" in (repo.recent_posts(blog_id=blog.id)[-1]["tags_text"] or "")


def test_recrawl_dedups(db, tmp_path, tumblr_pages):
    pages, blobs = tumblr_pages
    http = FakeHttpClient(pages=pages, blobs=blobs)
    _run_crawl(db, tmp_path, http)
    files_after_first = Repository(db).stats()["files"]

    ctx2, repo, _ = _run_crawl(db, tmp_path, http)
    assert repo.stats()["files"] == files_after_first  # nothing new downloaded
    assert ctx2.counts["duplicates"] == 3
    assert ctx2.counts["downloaded"] == 1  # only the text post counts as "saved"


def test_photo_toggle_off(db, tmp_path, tumblr_pages):
    pages, blobs = tumblr_pages
    http = FakeHttpClient(pages=pages, blobs=blobs)
    repo = Repository(db)
    blog = repo.add_blog(Blog(name="b", blog_type=BlogType.tumblr, url="https://b.tumblr.com",
                              download_photo=False))
    ctx = CrawlContext(
        db=db, repo=repo, blog=blog, files=FileIndex(db, blog.id),
        cancel=threading.Event(), progress=lambda e: None,
        media_dir=tmp_path / "media", http=http,
    )
    ctx.downloader = Downloader(ctx, http, tmp_path / "media", max_workers=2)
    TumblrCrawler(page_size=2).crawl(ctx)
    ctx.downloader.join()
    assert repo.stats()["files"] == 0          # photos skipped
    assert repo.search_posts("unicorn")        # text still saved
