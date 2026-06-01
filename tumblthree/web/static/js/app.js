// Live crawl progress over Server-Sent Events.
const TT = {
  _source: null,
  _ensureSource() {
    if (this._source) return this._source;
    this._source = new EventSource("/events");
    return this._source;
  },

  // Update progress UI on a blog detail page.
  liveProgress(blogId) {
    const box = document.getElementById("progress");
    if (!box) return;
    const bar = box.querySelector(".progress-bar span");
    const msg = box.querySelector(".progress-msg");
    const src = this._ensureSource();
    src.onmessage = (e) => {
      const ev = JSON.parse(e.data);
      if (ev.blog_id !== blogId) return;
      box.hidden = false;
      if (ev.total) {
        const pct = Math.min(100, Math.round((ev.downloaded + ev.duplicates) / ev.total * 100));
        bar.style.width = pct + "%";
      }
      msg.textContent = ev.message;
      document.querySelectorAll("[data-field]").forEach((el) => {
        const f = el.getAttribute("data-field");
        if (ev[f] !== undefined && ev[f] !== null && (f !== "total" || ev.total)) el.textContent = ev[f];
      });
      if (ev.type === "done" || ev.type === "error") {
        msg.textContent = ev.message;
        bar.style.width = "100%";
      }
    };
  },

  // Click-to-zoom gallery lightbox.
  lightbox() {
    const box = document.getElementById("lightbox");
    if (!box) return;
    const img = box.querySelector("img");
    const cap = box.querySelector(".lb-caption");
    document.querySelectorAll(".grid img[data-full]").forEach((el) => {
      el.addEventListener("click", () => {
        img.src = el.getAttribute("data-full");
        cap.textContent = el.getAttribute("data-caption") || "";
        box.hidden = false;
      });
    });
    box.addEventListener("click", () => { box.hidden = true; img.src = ""; });
    document.addEventListener("keydown", (e) => {
      if (e.key === "Escape") { box.hidden = true; img.src = ""; }
    });
  },

  // Light-touch: reflect crawl activity badges on list pages.
  liveBadges() {
    const src = this._ensureSource();
    src.onmessage = (e) => {
      const ev = JSON.parse(e.data);
      const badge = document.querySelector(`.badge[data-blog="${ev.blog_id}"]`);
      if (ev.type === "done" && badge) badge.remove();
    };
  },
};
window.TT = TT;
