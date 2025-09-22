import unittest
from unittest.mock import patch, MagicMock
from tumblthree_py.app import create_app
from tumblthree_py.models import db, Blog
from tumblthree_py.crawler import TumblrCrawler

class CrawlerTestCase(unittest.TestCase):
    def setUp(self):
        self.app = create_app()
        self.app.config['TESTING'] = True
        self.app.config['SQLALCHEMY_DATABASE_URI'] = 'sqlite:///:memory:'
        self.app_context = self.app.app_context()
        self.app_context.push()
        db.create_all()

    def tearDown(self):
        db.session.remove()
        db.drop_all()
        self.app_context.pop()

    @patch('requests.get')
    def test_crawl_photo_post(self, mock_get):
        # Mock the API response
        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {
            'response': {
                'total_posts': 1,
                'posts': [{
                    'type': 'photo',
                    'photos': [{
                        'original_size': {
                            'url': 'http://example.com/photo.jpg'
                        }
                    }]
                }]
            }
        }
        mock_get.return_value = mock_response

        # Create a dummy blog
        blog = Blog(name='photoblog', url='http://photoblog.tumblr.com')
        db.session.add(blog)
        db.session.commit()

        crawler = TumblrCrawler(blog, self.app_context)

        with patch.object(crawler, '_download_file') as mock_download:
            crawler.crawl()
            mock_download.assert_called_once_with('http://example.com/photo.jpg', 'tumblthree_py/downloads/photoblog')

    @patch('requests.get')
    def test_crawl_video_post(self, mock_get):
        # Mock the API response
        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {
            'response': {
                'total_posts': 1,
                'posts': [{
                    'type': 'video',
                    'video_url': 'http://example.com/video.mp4'
                }]
            }
        }
        mock_get.return_value = mock_response

        # Create a dummy blog
        blog = Blog(name='videoblog', url='http://videoblog.tumblr.com')
        db.session.add(blog)
        db.session.commit()

        crawler = TumblrCrawler(blog, self.app_context)

        with patch.object(crawler, '_download_file') as mock_download:
            crawler.crawl()
            mock_download.assert_called_once_with('http://example.com/video.mp4', 'tumblthree_py/downloads/videoblog')

    @patch('requests.get')
    def test_crawl_audio_post(self, mock_get):
        # Mock the API response
        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {
            'response': {
                'total_posts': 1,
                'posts': [{
                    'type': 'audio',
                    'audio_url': 'http://example.com/audio.mp3'
                }]
            }
        }
        mock_get.return_value = mock_response

        # Create a dummy blog
        blog = Blog(name='audioblog', url='http://audioblog.tumblr.com')
        db.session.add(blog)
        db.session.commit()

        crawler = TumblrCrawler(blog, self.app_context)

        with patch.object(crawler, '_download_file') as mock_download:
            crawler.crawl()
            mock_download.assert_called_once_with('http://example.com/audio.mp3', 'tumblthree_py/downloads/audioblog')

    @patch('requests.get')
    def test_crawl_text_post(self, mock_get):
        # Mock the API response
        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {
            'response': {
                'total_posts': 1,
                'posts': [{
                    'type': 'text',
                    'id': 12345,
                    'body': 'This is a text post.'
                }]
            }
        }
        mock_get.return_value = mock_response

        # Create a dummy blog
        blog = Blog(name='textblog', url='http://textblog.tumblr.com')
        db.session.add(blog)
        db.session.commit()

        crawler = TumblrCrawler(blog, self.app_context)

        with patch.object(crawler, '_save_file') as mock_save:
            crawler.crawl()
            mock_save.assert_called_once_with('12345.txt', 'This is a text post.', 'tumblthree_py/downloads/textblog', link='text_post_12345')

if __name__ == '__main__':
    unittest.main()
