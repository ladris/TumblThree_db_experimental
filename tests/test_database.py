from tumblthree.db import Repository, Blog, BlogType, Post


def test_add_and_get_blog(db):
    repo = Repository(db)
    blog = repo.add_blog(Blog(name="foo", blog_type=BlogType.tumblr, url="https://foo.tumblr.com"))
    assert blog.id is not None
    got = repo.get_blog(blog.id)
    assert got.name == "foo"
    assert got.blog_type is BlogType.tumblr


def test_blog_unique_conflict_returns_existing(db):
    repo = Repository(db)
    a = repo.add_blog(Blog(name="dup", blog_type=BlogType.tumblr))
    b = repo.add_blog(Blog(name="dup", blog_type=BlogType.tumblr))
    assert a.id == b.id
    assert len(repo.list_blogs()) == 1


def test_post_dedup_and_fts(db):
    repo = Repository(db)
    blog = repo.add_blog(Blog(name="b", blog_type=BlogType.tumblr))
    p1 = repo.add_post(Post(blog_id=blog.id, post_type="text", remote_id="1",
                            body="a glittering aurora over the fjord", tags_text="nature"))
    p1b = repo.add_post(Post(blog_id=blog.id, post_type="text", remote_id="1", body="dupe"))
    assert p1 == p1b  # same unique key -> same row
    repo.add_post(Post(blog_id=blog.id, post_type="text", remote_id="2", body="city lights at night"))

    hits = repo.search_posts("aurora")
    assert [h["remote_id"] for h in hits] == ["1"]
    assert repo.search_posts("lights")[0]["remote_id"] == "2"
    assert repo.search_posts("nonexistentword") == []


def test_search_tolerates_malformed_input(db):
    repo = Repository(db)
    blog = repo.add_blog(Blog(name="b", blog_type=BlogType.tumblr))
    repo.add_post(Post(blog_id=blog.id, post_type="text", remote_id="1",
                       body="hello glorious world"))
    # none of these may raise; bare operators/punctuation become plain tokens
    for q in ["hello", "a OR", '"unbalanced', "foo*(", "NEAR(", "()", "   "]:
        repo.search_posts(q)
    assert repo.search_posts("glorious")[0]["remote_id"] == "1"
    assert repo.search_posts("hello world")  # multi-token AND


def test_add_post_ex_reports_creation(db):
    repo = Repository(db)
    blog = repo.add_blog(Blog(name="b", blog_type=BlogType.tumblr))
    pid1, created1 = repo.add_post_ex(Post(blog_id=blog.id, post_type="text", remote_id="9", body="x"))
    pid2, created2 = repo.add_post_ex(Post(blog_id=blog.id, post_type="text", remote_id="9", body="x"))
    assert created1 is True and created2 is False and pid1 == pid2


def test_progress_counters(db):
    repo = Repository(db)
    blog = repo.add_blog(Blog(name="c", blog_type=BlogType.tumblr))
    repo.update_blog_progress(blog.id, downloaded=5, duplicates=2, total=20)
    got = repo.get_blog(blog.id)
    assert (got.downloaded_items, got.duplicates, got.total_posts) == (5, 2, 20)


def test_delete_cascades_posts(db):
    repo = Repository(db)
    blog = repo.add_blog(Blog(name="d", blog_type=BlogType.tumblr))
    repo.add_post(Post(blog_id=blog.id, post_type="text", remote_id="1", body="x"))
    repo.delete_blog(blog.id)
    assert repo.stats()["posts"] == 0
    assert repo.search_posts("x") == []  # FTS row cleaned up by trigger
