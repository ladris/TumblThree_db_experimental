using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TumblThree.Applications.Properties; // For AppSettings, ISettingsProvider
using TumblThree.Domain.Database;
using TumblThree.Domain.Models.Blogs; // For Blog.BlogTypes (assuming it's still accessible like this)

namespace TumblThree.Applications.Services
{
    // Temporary DTOs for old structures

    public class OldBlogFileDto
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string Location { get; set; } // Path to "Index" folder
        public string ChildId { get; set; } // Path to _files.json or similar
        public ulong LastId { get; set; }
        public string Tags { get; set; }
        public string Notes { get; set; }
        public BlogTypes BlogType { get; set; } // Direct enum use
        public string Version { get; set; }
        public DateTime DateAdded { get; set; }
        public DateTime LastCompleteCrawl { get; set; }

        // Common boolean settings from old Blog.cs
        public bool DownloadPhoto { get; set; } = true;
        public bool DownloadVideo { get; set; } = true;
        public bool DownloadAudio { get; set; } = true;
        public bool DownloadText { get; set; } = true;
        public bool DownloadAnswer { get; set; } = true;
        public bool DownloadConversation { get; set; } = true;
        public bool DownloadLink { get; set; } = true;
        public bool DownloadQuote { get; set; } = true;
        public bool CreatePhotoMeta { get; set; } = false;
        public bool CreateVideoMeta { get; set; } = false;
        public bool CreateAudioMeta { get; set; } = false;
        public bool SkipGif { get; set; } = false;
        public bool DownloadRebloggedPosts { get; set; } = false;
        public bool GroupPhotoSets { get; set; } = false;
        public bool DownloadUrlList { get; set; } = false;
        public bool ForceRescan { get; set; } = false;
        public bool ForceSize { get; set; } = false;
        public bool DownloadVideoThumbnail { get; set; } = false;
        public bool DownloadImgur { get; set; } = false;
        public bool DownloadWebmshare { get; set; } = true;
        public bool DownloadUguu { get; set; } = true;
        public bool DownloadCatBox { get; set; } = true;
        public bool DownloadPages { get; set; } = false;
        public bool DumpCrawlerData { get; set; } = false;
        public bool SaveTextsIndividualFiles { get; set; } = false;
        public bool ZipCrawlerData { get; set; } = false;
        public bool CheckDirectoryForFiles { get; set; } = false;


        // Settings with specific types/names
        public string FilenameTemplate { get; set; } = "%f";
        public int PageSize { get; set; } = 250;
        public string DownloadFrom { get; set; } = string.Empty;
        public string DownloadTo { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string RegExPhotos { get; set; } = string.Empty;
        public string RegExVideos { get; set; } = string.Empty;
        
        // Assuming these were stored as strings or mapped from enums in older versions if not directly string
        public string PnjDownloadFormat { get; set; } // e.g., "Apng" or "Png"
        public string MetadataFormat { get; set; } // e.g., "Text"
        public string WebmshareType { get; set; } // e.g., "Mp4"
        public string UguuType { get; set; } // e.g., "Original"
        public string CatBoxType { get; set; } // e.g., "Original"
    }

    public class OldFileEntryDto
    {
        public string Link { get; set; }
        public string OriginalLink { get; set; }
        public string Filename { get; set; }
        // Timestamp from FileEntry is not in OldFilesFileDto, seems it was implicit or not stored per entry there
    }

    public class OldFilesFileDto
    {
        // Prioritizing "Entries" which was HashSet<FileEntry>
        // FileEntry had: FileName, Link, Url (OriginalLink)
        [JsonProperty("Entries")] // Handles case-sensitivity if JSON used lowercase "entries"
        public List<OldFileEntryDto> Entries { get; set; }

        // Fallback for very old "Links" (List<string>) format
        // If "Entries" is null after deserialization, this could be checked.
        // For simplicity, current DTO focuses on "Entries".
        // public List<string> Links { get; set; } 

        public string Version { get; set; }
        // Other root properties from old Files.cs if any (Name, Location, BlogType were there)
        public string Name { get; set; }
        public string Location { get; set; }
        public BlogTypes BlogType { get; set; }
    }


    public interface IMigrationService
    {
        bool IsMigrationNeeded();
        Task PerformMigrationAsync(IProgress<string> progressReport);
    }

    public class MigrationService : IMigrationService
    {
        private readonly DatabaseService _databaseService;
        private readonly IShellService _shellService;
        private readonly IEnvironmentService _environmentService;
        private readonly ISettingsProvider _settingsProvider;

        public MigrationService(DatabaseService databaseService, IShellService shellService, IEnvironmentService environmentService, ISettingsProvider settingsProvider)
        {
            _databaseService = databaseService;
            _shellService = shellService;
            _environmentService = environmentService;
            _settingsProvider = settingsProvider;
        }

        public bool IsMigrationNeeded()
        {
            // Updated flag name for this specific migration
            if (_shellService.Settings.MigrationToDbV1Completed) 
            {
                return false;
            }

            foreach (var collection in _shellService.Settings.Collections)
            {
                var indexPath = Path.Combine(collection.DownloadLocation, "Index");
                if (Directory.Exists(indexPath))
                {
                    // Check for old blog definition files (e.g., .tumblr, .twitter)
                    // This is a simplified check; more robust would be to check for specific patterns
                    // excluding _files.* and other known non-blog files.
                    var blogFiles = Directory.GetFiles(indexPath)
                                         .Where(file => !Path.GetFileName(file).Contains("_files.") && 
                                                        (Path.GetExtension(file).Equals(".tumblr", StringComparison.OrdinalIgnoreCase) ||
                                                         Path.GetExtension(file).Equals(".twitter", StringComparison.OrdinalIgnoreCase) ||
                                                         Path.GetExtension(file).Equals(".tlb", StringComparison.OrdinalIgnoreCase) ||
                                                         Path.GetExtension(file).Equals(".instagram", StringComparison.OrdinalIgnoreCase) || // Add other old types
                                                         Path.GetExtension(file).Equals(".newtumbl", StringComparison.OrdinalIgnoreCase) 
                                                         // Add more extensions if there were others for blog definitions
                                                         )
                                         );
                    if (blogFiles.Any())
                    {
                        return true; // Found old blog files, migration needed
                    }
                }
            }
            return false;
        }

        public async Task PerformMigrationAsync(IProgress<string> progressReport)
        {
            if (!IsMigrationNeeded())
            {
                progressReport.Report("Migration not needed or already completed.");
                return;
            }

            progressReport.Report("Starting data migration to centralized database...");
            var oldJsonFileIndexToNewBlogIdMap = new Dictionary<string, int>();

            // Step 1: Migrate Blog Definitions
            progressReport.Report("Migrating blog definitions...");
            foreach (var collection in _shellService.Settings.Collections)
            {
                var indexPath = Path.Combine(collection.DownloadLocation, "Index");
                if (!Directory.Exists(indexPath)) continue;

                var blogDefinitionFiles = Directory.GetFiles(indexPath)
                    .Where(file => !Path.GetFileName(file).Contains("_files.") &&
                                   (Domain.Models.Blogs.Blog.SupportedFileTypes.Any(ext => Path.GetExtension(file).Equals(ext, StringComparison.OrdinalIgnoreCase))
                                    // Ensure Blog.SupportedFileTypes exists and is relevant or use explicit list as above
                                   || Path.GetExtension(file).Equals(".tumblr", StringComparison.OrdinalIgnoreCase) // Example, refine this list
                                   )
                           );

                foreach (var blogFilePath in blogDefinitionFiles)
                {
                    try
                    {
                        progressReport.Report($"Processing blog file: {blogFilePath}");
                        string json = await Task.Run(() => File.ReadAllText(blogFilePath)); // Async file read
                        OldBlogFileDto oldBlog = JsonConvert.DeserializeObject<OldBlogFileDto>(json);

                        if (oldBlog == null || string.IsNullOrEmpty(oldBlog.Name))
                        {
                            progressReport.Report($"WARNING: Could not deserialize or name is empty for blog file: {blogFilePath}");
                            continue;
                        }
                        
                        if (_databaseService.GetBlogByName(oldBlog.Name) != null)
                        {
                            progressReport.Report($"Blog '{oldBlog.Name}' already exists in DB. Skipping definition.");
                            // Still need to map ChildId for file migration if it exists
                            if (!string.IsNullOrEmpty(oldBlog.ChildId) && Path.IsPathRooted(oldBlog.ChildId) && File.Exists(oldBlog.ChildId))
                            {
                                var existingDto = _databaseService.GetBlogByName(oldBlog.Name);
                                if (existingDto != null && !oldJsonFileIndexToNewBlogIdMap.ContainsKey(oldBlog.ChildId))
                                {
                                     oldJsonFileIndexToNewBlogIdMap[oldBlog.ChildId] = existingDto.BlogId;
                                }
                            }
                            continue;
                        }

                        var newBlogDto = new BlogDto
                        {
                            Name = oldBlog.Name,
                            Url = oldBlog.Url,
                            BlogType = oldBlog.BlogType.ToString(),
                            // Old Location was Index path, new DownloadLocation is parent of Index
                            DownloadLocation = Directory.GetParent(oldBlog.Location)?.FullName ?? collection.DownloadLocation, 
                            LastCrawledPostId = oldBlog.LastId.ToString(),
                            LastCrawledTimestamp = (oldBlog.LastCompleteCrawl == DateTime.MinValue) ? (long?)null : new DateTimeOffset(oldBlog.LastCompleteCrawl).ToUnixTimeSeconds(),
                            Notes = oldBlog.Notes,
                            OnlineStatus = true, // Assume online, can be updated later
                            Version = oldBlog.Version, // Or use current app version
                            AddedTimestamp = new DateTimeOffset(oldBlog.DateAdded).ToUnixTimeSeconds(),
                            Title = string.Empty, // Old format might not have Title/Description directly
                            Description = string.Empty
                        };

                        var settings = new BlogRuntimeSettings
                        {
                            DownloadPhoto = oldBlog.DownloadPhoto,
                            DownloadVideo = oldBlog.DownloadVideo,
                            DownloadAudio = oldBlog.DownloadAudio,
                            DownloadText = oldBlog.DownloadText,
                            DownloadAnswer = oldBlog.DownloadAnswer,
                            DownloadConversation = oldBlog.DownloadConversation,
                            DownloadLink = oldBlog.DownloadLink,
                            DownloadQuote = oldBlog.DownloadQuote,
                            CreatePhotoMeta = oldBlog.CreatePhotoMeta,
                            CreateVideoMeta = oldBlog.CreateVideoMeta,
                            CreateAudioMeta = oldBlog.CreateAudioMeta,
                            SkipGif = oldBlog.SkipGif,
                            DownloadRebloggedPosts = oldBlog.DownloadRebloggedPosts,
                            GroupPhotoSets = oldBlog.GroupPhotoSets,
                            DownloadUrlList = oldBlog.DownloadUrlList,
                            ForceRescan = oldBlog.ForceRescan,
                            ForceSize = oldBlog.ForceSize,
                            DownloadVideoThumbnail = oldBlog.DownloadVideoThumbnail,
                            DownloadImgur = oldBlog.DownloadImgur,
                            DownloadWebmshare = oldBlog.DownloadWebmshare,
                            DownloadUguu = oldBlog.DownloadUguu,
                            DownloadCatBox = oldBlog.DownloadCatBox,
                            DownloadPages = oldBlog.DownloadPages,
                            DumpCrawlerData = oldBlog.DumpCrawlerData,
                            SaveTextsIndividualFiles = oldBlog.SaveTextsIndividualFiles,
                            ZipCrawlerData = oldBlog.ZipCrawlerData,
                            CheckDirectoryForFiles = oldBlog.CheckDirectoryForFiles,
                            FilenameTemplate = oldBlog.FilenameTemplate,
                            PageSize = oldBlog.PageSize,
                            DownloadFrom = oldBlog.DownloadFrom,
                            DownloadTo = oldBlog.DownloadTo,
                            Password = oldBlog.Password,
                            RegExPhotos = oldBlog.RegExPhotos,
                            RegExVideos = oldBlog.RegExVideos
                        };
                        // Mapping for enum-like string settings
                        Enum.TryParse<PnjDownloadType>(oldBlog.PnjDownloadFormat, true, out var pnj); settings.PnjDownloadFormat = pnj;
                        Enum.TryParse<Domain.Models.MetadataType>(oldBlog.MetadataFormat, true, out var meta); settings.MetadataFormat = meta; // Ensure correct MetadataType enum
                        Enum.TryParse<WebmshareTypes>(oldBlog.WebmshareType, true, out var webm); settings.WebmshareType = webm;
                        Enum.TryParse<UguuTypes>(oldBlog.UguuType, true, out var uguu); settings.UguuType = uguu;
                        Enum.TryParse<CatBoxType>(oldBlog.CatBoxType, true, out var catbox); settings.CatBoxType = catbox;

                        newBlogDto.SettingsJson = JsonConvert.SerializeObject(settings);
                        int newBlogId = _databaseService.AddBlog(newBlogDto);

                        if (newBlogId > 0)
                        {
                            progressReport.Report($"Migrated blog definition: {oldBlog.Name} to BlogId: {newBlogId}");
                            // Ensure ChildId is a full path before using. Old data might have it relative.
                            // It seems ChildId was already a full path to the _files.json file.
                            if (!string.IsNullOrEmpty(oldBlog.ChildId) && Path.IsPathRooted(oldBlog.ChildId) && File.Exists(oldBlog.ChildId))
                            {
                                 if (!oldJsonFileIndexToNewBlogIdMap.ContainsKey(oldBlog.ChildId))
                                 {
                                     oldJsonFileIndexToNewBlogIdMap.Add(oldBlog.ChildId, newBlogId);
                                 }
                            }
                            else if (!string.IsNullOrEmpty(oldBlog.ChildId))
                            {
                                progressReport.Report($"WARNING: ChildId '{oldBlog.ChildId}' for blog '{oldBlog.Name}' is not a full path or file does not exist. Cannot migrate its files entries.");
                            }
                        }
                        else
                        {
                            progressReport.Report($"ERROR: Failed to add blog {oldBlog.Name} to database.");
                        }
                    }
                    catch (Exception ex)
                    {
                        progressReport.Report($"ERROR migrating blog file {blogFilePath}: {ex.Message}");
                    }
                }
            }

            // Step 2: Migrate File Entries
            progressReport.Report("Migrating file entries...");
            foreach (var entry in oldJsonFileIndexToNewBlogIdMap)
            {
                string oldFilesPath = entry.Key;
                int newBlogId = entry.Value;
                try
                {
                    if (!File.Exists(oldFilesPath))
                    {
                        progressReport.Report($"WARNING: Files file not found: {oldFilesPath} for new BlogId {newBlogId}. Skipping file entries.");
                        continue;
                    }
                    progressReport.Report($"Processing file entries from: {oldFilesPath} for BlogId {newBlogId}");
                    string json = await Task.Run(() => File.ReadAllText(oldFilesPath)); // Async file read
                    OldFilesFileDto oldFilesData = JsonConvert.DeserializeObject<OldFilesFileDto>(json);

                    if (oldFilesData?.Entries != null && oldFilesData.Entries.Any())
                    {
                        int filesMigratedCount = 0;
                        foreach (var fileEntry in oldFilesData.Entries)
                        {
                            if (string.IsNullOrEmpty(fileEntry.Link) || string.IsNullOrEmpty(fileEntry.Filename))
                            {
                                progressReport.Report($"WARNING: Skipping file entry with missing Link or Filename in {oldFilesPath}");
                                continue;
                            }
                            var newFileDto = new FileDto
                            {
                                BlogId = newBlogId,
                                Link = fileEntry.Link,
                                OriginalLink = fileEntry.OriginalLink, // Might be null
                                Filename = fileEntry.Filename,
                                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() // Old format didn't store per-file timestamp
                            };
                            // AddFile should handle potential duplicates (e.g. by Link for a given BlogId)
                            _databaseService.AddFile(newFileDto); 
                            filesMigratedCount++;
                        }
                        progressReport.Report($"Migrated {filesMigratedCount} file entries for blogId {newBlogId} from {oldFilesPath}");
                    }
                    else
                    {
                        progressReport.Report($"No entries found or 'Entries' field missing in {oldFilesPath}");
                    }
                }
                catch (Exception ex)
                {
                    progressReport.Report($"ERROR migrating files from {oldFilesPath}: {ex.Message}");
                }
            }

            // Step 3: Post-Migration Tasks
            progressReport.Report("Performing post-migration tasks...");
            try
            {
                foreach (var collection in _shellService.Settings.Collections)
                {
                    var indexPath = Path.Combine(collection.DownloadLocation, "Index");
                    if (Directory.Exists(indexPath))
                    {
                        var migratedIndexPath = Path.Combine(collection.DownloadLocation, "Index_migrated_to_db_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                        Directory.Move(indexPath, migratedIndexPath);
                        progressReport.Report($"Archived old index folder: {indexPath} to {migratedIndexPath}");
                    }
                }

                _shellService.Settings.MigrationToDbV1Completed = true; // Use the correct flag name
                _settingsProvider.SaveSettings(); // Assuming SaveSettings() knows the path or uses EnvironmentService
                progressReport.Report("Data migration completed successfully!");
            }
            catch (Exception ex)
            {
                progressReport.Report($"ERROR in post-migration tasks: {ex.Message}");
            }
        }
    }
}
