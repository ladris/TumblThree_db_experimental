import requests
import json
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

        # 3. TODO: Implement downloader to process the download_queue.
        print(f"Download queue for {self.blog.name}:")
        for item in self.download_queue:
            print(item)

        print(f"Finished crawling blog: {self.blog.name}")

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
                    self.download_queue.append(photo['original_size']['url'])
            elif post_type == 'video':
                # This might need more sophisticated parsing to get the best quality.
                if 'video_url' in post:
                    self.download_queue.append(post['video_url'])
                elif 'permalink_url' in post:
                    # Fallback for some video types
                    self.download_queue.append(post['permalink_url'])
            # TODO: Add support for other post types (audio, text, etc.)
