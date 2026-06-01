"""HTTP routes: onboarding, dashboard, blogs, crawl + SSE, search, settings."""
from __future__ import annotations

import json
import queue
from urllib.parse import urlparse

from flask import (
    Blueprint, Response, current_app, flash, redirect,
    render_template, request, stream_with_context, url_for,
)

from ..db.models import Blog, BlogType
from ..importer import LegacyLibrary, import_library

bp = Blueprint("main", __name__)


def services():
    return current_app.config["SERVICES"]


def parse_blog_url(url: str, blog_type: BlogType) -> tuple[str, str]:
    """Best-effort (name, normalized_url) from a pasted URL."""
    url = url.strip()
    parsed = urlparse(url if "//" in url else "https://" + url)
    host = parsed.netloc.lower()
    path = parsed.path.strip("/")
    name = ""
    if host.endswith("tumblr.com") and host not in ("www.tumblr.com", "tumblr.com"):
        name = host.split(".")[0]
    elif host in ("twitter.com", "x.com", "www.twitter.com") and path:
        name = path.split("/")[0]
    elif "bsky" in host and path:
        name = path.split("/")[-1]
    if not name:
        name = path.split("/")[0] if path else host.split(".")[0]
    return name or url, (url if "//" in url else "https://" + url)


# ----- onboarding -----------------------------------------------------------
@bp.route("/setup", methods=["GET", "POST"])
def setup():
    svc = services()
    if request.method == "POST":
        action = request.form.get("action")
        if action == "import":
            path = request.form.get("path", "").strip()
            lib = LegacyLibrary(path)
            if not lib.is_valid():
                flash(f"No TumblThree library found at: {path}", "error")
                return redirect(url_for("main.setup"))
            summary = import_library(svc.db, path)
            svc.mark_initialized()
            flash(
                f"Imported {summary.blogs} blogs and {summary.files} de-dup records."
                + (f" {len(summary.errors)} errors." if summary.errors else ""),
                "success",
            )
            return redirect(url_for("main.dashboard"))
        else:  # fresh
            svc.mark_initialized()
            flash("Started a fresh library.", "success")
            return redirect(url_for("main.dashboard"))
    return render_template("setup.html")


# ----- dashboard ------------------------------------------------------------
@bp.route("/")
def dashboard():
    svc = services()
    if not svc.initialized and not svc.repo.list_blogs():
        return redirect(url_for("main.setup"))
    return render_template(
        "dashboard.html",
        stats=svc.repo.stats(),
        blogs=svc.repo.list_blogs(),
        recent=svc.repo.recent_posts(limit=12),
        running=svc.crawls.running_ids(),
    )


# ----- blogs ----------------------------------------------------------------
@bp.route("/blogs")
def blogs():
    svc = services()
    return render_template(
        "blogs.html", blogs=svc.repo.list_blogs(), running=svc.crawls.running_ids()
    )


@bp.route("/blogs/add", methods=["POST"])
def add_blog():
    svc = services()
    url = request.form.get("url", "").strip()
    blog_type = BlogType.parse(request.form.get("blog_type", "tumblr"))
    if not url:
        flash("Please enter a URL.", "error")
        return redirect(url_for("main.blogs"))
    name, norm = parse_blog_url(url, blog_type)
    blog = svc.repo.add_blog(Blog(name=name, blog_type=blog_type, url=norm))
    flash(f"Added blog '{blog.name}'.", "success")
    return redirect(url_for("main.blog_detail", blog_id=blog.id))


@bp.route("/blogs/<int:blog_id>")
def blog_detail(blog_id: int):
    svc = services()
    blog = svc.repo.get_blog(blog_id)
    if blog is None:
        flash("Blog not found.", "error")
        return redirect(url_for("main.blogs"))
    return render_template(
        "blog_detail.html",
        blog=blog,
        recent=svc.repo.recent_posts(blog_id=blog_id, limit=24),
        running=svc.crawls.is_running(blog_id),
    )


@bp.route("/blogs/<int:blog_id>/delete", methods=["POST"])
def delete_blog(blog_id: int):
    svc = services()
    svc.repo.delete_blog(blog_id)
    flash("Blog removed.", "success")
    return redirect(url_for("main.blogs"))


# ----- crawl ----------------------------------------------------------------
@bp.route("/blogs/<int:blog_id>/crawl", methods=["POST"])
def start_crawl(blog_id: int):
    svc = services()
    started = svc.crawls.start(blog_id)
    flash("Crawl started." if started else "Crawl already running.", "info")
    return redirect(url_for("main.blog_detail", blog_id=blog_id))


@bp.route("/blogs/<int:blog_id>/stop", methods=["POST"])
def stop_crawl(blog_id: int):
    services().crawls.stop(blog_id)
    flash("Stopping crawl…", "info")
    return redirect(url_for("main.blog_detail", blog_id=blog_id))


@bp.route("/events")
def events():
    """Server-Sent Events stream of crawl progress."""
    bus = services().crawls.bus
    q = bus.subscribe()

    @stream_with_context
    def stream():
        try:
            yield "retry: 2000\n\n"
            while True:
                try:
                    event = q.get(timeout=15)
                    yield f"data: {json.dumps(event.as_dict())}\n\n"
                except queue.Empty:
                    yield ": keep-alive\n\n"
        finally:
            bus.unsubscribe(q)

    return Response(stream(), mimetype="text/event-stream",
                    headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"})


# ----- search ---------------------------------------------------------------
@bp.route("/search")
def search():
    svc = services()
    q = request.args.get("q", "").strip()
    results = svc.repo.search_posts(q) if q else []
    return render_template("search.html", q=q, results=results)


# ----- settings -------------------------------------------------------------
@bp.route("/settings", methods=["GET", "POST"])
def settings():
    svc = services()
    if request.method == "POST":
        for key in ("concurrent_connections", "rate_limit_per_sec"):
            if key in request.form:
                svc.repo.set_setting(key, request.form[key])
        flash("Settings saved.", "success")
        return redirect(url_for("main.settings"))
    return render_template(
        "settings.html",
        config=svc.config,
        values={
            "concurrent_connections": svc.repo.get_setting(
                "concurrent_connections", str(svc.config.concurrent_connections)
            ),
            "rate_limit_per_sec": svc.repo.get_setting(
                "rate_limit_per_sec", str(svc.config.rate_limit_per_sec)
            ),
        },
    )
