import unittest
from unittest.mock import patch, MagicMock
from tumblthree_py.app import create_app
from tumblthree_py.models import db, Blog, TumblrCredential

class AppTestCase(unittest.TestCase):
    def setUp(self):
        self.app = create_app()
        self.app.config['TESTING'] = True
        self.app.config['SQLALCHEMY_DATABASE_URI'] = 'sqlite:///:memory:'
        self.app.config['SECRET_KEY'] = 'test-secret-key'
        self.client = self.app.test_client()

        with self.app.app_context():
            db.create_all()

    def tearDown(self):
        with self.app.app_context():
            db.session.remove()
            db.drop_all()

    def test_index_page(self):
        response = self.client.get('/')
        self.assertEqual(response.status_code, 200)
        self.assertIn(b'TumblThree-py', response.data)

    @patch('requests.get')
    def test_add_blog(self, mock_get):
        # Mock the API response for blog info
        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {
            'response': {
                'blog': {
                    'uuid': 'test-uuid',
                    'name': 'testblog',
                    'url': 'http://testblog.tumblr.com',
                    'title': 'Test Blog',
                    'description': 'A blog for testing.',
                    'ask': False,
                    'ask_anon': False,
                    'posts': 10,
                    'updated': 1234567890
                }
            }
        }
        mock_get.return_value = mock_response

        response = self.client.post('/add_blog', data={'url': 'http://testblog.tumblr.com'})
        self.assertEqual(response.status_code, 302)  # Redirect

        with self.app.app_context():
            blog = Blog.query.filter_by(name='testblog').first()
            self.assertIsNotNone(blog)
            self.assertEqual(blog.title, 'Test Blog')

    def test_settings_page(self):
        with self.app.app_context():
            blog = Blog(uuid='test-uuid', name='testblog', url='http://testblog.tumblr.com')
            db.session.add(blog)
            db.session.commit()
            response = self.client.get(f'/settings/{blog.id}')
            self.assertEqual(response.status_code, 200)
            self.assertIn(b'Settings for testblog', response.data)

    def test_maintenance_page(self):
        response = self.client.get('/maintenance')
        self.assertEqual(response.status_code, 200)
        self.assertIn(b'Maintenance', response.data)

    def test_status_endpoint(self):
        with self.app.app_context():
            blog = Blog(uuid='test-uuid', name='testblog', url='http://testblog.tumblr.com', status='crawling', progress=50.0, posts_total=100)
            db.session.add(blog)
            db.session.commit()
            response = self.client.get('/status')
            self.assertEqual(response.status_code, 200)
            data = response.get_json()
            self.assertIn(str(blog.id), data)
            self.assertEqual(data[str(blog.id)]['status'], 'crawling')

if __name__ == '__main__':
    unittest.main()
