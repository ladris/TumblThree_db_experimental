import json
import sqlite3
from pathlib import Path

import pytest

from tumblthree.config import Config
from tumblthree.db import Database


@pytest.fixture
def db(tmp_path):
    d = Database(tmp_path / "test.db")
    yield d
    d.close()


@pytest.fixture
def config(tmp_path):
    return Config(data_dir=tmp_path / "data")


@pytest.fixture
def app(config):
    from tumblthree.web import create_app
    application = create_app(config)
    application.config.update(TESTING=True)
    return application


@pytest.fixture
def client(app):
    return app.test_client()


@pytest.fixture
def legacy_library(tmp_path):
    """A synthetic legacy TumblThree library: one JSON dedup db, one SQLite one."""
    root = tmp_path / "Blogs"
    index = root / "Index"
    index.mkdir(parents=True)

    (index / "alpha.tumblr").write_text(json.dumps(
        {"Name": "alpha", "Url": "https://alpha.tumblr.com", "Posts": 10, "DownloadedItems": 3}))
    (index / "alpha_files.tumblr").write_text(json.dumps({"Name": "alpha", "Entries": [
        {"L": "http://a/1.jpg", "F": "1.jpg"},
        {"L": "http://a/2.jpg", "O": "http://orig/2.jpg", "F": "2.jpg"},
    ]}))

    (index / "beta.twitter").write_text(json.dumps({"Name": "beta", "Url": "https://x.com/beta"}))
    con = sqlite3.connect(str(index / "beta_files.twitter"))
    con.execute("CREATE TABLE meta(k TEXT PRIMARY KEY, v TEXT)")
    con.execute("CREATE TABLE entry(link TEXT NOT NULL, orig TEXT, fn TEXT)")
    con.executemany("INSERT INTO entry(link,orig,fn) VALUES(?,?,?)",
                    [("http://b/x.mp4", None, None), ("http://b/y.mp4", None, "y.mp4")])
    con.commit()
    con.close()
    return root
