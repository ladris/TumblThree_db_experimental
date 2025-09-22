import os
from flask import Flask, render_template, request, redirect, url_for
from flask_apscheduler import APScheduler
import click
from .models import db
from .migrate import migrate_data

# set configuration values
class Config:
    SCHEDULER_API_ENABLED = True

def create_app():
    app = Flask(__name__)
    app.config.from_object(Config())

    # Configure the database
    db_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'tumblthree.db')
    app.config['SQLALCHEMY_DATABASE_URI'] = f'sqlite:///{db_path}'
    app.config['SQLALCHEMY_TRACK_MODIFICATIONS'] = False

    # Initialize the database with the app
    db.init_app(app)

    with app.app_context():
        if not db.engine.dialect.has_table(db.engine.connect(), "blog"):
            print("Blog table not found, creating all tables.")
            db.create_all()

    # initialize scheduler
    scheduler = APScheduler()
    scheduler.init_app(app)
    scheduler.start()

    @app.route('/')
    def index():
        from .models import Blog
        blogs = Blog.query.all()
        return render_template('index.html', blogs=blogs)

    @app.route('/add_blog', methods=['POST'])
    def add_blog():
        from .models import Blog
        from datetime import datetime
        name = request.form['name']
        url = request.form['url']
        if not Blog.query.filter_by(name=name).first():
            new_blog = Blog(name=name, url=url, date_added=datetime.utcnow(), download_photo=True, download_video=True)
            db.session.add(new_blog)
            db.session.commit()
        return redirect(url_for('index'))

    # Add a command to create the database tables
    @app.cli.command('init-db')
    def init_db_command():
        """Creates the database tables."""
        with app.app_context():
            db.create_all()
        print('Initialized the database.')

    # Add a command to migrate data
    @app.cli.command('migrate')
    @click.argument('path')
    def migrate_command(path):
        """Migrates data from the old TumblThree JSON files."""
        with app.app_context():
            migrate_data(path)

    def crawl_blog_job(blog_id):
        with app.app_context():
            from .crawler import TumblrCrawler
            from .models import Blog
            blog = Blog.query.get(blog_id)
            if blog:
                blog.status = 'crawling'
                db.session.commit()
                try:
                    crawler = TumblrCrawler(blog, app.app_context())
                    crawler.crawl()
                    blog.status = 'finished'
                except Exception as e:
                    print(f"Error crawling blog {blog.name}: {e}")
                    blog.status = 'error'
                db.session.commit()

    @app.route('/crawl/<int:blog_id>')
    def crawl_route(blog_id):
        scheduler.add_job(func=crawl_blog_job, args=[blog_id], id=f'crawl_job_{blog_id}', replace_existing=True)
        return redirect(url_for('index'))

    # Add a command to crawl a blog
    @app.cli.command('crawl')
    @click.argument('blog_name')
    def crawl_command(blog_name):
        """Crawls a specific blog."""
        with app.app_context():
            from .models import Blog
            blog = Blog.query.filter_by(name=blog_name).first()
            if blog:
                scheduler.add_job(func=crawl_blog_job, args=[blog.id], id=f'crawl_job_{blog.id}', replace_existing=True)
                print(f"Scheduled crawl for blog: {blog.name}")
            else:
                print(f"Blog '{blog_name}' not found in the database.")

    @app.route('/settings/<int:blog_id>', methods=['GET', 'POST'])
    def settings(blog_id):
        from .models import Blog
        blog = Blog.query.get_or_404(blog_id)
        if request.method == 'POST':
            blog.download_photo = 'download_photo' in request.form
            blog.download_video = 'download_video' in request.form
            blog.tags = request.form['tags']
            db.session.commit()
            return redirect(url_for('index'))
        return render_template('settings.html', blog=blog)

    # Add a command to add dummy files for testing the gallery
    @app.cli.command('add-dummy-files')
    def add_dummy_files_command():
        """Adds dummy file records to the database for testing."""
        from .models import Blog, File
        with app.app_context():
            nasa_blog = Blog.query.filter_by(name='nasa').first()
            if nasa_blog:
                file1 = File(blog=nasa_blog, link='dummy_link_1', filename='1.jpg')
                file2 = File(blog=nasa_blog, link='dummy_link_2', filename='2.png')
                db.session.add(file1)
                db.session.add(file2)
                db.session.commit()
                print("Added dummy files for nasa blog.")
            else:
                print("Could not find nasa blog.")

    @app.route('/gallery/<int:blog_id>')
    def gallery(blog_id):
        from .models import Blog
        blog = Blog.query.get_or_404(blog_id)
        return render_template('gallery.html', blog=blog)

    @app.route('/archive/<int:file_id>', methods=['POST'])
    def archive_file(file_id):
        from .models import File
        file = File.query.get_or_404(file_id)
        file.status = 'archived'
        db.session.commit()
        return redirect(url_for('gallery', blog_id=file.blog_id))

    @app.route('/delete/<int:file_id>', methods=['POST'])
    def delete_file(file_id):
        from .models import File
        file = File.query.get_or_404(file_id)
        blog_id = file.blog_id

        # Delete the actual file from disk
        try:
            filepath = os.path.join('tumblthree_py', 'downloads', file.blog.name, file.filename)
            os.remove(filepath)
            print(f"Deleted file: {filepath}")
        except FileNotFoundError:
            print(f"File not found, could not delete: {filepath}")
        except Exception as e:
            print(f"Error deleting file {filepath}: {e}")

        db.session.delete(file)
        db.session.commit()
        return redirect(url_for('gallery', blog_id=blog_id))

    # Add a command to add a new blog
    @app.cli.command('add-blog')
    @click.argument('name')
    @click.argument('url')
    def add_blog_command(name, url):
        """Adds a new blog to the database."""
        from .models import Blog
        from datetime import datetime
        with app.app_context():
            if Blog.query.filter_by(name=name).first():
                print(f"Blog '{name}' already exists.")
                return
            new_blog = Blog(name=name, url=url, date_added=datetime.utcnow(), download_photo=True, download_video=True)
            db.session.add(new_blog)
            db.session.commit()
            print(f"Blog '{name}' added.")

    @app.route('/maintenance')
    def maintenance():
        return render_template('maintenance.html')

    @app.route('/maintenance/init-db', methods=['POST'])
    def init_db_route():
        with app.app_context():
            db.create_all()
        print('Initialized the database.')
        return redirect(url_for('maintenance'))

    @app.route('/maintenance/migrate', methods=['POST'])
    def migrate_route():
        path = request.form['path']
        with app.app_context():
            migrate_data(path)
        return redirect(url_for('maintenance'))

    return app

if __name__ == '__main__':
    app = create_app()
    app.run(debug=True)
