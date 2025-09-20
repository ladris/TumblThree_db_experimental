# TumblThree Python Version - Project Status and TODO

This document outlines the current status of the TumblThree Python conversion project, including what has been implemented, the technical architecture, and a list of features that are still needed to achieve full functionality.

## Implemented Features

The current version of the application provides a solid foundation for the new TumblThree. The following features have been implemented:

-   **Web-based User Interface**: A simple web interface built with Flask allows users to manage blogs and settings.
-   **MySQL Database Backend**: The application uses a MySQL database to store all blog metadata and post information, replacing the old file-based system.
-   **Blog Management**: Users can add, view, edit, and delete blogs through the web interface.
-   **Background Crawling**: Crawling and downloading tasks are run in the background using Celery and Redis, ensuring the web interface remains responsive.
-   **Basic Tumblr Crawler**: A basic crawler for Tumblr blogs has been implemented. It can fetch photo and video posts, as well as images from regular text posts.
-   **File Downloader**: A concurrent downloader saves files to blog-specific directories.
-   **Settings Management**: A basic settings page allows for managing application-wide settings.

## How it Works

The application is built on a modern Python web stack:

-   **Flask**: A lightweight web framework used for the web interface and the backend API.
-   **SQLAlchemy**: A powerful ORM (Object-Relational Mapper) that handles all interactions with the MySQL database.
-   **Celery**: A distributed task queue used to run the crawling and downloading tasks asynchronously in the background.
-   **Redis**: A message broker used by Celery to manage the task queue.

## Missing Features (Future Tasks)

To achieve full feature parity with the original C# application, the following features need to be implemented. This list can be used as a basis for future development tasks.

### 1. Enhanced Crawlers

The current crawler is very basic. The following crawlers need to be ported from the C# application:

-   [ ] `TumblrLikedByCrawler`: To download posts liked by a user.
-   [ ] `TumblrHiddenCrawler`: To download posts from hidden Tumblr blogs.
-   [ ] `TumblrTagSearchCrawler`: To download posts matching a specific tag.
-   [ ] `TumblrSearchCrawler`: To download posts from a Tumblr search query.
-   [ ] `TwitterCrawler`: To download media from Twitter profiles.
-   [ ] `NewTumblCrawler`: To download from NewTumbl blogs.
-   [ ] `BlueskyCrawler`: To download from Bluesky profiles.

### 2. Full Post Type Support

The current implementation only handles photos and videos in a basic way. Full support for all of Tumblr's post types is needed:

-   [ ] **Audio Posts**: Download audio files.
-   [ ] **Quote Posts**: Save the text of quote posts.
-   [ ] **Link Posts**: Save the content of link posts.
-   [ ] **Conversation Posts**: Save the text of conversation posts.
-   [ ] **Answer Posts**: Save the text of answer posts.
-   [ ] **Meta Posts**: Create metadata files for photo, video, and audio posts, as the original application did.

### 3. External Site Parsers

The application should be able to download media from external sites that are often linked in Tumblr posts:

-   [ ] Imgur
-   [ ] Webmshare
-   [ ] Uguu
-   [ ] CatBox

### 4. Advanced Blog Settings

The original application had a rich set of settings for each blog. These need to be added to the `Blog` model and integrated into the web interface:

-   [ ] Download settings for each post type (e.g., `downloadReplies`, `downloadRebloggedPosts`).
-   [ ] Filename template customization.
-   [ ] Date and tag-based filtering.
-   [ ] And many more from the original `Blog.cs` class.

### 5. Improved Web Interface

The current UI is functional but very basic. The following improvements are needed:

-   [ ] **View Downloaded Posts**: A page to view the posts that have been downloaded for a specific blog.
-   [ ] **Detailed Blog Settings Page**: A form to edit all the advanced settings for a blog.
-   [ ] **Real-time Progress**: Use JavaScript to periodically check the status of a crawling task and display the progress in the UI.
-   [ ] **Log Viewer**: A page to view the application logs.
-   [ ] **User Authentication**: A login system to protect access to the application.

### 6. Data Migration

-   [ ] A script to migrate existing data from the old JSON-based storage to the new MySQL database needs to be created.

### 7. Documentation

-   [ ] The documentation needs to be updated with more details about the API endpoints, the database schema, and how to extend the application with new crawlers.
