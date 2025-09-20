import requests
from bs4 import BeautifulSoup
import json
import re
import time

class TumblrBlogCrawler:
    def __init__(self, blog):
        self.blog = blog
        self.session = requests.Session()
        self.posts = []

    def get_api_url(self, count, start=0):
        url = self.blog.url
        if not url.endswith('/'):
            url += '/'
        url += 'api/read/json?debug=1&'
        params = {'num': count}
        if start > 0:
            params['start'] = start
        # Use requests' own urlencode, which is available at requests.compat.urlencode
        return url + requests.compat.urlencode(params)

    def get_api_page(self, page_num):
        time.sleep(1) # Rate limiting
        page_size = self.blog.page_size or 50
        start = page_num * page_size
        url = self.get_api_url(page_size, start)
        try:
            response = self.session.get(url, timeout=10)
            response.raise_for_status()
            # The response is a JS variable assignment, so we need to extract the JSON part.
            match = re.search(r'var tumblr_api_read = ({.*?});', response.text, re.DOTALL)
            if match:
                json_text = match.group(1)
                return json.loads(json_text)
            return None
        except requests.exceptions.RequestException as e:
            print(f"Error fetching page {page_num} for {self.blog.name}: {e}")
            return None
        except json.JSONDecodeError as e:
            print(f"Error decoding JSON for {self.blog.name} on page {page_num}: {e}")
            return None

    def _process_posts(self, posts_data):
        for post_data in posts_data:
            post_type = post_data.get('type')
            if post_type == 'photo':
                self.extract_photo_urls(post_data)
            elif post_type == 'video':
                self.extract_video_urls(post_data)
            elif post_type == 'regular':
                self.extract_inline_media(post_data)

    def crawl(self, max_pages=None):
        # Get total posts to calculate pages
        api_data = self.get_api_page(0)
        if not api_data:
            return []

        # Process page 0
        self._process_posts(api_data.get('posts', []))

        total_posts = api_data.get('posts-total', 0)
        self.blog.total_count = total_posts
        page_size = self.blog.page_size or 50
        total_pages = (total_posts // page_size) + 1

        pages_to_crawl = range(1, total_pages) # Start from page 1
        if max_pages:
            pages_to_crawl = range(1, min(total_pages, max_pages))

        for page_num in pages_to_crawl:
            print(f"Crawling page {page_num + 1}/{len(pages_to_crawl) + 1} for {self.blog.name}")
            api_data = self.get_api_page(page_num)
            if not api_data or 'posts' not in api_data:
                continue
            self._process_posts(api_data.get('posts', []))

        return self.posts

    def extract_photo_urls(self, post_data):
        # Handle single photo post
        if 'photo-url-1280' in post_data:
            self.posts.append({'url': post_data['photo-url-1280'], 'type': 'photo', 'id': post_data.get('id'), 'timestamp': post_data.get('unix-timestamp')})

        # Handle photoset
        if 'photos' in post_data:
            for photo in post_data['photos']:
                if 'photo-url-1280' in photo:
                    self.posts.append({'url': photo['photo-url-1280'], 'type': 'photo', 'id': post_data.get('id'), 'timestamp': post_data.get('unix-timestamp')})

    def extract_inline_media(self, post_data):
        body = post_data.get('regular-body', '')
        if not isinstance(body, str):
            return
        # Simple regex for images
        for url in re.findall(r'https?://\S+\.(?:jpg|jpeg|gif|png)', body):
            self.posts.append({'url': url, 'type': 'photo', 'id': post_data.get('id'), 'timestamp': post_data.get('unix-timestamp')})

    def extract_video_urls(self, post_data):
        # This is more complex, need to parse the video player HTML
        video_player = post_data.get('video-player', '')

        if not isinstance(video_player, str):
            return

        # Regex for tumblr video urls
        match = re.search(r'src="https://www\.tumblr\.com/video_file/(\w+)/(\w+)"', video_player)
        if match:
            video_id = match.group(2)
            video_url = f"https://vtt.tumblr.com/{video_id}.mp4"
            self.posts.append({'url': video_url, 'type': 'video', 'id': post_data.get('id'), 'timestamp': post_data.get('unix-timestamp')})
            return

        # Regex for vtt.tumblr.com video urls
        match = re.search(r'src="(https://vtt\.tumblr\.com/tumblr_\w+\.mp4)"', video_player)
        if match:
            self.posts.append({'url': match.group(1), 'type': 'video', 'id': post_data.get('id'), 'timestamp': post_data.get('unix-timestamp')})
