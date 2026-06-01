"""Application configuration and filesystem paths.

Personal/localhost deployment: a single SQLite database plus a media tree, by
default under the user's data directory, overridable via environment variables
so tests (and power users) can point somewhere else.
"""
from __future__ import annotations

import os
from dataclasses import dataclass, field
from pathlib import Path


def _default_data_dir() -> Path:
    env = os.environ.get("TUMBLTHREE_DATA_DIR")
    if env:
        return Path(env).expanduser()
    base = os.environ.get("XDG_DATA_HOME")
    if base:
        return Path(base) / "tumblthree"
    return Path.home() / ".local" / "share" / "tumblthree"


@dataclass
class Config:
    """Runtime configuration. Construct via :meth:`load` or directly in tests."""

    data_dir: Path = field(default_factory=_default_data_dir)
    host: str = "127.0.0.1"
    port: int = 5000
    # crawl/download tuning
    concurrent_connections: int = 8
    # default per-host requests/second ceiling
    rate_limit_per_sec: float = 4.0

    @property
    def db_path(self) -> Path:
        return self.data_dir / "tumblthree.db"

    @property
    def media_dir(self) -> Path:
        return self.data_dir / "media"

    def ensure_dirs(self) -> None:
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.media_dir.mkdir(parents=True, exist_ok=True)

    @classmethod
    def load(cls) -> "Config":
        cfg = cls()
        if "TUMBLTHREE_HOST" in os.environ:
            cfg.host = os.environ["TUMBLTHREE_HOST"]
        if "TUMBLTHREE_PORT" in os.environ:
            cfg.port = int(os.environ["TUMBLTHREE_PORT"])
        return cfg
