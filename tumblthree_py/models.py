from flask_sqlalchemy import SQLAlchemy
from sqlalchemy.dialects.sqlite import JSON

db = SQLAlchemy()

# Association table for the many-to-many relationship between posts and tags
post_tags = db.Table('post_tags',
    db.Column('post_id', db.Integer, db.ForeignKey('post.id'), primary_key=True),
    db.Column('tag_id', db.Integer, db.ForeignKey('tag.id'), primary_key=True)
)

class TumblrCredential(db.Model):
    id = db.Column(db.Integer, primary_key=True)
    consumer_key = db.Column(db.String(255), nullable=False)
    consumer_secret = db.Column(db.String(255), nullable=False)
    access_token = db.Column(db.String(255), nullable=False)
    access_token_secret = db.Column(db.String(255), nullable=False)

class Blog(db.Model):
    id = db.Column(db.Integer, primary_key=True)
    uuid = db.Column(db.String(255), nullable=False, unique=True)
    name = db.Column(db.String(255), nullable=False, unique=True)
    url = db.Column(db.String(255), nullable=False)
    title = db.Column(db.String(255))
    description = db.Column(db.Text)

    # From API /info
    ask = db.Column(db.Boolean)
    ask_anon = db.Column(db.Boolean)
    followed = db.Column(db.Boolean)
    likes = db.Column(db.Integer)
    is_blocked_from_primary = db.Column(db.Boolean)

    posts_total = db.Column(db.Integer) # 'posts' in API
    updated = db.Column(db.BigInteger)

    # App-specific fields
    last_id = db.Column(db.BigInteger) # for incremental crawling
    last_complete_crawl = db.Column(db.DateTime)
    status = db.Column(db.String(50), default='idle')  # idle, crawling, finished, error
    progress = db.Column(db.Integer, default=0)

    # Relationships
    posts = db.relationship('Post', backref='blog', lazy=True, cascade="all, delete-orphan")

    # Download settings
    download_photo = db.Column(db.Boolean, default=True)
    download_video = db.Column(db.Boolean, default=True)
    download_audio = db.Column(db.Boolean, default=True)
    download_text = db.Column(db.Boolean, default=True)
    download_quote = db.Column(db.Boolean, default=True)
    download_link = db.Column(db.Boolean, default=True)
    download_chat = db.Column(db.Boolean, default=True)
    download_answer = db.Column(db.Boolean, default=True)

class Post(db.Model):
    id = db.Column(db.BigInteger, primary_key=True, autoincrement=False) # Use Tumblr's post ID
    blog_id = db.Column(db.Integer, db.ForeignKey('blog.id'), nullable=False)

    # Common post fields from API
    post_url = db.Column(db.String(255), nullable=False)
    type = db.Column(db.String(50), nullable=False)
    timestamp = db.Column(db.BigInteger, nullable=False)
    date = db.Column(db.String(255))
    format = db.Column(db.String(50))
    reblog_key = db.Column(db.String(255))
    note_count = db.Column(db.Integer)

    # NPF data
    npf_data = db.Column(JSON)

    # Relationships
    tags = db.relationship('Tag', secondary=post_tags, lazy='subquery',
                           backref=db.backref('posts', lazy=True))
    files = db.relationship('File', backref='post', lazy=True, cascade="all, delete-orphan")

    # Polymorphic relationship for different post types
    __mapper_args__ = {
        'polymorphic_identity': 'post',
        'polymorphic_on': type
    }

class TextPost(Post):
    __tablename__ = 'text_post'
    id = db.Column(db.BigInteger, db.ForeignKey('post.id'), primary_key=True)
    title = db.Column(db.String(255))
    body = db.Column(db.Text)
    __mapper_args__ = {'polymorphic_identity': 'text'}

class PhotoPost(Post):
    __tablename__ = 'photo_post'
    id = db.Column(db.BigInteger, db.ForeignKey('post.id'), primary_key=True)
    caption = db.Column(db.Text)
    __mapper_args__ = {'polymorphic_identity': 'photo'}

class QuotePost(Post):
    __tablename__ = 'quote_post'
    id = db.Column(db.BigInteger, db.ForeignKey('post.id'), primary_key=True)
    text = db.Column(db.Text)
    source = db.Column(db.Text)
    __mapper_args__ = {'polymorphic_identity': 'quote'}

class LinkPost(Post):
    __tablename__ = 'link_post'
    id = db.Column(db.BigInteger, db.ForeignKey('post.id'), primary_key=True)
    title = db.Column(db.String(255))
    url = db.Column(db.String(255))
    description = db.Column(db.Text)
    __mapper_args__ = {'polymorphic_identity': 'link'}

class ChatPost(Post):
    __tablename__ = 'chat_post'
    id = db.Column(db.BigInteger, db.ForeignKey('post.id'), primary_key=True)
    title = db.Column(db.String(255))
    body = db.Column(db.Text)
    dialogue = db.Column(JSON)
    __mapper_args__ = {'polymorphic_identity': 'chat'}

class AudioPost(Post):
    __tablename__ = 'audio_post'
    id = db.Column(db.BigInteger, db.ForeignKey('post.id'), primary_key=True)
    caption = db.Column(db.Text)
    player = db.Column(db.Text)
    plays = db.Column(db.Integer)
    __mapper_args__ = {'polymorphic_identity': 'audio'}

class VideoPost(Post):
    __tablename__ = 'video_post'
    id = db.Column(db.BigInteger, db.ForeignKey('post.id'), primary_key=True)
    caption = db.Column(db.Text)
    player = db.Column(JSON)
    __mapper_args__ = {'polymorphic_identity': 'video'}

class AnswerPost(Post):
    __tablename__ = 'answer_post'
    id = db.Column(db.BigInteger, db.ForeignKey('post.id'), primary_key=True)
    asking_name = db.Column(db.String(255))
    asking_url = db.Column(db.String(255))
    question = db.Column(db.Text)
    answer = db.Column(db.Text)
    __mapper_args__ = {'polymorphic_identity': 'answer'}

class Tag(db.Model):
    id = db.Column(db.Integer, primary_key=True)
    name = db.Column(db.String(255), nullable=False, unique=True)

class File(db.Model):
    id = db.Column(db.Integer, primary_key=True)
    post_id = db.Column(db.BigInteger, db.ForeignKey('post.id'), nullable=False)
    url = db.Column(db.Text, nullable=False)
    filename = db.Column(db.Text, nullable=False)
    status = db.Column(db.String(20), default='downloaded', nullable=False) # downloaded, archived, deleted
