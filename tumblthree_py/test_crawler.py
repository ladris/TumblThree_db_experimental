import unittest
from unittest.mock import patch, MagicMock
from tumblthree_py.app import create_app
from tumblthree_py.models import db, Blog, Post, PhotoPost, TumblrCredential
from tumblthree_py.crawler import TumblrCrawler

class CrawlerTestCase(unittest.TestCase):
    def setUp(self):
        self.app = create_app()
        self.app.config['TESTING'] = True
        self.app.config['SQLALCHEMY_DATABASE_URI'] = 'sqlite:///:memory:'
        self.app.config['SECRET_KEY'] = 'test-secret-key'
        self.app_context = self.app.app_context()
        self.app_context.push()
        db.create_all()

    def tearDown(self):
        db.session.remove()
        db.drop_all()
        self.app_context.pop()

    @patch('requests.Session.get')
    def test_crawl_photo_post(self, mock_get):
        # Mock the API responses
        mock_info_response = MagicMock()
        mock_info_response.status_code = 200
        mock_info_response.json.return_value = {
            'response': {
                'blog': {
                    'uuid': 'photoblog-uuid',
                    'name': 'photoblog',
                    'title': 'Photo Blog',
                    'description': 'A blog for photos.',
                    'ask': False,
                    'ask_anon': False,
                    'posts': 1,
                    'updated': 1234567890,
                    'url': 'http://photoblog.tumblr.com'
                }
            }
        }

        mock_posts_response = MagicMock()
        mock_posts_response.status_code = 200
        mock_posts_response.json.return_value = {
            'response': {
                'posts': [{
                    'id': 123,
                    'type': 'photo',
                    'post_url': 'http://photoblog.tumblr.com/post/123',
                    'timestamp': 1234567890,
                    'photos': [{'original_size': {'url': 'http://example.com/photo.jpg'}}]
                }]
            }
        }
        mock_get.side_effect = [mock_info_response, mock_posts_response]

        # Create a dummy blog
        blog = Blog(uuid='photoblog-uuid', name='photoblog', url='http://photoblog.tumblr.com')
        db.session.add(blog)
        db.session.commit()

        with self.app.test_request_context():
            crawler = TumblrCrawler(blog, self.app_context)

        with patch.object(crawler, '_download_file') as mock_download, self.app.test_request_context():
            crawler.crawl()
            # Verify that a PhotoPost was created
            post = db.session.get(Post, 123)
            self.assertIsNotNone(post)
            self.assertEqual(post.type, 'photo')

            photo_post = db.session.get(PhotoPost, 123)
            self.assertIsNotNone(photo_post)

            # Verify that the file was downloaded
            mock_download.assert_called_once_with('http://example.com/photo.jpg', 123)

if __name__ == '__main__':
    unittest.main()
