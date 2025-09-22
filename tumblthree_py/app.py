import os
import sys
import requests
import json
from flask import Flask, render_template, request, redirect, url_for, jsonify, session
from urllib.parse import urlparse
from flask_apscheduler import APScheduler
from requests_oauthlib import OAuth2Session
import click
from .models import db, Blog, Post, File, TumblrCredential

# set configuration values
class Config:
    SCHEDULER_API_ENABLED = True

def create_app():
    app = Flask(__name__)
    app.config.from_object(Config())
    app.secret_key = os.urandom(24)

    # OAuth2 settings
    client_id = None
    client_secret = None
    redirect_uri = 'http://localhost:5000/callback'
    scope = ['basic', 'write']
    authorization_base_url = 'https://www.tumblr.com/oauth2/authorize'
    token_url = 'https://api.tumblr.com/v2/oauth2/token'

    # Configure the database
    db_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'tumblthree.db')
    app.config['SQLALCHEMY_DATABASE_URI'] = f'sqlite:///{db_path}'
    app.config['SQLALCHEMY_TRACK_MODIFICATIONS'] = False

    # Initialize the database with the app
    db.init_app(app)

    with app.app_context():
        db.create_all()
        # Create logs directory if it doesn't exist
        logs_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'logs')
        os.makedirs(logs_dir, exist_ok=True)

        # Load credentials from DB
        credential = TumblrCredential.query.first()
        if credential:
            client_id = credential.consumer_key
            client_secret = credential.consumer_secret


    # initialize scheduler
    scheduler = APScheduler()
    scheduler.init_app(app)
    scheduler.start()

    @app.route('/')
    def index():
        blogs = Blog.query.all()
        user_info = session.get('user_info')
        return render_template('index.html', blogs=blogs, user_info=user_info)

    @app.route("/login")
    def login():
        if not client_id or not client_secret:
            return "OAuth2 credentials not configured in the database."
        tumblr = OAuth2Session(client_id, redirect_uri=redirect_uri, scope=scope)
        authorization_url, state = tumblr.authorization_url(authorization_base_url)
        session['oauth_state'] = state
        return redirect(authorization_url)

    @app.route("/callback")
    def callback():
        if not client_id or not client_secret:
            return "OAuth2 credentials not configured in the database."
        tumblr = OAuth2Session(client_id, state=session['oauth_state'])
        token = tumblr.fetch_token(token_url, client_secret=client_secret,
                                   authorization_response=request.url)
        session['oauth_token'] = token

        # Fetch user info
        user_info_response = tumblr.get('https://api.tumblr.com/v2/user/info')
        session['user_info'] = user_info_response.json().get('response', {}).get('user', {})

        return redirect(url_for('index'))

    @app.route("/logout")
    def logout():
        session.clear()
        return redirect(url_for('index'))

    @app.route('/add_blog', methods=['POST'])
    def add_blog():
        url = request.form['url']
        name = request.form.get('name')
        if not name:
            try:
                parsed_url = urlparse(url)
                hostname = parsed_url.hostname
                if hostname:
                    # Extract the blog name from a tumblr.com URL or a custom domain
                    if 'tumblr.com' in hostname:
                        name = hostname.split('.')[0]
                    else:
                        # For custom domains, we'll have to hit the API to get the name
                        # For now, we'll just use the hostname as the name
                        name = hostname
            except Exception as e:
                print(f"Could not parse blog name from url: {e}")
                return redirect(url_for('index'))

        if name and not Blog.query.filter_by(name=name).first():
            # Fetch blog info from API to get UUID and other details
            credential = TumblrCredential.query.first()
            api_key = credential.consumer_key if credential else "fuiKNFp9vQFvjLNvx4sUwti4Yb5yGutBN4Xh10LXZhhRKjWlV4"
            api_url = f"https://api.tumblr.com/v2/blog/{name}/info?api_key={api_key}"
            try:
                response = requests.get(api_url)
                response.raise_for_status()
                data = response.json().get('response', {}).get('blog', {})

                new_blog = Blog(
                    uuid=data.get('uuid'),
                    name=data.get('name'),
                    url=data.get('url'),
                    title=data.get('title'),
                    description=data.get('description'),
                    ask=data.get('ask'),
                    ask_anon=data.get('ask_anon'),
                    posts_total=data.get('posts'),
                    updated=data.get('updated')
                )
                db.session.add(new_blog)
                db.session.commit()
            except (requests.RequestException, json.JSONDecodeError) as e:
                print(f"Error fetching blog info for {name}: {e}")

        return redirect(url_for('index'))

    def crawl_blog_job(blog_id):
        with app.app_context():
            from .crawler import TumblrCrawler
            blog = Blog.query.get(blog_id)
            if blog:
                log_file_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'logs', f'{blog.name}.log')
                with open(log_file_path, 'w', encoding='utf-8') as log_file:
                    # Redirect stdout and stderr to the log file
                    original_stdout = sys.stdout
                    original_stderr = sys.stderr
                    sys.stdout = log_file
                    sys.stderr = log_file
                    try:
                        crawler = TumblrCrawler(blog, app.app_context())
                        crawler.crawl()
                    except Exception as e:
                        print(f"Error crawling blog {blog.name}: {e}")
                        blog.status = 'error'
                        db.session.commit()
                    finally:
                        # Restore stdout and stderr
                        sys.stdout = original_stdout
                        sys.stderr = original_stderr

    @app.route('/crawl/<int:blog_id>')
    def crawl_route(blog_id):
        scheduler.add_job(func=crawl_blog_job, args=[blog_id], id=f'crawl_job_{blog_id}', replace_existing=True)
        return redirect(url_for('index'))

    @app.route('/settings/<int:blog_id>', methods=['GET', 'POST'])
    def settings(blog_id):
        blog = Blog.query.get_or_404(blog_id)
        if request.method == 'POST':
            blog.download_photo = 'download_photo' in request.form
            blog.download_video = 'download_video' in request.form
            blog.download_audio = 'download_audio' in request.form
            blog.download_text = 'download_text' in request.form
            blog.download_quote = 'download_quote' in request.form
            blog.download_link = 'download_link' in request.form
            blog.download_chat = 'download_chat' in request.form
            blog.download_answer = 'download_answer' in request.form
            db.session.commit()
            return redirect(url_for('index'))
        return render_template('settings.html', blog=blog)

    @app.route('/gallery/<int:blog_id>')
    def gallery(blog_id):
        blog = Blog.query.get_or_404(blog_id)
        return render_template('gallery.html', blog=blog)

    @app.route('/maintenance')
    def maintenance():
        credential = TumblrCredential.query.first()
        return render_template('maintenance.html', credential=credential)

    @app.route('/maintenance/credentials', methods=['POST'])
    def update_credentials():
        consumer_key = request.form['consumer_key']
        consumer_secret = request.form['consumer_secret']
        access_token = request.form['access_token']
        access_token_secret = request.form['access_token_secret']

        credential = TumblrCredential.query.first()
        if not credential:
            credential = TumblrCredential()
            db.session.add(credential)

        credential.consumer_key = consumer_key
        credential.consumer_secret = consumer_secret
        credential.access_token = access_token
        credential.access_token_secret = access_token_secret

        db.session.commit()
        return redirect(url_for('maintenance'))

    @app.route('/maintenance/init-db', methods=['POST'])
    def init_db_route():
        with app.app_context():
            db.create_all()
        print('Initialized the database.')
        return redirect(url_for('maintenance'))

    @app.route('/status')
    def status():
        blogs = Blog.query.all()
        status_data = {
            blog.id: {
                'status': blog.status,
                'progress': f"{blog.progress or 0:.2f}%",
                'posts_crawled': len(blog.posts),
                'posts_total': blog.posts_total
            } for blog in blogs
        }
        return jsonify(status_data)

    @app.route('/log/<int:blog_id>')
    def log(blog_id):
        blog = Blog.query.get_or_404(blog_id)
        log_file_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'logs', f'{blog.name}.log')
        try:
            with open(log_file_path, 'r') as f:
                return f.read()
        except FileNotFoundError:
            return "Log file not found."

    return app

if __name__ == '__main__':
    app = create_app()
    app.run(debug=True)
