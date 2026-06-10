import pytest

from tumblthree.auth import (
    Session, SessionStore, parse_cookie_string,
    session_from_cookies, bluesky_login, bluesky_refresh,
)
from tumblthree.auth.cookies import parse_netscape
from tumblthree.net import FakeHttpClient


def test_parse_cookie_header():
    assert parse_cookie_string("a=1; b=2; c=3") == {"a": "1", "b": "2", "c": "3"}
    assert parse_cookie_string("  token=abc.def  ") == {"token": "abc.def"}
    assert parse_cookie_string("") == {}


def test_parse_netscape():
    text = "\n".join([
        "# Netscape HTTP Cookie File",
        ".tumblr.com\tTRUE\t/\tTRUE\t0\tsid\tSECRET123",
        ".tumblr.com\tTRUE\t/\tTRUE\t0\tpfg\tval2",
    ])
    parsed = parse_netscape(text)
    assert parsed == {"sid": "SECRET123", "pfg": "val2"}
    # auto-detect routes to netscape parser
    assert parse_cookie_string(text)["sid"] == "SECRET123"


def test_session_store_roundtrip(db):
    store = SessionStore(db)
    assert store.get("tumblr") is None
    store.save(Session(platform="tumblr", cookies={"sid": "x"}, label="me"))
    got = store.get("tumblr")
    assert got.cookies == {"sid": "x"} and got.label == "me"
    assert got.updated_utc is not None

    status = store.status()
    assert status["tumblr"]["has_cookies"] is True
    assert "SECRET" not in str(status)  # status must not leak values

    store.delete("tumblr")
    assert store.get("tumblr") is None


def test_session_from_cookies_rejects_empty():
    with pytest.raises(ValueError):
        session_from_cookies("tumblr", "")


def test_bluesky_login_builds_bearer_session():
    http = FakeHttpClient(posts={
        "https://bsky.social/xrpc/com.atproto.server.createSession": {
            "accessJwt": "ACCESS", "refreshJwt": "REFRESH",
            "did": "did:plc:abc", "handle": "me.bsky.social",
        }
    })
    s = bluesky_login(http, "@me.bsky.social", "app-pass-1234")
    assert s.platform == "bluesky"
    assert s.headers["Authorization"] == "Bearer ACCESS"
    assert s.data["did"] == "did:plc:abc" and s.data["refreshJwt"] == "REFRESH"
    assert s.label == "me.bsky.social"
    # the handle was sent without the leading '@'
    assert http.posted[0][1]["identifier"] == "me.bsky.social"


def test_bluesky_login_failure_raises():
    http = FakeHttpClient(posts={
        "https://bsky.social/xrpc/com.atproto.server.createSession": {"error": "AuthError"}
    })
    with pytest.raises(ValueError):
        bluesky_login(http, "me.bsky.social", "wrong")


def test_bluesky_refresh_updates_token():
    s = Session(platform="bluesky", headers={"Authorization": "Bearer OLD"},
                data={"refreshJwt": "R1", "pds": "https://bsky.social"})
    http = FakeHttpClient(posts={
        "https://bsky.social/xrpc/com.atproto.server.refreshSession": {
            "accessJwt": "NEW", "refreshJwt": "R2",
        }
    })
    bluesky_refresh(http, s)
    assert s.headers["Authorization"] == "Bearer NEW"
    assert s.data["refreshJwt"] == "R2"
