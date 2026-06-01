using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Waf.Foundation;

using Microsoft.Data.Sqlite;

namespace TumblThree.Domain.Models.Files
{
    /// <summary>
    /// SQLite-backed implementation of the per-blog "files" de-duplication database.
    ///
    /// This replaces the legacy <see cref="Files"/> implementation that loaded the whole
    /// HashSet of entries from a JSON document into memory and re-serialized the entire
    /// document on every save.
    ///
    /// Design goals (speed first, low memory second):
    ///   * The hot path is <see cref="CheckIfFileExistsInDB"/>, called once per candidate
    ///     download. The common answer is "no, not downloaded yet". We answer that case
    ///     with a pure in-RAM lookup against a <see cref="HashSet{Int64}"/> of 64-bit FNV-1a
    ///     hashes (~8 bytes/entry, no string retention, zero disk I/O).
    ///   * Only on a positive hash hit do we touch SQLite, confirming the exact string via a
    ///     unique index. This rules out the astronomically rare hash collision so we never
    ///     skip a download by mistake.
    ///   * Writes are buffered inside a single open transaction and flushed on <see cref="Save"/>
    ///     (the existing 120 s autosave cadence) instead of rewriting a whole JSON file.
    ///   * The database file lives at the SAME path as the legacy "{name}_files.{type}" file
    ///     (SQLite does not care about the extension). That keeps blog discovery, ChildId,
    ///     and the archive-copy mechanism working unchanged, and avoids a second file that
    ///     the discovery globs would load twice.
    /// </summary>
    public sealed class SqliteFiles : Model, IFiles, IDisposable
    {
        internal const int SQLITE_SCHEMA_VERSION = 1;

        // 16-byte SQLite file magic: "SQLite format 3\0".
        private static readonly byte[] SqliteMagic = Encoding.ASCII.GetBytes("SQLite format 3\0");

        private readonly object _gate = new object();
        private readonly string _path;
        private readonly bool _readOnly;

        // In-RAM fast-negative indexes. Membership is approximate (hash-only); a positive
        // hit is always confirmed against the DB before being treated as "already present".
        private HashSet<long> _linkHashes = new HashSet<long>();
        private HashSet<long> _origHashes = new HashSet<long>();

        private SqliteConnection _conn;
        private SqliteTransaction _tx;

        private SqliteCommand _cmdExistsLink;
        private SqliteCommand _cmdExistsOrig;
        private SqliteCommand _cmdInsert;
        private SqliteCommand _cmdUpdateOrig;

        private bool _isDirty;
        private bool _disposed;
        private string _version = "6";

        private SqliteFiles(string path, string name, BlogTypes blogType, bool readOnly)
        {
            _path = path;
            Name = name;
            BlogType = blogType;
            _readOnly = readOnly;

            OpenConnection();
            EnsureSchema();
            if (!readOnly) WriteMeta();
            ReadMeta();
            LoadHashIndex();
        }

        #region IFiles

        public string Name { get; private set; }

        public BlogTypes BlogType { get; private set; }

        public string Version
        {
            get => _version;
            set => _version = value;
        }

        public bool IsDirty => _isDirty;

        /// <summary>
        /// Materialises every entry. Not used on the hot path (only for export / inspection);
        /// safe but potentially large, so it snapshots under the lock.
        /// </summary>
        public IEnumerable<FileEntry> Entries
        {
            get
            {
                var list = new List<FileEntry>();
                lock (_gate)
                {
                    if (_disposed) return list;
                    FlushNoLock();
                    using (var cmd = _conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT link, orig, fn FROM entry";
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                list.Add(new FileEntry
                                {
                                    Link = r.GetString(0),
                                    OriginalLinkSer = r.IsDBNull(1) ? null : r.GetString(1),
                                    FilenameSer = r.IsDBNull(2) ? null : r.GetString(2)
                                });
                            }
                        }
                    }
                }
                return list;
            }
        }

        public bool CheckIfFileExistsInDB(string filenameUrl, bool checkOriginalLinkFirst)
        {
            if (string.IsNullOrEmpty(filenameUrl)) return false;
            long h = Fnv1a64(filenameUrl);
            lock (_gate)
            {
                if (_disposed) return false;
                if (checkOriginalLinkFirst && _origHashes.Contains(h) && ExistsByColumn(_cmdExistsOrig, filenameUrl))
                {
                    return true;
                }
                if (_linkHashes.Contains(h) && ExistsByColumn(_cmdExistsLink, filenameUrl))
                {
                    return true;
                }
                return false;
            }
        }

        public void AddFileToDb(string fileNameUrl, string fileNameOriginalUrl, string fileName)
        {
            lock (_gate)
            {
                InsertEntryNoLock(fileNameUrl, fileNameOriginalUrl, fileName);
            }
        }

        public string AddFileToDb(string fileNameUrl, string fileNameOriginalUrl, string fileName, string appendTemplate)
        {
            lock (_gate)
            {
                int n = CountMatchingFilenamesNoLock(fileName, appendTemplate);
                if (n > 0)
                {
                    fileName = Path.GetFileNameWithoutExtension(fileName)
                               + appendTemplate.Replace("<0>", (n + 1).ToString())
                               + Path.GetExtension(fileName);
                }
                InsertEntryNoLock(fileNameUrl, fileNameOriginalUrl, fileName);
                return fileName;
            }
        }

        public void UpdateOriginalLink(string filenameUrl, string filenameOriginalUrl)
        {
            if (string.IsNullOrEmpty(filenameUrl)) return;
            lock (_gate)
            {
                if (_readOnly) return;
                string normOrig = NormalizeOrig(filenameOriginalUrl, filenameUrl);
                EnsureTransactionNoLock();

                _cmdUpdateOrig.Parameters["@l"].Value = filenameUrl;
                _cmdUpdateOrig.Parameters["@o"].Value = (object)normOrig ?? DBNull.Value;
                _cmdUpdateOrig.Transaction = _tx;
                int rows = _cmdUpdateOrig.ExecuteNonQuery();
                if (rows == 0)
                {
                    ExecInsertNoLock(filenameUrl, normOrig, null);
                }

                _linkHashes.Add(Fnv1a64(filenameUrl));
                if (normOrig != null) _origHashes.Add(Fnv1a64(normOrig));
                _isDirty = true;
            }
        }

        public bool Save()
        {
            lock (_gate)
            {
                if (_disposed || _readOnly) return true;
                try
                {
                    FlushNoLock();
                    Checkpoint();
                    _isDirty = false;
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Error("SqliteFiles:Save: {0}", ex);
                    throw;
                }
            }
        }

        #endregion

        #region write helpers

        private void InsertEntryNoLock(string url, string orig, string fileName)
        {
            if (_readOnly || string.IsNullOrEmpty(url)) return;
            string normOrig = NormalizeOrig(orig, url);
            string normFn = (fileName == url) ? null : fileName;

            ExecInsertNoLock(url, normOrig, normFn);

            _linkHashes.Add(Fnv1a64(url));
            if (normOrig != null) _origHashes.Add(Fnv1a64(normOrig));
            _isDirty = true;
        }

        private void ExecInsertNoLock(string url, string normOrig, string normFn)
        {
            EnsureTransactionNoLock();
            _cmdInsert.Parameters["@l"].Value = url;
            _cmdInsert.Parameters["@o"].Value = (object)normOrig ?? DBNull.Value;
            _cmdInsert.Parameters["@f"].Value = (object)normFn ?? DBNull.Value;
            _cmdInsert.Transaction = _tx;
            _cmdInsert.ExecuteNonQuery();
        }

        private bool ExistsByColumn(SqliteCommand cmd, string value)
        {
            cmd.Parameters["@v"].Value = value;
            cmd.Transaction = _tx; // see uncommitted rows on the same connection
            using (var r = cmd.ExecuteReader())
            {
                return r.Read();
            }
        }

        private int CountMatchingFilenamesNoLock(string fileName, string appendTemplate)
        {
            // Narrow the candidate set with a prefix LIKE on the base name, then apply the
            // exact (extension-aware) match in C#. Avoids scanning the whole table.
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            string like = EscapeLike(baseName) + "%";

            int count = 0;
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = _tx;
                cmd.CommandText = "SELECT link, fn FROM entry WHERE " +
                                  "(fn IS NOT NULL AND fn LIKE @p ESCAPE '\\') OR " +
                                  "(fn IS NULL AND link LIKE @p ESCAPE '\\')";
                cmd.Parameters.AddWithValue("@p", like);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string link = r.GetString(0);
                        string fn = r.IsDBNull(1) ? link : r.GetString(1);
                        if (IsMatch(fn, fileName, ext, baseName, appendTemplate)) count++;
                    }
                }
            }
            return count;
        }

        private static bool IsMatch(string candidateFilename, string fileName, string ext, string baseName, string appendTemplate)
        {
            if (candidateFilename == fileName) return true;
            if (string.Compare(Path.GetExtension(candidateFilename), ext, StringComparison.InvariantCultureIgnoreCase) != 0) return false;
            var pattern = Regex.Escape(baseName + appendTemplate).Replace("<0>", @"[\d]+");
            return Regex.IsMatch(Path.GetFileNameWithoutExtension(candidateFilename), pattern);
        }

        private void EnsureTransactionNoLock()
        {
            if (_tx == null) _tx = _conn.BeginTransaction();
        }

        private void FlushNoLock()
        {
            if (_tx != null)
            {
                _tx.Commit();
                _tx.Dispose();
                _tx = null;
            }
        }

        private void Checkpoint()
        {
            // Keep the main db file current and the WAL small so the archive-copy mechanism
            // (plain File.Copy of the db) snapshots a consistent state. Best-effort.
            try
            {
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Logger.Verbose("SqliteFiles:Checkpoint: {0}", ex.Message);
            }
        }

        #endregion

        #region connection / schema

        private void OpenConnection()
        {
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = _path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            };
            _conn = new SqliteConnection(csb.ToString());
            _conn.Open();

            ExecPragma("PRAGMA journal_mode=WAL;");
            ExecPragma("PRAGMA synchronous=NORMAL;");
            ExecPragma("PRAGMA temp_store=MEMORY;");
            ExecPragma("PRAGMA mmap_size=268435456;");   // 256 MB memory-mapped I/O
            ExecPragma("PRAGMA cache_size=-16384;");      // ~16 MB page cache
            ExecPragma("PRAGMA busy_timeout=10000;");
        }

        private void ExecPragma(string sql)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        private void EnsureSchema()
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS meta(k TEXT PRIMARY KEY, v TEXT);" +
                    "CREATE TABLE IF NOT EXISTS entry(link TEXT NOT NULL, orig TEXT, fn TEXT);" +
                    "CREATE UNIQUE INDEX IF NOT EXISTS ux_entry_link ON entry(link);" +
                    "CREATE INDEX IF NOT EXISTS ix_entry_orig ON entry(orig) WHERE orig IS NOT NULL;";
                cmd.ExecuteNonQuery();
            }

            // Prepared, reusable commands for the hot paths.
            _cmdExistsLink = _conn.CreateCommand();
            _cmdExistsLink.CommandText = "SELECT 1 FROM entry WHERE link=@v LIMIT 1;";
            _cmdExistsLink.Parameters.Add("@v", SqliteType.Text);

            _cmdExistsOrig = _conn.CreateCommand();
            _cmdExistsOrig.CommandText = "SELECT 1 FROM entry WHERE orig=@v LIMIT 1;";
            _cmdExistsOrig.Parameters.Add("@v", SqliteType.Text);

            _cmdInsert = _conn.CreateCommand();
            _cmdInsert.CommandText = "INSERT OR IGNORE INTO entry(link, orig, fn) VALUES(@l, @o, @f);";
            _cmdInsert.Parameters.Add("@l", SqliteType.Text);
            _cmdInsert.Parameters.Add("@o", SqliteType.Text);
            _cmdInsert.Parameters.Add("@f", SqliteType.Text);

            _cmdUpdateOrig = _conn.CreateCommand();
            _cmdUpdateOrig.CommandText = "UPDATE entry SET orig=@o WHERE link=@l;";
            _cmdUpdateOrig.Parameters.Add("@l", SqliteType.Text);
            _cmdUpdateOrig.Parameters.Add("@o", SqliteType.Text);
        }

        private void WriteMeta()
        {
            // Insert-only: the stored metadata of an existing database is authoritative and must
            // not be clobbered by values parsed from the file name on a subsequent open.
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT OR IGNORE INTO meta(k, v) VALUES('name', @name);" +
                    "INSERT OR IGNORE INTO meta(k, v) VALUES('blogtype', @blogtype);" +
                    "INSERT OR IGNORE INTO meta(k, v) VALUES('schema', @schema);" +
                    "INSERT OR IGNORE INTO meta(k, v) VALUES('version', @version);";
                cmd.Parameters.AddWithValue("@name", (object)Name ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@blogtype", BlogType.ToString());
                cmd.Parameters.AddWithValue("@schema", SQLITE_SCHEMA_VERSION.ToString());
                cmd.Parameters.AddWithValue("@version", _version);
                cmd.ExecuteNonQuery();
            }
        }

        private void ReadMeta()
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT k, v FROM meta";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string k = r.GetString(0);
                        string v = r.IsDBNull(1) ? null : r.GetString(1);
                        if (k == "name" && !string.IsNullOrEmpty(v)) Name = v;
                        else if (k == "blogtype" && Enum.TryParse(v, out BlogTypes bt)) BlogType = bt;
                        else if (k == "version" && !string.IsNullOrEmpty(v)) _version = v;
                    }
                }
            }
        }

        private void LoadHashIndex()
        {
            var links = new HashSet<long>();
            var origs = new HashSet<long>();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT link, orig FROM entry";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        links.Add(Fnv1a64(r.GetString(0)));
                        if (!r.IsDBNull(1)) origs.Add(Fnv1a64(r.GetString(1)));
                    }
                }
            }
            _linkHashes = links;
            _origHashes = origs;
        }

        private void BulkImport(IEnumerable<FileEntry> entries)
        {
            EnsureTransactionNoLock();
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.Link)) continue;
                string normOrig = NormalizeOrig(e.OriginalLink, e.Link);
                string normFn = (e.Filename == e.Link) ? null : e.Filename;
                _cmdInsert.Parameters["@l"].Value = e.Link;
                _cmdInsert.Parameters["@o"].Value = (object)normOrig ?? DBNull.Value;
                _cmdInsert.Parameters["@f"].Value = (object)normFn ?? DBNull.Value;
                _cmdInsert.Transaction = _tx;
                _cmdInsert.ExecuteNonQuery();
            }
            _isDirty = true;
        }

        #endregion

        #region static factory / migration

        /// <summary>Opens an existing database, creates an empty one, or migrates a legacy JSON file in place.</summary>
        public static IFiles LoadOrMigrate(string path, int bufferSizeKB)
        {
            ParseFilename(path, out string name, out BlogTypes blogType);

            if (!File.Exists(path))
            {
                return new SqliteFiles(path, name, blogType, false);
            }
            if (IsSqliteFile(path))
            {
                return new SqliteFiles(path, name, blogType, false);
            }
            return Migrate(path, bufferSizeKB);
        }

        /// <summary>Creates (or opens) the database for a brand-new blog.</summary>
        public static IFiles CreateNew(string name, string location, BlogTypes blogType)
        {
            string path = Path.Combine(location, name + "_files." + blogType);
            Directory.CreateDirectory(location);
            if (!File.Exists(path))
            {
                return new SqliteFiles(path, name, blogType, false);
            }
            return LoadOrMigrate(path, 4);
        }

        /// <summary>Opens an archive (read-only) database snapshot.</summary>
        public static IFiles OpenReadOnly(string path)
        {
            ParseFilename(path, out string name, out BlogTypes blogType);
            return new SqliteFiles(path, name, blogType, true);
        }

        public static bool IsSqliteFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                var header = new byte[16];
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Read(header, 0, 16) < 16) return false;
                }
                for (int i = 0; i < 16; i++)
                {
                    if (header[i] != SqliteMagic[i]) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IFiles Migrate(string path, int bufferSizeKB)
        {
            Logger.Information("SqliteFiles: migrating legacy JSON database '{0}' to SQLite.", path);

            IFiles legacy = Files.LoadLegacyJson(path, bufferSizeKB, isArchive: true);
            string name = legacy.Name;
            BlogTypes blogType = legacy.BlogType;

            string tmp = path + ".sqlite.tmp";
            DeleteIfExists(tmp);
            DeleteIfExists(tmp + "-wal");
            DeleteIfExists(tmp + "-shm");

            var built = new SqliteFiles(tmp, name, blogType, false);
            try
            {
                lock (built._gate)
                {
                    built.BulkImport(legacy.Entries);
                    built.FlushNoLock();
                    built.Checkpoint();
                }
            }
            finally
            {
                built.Dispose();
            }

            // Move the legacy JSON out of the way (keep as a backup) and promote the SQLite db
            // into the canonical path so discovery globs see exactly one file.
            string dir = Path.GetDirectoryName(path);
            string backupDir = Path.Combine(dir, "_legacy_json_backup");
            Directory.CreateDirectory(backupDir);
            string backupPath = Path.Combine(backupDir, Path.GetFileName(path));
            if (File.Exists(backupPath)) backupPath += "." + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Move(path, backupPath);

            MoveIfExists(tmp + "-wal", path + "-wal");
            MoveIfExists(tmp + "-shm", path + "-shm");
            File.Move(tmp, path);

            return new SqliteFiles(path, name, blogType, false);
        }

        private static void ParseFilename(string path, out string name, out BlogTypes blogType)
        {
            string fileName = Path.GetFileName(path);
            string ext = Path.GetExtension(fileName);            // ".tumblr"
            _ = Enum.TryParse(ext.TrimStart('.'), out blogType);

            string withoutExt = Path.GetFileNameWithoutExtension(fileName); // "{name}_files"
            const string marker = "_files";
            name = withoutExt.EndsWith(marker, StringComparison.Ordinal)
                ? withoutExt.Substring(0, withoutExt.Length - marker.Length)
                : withoutExt;
        }

        private static void DeleteIfExists(string p)
        {
            if (File.Exists(p)) File.Delete(p);
        }

        private static void MoveIfExists(string from, string to)
        {
            if (File.Exists(from))
            {
                DeleteIfExists(to);
                File.Move(from, to);
            }
        }

        #endregion

        #region helpers

        private static string NormalizeOrig(string orig, string link)
        {
            return (string.IsNullOrEmpty(orig) || orig == link) ? null : orig;
        }

        private static string EscapeLike(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
        }

        /// <summary>FNV-1a 64-bit hash. Fast, allocation-free, good distribution for URLs/filenames.</summary>
        internal static long Fnv1a64(string s)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                hash ^= (byte)(c & 0xFF);
                hash *= prime;
                hash ^= (byte)(c >> 8);
                hash *= prime;
            }
            return unchecked((long)hash);
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                try { FlushNoLock(); } catch (Exception ex) { Logger.Verbose("SqliteFiles:Dispose flush: {0}", ex.Message); }
                _cmdExistsLink?.Dispose();
                _cmdExistsOrig?.Dispose();
                _cmdInsert?.Dispose();
                _cmdUpdateOrig?.Dispose();
                _conn?.Dispose();
            }
        }
    }
}
