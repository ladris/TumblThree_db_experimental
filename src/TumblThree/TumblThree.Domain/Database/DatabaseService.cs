using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO; // Required for Path.GetDirectoryName

namespace TumblThree.Domain.Database
{
    public class DatabaseService
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        // Simple logger action
        public static Action<string> Logger { get; set; } = Console.WriteLine;

        public DatabaseService(string dbPath)
        {
            _dbPath = dbPath;
            _connectionString = $"Data Source={_dbPath}";

            // Ensure the directory for the database exists
            string directory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                Logger?.Invoke($"Created database directory: {directory}");
            }

            InitializeDatabase();
        }

        public void InitializeDatabase()
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();

                    // Blogs Table
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Blogs (
                            BlogId INTEGER PRIMARY KEY AUTOINCREMENT,
                            Name TEXT NOT NULL UNIQUE,
                            Url TEXT NOT NULL,
                            BlogType TEXT NOT NULL,
                            DownloadLocation TEXT NOT NULL,
                            LastCrawledPostId TEXT,
                            LastCrawledTimestamp INTEGER,
                            SettingsJson TEXT,
                            Notes TEXT,
                            OnlineStatus INTEGER DEFAULT 1,
                            Version TEXT,
                            AddedTimestamp INTEGER DEFAULT (STRFTIME('%s', 'now'))
                        );
                    ";
                    command.ExecuteNonQuery();
                    Logger?.Invoke("Blogs table created or already exists.");

                    // Files Table
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Files (
                            FileId INTEGER PRIMARY KEY AUTOINCREMENT,
                            BlogId INTEGER NOT NULL,
                            Link TEXT NOT NULL,
                            OriginalLink TEXT,
                            Filename TEXT NOT NULL,
                            Timestamp INTEGER DEFAULT (STRFTIME('%s', 'now')),
                            Md5Hash TEXT,
                            FileSize INTEGER,
                            AdditionalProperties TEXT,
                            FOREIGN KEY (BlogId) REFERENCES Blogs(BlogId) ON DELETE CASCADE,
                            CONSTRAINT UQ_BlogLink UNIQUE (BlogId, Link)
                        );
                    ";
                    command.ExecuteNonQuery();
                    Logger?.Invoke("Files table created or already exists.");

                    // Downloads Table
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Downloads (
                            DownloadId INTEGER PRIMARY KEY AUTOINCREMENT,
                            FileId INTEGER NOT NULL,
                            Status TEXT NOT NULL DEFAULT 'pending',
                            ProgressBytes INTEGER DEFAULT 0,
                            TotalBytes INTEGER DEFAULT 0,
                            LastAttemptTimestamp INTEGER DEFAULT (STRFTIME('%s', 'now')),
                            RetryCount INTEGER DEFAULT 0,
                            DownloadPriority INTEGER DEFAULT 0,
                            DownloadUrl TEXT,
                            FOREIGN KEY (FileId) REFERENCES Files(FileId) ON DELETE CASCADE
                        );
                    ";
                    command.ExecuteNonQuery();
                    Logger?.Invoke("Downloads table created or already exists.");

                    // Indexes for Blogs Table
                    command.CommandText = "CREATE INDEX IF NOT EXISTS IDX_Blogs_Name ON Blogs(Name);";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE INDEX IF NOT EXISTS IDX_Blogs_Url ON Blogs(Url);";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE INDEX IF NOT EXISTS IDX_Blogs_BlogType ON Blogs(BlogType);";
                    command.ExecuteNonQuery();
                    Logger?.Invoke("Indexes for Blogs table created or already exist.");

                    // Indexes for Files Table
                    command.CommandText = "CREATE INDEX IF NOT EXISTS IDX_Files_BlogId ON Files(BlogId);";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE INDEX IF NOT EXISTS IDX_Files_Link ON Files(Link);";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE INDEX IF NOT EXISTS IDX_Files_Filename ON Files(Filename);";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE INDEX IF NOT EXISTS IDX_Files_Md5Hash ON Files(Md5Hash);";
                    command.ExecuteNonQuery();
                    Logger?.Invoke("Indexes for Files table created or already exist.");

                    // Indexes for Downloads Table
                    command.CommandText = "CREATE INDEX IF NOT EXISTS IDX_Downloads_FileId ON Downloads(FileId);";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE INDEX IF NOT EXISTS IDX_Downloads_Status ON Downloads(Status);";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE INDEX IF NOT EXISTS IDX_Downloads_Priority ON Downloads(DownloadPriority);";
                    command.ExecuteNonQuery();
                    Logger?.Invoke("Indexes for Downloads table created or already exist.");
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error initializing database: {ex.Message}");
                // Depending on the application strategy, might rethrow or handle
            }
        }

        public int AddBlog(BlogDto blog)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO Blogs (Name, Url, BlogType, DownloadLocation, LastCrawledPostId, LastCrawledTimestamp, SettingsJson, Notes, OnlineStatus, Version, AddedTimestamp)
                        VALUES (@Name, @Url, @BlogType, @DownloadLocation, @LastCrawledPostId, @LastCrawledTimestamp, @SettingsJson, @Notes, @OnlineStatus, @Version, @AddedTimestamp);
                        SELECT last_insert_rowid();
                    ";
                    command.Parameters.AddWithValue("@Name", blog.Name);
                    command.Parameters.AddWithValue("@Url", blog.Url);
                    command.Parameters.AddWithValue("@BlogType", blog.BlogType);
                    command.Parameters.AddWithValue("@DownloadLocation", blog.DownloadLocation);
                    command.Parameters.AddWithValue("@LastCrawledPostId", blog.LastCrawledPostId ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@LastCrawledTimestamp", blog.LastCrawledTimestamp ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@SettingsJson", blog.SettingsJson ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Notes", blog.Notes ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@OnlineStatus", blog.OnlineStatus ? 1 : 0);
                    command.Parameters.AddWithValue("@Version", blog.Version ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@AddedTimestamp", blog.AddedTimestamp); // Assuming this is always provided

                    var result = command.ExecuteScalar();
                    return result != null ? Convert.ToInt32(result) : 0;
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error adding blog '{blog.Name}': {ex.Message}");
                return 0;
            }
        }

        public bool UpdateBlog(BlogDto blog)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        UPDATE Blogs SET
                            Name = @Name,
                            Url = @Url,
                            BlogType = @BlogType,
                            DownloadLocation = @DownloadLocation,
                            LastCrawledPostId = @LastCrawledPostId,
                            LastCrawledTimestamp = @LastCrawledTimestamp,
                            SettingsJson = @SettingsJson,
                            Notes = @Notes,
                            OnlineStatus = @OnlineStatus,
                            Version = @Version
                        WHERE BlogId = @BlogId;
                    ";
                    command.Parameters.AddWithValue("@Name", blog.Name);
                    command.Parameters.AddWithValue("@Url", blog.Url);
                    command.Parameters.AddWithValue("@BlogType", blog.BlogType);
                    command.Parameters.AddWithValue("@DownloadLocation", blog.DownloadLocation);
                    command.Parameters.AddWithValue("@LastCrawledPostId", blog.LastCrawledPostId ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@LastCrawledTimestamp", blog.LastCrawledTimestamp ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@SettingsJson", blog.SettingsJson ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Notes", blog.Notes ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@OnlineStatus", blog.OnlineStatus ? 1 : 0);
                    command.Parameters.AddWithValue("@Version", blog.Version ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@BlogId", blog.BlogId);

                    return command.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error updating blog ID '{blog.BlogId}': {ex.Message}");
                return false;
            }
        }

        public BlogDto GetBlog(int blogId)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT * FROM Blogs WHERE BlogId = @BlogId;";
                    command.Parameters.AddWithValue("@BlogId", blogId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return MapReaderToBlogDto(reader);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error getting blog ID '{blogId}': {ex.Message}");
            }
            return null;
        }

        public BlogDto GetBlogByName(string name)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT * FROM Blogs WHERE Name = @Name;";
                    command.Parameters.AddWithValue("@Name", name);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return MapReaderToBlogDto(reader);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error getting blog by name '{name}': {ex.Message}");
            }
            return null;
        }

        public IEnumerable<BlogDto> GetAllBlogs()
        {
            var blogs = new List<BlogDto>();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT * FROM Blogs;";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            blogs.Add(MapReaderToBlogDto(reader));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error getting all blogs: {ex.Message}");
            }
            return blogs;
        }

        public bool DeleteBlog(int blogId)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "DELETE FROM Blogs WHERE BlogId = @BlogId;";
                    command.Parameters.AddWithValue("@BlogId", blogId);
                    return command.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error deleting blog ID '{blogId}': {ex.Message}");
                return false;
            }
        }

        private BlogDto MapReaderToBlogDto(SqliteDataReader reader)
        {
            return new BlogDto
            {
                BlogId = Convert.ToInt32(reader["BlogId"]),
                Name = reader["Name"].ToString(),
                Url = reader["Url"].ToString(),
                BlogType = reader["BlogType"].ToString(),
                DownloadLocation = reader["DownloadLocation"].ToString(),
                LastCrawledPostId = reader["LastCrawledPostId"] != DBNull.Value ? reader["LastCrawledPostId"].ToString() : null,
                LastCrawledTimestamp = reader["LastCrawledTimestamp"] != DBNull.Value ? (long?)Convert.ToInt64(reader["LastCrawledTimestamp"]) : null,
                SettingsJson = reader["SettingsJson"] != DBNull.Value ? reader["SettingsJson"].ToString() : null,
                Notes = reader["Notes"] != DBNull.Value ? reader["Notes"].ToString() : null,
                OnlineStatus = Convert.ToInt32(reader["OnlineStatus"]) == 1,
                Version = reader["Version"] != DBNull.Value ? reader["Version"].ToString() : null,
                AddedTimestamp = Convert.ToInt64(reader["AddedTimestamp"])
            };
        }

        // File Operations

        public int AddFile(FileDto file)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO Files (BlogId, Link, OriginalLink, Filename, Timestamp, Md5Hash, FileSize, AdditionalProperties)
                        VALUES (@BlogId, @Link, @OriginalLink, @Filename, @Timestamp, @Md5Hash, @FileSize, @AdditionalProperties);
                        SELECT last_insert_rowid();
                    ";
                    command.Parameters.AddWithValue("@BlogId", file.BlogId);
                    command.Parameters.AddWithValue("@Link", file.Link);
                    command.Parameters.AddWithValue("@OriginalLink", file.OriginalLink ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Filename", file.Filename);
                    command.Parameters.AddWithValue("@Timestamp", file.Timestamp);
                    command.Parameters.AddWithValue("@Md5Hash", file.Md5Hash ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@FileSize", file.FileSize);
                    command.Parameters.AddWithValue("@AdditionalProperties", file.AdditionalProperties ?? (object)DBNull.Value);

                    var result = command.ExecuteScalar();
                    return result != null ? Convert.ToInt32(result) : 0;
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error adding file '{file.Filename}' for BlogId '{file.BlogId}': {ex.Message}");
                return 0;
            }
        }

        public FileDto GetFile(int fileId)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT * FROM Files WHERE FileId = @FileId;";
                    command.Parameters.AddWithValue("@FileId", fileId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return MapReaderToFileDto(reader);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error getting file ID '{fileId}': {ex.Message}");
            }
            return null;
        }

        public FileDto GetFileByBlogIdAndLink(int blogId, string link)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT * FROM Files WHERE BlogId = @BlogId AND Link = @Link;";
                    command.Parameters.AddWithValue("@BlogId", blogId);
                    command.Parameters.AddWithValue("@Link", link);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return MapReaderToFileDto(reader);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error getting file by BlogId '{blogId}' and Link '{link}': {ex.Message}");
            }
            return null;
        }

        public IEnumerable<FileDto> GetFilesForBlog(int blogId)
        {
            var files = new List<FileDto>();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT * FROM Files WHERE BlogId = @BlogId;";
                    command.Parameters.AddWithValue("@BlogId", blogId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            files.Add(MapReaderToFileDto(reader));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error getting files for BlogId '{blogId}': {ex.Message}");
            }
            return files;
        }

        public bool UpdateFile(FileDto file)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        UPDATE Files SET
                            BlogId = @BlogId,
                            Link = @Link,
                            OriginalLink = @OriginalLink,
                            Filename = @Filename,
                            Timestamp = @Timestamp,
                            Md5Hash = @Md5Hash,
                            FileSize = @FileSize,
                            AdditionalProperties = @AdditionalProperties
                        WHERE FileId = @FileId;
                    ";
                    command.Parameters.AddWithValue("@BlogId", file.BlogId);
                    command.Parameters.AddWithValue("@Link", file.Link);
                    command.Parameters.AddWithValue("@OriginalLink", file.OriginalLink ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Filename", file.Filename);
                    command.Parameters.AddWithValue("@Timestamp", file.Timestamp);
                    command.Parameters.AddWithValue("@Md5Hash", file.Md5Hash ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@FileSize", file.FileSize);
                    command.Parameters.AddWithValue("@AdditionalProperties", file.AdditionalProperties ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@FileId", file.FileId);

                    return command.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error updating file ID '{file.FileId}': {ex.Message}");
                return false;
            }
        }

        public bool DeleteFile(int fileId)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "DELETE FROM Files WHERE FileId = @FileId;";
                    command.Parameters.AddWithValue("@FileId", fileId);
                    return command.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error deleting file ID '{fileId}': {ex.Message}");
                return false;
            }
        }

        public bool CheckFileExists(int blogId, string link)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(1) FROM Files WHERE BlogId = @BlogId AND Link = @Link;";
                    command.Parameters.AddWithValue("@BlogId", blogId);
                    command.Parameters.AddWithValue("@Link", link);
                    var result = command.ExecuteScalar();
                    return result != null && Convert.ToInt32(result) > 0;
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error checking file existence for BlogId '{blogId}' and Link '{link}': {ex.Message}");
                return false;
            }
        }

        private FileDto MapReaderToFileDto(SqliteDataReader reader)
        {
            return new FileDto
            {
                FileId = Convert.ToInt32(reader["FileId"]),
                BlogId = Convert.ToInt32(reader["BlogId"]),
                Link = reader["Link"].ToString(),
                OriginalLink = reader["OriginalLink"] != DBNull.Value ? reader["OriginalLink"].ToString() : null,
                Filename = reader["Filename"].ToString(),
                Timestamp = Convert.ToInt64(reader["Timestamp"]),
                Md5Hash = reader["Md5Hash"] != DBNull.Value ? reader["Md5Hash"].ToString() : null,
                FileSize = reader["FileSize"] != DBNull.Value ? Convert.ToInt64(reader["FileSize"]) : 0,
                AdditionalProperties = reader["AdditionalProperties"] != DBNull.Value ? reader["AdditionalProperties"].ToString() : null
            };
        }

        // Download Operations

        public int AddDownload(DownloadDto download)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO Downloads (FileId, Status, ProgressBytes, TotalBytes, LastAttemptTimestamp, RetryCount, DownloadPriority, DownloadUrl)
                        VALUES (@FileId, @Status, @ProgressBytes, @TotalBytes, @LastAttemptTimestamp, @RetryCount, @DownloadPriority, @DownloadUrl);
                        SELECT last_insert_rowid();
                    ";
                    command.Parameters.AddWithValue("@FileId", download.FileId);
                    command.Parameters.AddWithValue("@Status", download.Status);
                    command.Parameters.AddWithValue("@ProgressBytes", download.ProgressBytes);
                    command.Parameters.AddWithValue("@TotalBytes", download.TotalBytes);
                    command.Parameters.AddWithValue("@LastAttemptTimestamp", download.LastAttemptTimestamp);
                    command.Parameters.AddWithValue("@RetryCount", download.RetryCount);
                    command.Parameters.AddWithValue("@DownloadPriority", download.DownloadPriority);
                    command.Parameters.AddWithValue("@DownloadUrl", download.DownloadUrl ?? (object)DBNull.Value);

                    var result = command.ExecuteScalar();
                    return result != null ? Convert.ToInt32(result) : 0;
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error adding download for FileId '{download.FileId}': {ex.Message}");
                return 0;
            }
        }

        public bool UpdateDownload(DownloadDto download) // This can be used for full updates
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        UPDATE Downloads SET
                            FileId = @FileId,
                            Status = @Status,
                            ProgressBytes = @ProgressBytes,
                            TotalBytes = @TotalBytes,
                            LastAttemptTimestamp = @LastAttemptTimestamp,
                            RetryCount = @RetryCount,
                            DownloadPriority = @DownloadPriority,
                            DownloadUrl = @DownloadUrl
                        WHERE DownloadId = @DownloadId;
                    ";
                    command.Parameters.AddWithValue("@FileId", download.FileId);
                    command.Parameters.AddWithValue("@Status", download.Status);
                    command.Parameters.AddWithValue("@ProgressBytes", download.ProgressBytes);
                    command.Parameters.AddWithValue("@TotalBytes", download.TotalBytes);
                    command.Parameters.AddWithValue("@LastAttemptTimestamp", download.LastAttemptTimestamp);
                    command.Parameters.AddWithValue("@RetryCount", download.RetryCount);
                    command.Parameters.AddWithValue("@DownloadPriority", download.DownloadPriority);
                    command.Parameters.AddWithValue("@DownloadUrl", download.DownloadUrl ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@DownloadId", download.DownloadId);

                    return command.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error updating download ID '{download.DownloadId}': {ex.Message}");
                return false;
            }
        }

        // New method for progress and status
        public void UpdateDownloadProgressAndStatus(int downloadId, long progressBytes, long totalBytes, string status, long lastAttemptTimestamp)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        UPDATE Downloads SET
                            ProgressBytes = @ProgressBytes,
                            TotalBytes = @TotalBytes,
                            Status = @Status,
                            LastAttemptTimestamp = @LastAttemptTimestamp
                        WHERE DownloadId = @DownloadId;
                    ";
                    command.Parameters.AddWithValue("@ProgressBytes", progressBytes);
                    command.Parameters.AddWithValue("@TotalBytes", totalBytes);
                    command.Parameters.AddWithValue("@Status", status);
                    command.Parameters.AddWithValue("@LastAttemptTimestamp", lastAttemptTimestamp);
                    command.Parameters.AddWithValue("@DownloadId", downloadId);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error in UpdateDownloadProgressAndStatus for DownloadId '{downloadId}': {ex.Message}");
            }
        }

        // Modified method for just status
        public void UpdateDownloadStatus(int downloadId, string status, long lastAttemptTimestamp)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        UPDATE Downloads SET
                            Status = @Status,
                            LastAttemptTimestamp = @LastAttemptTimestamp
                        WHERE DownloadId = @DownloadId;
                    ";
                    command.Parameters.AddWithValue("@Status", status);
                    command.Parameters.AddWithValue("@LastAttemptTimestamp", lastAttemptTimestamp);
                    command.Parameters.AddWithValue("@DownloadId", downloadId);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error in UpdateDownloadStatus for DownloadId '{downloadId}': {ex.Message}");
            }
        }
        
        // New method name, keeps functionality clear
        public void IncrementDownloadRetry(int downloadId, long lastAttemptTimestamp)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        UPDATE Downloads SET
                            RetryCount = RetryCount + 1,
                            LastAttemptTimestamp = @LastAttemptTimestamp
                        WHERE DownloadId = @DownloadId;
                    ";
                    command.Parameters.AddWithValue("@LastAttemptTimestamp", lastAttemptTimestamp);
                    command.Parameters.AddWithValue("@DownloadId", downloadId);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error in IncrementDownloadRetry for DownloadId '{downloadId}': {ex.Message}");
            }
        }

        public DownloadDto GetDownload(int downloadId)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        SELECT d.*, f.Filename, f.Link, f.BlogId 
                        FROM Downloads d
                        JOIN Files f ON d.FileId = f.FileId
                        WHERE d.DownloadId = @DownloadId;
                    ";
                    command.Parameters.AddWithValue("@DownloadId", downloadId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return MapReaderToDownloadDto(reader, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error getting download ID '{downloadId}': {ex.Message}");
            }
            return null;
        }

        public IEnumerable<DownloadDto> GetPendingDownloads(int? blogId = null)
        {
            var downloads = new List<DownloadDto>();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    var sql = @"
                        SELECT d.*, f.Filename, f.Link, f.BlogId 
                        FROM Downloads d
                        JOIN Files f ON d.FileId = f.FileId
                        WHERE (d.Status = 'pending' OR (d.Status = 'error' AND d.RetryCount < 5))
                    ";
                    if (blogId.HasValue)
                    {
                        sql += " AND f.BlogId = @BlogId";
                        command.Parameters.AddWithValue("@BlogId", blogId.Value);
                    }
                    sql += ";";
                    command.CommandText = sql;

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            downloads.Add(MapReaderToDownloadDto(reader, true));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error getting pending downloads (BlogId: {blogId?.ToString() ?? "All"}): {ex.Message}");
            }
            return downloads;
        }

        public bool DeleteDownload(int downloadId)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "DELETE FROM Downloads WHERE DownloadId = @DownloadId;";
                    command.Parameters.AddWithValue("@DownloadId", downloadId);
                    return command.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error deleting download ID '{downloadId}': {ex.Message}");
                return false;
            }
        }

        private DownloadDto MapReaderToDownloadDto(SqliteDataReader reader, bool includeFileData = false)
        {
            var dto = new DownloadDto
            {
                DownloadId = Convert.ToInt32(reader["DownloadId"]),
                FileId = Convert.ToInt32(reader["FileId"]),
                Status = reader["Status"].ToString(),
                ProgressBytes = Convert.ToInt64(reader["ProgressBytes"]),
                TotalBytes = Convert.ToInt64(reader["TotalBytes"]),
                LastAttemptTimestamp = Convert.ToInt64(reader["LastAttemptTimestamp"]),
                RetryCount = Convert.ToInt32(reader["RetryCount"]),
                DownloadPriority = Convert.ToInt32(reader["DownloadPriority"]),
                DownloadUrl = reader["DownloadUrl"] != DBNull.Value ? reader["DownloadUrl"].ToString() : null
            };

            if (includeFileData)
            {
                // Check if columns exist before reading, to handle cases where they might not be selected
                // For this specific use case, they are always selected in GetDownload and GetPendingDownloads.
                dto.Filename = reader["Filename"].ToString();
                dto.Link = reader["Link"].ToString();
                dto.BlogId = Convert.ToInt32(reader["BlogId"]);
            }
            return dto;
        }

        // Method to update FileSize in Files table
        public void UpdateFileSizeInFilesTable(int fileId, long fileSize)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        UPDATE Files SET
                            FileSize = @FileSize
                        WHERE FileId = @FileId;
                    ";
                    command.Parameters.AddWithValue("@FileSize", fileSize);
                    command.Parameters.AddWithValue("@FileId", fileId);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Error updating FileSize for FileId '{fileId}': {ex.Message}");
            }
        }
    }
}
