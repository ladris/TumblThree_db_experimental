document.addEventListener('DOMContentLoaded', () => {
    const blogsTableBody = document.querySelector('#blogs-table tbody');
    const addBlogForm = document.querySelector('#add-blog-form');

    // Fetch and display blogs
    const fetchBlogs = async () => {
        const response = await fetch('/api/blogs');
        const blogs = await response.json();

        if (blogsTableBody) {
            blogsTableBody.innerHTML = ''; // Clear existing rows

            blogs.forEach(blog => {
                const row = document.createElement('tr');
                row.innerHTML = `
                    <td><a href="/blog/${blog.id}">${blog.name}</a></td>
                    <td>${blog.url}</td>
                    <td>${blog.last_complete_crawl || 'Never'}</td>
                    <td>
                        <button class="crawl-btn" data-id="${blog.id}">Crawl</button>
                        <button class="delete-btn" data-id="${blog.id}">Delete</button>
                    </td>
                `;
                blogsTableBody.appendChild(row);
            });
        }
    };

    // Add a new blog
    if (addBlogForm) {
        addBlogForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const formData = new FormData(addBlogForm);
            const data = Object.fromEntries(formData.entries());

            await fetch('/api/blogs', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });

            addBlogForm.reset();
            fetchBlogs();
        });
    }

    // Handle crawl and delete buttons
    if (blogsTableBody) {
        blogsTableBody.addEventListener('click', async (e) => {
            if (e.target.classList.contains('crawl-btn')) {
                const blogId = e.target.dataset.id;
                const response = await fetch(`/api/blogs/${blogId}/crawl`, { method: 'POST' });
                const data = await response.json();
                alert(`Crawling started. Task ID: ${data.task_id}`);
            }

            if (e.target.classList.contains('delete-btn')) {
                const blogId = e.target.dataset.id;
                if (confirm('Are you sure you want to delete this blog?')) {
                    await fetch(`/api/blogs/${blogId}`, { method: 'DELETE' });
                    fetchBlogs();
                }
            }
        });
    }

    // Initial fetch
    if (blogsTableBody) {
        fetchBlogs();
    }

    // Settings page
    const settingsForm = document.querySelector('#settings-form');
    if (settingsForm) {
        // Fetch and display settings
        const fetchSettings = async () => {
            const response = await fetch('/api/settings');
            const settings = await response.json();

            const settingsFormContent = document.createElement('div');
            for (const key in settings) {
                const label = document.createElement('label');
                label.for = key;
                label.textContent = key;
                const input = document.createElement('input');
                input.type = 'text';
                input.id = key;
                input.name = key;
                input.value = settings[key];
                settingsFormContent.appendChild(label);
                settingsFormContent.appendChild(input);
            }
            const submitButton = document.createElement('button');
            submitButton.type = 'submit';
            submitButton.textContent = 'Save Settings';

            settingsForm.innerHTML = '';
            settingsForm.appendChild(settingsFormContent);
            settingsForm.appendChild(submitButton);

        };

        // Handle settings form submission
        settingsForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const formData = new FormData(settingsForm);
            const data = Object.fromEntries(formData.entries());

            await fetch('/api/settings', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });

            alert('Settings saved!');
        });

        fetchSettings();
    }
});
