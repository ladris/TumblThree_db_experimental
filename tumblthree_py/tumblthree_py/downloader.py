import os
import requests
from concurrent.futures import ThreadPoolExecutor

class Downloader:
    def __init__(self, download_directory, num_workers=4):
        self.download_directory = download_directory
        self.session = requests.Session()
        self.executor = ThreadPoolExecutor(max_workers=num_workers)

    def download_file(self, url, blog_name):
        try:
            # Create blog-specific directory if it doesn't exist
            blog_dir = os.path.join(self.download_directory, blog_name)
            os.makedirs(blog_dir, exist_ok=True)

            # Get filename from URL
            filename = url.split('/')[-1].split('?')[0]
            filepath = os.path.join(blog_dir, filename)

            # Check if file already exists
            if os.path.exists(filepath):
                print(f"File already exists: {filepath}")
                return

            # Download the file
            print(f"Downloading: {url} to {filepath}")
            response = self.session.get(url, stream=True, timeout=30)
            response.raise_for_status()
            with open(filepath, 'wb') as f:
                for chunk in response.iter_content(chunk_size=8192):
                    f.write(chunk)
            print(f"Downloaded: {filepath}")
        except requests.exceptions.RequestException as e:
            print(f"Error downloading {url}: {e}")

    def download_posts(self, posts, blog_name):
        futures = []
        for post in posts:
            future = self.executor.submit(self.download_file, post['url'], blog_name)
            futures.append(future)

        # Wait for all downloads to complete
        for future in futures:
            try:
                future.result()
            except Exception as e:
                print(f"An error occurred during download: {e}")
