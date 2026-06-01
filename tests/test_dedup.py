from tumblthree.db import Repository, FileIndex, Blog, BlogType, fnv1a64


def test_fnv_stable_and_64bit():
    assert fnv1a64("") == 0xCBF29CE484222325
    assert 0 <= fnv1a64("https://example/x.jpg") <= 0xFFFFFFFFFFFFFFFF


def test_exists_add_roundtrip(db):
    repo = Repository(db)
    blog = repo.add_blog(Blog(name="b", blog_type=BlogType.tumblr))
    fi = FileIndex(db, blog.id)
    assert not fi.exists("http://x/1.jpg")
    fi.add("http://x/1.jpg", filename="1.jpg")
    assert fi.exists("http://x/1.jpg")


def test_original_link_dedup(db):
    repo = Repository(db)
    blog = repo.add_blog(Blog(name="b", blog_type=BlogType.tumblr))
    fi = FileIndex(db, blog.id)
    fi.add("http://x/2.jpg", original_link="http://orig/2.jpg", filename="2.jpg")
    assert fi.exists("http://orig/2.jpg", check_original_first=True)
    assert not fi.exists("http://orig/2.jpg", check_original_first=False)


def test_idempotent_add(db):
    repo = Repository(db)
    blog = repo.add_blog(Blog(name="b", blog_type=BlogType.tumblr))
    fi = FileIndex(db, blog.id)
    fi.add("http://x/1.jpg")
    fi.add("http://x/1.jpg")  # no-op due to unique index
    assert repo.stats()["files"] == 1


def test_index_persists_across_instances(db):
    repo = Repository(db)
    blog = repo.add_blog(Blog(name="b", blog_type=BlogType.tumblr))
    FileIndex(db, blog.id).add("http://x/9.jpg")
    fresh = FileIndex(db, blog.id)  # rebuilds the in-memory hash set from disk
    assert fresh.exists("http://x/9.jpg")


def test_cross_blog_dedup(db):
    repo = Repository(db)
    a = repo.add_blog(Blog(name="a", blog_type=BlogType.tumblr))
    b = repo.add_blog(Blog(name="b", blog_type=BlogType.tumblr))
    FileIndex(db, a.id).add("http://shared/pic.jpg")
    assert FileIndex(db, b.id).exists_anywhere("http://shared/pic.jpg")
    assert not FileIndex(db, b.id).exists("http://shared/pic.jpg")  # per-blog scope
