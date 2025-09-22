import requests
import json
import os
from .models import db, Blog, File

class TumblrCrawler:
    def __init__(self, blog: Blog, app_context):
        self.blog = blog
        self.app_context = app_context
        self.download_queue = []

    def crawl(self):
        """
        Main method to crawl a Tumblr blog.
        """
        print(f"Crawling blog: {self.blog.name}")

        # 1. Get the total number of posts.
        total_posts = self._get_total_posts()
        if total_posts is None:
            print(f"Could not determine the total number of posts for {self.blog.name}. Aborting.")
            return

        self.blog.total_count = total_posts
        db.session.commit()
        print(f"Total posts to check: {total_posts}")

        # 2. Iterate through pages and get post URLs.
        # For now, let's just try to get the first page.
        self._crawl_page(0)

        # 3. Download files.
        self._process_download_queue()

        print(f"Finished crawling blog: {self.blog.name}")

    def _process_download_queue(self):
        """Processes the download queue."""
        download_dir = os.path.join('tumblthree_py', 'downloads', self.blog.name)
        os.makedirs(download_dir, exist_ok=True)

        for item_type, data in self.download_queue:
            if item_type == 'url':
                self._download_file(data, download_dir)
            elif item_type == 'text':
                filename = f"{data['id']}.txt"
                self._save_file(filename, data['body'], download_dir, link=f"text_post_{data['id']}")

    def _save_file(self, filename, content, download_dir, link):
        """Saves content to a file."""
        filepath = os.path.join(download_dir, filename)
        try:
            with open(filepath, 'wb' if isinstance(content, bytes) else 'w') as f:
                f.write(content)

            # Add file to the database
            new_file = File(blog_id=self.blog.id, link=link, filename=filename)
            db.session.add(new_file)
            db.session.commit()

            print(f"Saved: {filename}")
        except IOError as e:
            print(f"Error saving file {filepath}: {e}")

    def _download_file(self, url, download_dir):
        """Downloads a file from a URL."""
        try:
            response = requests.get(url, stream=True)
            response.raise_for_status()
            filename = url.split('/')[-1].split('?')[0]
            self._save_file(filename, response.content, download_dir, url)
        except requests.RequestException as e:
            print(f"Error downloading {url}: {e}")

    def _get_api_url(self, start=0):
        """Constructs the API URL for the blog."""
        url = self.blog.url
        if not url.endswith('/'):
            url += '/'
        # Using v2 API with api_key, as v1 is deprecated.
        # This will require an API key. For now, I'll use a placeholder.
        # A proper implementation would need a way for the user to provide their own key.
        api_key = "fuiKNFp9vQFvjLNvx4sUwti4Yb5yGutBN4Xh10LXZhhRKjWlV4" # This is a public key from Tumblr's examples
        return f"https://api.tumblr.com/v2/blog/{self.blog.name}.tumblr.com/posts?api_key={api_key}&offset={start}"

    def _get_total_posts(self):
        """Gets the total number of posts for the blog."""
        api_url = self._get_api_url()
        try:
            response = requests.get(api_url)
            response.raise_for_status()
            data = response.json()
            return data.get('response', {}).get('total_posts')
        except (requests.RequestException, json.JSONDecodeError) as e:
            print(f"Error getting total posts for {self.blog.name}: {e}")
            return None

    def _crawl_page(self, start):
        """Crawls a single page of the blog's API."""
        api_url = self._get_api_url(start)
        print(f"Crawling page: {api_url}")
        try:
            response = requests.get(api_url)
            response.raise_for_status()
            data = response.json()
            posts = data.get('response', {}).get('posts', [])
            self._add_urls_to_download_list(posts)
        except (requests.RequestException, json.JSONDecodeError) as e:
            print(f"Error crawling page for {self.blog.name}: {e}")

    def _add_urls_to_download_list(self, posts):
        """Parses posts and adds media URLs to the download queue."""
        for post in posts:
            post_type = post.get('type')
            if post_type == 'photo':
                for photo in post.get('photos', []):
                    self.download_queue.append(('url', photo['original_size']['url']))
            elif post_type == 'video':
                if 'video_url' in post:
                    self.download_queue.append(('url', post['video_url']))
                elif 'permalink_url' in post:
                    self.download_queue.append(('url', post['permalink_url']))
            elif post_type == 'audio':
                if 'audio_url' in post:
                    self.download_queue.append(('url', post['audio_url']))
            elif post_type == 'text':
                if 'body' in post:
                    self.download_queue.append(('text', {'id': post['id'], 'body': post['body']}))
