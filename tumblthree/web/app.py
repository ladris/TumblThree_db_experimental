"""Flask application factory."""
from __future__ import annotations

import os
from typing import Optional

from flask import Flask

from ..config import Config
from ..crawl import CrawlManager
from ..db import Database, Repository


class Services:
    """Holds the long-lived application objects (DB, repo, crawl manager)."""

    def __init__(self, config: Config):
        config.ensure_dirs()
        self.config = config
        self.db = Database(config.db_path)
        self.repo = Repository(self.db)
        self.crawls = CrawlManager(config.db_path, config.media_dir)

    @property
    def initialized(self) -> bool:
        return self.repo.get_setting("initialized") == "1"

    def mark_initialized(self) -> None:
        self.repo.set_setting("initialized", "1")


def create_app(config: Optional[Config] = None) -> Flask:
    app = Flask(__name__)
    app.secret_key = os.environ.get("TUMBLTHREE_SECRET", "dev-localhost-secret")
    app.config["SERVICES"] = Services(config or Config.load())

    from .routes import bp
    app.register_blueprint(bp)

    @app.template_filter("filesize")
    def _filesize(num: Optional[int]) -> str:
        n = float(num or 0)
        for unit in ("B", "KB", "MB", "GB", "TB"):
            if n < 1024 or unit == "TB":
                return f"{n:.0f} {unit}" if unit == "B" else f"{n:.1f} {unit}"
            n /= 1024
        return f"{n:.1f} TB"

    return app
