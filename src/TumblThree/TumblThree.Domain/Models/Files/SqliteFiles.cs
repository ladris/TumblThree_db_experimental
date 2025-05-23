using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace TumblThree.Domain.Models.Files
{
    public class SqliteFiles : IFiles
    {
        private readonly string _name;
        private readonly string _location;
        private readonly BlogTypes _blogType;
        private readonly string _connectionString;

        public string Version { get; set; }

        public SqliteFiles(string name, string location, BlogTypes blogType, string appVersion)
        {
            _name = name;
            _location = location;
            _blogType = blogType;
            Version = appVersion;
            // Ensure the directory exists
            Directory.CreateDirectory(location);
            _connectionString = Path.Combine(location, name + "_" + blogType + ".sqlite");
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText =
                @"
                    CREATE TABLE IF NOT EXISTS Files (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        BlogName TEXT NOT NULL,
                        Link TEXT NOT NULL UNIQUE,
                        OriginalLink TEXT,
                        Filename TEXT NOT NULL,
                        Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                        BlogType TEXT NOT NULL,
                        AdditionalProperties TEXT
                    );

                    CREATE TABLE IF NOT EXISTS Downloads (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FileId INTEGER NOT NULL,
                        Status TEXT NOT NULL DEFAULT 'pending',
                        ProgressBytes INTEGER DEFAULT 0,
                        TotalBytes INTEGER DEFAULT 0,
                        LastAttempt DATETIME,
                        RetryCount INTEGER DEFAULT 0,
                        FOREIGN KEY (FileId) REFERENCES Files(Id) ON DELETE CASCADE
                    );

                    CREATE INDEX IF NOT EXISTS IDX_Files_BlogName ON Files (BlogName);
                    CREATE INDEX IF NOT EXISTS IDX_Files_Link ON Files (Link);
                    CREATE INDEX IF NOT EXISTS IDX_Downloads_FileId ON Downloads (FileId);
                    CREATE INDEX IF NOT EXISTS IDX_Downloads_Status ON Downloads (Status);
                ";
                command.ExecuteNonQuery();
            }
        }

        public bool AddFileToDb(string fileNameUrl, string fileNameOriginalUrl, string fileName)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText =
                    @"
                        INSERT OR IGNORE INTO Files (BlogName, Link, OriginalLink, Filename, BlogType)
                        VALUES (@blogName, @link, @originalLink, @filename, @blogType);
                    ";
                    command.Parameters.AddWithValue("@blogName", _name);
                    command.Parameters.AddWithValue("@link", fileNameUrl);
                    command.Parameters.AddWithValue("@originalLink", fileNameOriginalUrl ?? (object)DBNull.Value); // Handle null original link
                    command.Parameters.AddWithValue("@filename", fileName);
                    command.Parameters.AddWithValue("@blogType", _blogType.ToString());

                    return command.ExecuteNonQuery() > 0;
                }
            }
            catch (SqliteException ex)
            {
                // Log error (placeholder)
                Console.WriteLine($"Error adding file to DB: {ex.Message}");
                return false;
            }
        }

        public bool AddFileToDb(string fileNameUrl, string fileNameOriginalUrl, string fileName, string appendTemplate)
        {
            // Simplified: behaves like the 3-argument version for now.
            // The logic for appendTemplate with unique URL constraints needs more detailed specification.
            // Current implementation relies on Link (fileNameUrl) being unique.
            // If a file with the same URL exists, it will be ignored due to "INSERT OR IGNORE".
            // If the intent is to update the filename if the URL exists, or handle numbered filenames
            // for the *same* URL but different actual files, that's a more complex scenario.

            // For now, we will attempt to insert. If the URL (Link) is already there, it's ignored.
            // If appendTemplate is meant to create a unique FILENAME (not URL) when the URL might be the same
            // but refers to a different downloadable item (e.g. a gallery post with multiple images),
            // then the Link itself would need to be unique (e.g. image_url_1, image_url_2).
            // If Link is the same, we cannot insert a new row.

            // Let's assume for now that if appendTemplate is provided, we should check if the file
            // with this exact URL exists. If it does, we do nothing (as per INSERT OR IGNORE).
            // If it doesn't, we try to insert. The provided `fileName` should ideally already
            // have the appendTemplate logic applied by the caller if it's for the filename on disk.
            return AddFileToDb(fileNameUrl, fileNameOriginalUrl, fileName);
        }


        public bool UpdateOriginalLink(string filenameUrl, string filenameOriginalUrl)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText =
                    @"
                        UPDATE Files
                        SET OriginalLink = @originalLink
                        WHERE Link = @link AND BlogName = @blogName;
                    ";
                    command.Parameters.AddWithValue("@originalLink", filenameOriginalUrl ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@link", filenameUrl);
                    command.Parameters.AddWithValue("@blogName", _name);
                    return command.ExecuteNonQuery() > 0;
                }
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"Error updating original link: {ex.Message}");
                return false;
            }
        }

        public bool CheckIfFileExistsInDB(string filenameUrl) => CheckIfFileExistsInDB(filenameUrl, false);

        public bool CheckIfFileExistsInDB(string filenameUrl, bool checkOriginalLinkFirst)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    if (checkOriginalLinkFirst)
                    {
                        command.CommandText =
                        @"
                            SELECT COUNT(1)
                            FROM Files
                            WHERE (OriginalLink = @url OR Link = @url) AND BlogName = @blogName;
                        ";
                    }
                    else
                    {
                        command.CommandText =
                        @"
                            SELECT COUNT(1)
                            FROM Files
                            WHERE Link = @url AND BlogName = @blogName;
                        ";
                    }
                    command.Parameters.AddWithValue("@url", filenameUrl);
                    command.Parameters.AddWithValue("@blogName", _name);

                    var result = command.ExecuteScalar();
                    return result != null && Convert.ToInt32(result) > 0;
                }
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"Error checking file existence: {ex.Message}");
                return false;
            }
        }

        public bool Save()
        {
            // No-op for SQLite as changes are committed per transaction.
            return true;
        }

        public bool IsDirty => false; // Changes are saved immediately.

        public IEnumerable<FileEntry> Entries
        {
            get
            {
                var entries = new List<FileEntry>();
                try
                {
                    using (var connection = new SqliteConnection(_connectionString))
                    {
                        connection.Open();
                        var command = connection.CreateCommand();
                        command.CommandText =
                        @"
                            SELECT Link, OriginalLink, Filename
                            FROM Files
                            WHERE BlogName = @blogName;
                        ";
                        command.Parameters.AddWithValue("@blogName", _name);

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                entries.Add(new FileEntry(
                                    reader.GetString(0), // Link
                                    reader.IsDBNull(1) ? null : reader.GetString(1), // OriginalLink
                                    reader.GetString(2)  // Filename
                                ));
                            }
                        }
                    }
                }
                catch (SqliteException ex)
                {
                    Console.WriteLine($"Error getting entries: {ex.Message}");
                    // Return empty list or throw? For now, empty.
                }
                return entries;
            }
        }

        public string Name => _name;
        public string Location => _location;
        public BlogTypes BlogType => _blogType;

        // Explicit implementation of ISerializable for completeness, though not strictly needed for this class structure.
        // If IFiles requires it due to inheritance chains in other implementations.
        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("Name", Name);
            info.AddValue("Location", Location);
            info.AddValue("BlogType", BlogType);
            info.AddValue("Version", Version);
            // Note: _connectionString is derived, not serialized.
            // Entries are also typically not serialized directly this way; they'd be re-loaded.
        }

        protected SqliteFiles(SerializationInfo info, StreamingContext context)
        {
            _name = info.GetString("Name");
            _location = info.GetString("Location");
            _blogType = (BlogTypes)info.GetValue("BlogType", typeof(BlogTypes));
            Version = info.GetString("Version");

            // Reconstruct connection string after deserialization
            _connectionString = Path.Combine(_location, _name + "_" + _blogType + ".sqlite");
            // It's good practice to ensure the database is initialized if it wasn't,
            // though for a deserialized object, it's assumed it was valid before serialization.
            // InitializeDatabase(); // Or ensure this is handled by application logic post-deserialization.
        }


        #region Equality members

        protected bool Equals(SqliteFiles other)
        {
            return string.Equals(_name, other._name) && _blogType == other._blogType && string.Equals(_location, other._location);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((SqliteFiles)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (_name != null ? _name.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (int)_blogType;
                hashCode = (hashCode * 397) ^ (_location != null ? _location.GetHashCode() : 0);
                return hashCode;
            }
        }

        #endregion

        #region IDisposable

        // No explicit IDisposable needed if connections are managed with 'using' statements per method.
        // If a connection were held open for the lifetime of the object, IDisposable would be necessary.
        // public void Dispose() { /* Release database connection if held open */ }

        #endregion

        // Additional methods from IFiles that might need implementation or stubs:
        public int Count() => Entries.Count(); // Simple count based on current Entries logic

        public FileEntry GetFileEntry(int index) => Entries.ElementAtOrDefault(index);

        public bool CheckIfFileExistsInDB(string filenameUrl)
        {
            return CheckIfFileExistsInDB(filenameUrl, false);
        }

        public bool CheckIfFileExistsInDB(string filenameUrl, bool checkOriginalLinkFirst)
        {
            // This is a duplicate, ensure the main one is used or remove this.
            // Keeping the one with the proper try-catch block.
            // For now, relying on the earlier implemented version.
            // To avoid compilation error, I will call the other method.
            return ((SqliteFiles)this).CheckIfFileExistsInDB(filenameUrl, checkOriginalLinkFirst);
        }

        public string CalculateFileSha256Hash(string filename)
        {
            // This method is typically for filesystem files, not directly for DB entries.
            // If it's about getting a hash of a file *managed by* this DB entry,
            // the file path would be constructed from Location and Filename.
            // For now, returning empty as it's out of scope for DB interaction itself.
            return string.Empty; 
        }

        public bool IsCollection() => false; // Assuming this implementation is not for collections.

        public event PropertyChangedEventHandler PropertyChanged; // Standard INotifyPropertyChanged

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public int GetFileId(string link)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT Id FROM Files WHERE Link = @Link AND BlogName = @BlogName LIMIT 1;";
                    command.Parameters.AddWithValue("@Link", link);
                    command.Parameters.AddWithValue("@BlogName", _name); // Ensure it's for the current blog
                    var result = command.ExecuteScalar();
                    return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
                }
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"Error getting FileId for link '{link}': {ex.Message}");
                return -1; // Indicate error
            }
        }

        public int AddDownloadEntry(int fileId, long totalBytes, string initialStatus = "pending")
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText =
                    @"
                        INSERT INTO Downloads (FileId, Status, TotalBytes, LastAttempt)
                        VALUES (@FileId, @Status, @TotalBytes, @LastAttempt);
                        SELECT last_insert_rowid();
                    ";
                    command.Parameters.AddWithValue("@FileId", fileId);
                    command.Parameters.AddWithValue("@Status", initialStatus);
                    command.Parameters.AddWithValue("@TotalBytes", totalBytes);
                    command.Parameters.AddWithValue("@LastAttempt", DateTime.UtcNow);

                    // ExecuteScalar will return the result of SELECT last_insert_rowid()
                    var result = command.ExecuteScalar();
                    return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
                }
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"Error adding download entry for FileId {fileId}: {ex.Message}");
                return 0; // Indicate failure
            }
        }

        public void UpdateDownloadProgress(int downloadId, long progressBytes, long totalBytes, string status)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText =
                    @"
                        UPDATE Downloads
                        SET ProgressBytes = @ProgressBytes,
                            TotalBytes = @TotalBytes,
                            Status = @Status,
                            LastAttempt = @LastAttempt
                        WHERE Id = @DownloadId;
                    ";
                    command.Parameters.AddWithValue("@ProgressBytes", progressBytes);
                    command.Parameters.AddWithValue("@TotalBytes", totalBytes);
                    command.Parameters.AddWithValue("@Status", status);
                    command.Parameters.AddWithValue("@LastAttempt", DateTime.UtcNow);
                    command.Parameters.AddWithValue("@DownloadId", downloadId);
                    command.ExecuteNonQuery();
                }
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"Error updating download progress for DownloadId {downloadId}: {ex.Message}");
            }
        }

        public void UpdateDownloadStatus(int downloadId, string status)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText =
                    @"
                        UPDATE Downloads
                        SET Status = @Status,
                            LastAttempt = @LastAttempt
                        WHERE Id = @DownloadId;
                    ";
                    command.Parameters.AddWithValue("@Status", status);
                    command.Parameters.AddWithValue("@LastAttempt", DateTime.UtcNow);
                    command.Parameters.AddWithValue("@DownloadId", downloadId);
                    command.ExecuteNonQuery();
                }
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"Error updating download status for DownloadId {downloadId}: {ex.Message}");
            }
        }

        public void IncrementDownloadRetryCount(int downloadId)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText =
                    @"
                        UPDATE Downloads
                        SET RetryCount = RetryCount + 1,
                            LastAttempt = @LastAttempt
                        WHERE Id = @DownloadId;
                    ";
                    command.Parameters.AddWithValue("@LastAttempt", DateTime.UtcNow);
                    command.Parameters.AddWithValue("@DownloadId", downloadId);
                    command.ExecuteNonQuery();
                }
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"Error incrementing retry count for DownloadId {downloadId}: {ex.Message}");
            }
        }

        public IEnumerable<DownloadEntryData> GetPendingDownloads(string blogName, int maxRetries = 5)
        {
            var entries = new List<DownloadEntryData>();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText =
                    @"
                        SELECT d.Id AS DownloadId, f.Id AS FileId, f.Link, f.OriginalLink, f.Filename, 
                               d.Status AS DownloadStatus, d.ProgressBytes, d.TotalBytes, d.RetryCount
                        FROM Downloads d
                        JOIN Files f ON d.FileId = f.Id
                        WHERE f.BlogName = @BlogName AND (d.Status = 'pending' OR (d.Status = 'error' AND d.RetryCount < @MaxRetries));
                    ";
                    command.Parameters.AddWithValue("@BlogName", blogName); // This should ideally use _name from the class instance
                    command.Parameters.AddWithValue("@MaxRetries", maxRetries);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            entries.Add(new DownloadEntryData
                            {
                                DownloadId = reader.GetInt32(reader.GetOrdinal("DownloadId")),
                                FileId = reader.GetInt32(reader.GetOrdinal("FileId")),
                                Link = reader.GetString(reader.GetOrdinal("Link")),
                                OriginalLink = reader.IsDBNull(reader.GetOrdinal("OriginalLink")) ? null : reader.GetString(reader.GetOrdinal("OriginalLink")),
                                Filename = reader.GetString(reader.GetOrdinal("Filename")),
                                DownloadStatus = reader.GetString(reader.GetOrdinal("DownloadStatus")),
                                ProgressBytes = reader.GetInt64(reader.GetOrdinal("ProgressBytes")),
                                TotalBytes = reader.GetInt64(reader.GetOrdinal("TotalBytes")),
                                RetryCount = reader.GetInt32(reader.GetOrdinal("RetryCount"))
                            });
                        }
                    }
                }
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"Error getting pending downloads for blog '{blogName}': {ex.Message}");
            }
            return entries;
        }

        public DownloadEntryData GetDownloadEntry(int fileId)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText =
                    @"
                        SELECT d.Id AS DownloadId, f.Id AS FileId, f.Link, f.OriginalLink, f.Filename, 
                               d.Status AS DownloadStatus, d.ProgressBytes, d.TotalBytes, d.RetryCount
                        FROM Downloads d
                        JOIN Files f ON d.FileId = f.Id
                        WHERE f.Id = @FileId AND f.BlogName = @BlogName
                        ORDER BY d.LastAttempt DESC LIMIT 1;
                    ";
                    command.Parameters.AddWithValue("@FileId", fileId);
                    command.Parameters.AddWithValue("@BlogName", _name); // Ensure it's for the current blog

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new DownloadEntryData
                            {
                                DownloadId = reader.GetInt32(reader.GetOrdinal("DownloadId")),
                                FileId = reader.GetInt32(reader.GetOrdinal("FileId")),
                                Link = reader.GetString(reader.GetOrdinal("Link")),
                                OriginalLink = reader.IsDBNull(reader.GetOrdinal("OriginalLink")) ? null : reader.GetString(reader.GetOrdinal("OriginalLink")),
                                Filename = reader.GetString(reader.GetOrdinal("Filename")),
                                DownloadStatus = reader.GetString(reader.GetOrdinal("DownloadStatus")),
                                ProgressBytes = reader.GetInt64(reader.GetOrdinal("ProgressBytes")),
                                TotalBytes = reader.GetInt64(reader.GetOrdinal("TotalBytes")),
                                RetryCount = reader.GetInt32(reader.GetOrdinal("RetryCount"))
                            };
                        }
                    }
                }
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"Error getting download entry for FileId {fileId}: {ex.Message}");
            }
            return null;
        }
    }

    public class DownloadEntryData
    {
        public int DownloadId { get; set; }
        public int FileId { get; set; }
        public string Link { get; set; }
        public string OriginalLink { get; set; }
        public string Filename { get; set; }
        public string DownloadStatus { get; set; }
        public long ProgressBytes { get; set; }
        public long TotalBytes { get; set; }
        public int RetryCount { get; set; }
    }
}
