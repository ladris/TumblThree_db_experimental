import os
import unittest
import json
from app import create_app, db, Blog, Setting

class TumblThreeTestCase(unittest.TestCase):

    def setUp(self):
        self.app = create_app('testing')
        self.app_context = self.app.app_context()
        self.app_context.push()
        db.create_all()
        self.client = self.app.test_client()

    def tearDown(self):
        db.session.remove()
        db.drop_all()
        self.app_context.pop()

    def test_get_blogs_empty(self):
        response = self.client.get('/api/blogs')
        self.assertEqual(response.status_code, 200)
        self.assertEqual(json.loads(response.data), [])

    def test_create_blog(self):
        response = self.client.post('/api/blogs',
                                 data=json.dumps({'name': 'testblog', 'url': 'https://testblog.tumblr.com'}),
                                 content_type='application/json')
        self.assertEqual(response.status_code, 201)
        data = json.loads(response.data)
        self.assertEqual(data['name'], 'testblog')

    def test_get_blog(self):
        blog = Blog(name='testblog', url='https://testblog.tumblr.com')
        db.session.add(blog)
        db.session.commit()
        blog_id = blog.id

        response = self.client.get(f'/api/blogs/{blog_id}')
        self.assertEqual(response.status_code, 200)
        data = json.loads(response.data)
        self.assertEqual(data['name'], 'testblog')

    def test_update_blog(self):
        blog = Blog(name='testblog', url='https://testblog.tumblr.com')
        db.session.add(blog)
        db.session.commit()
        blog_id = blog.id

        response = self.client.put(f'/api/blogs/{blog_id}',
                                data=json.dumps({'name': 'updatedblog'}),
                                content_type='application/json')
        self.assertEqual(response.status_code, 200)
        data = json.loads(response.data)
        self.assertEqual(data['name'], 'updatedblog')

    def test_delete_blog(self):
        blog = Blog(name='testblog', url='https://testblog.tumblr.com')
        db.session.add(blog)
        db.session.commit()
        blog_id = blog.id

        response = self.client.delete(f'/api/blogs/{blog_id}')
        self.assertEqual(response.status_code, 204)

        response = self.client.get(f'/api/blogs/{blog_id}')
        self.assertEqual(response.status_code, 404)

    def test_get_settings_empty(self):
        response = self.client.get('/api/settings')
        self.assertEqual(response.status_code, 200)
        self.assertEqual(json.loads(response.data), {})

    def test_update_settings(self):
        response = self.client.put('/api/settings',
                                data=json.dumps({'theme': 'dark'}),
                                content_type='application/json')
        self.assertEqual(response.status_code, 200)

        response = self.client.get('/api/settings')
        self.assertEqual(response.status_code, 200)
        data = json.loads(response.data)
        self.assertEqual(data['theme'], 'dark')

if __name__ == '__main__':
    unittest.main()
