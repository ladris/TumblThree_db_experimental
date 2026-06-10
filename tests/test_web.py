import time


def test_redirects_to_setup_when_empty(client):
    resp = client.get("/")
    assert resp.status_code == 302
    assert "/setup" in resp.headers["Location"]


def test_fresh_start(client):
    resp = client.post("/setup", data={"action": "fresh"}, follow_redirects=True)
    assert resp.status_code == 200
    assert b"Dashboard" in resp.data


def test_import_flow(client, legacy_library):
    resp = client.post("/setup", data={"action": "import", "path": str(legacy_library)},
                       follow_redirects=True)
    assert b"Imported 2 blogs" in resp.data
    assert client.get("/").status_code == 200
    assert b"alpha" in client.get("/blogs").data


def test_add_blog_and_detail(client):
    client.post("/setup", data={"action": "fresh"})
    resp = client.post("/blogs/add", data={"url": "https://cool.tumblr.com", "blog_type": "tumblr"},
                       follow_redirects=True)
    assert b"cool" in resp.data
    assert b"Start crawl" in resp.data


def test_crawl_runs_and_persists(client, app):
    # blog_type 'newtumbl' routes to the offline demo crawler (no network).
    client.post("/setup", data={"action": "fresh"})
    client.post("/blogs/add", data={"url": "https://demo.newtumbl.com", "blog_type": "newtumbl"})
    svc = app.config["SERVICES"]
    blog = svc.repo.list_blogs()[0]

    client.post(f"/blogs/{blog.id}/crawl")
    for _ in range(100):
        if not svc.crawls.is_running(blog.id):
            break
        time.sleep(0.05)
    assert not svc.crawls.is_running(blog.id)

    updated = svc.repo.get_blog(blog.id)
    assert updated.downloaded_items > 0
    assert svc.repo.stats()["posts"] > 0


def test_gallery_and_media_serving(client, app):
    client.post("/setup", data={"action": "fresh"})
    svc = app.config["SERVICES"]
    from tumblthree.db import Blog, BlogType, FileIndex
    from tumblthree.download.downloader import safe_name

    blog = svc.repo.add_blog(Blog(name="picblog", blog_type=BlogType.tumblr))
    # place a real file on disk + record it
    media_dir = svc.config.media_dir / safe_name(blog.name)
    media_dir.mkdir(parents=True, exist_ok=True)
    (media_dir / "cat.jpg").write_bytes(b"\xff\xd8\xff catbytes")
    FileIndex(svc.db, blog.id).add("http://x/cat.jpg", filename="cat.jpg", size_bytes=10)

    page = client.get("/gallery")
    assert page.status_code == 200
    assert b"cat.jpg" in page.data
    assert b"picblog" in page.data

    served = client.get(f"/media/{blog.id}/cat.jpg")
    assert served.status_code == 200
    assert served.data == b"\xff\xd8\xff catbytes"

    # traversal / missing -> 404
    assert client.get(f"/media/{blog.id}/nope.jpg").status_code == 404


def test_cookie_import_via_settings(client, app):
    client.post("/setup", data={"action": "fresh"})
    resp = client.post("/auth/cookies",
                       data={"platform": "tumblr", "cookies": "sid=abc; pfg=xyz"},
                       follow_redirects=True)
    assert b"Saved 2 cookies" in resp.data
    svc = app.config["SERVICES"]
    s = svc.sessions.get("tumblr")
    assert s.cookies == {"sid": "abc", "pfg": "xyz"}
    # settings page shows it active and never leaks the value
    page = client.get("/settings").data
    assert b"active" in page
    assert b"abc" not in page

    client.post("/auth/tumblr/clear", follow_redirects=True)
    assert svc.sessions.get("tumblr") is None


def test_search_endpoint(client, app):
    client.post("/setup", data={"action": "fresh"})
    svc = app.config["SERVICES"]
    from tumblthree.db import Blog, BlogType, Post
    b = svc.repo.add_blog(Blog(name="s", blog_type=BlogType.tumblr))
    svc.repo.add_post(Post(blog_id=b.id, post_type="text", remote_id="1",
                           body="a magnificent peculiar walrus"))
    resp = client.get("/search?q=walrus")
    assert b"walrus" in resp.data
