"""Session acquisition providers.

Each returns a :class:`Session`. Three mechanisms, in increasing capability:
  * cookie import (cookies pasted from the user's own browser),
  * API login (Bluesky app password -> JWT),
  * interactive Playwright login (optional dependency) for platforms whose login
    involves redirects / 2FA / CAPTCHAs that a paste can't satisfy.
"""
from __future__ import annotations

from typing import Optional

from .cookies import parse_cookie_string
from .session import Session
from ..net.http import HttpClient

BLUESKY_PDS = "https://bsky.social"


def session_from_cookies(platform: str, cookie_text: str, label: str = "") -> Session:
    cookies = parse_cookie_string(cookie_text)
    if not cookies:
        raise ValueError("No cookies could be parsed from the provided text.")
    return Session(platform=platform, cookies=cookies, label=label or "imported cookies")


def bluesky_login(http: HttpClient, handle: str, app_password: str,
                  pds: str = BLUESKY_PDS) -> Session:
    """Log in to Bluesky with an app password (AT Protocol createSession).

    Returns a session carrying a Bearer access token plus the refresh token and
    DID for later refresh. This is the most stable auth path of any platform.
    """
    handle = handle.strip().lstrip("@")
    resp = http.post_json(
        f"{pds}/xrpc/com.atproto.server.createSession",
        {"identifier": handle, "password": app_password},
    )
    access = resp.get("accessJwt")
    if not access:
        raise ValueError("Bluesky login failed: no access token returned.")
    return Session(
        platform="bluesky",
        headers={"Authorization": f"Bearer {access}"},
        data={
            "did": resp.get("did", ""),
            "handle": resp.get("handle", handle),
            "refreshJwt": resp.get("refreshJwt", ""),
            "pds": pds,
        },
        label=resp.get("handle", handle),
    )


def bluesky_refresh(http: HttpClient, session: Session) -> Session:
    """Refresh a Bluesky access token using the stored refresh token."""
    refresh = session.data.get("refreshJwt")
    pds = session.data.get("pds", BLUESKY_PDS)
    if not refresh:
        raise ValueError("No refresh token stored; please log in again.")
    resp = http.post_json(
        f"{pds}/xrpc/com.atproto.server.refreshSession",
        {},
        headers={"Authorization": f"Bearer {refresh}"},
    )
    access = resp.get("accessJwt")
    if not access:
        raise ValueError("Bluesky token refresh failed.")
    session.headers["Authorization"] = f"Bearer {access}"
    session.data["refreshJwt"] = resp.get("refreshJwt", refresh)
    return session


def playwright_available() -> bool:
    try:
        import playwright  # noqa: F401
        return True
    except ImportError:
        return False


def interactive_login(platform: str, login_url: str, *, success_url_contains: str = "",
                      timeout_sec: int = 300) -> Session:
    """Open a real browser, let the user log in, then capture the session.

    Requires the optional 'playwright' extra (``pip install playwright`` +
    ``playwright install chromium``). Captures cookies and localStorage. This is
    the most capable acquirer: it handles 2FA, CAPTCHAs and OAuth redirects that
    cookie-paste/API login cannot.
    """
    if not playwright_available():
        raise RuntimeError(
            "Interactive login needs Playwright. Install it with "
            "`pip install playwright` then `playwright install chromium`, "
            "or use cookie import instead."
        )
    from playwright.sync_api import sync_playwright  # type: ignore

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=False)
        context = browser.new_context()
        page = context.new_page()
        page.goto(login_url)
        # Wait until the user has logged in (URL changes / success marker / timeout).
        try:
            if success_url_contains:
                page.wait_for_url(f"**{success_url_contains}**", timeout=timeout_sec * 1000)
            else:
                page.wait_for_timeout(timeout_sec * 1000)
        except Exception:  # noqa: BLE001 - capture whatever state exists at timeout
            pass
        cookies = {c["name"]: c["value"] for c in context.cookies()}
        try:
            storage = page.evaluate(
                "() => Object.fromEntries(Object.entries(window.localStorage))"
            )
        except Exception:  # noqa: BLE001
            storage = {}
        browser.close()

    return Session(platform=platform, cookies=cookies,
                   data={"localStorage": storage}, label="interactive login")
