"""Domain models (plain dataclasses) and the platform enum."""
from __future__ import annotations

import enum
from dataclasses import dataclass, field
from typing import Optional


class BlogType(str, enum.Enum):
    tumblr = "tumblr"
    tmblrpriv = "tmblrpriv"
    tlb = "tlb"
    tumblrsearch = "tumblrsearch"
    tumblrtagsearch = "tumblrtagsearch"
    twitter = "twitter"
    bluesky = "bluesky"
    newtumbl = "newtumbl"

    @classmethod
    def parse(cls, value: str) -> "BlogType":
        try:
            return cls(value)
        except ValueError:
            return cls.tumblr


@dataclass
class Blog:
    name: str
    blog_type: BlogType
    url: str = ""
    id: Optional[int] = None
    download_location: str = ""
    download_photo: bool = True
    download_video: bool = True
    download_audio: bool = True
    download_text: bool = True
    total_posts: int = 0
    downloaded_items: int = 0
    duplicates: int = 0
    date_added: Optional[int] = None
    last_crawl: Optional[int] = None
    last_id: Optional[str] = None
    rating: int = 0
    notes: str = ""
    settings: dict = field(default_factory=dict)


@dataclass
class Post:
    blog_id: int
    post_type: str
    remote_id: str = ""
    posted_utc: Optional[int] = None
    url: str = ""
    title: str = ""
    body: str = ""
    tags_text: str = ""
    raw_json: Optional[str] = None
    id: Optional[int] = None


@dataclass
class FileEntry:
    blog_id: int
    link: str
    original_link: Optional[str] = None
    filename: Optional[str] = None
    post_id: Optional[int] = None
    size_bytes: Optional[int] = None
    content_hash: Optional[str] = None
    downloaded_utc: Optional[int] = None
    id: Optional[int] = None
