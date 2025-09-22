import os
import unittest
from unittest import mock
from tumblthree_py.app import create_app
from tumblthree_py.models import db, Blog, File

class AppTestCase(unittest.TestCase):
    def setUp(self):
        self.app = create_app()
        self.app.config['TESTING'] = True
        self.app.config['SQLALCHEMY_DATABASE_URI'] = 'sqlite:///:memory:'
        self.client = self.app.test_client()

        with self.app.app_context():
            db.create_all()

    def tearDown(self):
        with self.app.app_context():
            db.session.remove()
            db.drop_all()

    def test_delete_file(self):
        with self.app.app_context():
            # Create a dummy blog
            blog = Blog(name='testblog', url='http://testblog.tumblr.com')
            db.session.add(blog)
            db.session.commit()

            # Create a dummy file record
            file = File(blog_id=blog.id, filename='test.jpg', link='http://example.com/test.jpg')
            db.session.add(file)
            db.session.commit()

            # Create a dummy file on disk
            download_dir = os.path.join('tumblthree_py', 'downloads', blog.name)
            os.makedirs(download_dir, exist_ok=True)
            filepath = os.path.join(download_dir, file.filename)
            with open(filepath, 'w') as f:
                f.write('test data')

            # Make sure the file exists before deleting
            self.assertTrue(os.path.exists(filepath))

            # Call the delete endpoint
            response = self.client.post(f'/delete/{file.id}')
            self.assertEqual(response.status_code, 302) # Redirect

            # Check that the file is deleted from the database
            deleted_file = File.query.get(file.id)
            self.assertIsNone(deleted_file)

            # Check that the file is deleted from disk
            self.assertFalse(os.path.exists(filepath))

    def test_maintenance_page(self):
        response = self.client.get('/maintenance')
        self.assertEqual(response.status_code, 200)
        self.assertIn(b'Maintenance', response.data)

    def test_init_db_route(self):
        with self.app.app_context():
            # This is a bit tricky to test without inspecting the logs or the database file directly.
            # We can check that the tables are created.
            with self.client:
                self.client.post('/maintenance/init-db')
                # Check if a table was created
                self.assertTrue(db.engine.dialect.has_table(db.engine.connect(), "blog"))

    @mock.patch('tumblthree_py.app.migrate_data')
    def test_migrate_route(self, mock_migrate_data):
        with self.client:
            self.client.post('/maintenance/migrate', data={'path': '/fake/path'})
            mock_migrate_data.assert_called_once_with('/fake/path')

if __name__ == '__main__':
    unittest.main()
