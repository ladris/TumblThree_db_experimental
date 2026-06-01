import threading

from tumblthree.crawl.base import CrawlContext
from tumblthree.download.downloader import Downloader, DownloadItem, safe_name
from tumblthree.db import Blog, BlogType, FileIndex, Repository
from tumblthree.net import FakeHttpClient


def _ctx(db, tmp_path, http, events=None):
    repo = Repository(db)
    blog = repo.add_blog(Blog(name="b", blog_type=BlogType.tumblr, url="https://b.tumblr.com"))
    events = events if events is not None else []
    ctx = CrawlContext(
        db=db, repo=repo, blog=blog, files=FileIndex(db, blog.id),
        cancel=threading.Event(), progress=events.append,
        media_dir=tmp_path / "media", http=http,
    )
    ctx.downloader = Downloader(ctx, http, tmp_path / "media", max_workers=4)
    return ctx, repo, blog


def test_safe_name():
    assert safe_name("a/b:c*.jpg") == "a_b_c_.jpg"
    assert safe_name("") == "file"


def test_download_writes_file_and_records(db, tmp_path):
    http = FakeHttpClient(blobs={"http://x/1.jpg": b"\xff\xd8imagedata"})
    ctx, repo, blog = _ctx(db, tmp_path, http)
    ctx.downloader.submit(DownloadItem(url="http://x/1.jpg", filename="1.jpg"))
    ctx.downloader.join()

    dest = tmp_path / "media" / "b" / "1.jpg"
    assert dest.exists() and dest.read_bytes() == b"\xff\xd8imagedata"
    assert ctx.counts["downloaded"] == 1
    assert repo.stats()["files"] == 1
    assert FileIndex(db, blog.id).exists("http://x/1.jpg")


def test_download_dedup_skips_second_time(db, tmp_path):
    http = FakeHttpClient(blobs={"http://x/1.jpg": b"data"})
    ctx, repo, blog = _ctx(db, tmp_path, http)
    ctx.downloader.submit(DownloadItem(url="http://x/1.jpg", filename="1.jpg"))
    ctx.downloader.join()

    # fresh downloader on a new context, same DB -> should see the duplicate
    ctx2, _, _ = _ctx(db, tmp_path, http)
    ctx2.downloader.submit(DownloadItem(url="http://x/1.jpg", filename="1.jpg"))
    ctx2.downloader.join()
    assert ctx2.counts["duplicates"] == 1
    assert ctx2.counts["downloaded"] == 0


def test_download_missing_url_is_reported_not_fatal(db, tmp_path):
    http = FakeHttpClient(blobs={})
    events = []
    ctx, repo, blog = _ctx(db, tmp_path, http, events)
    ctx.downloader.submit(DownloadItem(url="http://x/missing.jpg", filename="m.jpg"))
    ctx.downloader.join()
    assert ctx.counts["downloaded"] == 0
    assert repo.stats()["files"] == 0
    assert any("Failed" in e.message or "Error" in e.message for e in events)
