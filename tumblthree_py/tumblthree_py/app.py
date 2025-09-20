import os
from flask import Flask, request, jsonify, render_template
from flask_sqlalchemy import SQLAlchemy
from sqlalchemy import BigInteger
import datetime
from celery import Celery
from config import config

db = SQLAlchemy()
celery = Celery(__name__, broker=os.environ.get('CELERY_BROKER_URL', 'redis://localhost:6379/0'))

# --- Database Models ---
class Blog(db.Model):
    __tablename__ = 'blogs'
    id = db.Column(db.Integer, primary_key=True)
    name = db.Column(db.String(255), nullable=False)
    url = db.Column(db.String(2083), nullable=False)
    location = db.Column(db.String(255))
    child_id = db.Column(db.String(255))
    blog_type = db.Column(db.String(50))
    original_blog_type = db.Column(db.String(50))
    version = db.Column(db.String(10))
    description = db.Column(db.Text)
    title = db.Column(db.Text)
    tags = db.Column(db.Text)
    notes = db.Column(db.Text)
    password = db.Column(db.String(255))
    filename_template = db.Column(db.String(255))
    file_download_location = db.Column(db.String(255))
    last_downloaded_photo = db.Column(db.String(255))
    last_downloaded_video = db.Column(db.String(255))
    download_pages = db.Column(db.String(255))
    download_from = db.Column(db.String(255))
    download_to = db.Column(db.String(255))
    pnj_download_format = db.Column(db.String(10))
    rating = db.Column(db.Integer)
    duplicate_photos = db.Column(db.Integer)
    duplicate_videos = db.Column(db.Integer)
    duplicate_audios = db.Column(db.Integer)
    total_count = db.Column(db.Integer)
    posts_count = db.Column('posts', db.Integer) # Renamed to avoid conflict with relationship
    texts = db.Column(db.Integer)
    answers = db.Column(db.Integer)
    photos = db.Column(db.Integer)
    number_of_links = db.Column(db.Integer)
    conversations = db.Column(db.Integer)
    videos = db.Column(db.Integer)
    audios = db.Column(db.Integer)
    photo_metas = db.Column(db.Integer)
    video_metas = db.Column(db.Integer)
    audio_metas = db.Column(db.Integer)
    downloaded_texts = db.Column(db.Integer)
    downloaded_quotes = db.Column(db.Integer)
    downloaded_photos = db.Column(db.Integer)
    downloaded_links = db.Column(db.Integer)
    downloaded_answers = db.Column(db.Integer)
    downloaded_conversations = db.Column(db.Integer)
    downloaded_videos = db.Column(db.Integer)
    downloaded_audios = db.Column(db.Integer)
    downloaded_photo_metas = db.Column(db.Integer)
    downloaded_video_metas = db.Column(db.Integer)
    downloaded_audio_metas = db.Column(db.Integer)
    page_size = db.Column(db.Integer)
    settings_tab_index = db.Column(db.Integer)
    progress = db.Column(db.Integer)
    collection_id = db.Column(db.Integer)
    downloaded_items_new = db.Column(db.Integer)
    last_id = db.Column(BigInteger)
    date_added = db.Column(db.DateTime)
    last_complete_crawl = db.Column(db.DateTime)
    latest_post = db.Column(db.DateTime)
    download_text = db.Column(db.Boolean)
    download_quote = db.Column(db.Boolean)
    download_photo = db.Column(db.Boolean)
    download_link = db.Column(db.Boolean)
    download_answer = db.Column(db.Boolean)
    download_conversation = db.Column(db.Boolean)
    download_video = db.Column(db.Boolean)
    download_audio = db.Column(db.Boolean)
    create_photo_meta = db.Column(db.Boolean)
    create_video_meta = db.Column(db.Boolean)
    create_audio_meta = db.Column(db.Boolean)
    download_replies = db.Column(db.Boolean)
    download_reblogged_posts = db.Column(db.Boolean)
    dump_crawler_data = db.Column(db.Boolean)
    reg_ex_photos = db.Column(db.Boolean)
    reg_ex_videos = db.Column(db.Boolean)
    check_directory_for_files = db.Column(db.Boolean)
    download_url_list = db.Column(db.Boolean)
    skip_gif = db.Column(db.Boolean)
    download_video_thumbnail = db.Column(db.Boolean)
    force_size = db.Column(db.Boolean)
    force_rescan = db.Column(db.Boolean)
    group_photo_sets = db.Column(db.Boolean)
    save_texts_individual_files = db.Column(db.Boolean)
    zip_crawler_data = db.Column(db.Boolean)
    online = db.Column(db.Boolean)
    download_imgur = db.Column(db.Boolean)
    download_webmshare = db.Column(db.Boolean)
    download_uguu = db.Column(db.Boolean)
    download_catbox = db.Column(db.Boolean)
    webmshare_type = db.Column(db.String(50))
    uguu_type = db.Column(db.String(50))
    catbox_type = db.Column(db.String(50))
    states = db.Column(db.String(50))
    metadata_format = db.Column(db.String(50))

    posts = db.relationship('Post', backref='blog', lazy=True)

    def to_dict(self):
        result = {}
        for c in self.__class__.__table__.columns:
            value = getattr(self, c.name)
            if isinstance(value, datetime.datetime):
                result[c.name] = value.isoformat()
            else:
                result[c.name] = value
        return result

class Post(db.Model):
    __tablename__ = 'posts'
    id = db.Column(db.Integer, primary_key=True)
    blog_id = db.Column(db.Integer, db.ForeignKey('blogs.id'), nullable=False)
    filename = db.Column(db.String(255), nullable=False)
    url = db.Column(db.String(2083))
    timestamp = db.Column(db.DateTime)

    def to_dict(self):
        result = {}
        for c in self.__class__.__table__.columns:
            value = getattr(self, c.name)
            if isinstance(value, datetime.datetime):
                result[c.name] = value.isoformat()
            else:
                result[c.name] = value
        return result

class Setting(db.Model):
    __tablename__ = 'settings'
    key = db.Column(db.String(255), primary_key=True)
    value = db.Column(db.Text)

def create_app(config_name):
    app = Flask(__name__)
    app.config.from_object(config[config_name])

    db.init_app(app)
    celery.conf.update(app.config)

    # --- Celery Task ---
    @celery.task
    def crawl_blog_task(blog_id):
        from tumblthree_py.crawlers import TumblrBlogCrawler
        from tumblthree_py.downloader import Downloader

        # Create a new app context for the task
        with app.app_context():
            blog = db.session.get(Blog, blog_id)
            if not blog:
                return {'status': 'error', 'message': 'Blog not found'}

            crawler = TumblrBlogCrawler(blog)
            posts_to_download = crawler.crawl()

            for post_data in posts_to_download:
                # Check if post already exists
                existing_post = Post.query.filter_by(blog_id=blog.id, url=post_data['url']).first()
                if not existing_post:
                    post = Post(
                        blog_id=blog.id,
                        url=post_data['url'],
                        timestamp=datetime.datetime.fromtimestamp(post_data['timestamp'])
                    )
                    db.session.add(post)

            db.session.commit()

            downloader = Downloader(download_directory='tumblthree_py/downloads')
            downloader.download_posts(posts_to_download, blog.name)

            # Update blog's last_complete_crawl
            blog.last_complete_crawl = datetime.datetime.utcnow()
            db.session.commit()

            return {'status': 'success', 'message': f'Crawled {len(posts_to_download)} posts for {blog.name}'}

    # --- Frontend Routes ---
    @app.route('/')
    def index():
        return render_template('index.html')

    @app.route('/blog/<int:id>')
    def blog_page(id):
        return render_template('blog.html', blog_id=id)

    @app.route('/settings')
    def settings_page():
        return render_template('settings.html')

    # --- API Routes ---
    @app.route('/api/blogs', methods=['GET'])
    def get_blogs():
        blogs = Blog.query.all()
        return jsonify([blog.to_dict() for blog in blogs])

    @app.route('/api/blogs', methods=['POST'])
    def create_blog():
        data = request.get_json()
        if not data or not 'name' in data or not 'url' in data:
            return jsonify({'message': 'Name and URL are required'}), 400
        new_blog = Blog(**data)
        db.session.add(new_blog)
        db.session.commit()
        return jsonify(new_blog.to_dict()), 201

    @app.route('/api/blogs/<int:id>', methods=['GET'])
    def get_blog(id):
        blog = db.session.get(Blog, id)
        if blog is None:
            return jsonify({'message': 'Blog not found'}), 404
        return jsonify(blog.to_dict())

    @app.route('/api/blogs/<int:id>', methods=['PUT'])
    def update_blog(id):
        blog = db.session.get(Blog, id)
        if blog is None:
            return jsonify({'message': 'Blog not found'}), 404
        data = request.get_json()
        for key, value in data.items():
            setattr(blog, key, value)
        db.session.commit()
        return jsonify(blog.to_dict())

    @app.route('/api/blogs/<int:id>', methods=['DELETE'])
    def delete_blog(id):
        blog = db.session.get(Blog, id)
        if blog is None:
            return jsonify({'message': 'Blog not found'}), 404
        db.session.delete(blog)
        db.session.commit()
        return '', 204

    @app.route('/api/settings', methods=['GET'])
    def get_settings():
        settings = Setting.query.all()
        return jsonify({setting.key: setting.value for setting in settings})

    @app.route('/api/settings', methods=['PUT'])
    def update_settings():
        data = request.get_json()
        for key, value in data.items():
            setting = db.session.get(Setting, key)
            if setting:
                setting.value = str(value)
            else:
                new_setting = Setting(key=key, value=str(value))
                db.session.add(new_setting)
        db.session.commit()
        return jsonify({'message': 'Settings updated successfully'})

    @app.route('/api/blogs/<int:id>/crawl', methods=['POST'])
    def crawl_blog(id):
        task = crawl_blog_task.delay(id)
        return jsonify({'task_id': task.id}), 202

    @app.route('/api/tasks/<task_id>', methods=['GET'])
    def get_task_status(task_id):
        task = crawl_blog_task.AsyncResult(task_id)
        response = {
            'state': task.state,
            'info': task.info,
        }
        return jsonify(response)

    return app

if __name__ == '__main__':
    app = create_app(os.getenv('FLASK_CONFIG') or 'default')
    with app.app_context():
        db.create_all()
    app.run(debug=True)
