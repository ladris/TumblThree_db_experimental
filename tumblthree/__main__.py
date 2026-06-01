"""Run the TumblThree web app:  python -m tumblthree"""
from __future__ import annotations

import webbrowser

from .config import Config
from .web import create_app


def main() -> None:
    config = Config.load()
    config.ensure_dirs()
    app = create_app(config)
    url = f"http://{config.host}:{config.port}/"
    print(f"TumblThree → {url}  (data: {config.data_dir})")
    try:
        webbrowser.open(url)
    except Exception:
        pass
    app.run(host=config.host, port=config.port, threaded=True)


if __name__ == "__main__":
    main()
