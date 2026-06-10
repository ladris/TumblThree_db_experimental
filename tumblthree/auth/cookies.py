"""Parse cookies pasted by the user.

Two common shapes are supported:
  * a request ``Cookie:`` header value  ->  "a=1; b=2; c=3"
  * the Netscape ``cookies.txt`` export  ->  tab-separated lines
"""
from __future__ import annotations


def parse_cookie_header(value: str) -> dict:
    """Parse a 'name=value; name2=value2' Cookie header string."""
    out: dict[str, str] = {}
    for part in value.split(";"):
        part = part.strip()
        if not part or "=" not in part:
            continue
        name, _, val = part.partition("=")
        name = name.strip()
        if name:
            out[name] = val.strip()
    return out


def parse_netscape(text: str) -> dict:
    """Parse a Netscape cookies.txt export (one cookie per tab-separated line)."""
    out: dict[str, str] = {}
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        fields = line.split("\t")
        if len(fields) >= 7:
            out[fields[5]] = fields[6]
    return out


def parse_cookie_string(text: str) -> dict:
    """Auto-detect the paste format (Netscape vs header) and parse it."""
    text = (text or "").strip()
    if not text:
        return {}
    # Netscape exports are tab-separated with many columns per line.
    if "\t" in text and any(len(ln.split("\t")) >= 7 for ln in text.splitlines()):
        return parse_netscape(text)
    return parse_cookie_header(text.replace("\n", "; "))
