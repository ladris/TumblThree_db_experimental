import requests
import json
import os
import time
from datetime import datetime, timezone
from flask import session
from requests_oauthlib import OAuth2Session
from .models import db, Blog, Post, TextPost, PhotoPost, QuotePost, LinkPost, ChatPost, AudioPost, VideoPost, AnswerPost, Tag, File, TumblrCredential

class TumblrCrawler:
    def __init__(self, blog: Blog, app_context):
        self.blog = blog
        self.app_context = app_context
        self.session = self._get_session()

    def _get_session(self):
        token = session.get('oauth_token')
        if token:
            credential = TumblrCredential.query.first()
            client_id = credential.consumer_key if credential else None
            return OAuth2Session(client_id, token=token)
        else:
            return requests.Session()

    def crawl(self):
        """
        Main method to crawl a Tumblr blog.
        """
        print(f"Starting crawl for blog: {self.blog.name}")
        self.blog.status = 'crawling'
        db.session.commit()

        try:
            self._update_blog_info()
            self._crawl_posts()
            self.blog.last_complete_crawl = datetime.now(timezone.utc)
            self.blog.status = 'finished'
        except Exception as e:
            print(f"An error occurred while crawling {self.blog.name}: {e}")
            self.blog.status = 'error'

        db.session.commit()
        print(f"Finished crawl for blog: {self.blog.name}")

    def _update_blog_info(self):
        """Fetches and updates the blog's info from the API."""
        print(f"Updating blog info for {self.blog.name}")
        api_url = f"https://api.tumblr.com/v2/blog/{self.blog.name}/info"
        params = {}
        if not isinstance(self.session, OAuth2Session):
            credential = TumblrCredential.query.first()
            params['api_key'] = credential.consumer_key if credential else "fuiKNFp9vQFvjLNvx4sUwti4Yb5yGutBN4Xh10LXZhhRKjWlV4"

        try:
            response = self.session.get(api_url, params=params)
            response.raise_for_status()
            data = response.json().get('response', {}).get('blog', {})

            self.blog.uuid = data.get('uuid')
            self.blog.title = data.get('title')
            self.blog.description = data.get('description')
            self.blog.ask = data.get('ask')
            self.blog.ask_anon = data.get('ask_anon')
            self.blog.followed = data.get('followed')
            self.blog.likes = data.get('likes')
            self.blog.is_blocked_from_primary = data.get('is_blocked_from_primary')
            self.blog.posts_total = data.get('posts')
            self.blog.updated = data.get('updated')

            db.session.commit()
            print(f"Blog info updated for {self.blog.name}")
        except (requests.RequestException, json.JSONDecodeError) as e:
            print(f"Error fetching blog info for {self.blog.name}: {e}")

    def _crawl_posts(self):
        """Crawls all posts for the blog, handling pagination."""
        offset = 0
        limit = 20  # Max 20 per page
        total_posts = self.blog.posts_total or 0
        last_id = self.blog.last_id

        while offset < total_posts:
            api_url = f"https://api.tumblr.com/v2/blog/{self.blog.name}/posts"
            params = {'offset': offset, 'limit': limit, 'npf': 'true'}
            if last_id:
                params['since_id'] = last_id

            if not isinstance(self.session, OAuth2Session):
                credential = TumblrCredential.query.first()
                params['api_key'] = credential.consumer_key if credential else "fuiKNFp9vQFvjLNvx4sUwti4Yb5yGutBN4Xh10LXZhhRKjWlV4"

            print(f"Crawling posts from offset {offset}...")
            try:
                response = self.session.get(api_url, params=params)
                response.raise_for_status()
                data = response.json().get('response', {})
                posts = data.get('posts', [])

                if not posts:
                    break # No more posts to process

                for post_data in posts:
                    self._process_post(post_data)

                if not last_id and len(posts) > 0:
                    # Set the last_id to the ID of the first post on the first page
                    self.blog.last_id = posts[0].get('id')

                offset += len(posts)
                self.blog.progress = (offset / total_posts) * 100 if total_posts > 0 else 100
                db.session.commit()

                # Be respectful of the API rate limits
                time.sleep(1)

            except (requests.RequestException, json.JSONDecodeError) as e:
                print(f"Error crawling posts for {self.blog.name} at offset {offset}: {e}")
                break

    def _process_post(self, post_data):
        """Processes a single post from the API and saves it to the database."""
        post_id = post_data.get('id')
        if not post_id or db.session.get(Post, post_id):
            return  # Skip if post already exists

        post_type = post_data.get('type')
        new_post = None

        if post_type == 'text':
            new_post = TextPost(id=post_id, title=post_data.get('title'), body=post_data.get('body'))
        elif post_type == 'photo':
            new_post = PhotoPost(id=post_id, caption=post_data.get('caption'))
            if self.blog.download_photo:
                for photo in post_data.get('photos', []):
                    self._download_file(photo['original_size']['url'], post_id)
        elif post_type == 'quote':
            new_post = QuotePost(id=post_id, text=post_data.get('text'), source=post_data.get('source'))
        elif post_type == 'link':
            new_post = LinkPost(id=post_id, title=post_data.get('title'), url=post_data.get('url'), description=post_data.get('description'))
        elif post_type == 'chat':
            new_post = ChatPost(id=post_id, title=post_data.get('title'), body=post_data.get('body'), dialogue=post_data.get('dialogue'))
        elif post_type == 'audio':
            new_post = AudioPost(id=post_id, caption=post_data.get('caption'), player=post_data.get('player'), plays=post_data.get('plays'))
            if self.blog.download_audio and 'audio_url' in post_data:
                self._download_file(post_data['audio_url'], post_id)
        elif post_type == 'video':
            new_post = VideoPost(id=post_id, caption=post_data.get('caption'), player=post_data.get('player'))
            if self.blog.download_video and 'video_url' in post_data:
                self._download_file(post_data['video_url'], post_id)
        elif post_type == 'answer':
            new_post = AnswerPost(id=post_id, asking_name=post_data.get('asking_name'), asking_url=post_data.get('asking_url'), question=post_data.get('question'), answer=post_data.get('answer'))

        if new_post:
            new_post.blog_id = self.blog.id
            new_post.post_url = post_data.get('post_url')
            new_post.timestamp = post_data.get('timestamp')
            new_post.date = post_data.get('date')
            new_post.format = post_data.get('format')
            new_post.reblog_key = post_data.get('reblog_key')
            new_post.note_count = post_data.get('note_count')
            new_post.npf_data = post_data

            # Handle tags
            for tag_name in post_data.get('tags', []):
                tag = Tag.query.filter_by(name=tag_name).first()
                if not tag:
                    tag = Tag(name=tag_name)
                    db.session.add(tag)
                new_post.tags.append(tag)

            db.session.add(new_post)
            db.session.commit()
            print(f"Saved post {post_id} for blog {self.blog.name}")

    def _download_file(self, url, post_id):
        """Downloads a file from a URL."""
        download_dir = os.path.join('tumblthree_py', 'downloads', self.blog.name)
        os.makedirs(download_dir, exist_ok=True)

        try:
            response = requests.get(url, stream=True)
            response.raise_for_status()
            filename = url.split('/')[-1].split('?')[0]
            filepath = os.path.join(download_dir, filename)

            with open(filepath, 'wb') as f:
                for chunk in response.iter_content(chunk_size=8192):
                    f.write(chunk)

            # Add file to the database
            new_file = File(post_id=post_id, url=url, filename=filename)
            db.session.add(new_file)
            db.session.commit()

            print(f"Downloaded: {filename}")
        except requests.RequestException as e:
            print(f"Error downloading {url}: {e}")
        except IOError as e:
            print(f"Error saving file {filepath}: {e}")
