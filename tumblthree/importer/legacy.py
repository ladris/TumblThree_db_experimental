"""Import an existing (C#/WPF) TumblThree library into the Python SQLite store.

The legacy app stores, under ``<DownloadLocation>/Index/``:
  * one blog index per blog: ``{Name}.{blogtype}``         (DataContract JSON)
  * one de-duplication database: ``{Name}_files.{blogtype}``
      - either legacy JSON ({"Entries": [{"L":..,"O":..,"F":..}], ...})
      - or the phase-1 SQLite format (an ``entry`` table with link/orig/fn)

This module discovers blogs, imports their metadata, and bulk-loads their
de-dup records so a converted user never re-downloads anything they already
have. Downloaded media files are left in place; ``download_location`` points at
them.
"""
from __future__ import annotations

import json
import sqlite3
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterator, Optional

from ..db import Blog, BlogType, Database, FileIndex, Repository

_BLOG_TYPES = {t.value for t in BlogType}
_SQLITE_MAGIC = b"SQLite format 3\x00"


@dataclass
class ImportSummary:
    blogs: int = 0
    files: int = 0
    skipped: list[str] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)

    def as_dict(self) -> dict:
        return {
            "blogs": self.blogs,
            "files": self.files,
            "skipped": self.skipped,
            "errors": self.errors,
        }


def _is_sqlite(path: Path) -> bool:
    try:
        with path.open("rb") as fh:
            return fh.read(16) == _SQLITE_MAGIC
    except OSError:
        return False


class LegacyLibrary:
    """Locates the Index folder of a legacy library and enumerates its blogs."""

    def __init__(self, root: str | Path):
        self.root = Path(root).expanduser()
        self.index_dir = self._find_index_dir(self.root)

    @staticmethod
    def _find_index_dir(root: Path) -> Optional[Path]:
        if (root / "Index").is_dir():
            return root / "Index"
        if root.name == "Index" and root.is_dir():
            return root
        # the directory itself holds index files?
        if root.is_dir() and any(_blog_type_of(p) and "_files" not in p.name for p in root.glob("*.*")):
            return root
        return None

    def is_valid(self) -> bool:
        return self.index_dir is not None

    def iter_blog_index_files(self) -> Iterator[Path]:
        if not self.index_dir:
            return
        for p in sorted(self.index_dir.glob("*.*")):
            if "_files" in p.name:
                continue
            if _blog_type_of(p):
                yield p


def _blog_type_of(path: Path) -> Optional[str]:
    ext = path.suffix.lstrip(".")
    return ext if ext in _BLOG_TYPES else None


def _read_legacy_entries(path: Path) -> Iterator[tuple]:
    """Yield (link, original_link, filename) from a legacy _files database."""
    if _is_sqlite(path):
        conn = sqlite3.connect(str(path))
        try:
            try:
                rows = conn.execute("SELECT link, orig, fn FROM entry").fetchall()
            except sqlite3.Error:
                rows = []
            for link, orig, fn in rows:
                yield (link, orig, fn if fn is not None else link)
        finally:
            conn.close()
    else:
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
        except (OSError, json.JSONDecodeError):
            return
        for e in data.get("Entries", []) or []:
            link = e.get("L")
            if not link:
                continue
            yield (link, e.get("O"), e.get("F") if e.get("F") is not None else link)


def _load_blog(index_file: Path, media_root: Path) -> Blog:
    blog_type = BlogType.parse(_blog_type_of(index_file) or "tumblr")
    name = index_file.name[: -(len(index_file.suffix))]
    data: dict = {}
    try:
        data = json.loads(index_file.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        pass
    name = data.get("Name") or name
    return Blog(
        name=name,
        blog_type=blog_type,
        url=data.get("Url", "") or "",
        download_location=str(media_root / name),
        total_posts=int(data.get("Posts", data.get("TotalCount", 0)) or 0),
        downloaded_items=int(data.get("DownloadedItems", 0) or 0),
        date_added=None,
        notes=data.get("Notes", "") or "",
        settings={k: v for k, v in data.items() if k.startswith("Download") or k.startswith("Create")},
    )


def import_library(db: Database, root: str | Path) -> ImportSummary:
    """Import every blog + de-dup database found under *root* into *db*."""
    summary = ImportSummary()
    lib = LegacyLibrary(root)
    if not lib.is_valid():
        summary.errors.append(f"No TumblThree 'Index' folder found under {root}")
        return summary

    repo = Repository(db)
    index_dir = lib.index_dir
    media_root = index_dir.parent  # blogs live as siblings of Index

    for index_file in lib.iter_blog_index_files():
        try:
            blog = _load_blog(index_file, media_root)
            if repo.get_blog_by_name(blog.name, blog.blog_type) is not None:
                summary.skipped.append(blog.name)
                continue
            stored = repo.add_blog(blog)
            if stored.id is None:
                summary.skipped.append(blog.name)
                continue
            summary.blogs += 1

            files_db = index_file.with_name(f"{blog.name}_files.{blog.blog_type.value}")
            if files_db.exists():
                fi = FileIndex(db, stored.id)
                count = fi.add_many(_read_legacy_entries(files_db))
                summary.files += count
        except Exception as exc:  # noqa: BLE001 - importer must be resilient per-blog
            summary.errors.append(f"{index_file.name}: {exc}")

    return summary
