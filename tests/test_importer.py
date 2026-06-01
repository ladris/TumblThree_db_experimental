from tumblthree.db import Repository, FileIndex, BlogType
from tumblthree.importer import LegacyLibrary, import_library


def test_library_detection(legacy_library):
    assert LegacyLibrary(legacy_library).is_valid()
    assert LegacyLibrary(legacy_library / "Index").is_valid()
    assert not LegacyLibrary(legacy_library.parent / "nope").is_valid()


def test_import_blogs_and_files(db, legacy_library):
    summary = import_library(db, legacy_library)
    assert summary.blogs == 2
    assert summary.files == 4
    assert not summary.errors

    repo = Repository(db)
    names = {b.name: b for b in repo.list_blogs()}
    assert set(names) == {"alpha", "beta"}
    assert names["alpha"].blog_type is BlogType.tumblr
    assert names["beta"].blog_type is BlogType.twitter
    assert names["alpha"].total_posts == 10


def test_imported_dedup_records_prevent_redownload(db, legacy_library):
    import_library(db, legacy_library)
    repo = Repository(db)
    alpha = repo.get_blog_by_name("alpha", BlogType.tumblr)
    fi = FileIndex(db, alpha.id)
    assert fi.exists("http://a/1.jpg")
    assert fi.exists("http://orig/2.jpg", check_original_first=True)

    beta = repo.get_blog_by_name("beta", BlogType.twitter)
    assert FileIndex(db, beta.id).exists("http://b/x.mp4")  # came from SQLite-format legacy db


def test_reimport_is_idempotent(db, legacy_library):
    import_library(db, legacy_library)
    second = import_library(db, legacy_library)
    assert second.blogs == 0  # both already present
    assert len(Repository(db).list_blogs()) == 2
