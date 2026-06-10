"""Authentication / session layer.

A platform-agnostic :class:`Session` (cookies + headers + opaque data) that
crawlers attach to their HTTP client. Sessions are acquired by *providers*
(cookie/string import, Bluesky app-password, optional Playwright interactive
login) and persisted in the ``session`` table.
"""
from .session import Session, SessionStore
from .cookies import parse_cookie_string, parse_cookie_header
from .providers import (
    session_from_cookies, bluesky_login, bluesky_refresh,
    interactive_login, playwright_available,
)

__all__ = [
    "Session", "SessionStore", "parse_cookie_string", "parse_cookie_header",
    "session_from_cookies", "bluesky_login", "bluesky_refresh",
    "interactive_login", "playwright_available",
]
