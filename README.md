# TumblThree - Python Web Version

This is a Python-based web application for downloading and backing up Tumblr blogs. It is a conversion of the original [TumblThree](https://github.com/TumblThreeApp/TumblThree) C# WPF application.

The application provides a web interface to manage a list of blogs to be crawled. It uses a MySQL database to store blog information and downloaded post metadata, and Celery for running the crawling and downloading tasks in the background.

## Project Status

This project is currently under development. For a detailed overview of the implemented features and the road map for future development, please see the [TODO.md](TODO.md) file.

## How to Run the Application

### Prerequisites

-   Python 3
-   MySQL server
-   Redis server

### Setup

1.  **Clone the repository:**
    ```bash
    git clone <repository-url>
    cd <repository-name>
    ```

2.  **Install Python dependencies:**
    It is recommended to use a virtual environment.
    ```bash
    # Navigate to the application directory
    cd tumblthree_py

    # Create and activate a virtual environment
    python3 -m venv venv
    source venv/bin/activate

    # Install dependencies
    pip install -r requirements.txt
    ```

3.  **Setup the database**:
    -   Connect to your MySQL server and create a new database named `tumblthree`.
    -   The application will create the tables automatically when it starts.

4.  **Set up the environment variables**:
    -   You must set the `DATABASE_URI` environment variable to your MySQL connection string. For example:
        ```bash
        export DATABASE_URI="mysql+mysqlconnector://user:password@localhost/tumblthree"
        ```
        Replace `user` and `password` with your MySQL credentials.
    -   You can also set `CELERY_BROKER_URL` and `CELERY_RESULT_BACKEND` if your Redis server is not running on the default port.

### Running the Application

1.  **Run the Flask application**:
    *   Open a terminal, navigate to the `tumblthree_py` directory, and run:
        ```bash
        python3 app.py
        ```
    *   The web server will start on `http://127.0.0.1:5000`.

2.  **Run the Celery worker**:
    *   Open a new terminal, navigate to the `tumblthree_py` directory, and run:
        ```bash
        celery -A app.celery worker --loglevel=info
        ```
    *   The Celery worker will now be running and ready to process background tasks.

### Use the application

*   Open your web browser and go to `http://127.0.0.1:5000`.
*   You can add blogs, crawl them, and manage settings through the web interface.
