using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using TumblThree.Applications.DataModels;
using TumblThree.Applications.DataModels.TumblrPosts;
using TumblThree.Applications.Properties;
using TumblThree.Applications.Services;
using TumblThree.Domain;
using TumblThree.Domain.Database; // Added
using TumblThree.Domain.Models.Blogs;
//using TumblThree.Domain.Models.Files; // Removed IFiles dependency

namespace TumblThree.Applications.Downloader
{
    public abstract class AbstractDownloader : IDownloader, IDisposable
    {
        protected readonly IBlog blog;
        // protected readonly IFiles files; // Removed
        protected readonly DatabaseService databaseService; // Added
        protected readonly ICrawlerService crawlerService;
        protected readonly IManagerService managerService;
        protected readonly IProgress<DownloadProgress> progress;
        protected readonly object lockObjectDownload = new object();
        protected readonly IPostQueue<AbstractPost> postQueue;
        protected readonly IShellService shellService;
        protected CancellationToken ct;
        protected readonly PauseToken pt;
        protected readonly FileDownloader fileDownloader;
        private readonly string[] suffixes = { ".jpg", ".jpeg", ".png", ".tiff", ".tif", ".heif", ".heic", ".webp" };
        //private readonly object _saveTimerLock = new object(); // Removed
        //private Timer _saveTimer; // Removed
        private volatile bool _disposed;
        //private const int SAVE_TIMESPAN_SECS = 120; // Removed

        private SemaphoreSlim concurrentConnectionsSemaphore;
        private SemaphoreSlim concurrentVideoConnectionsSemaphore;
        private readonly Dictionary<string, StreamWriterWithInfo> streamWriters = new Dictionary<string, StreamWriterWithInfo>();
        private readonly object diskFilesLock = new object();
        private HashSet<string> diskFiles;

        protected AbstractDownloader(IShellService shellService, IManagerService managerService, CancellationToken ct, PauseToken pt, 
                                     IProgress<DownloadProgress> progress, IPostQueue<AbstractPost> postQueue, 
                                     FileDownloader fileDownloader, DatabaseService databaseService, /* Added */
                                     ICrawlerService crawlerService = null, IBlog blog = null /* IFiles files = null Removed */)
        {
            this.shellService = shellService;
            this.crawlerService = crawlerService;
            this.managerService = managerService;
            this.databaseService = databaseService; // Added
            this.blog = blog;
            //this.files = files; // Removed
            this.ct = ct;
            this.pt = pt;
            this.progress = progress;
            this.postQueue = postQueue;
            this.fileDownloader = fileDownloader;
            //Progress<Exception> prog = new Progress<Exception>((e) => shellService.ShowError(e, Resources.CouldNotSaveBlog, blog.Name)); // Removed
            //_saveTimer = new Timer(_ => OnSaveTimedEvent(prog), null, SAVE_TIMESPAN_SECS * 1000, SAVE_TIMESPAN_SECS * 1000); // Removed
        }

        public string AppendTemplate { get; set; } // This is likely a blog setting now, review if it should be here or from blog.Settings

        public void UpdateProgressQueueInformation(string format, params object[] args)
        {
            var newProgress = new DownloadProgress
            {
                Progress = string.Format(CultureInfo.CurrentCulture, format, args)
            };
            progress.Report(newProgress);
        }

        public void ChangeCancellationToken(CancellationToken ct)
        {
            this.ct = ct;
        }

        // Modified signature
        protected virtual async Task<(bool result, string fileLocation)> DownloadBinaryFileAsync(string fileLocation, string url, int downloadId, int blogId)
        {
            try
            {
                if (url.EndsWith("playlist.m3u8")) // m3u8 might need special handling for downloadId/blogId if it makes sub-requests
                {
                    // For now, assuming DownloadVideoPlaylist either uses the main downloadId or doesn't interact with DB for sub-parts yet
                    return await DownloadVideoPlaylist(url, fileLocation, downloadId, blogId); 
                }
                else
                {
                    // Pass downloadId, blogId, and databaseService to FileDownloader
                    return await fileDownloader.DownloadFileWithResumeAsync(url, fileLocation, downloadId, blogId, this.databaseService).ConfigureAwait(false);
                }
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == 0x27 || (ex.HResult & 0xFFFF) == 0x70)
            {
                // Disk Full, HRESULT: ‭-2147024784‬ == 0xFFFFFFFF80070070
                Logger.Error("AbstractDownloader:DownloadBinaryFile: {0}", ex);
                shellService.ShowError(ex, Resources.DiskFull);
                crawlerService.StopCommand.Execute(null);
                throw;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == 0x20)
            {
                // The process cannot access the file because it is being used by another process.", HRESULT: -2147024864 == 0xFFFFFFFF80070020
                return (true, fileLocation);
            }
            catch (WebException webException) when (webException.Response != null)
            {
                var webRespStatusCode = (int)((HttpWebResponse)webException.Response).StatusCode;
                if (webRespStatusCode >= 400 && webRespStatusCode < 600) // removes inaccessible files: http status codes 400 to 599
                {
                    try
                    {
                        File.Delete(fileLocation);
                    } // could be open again in a different thread
                    catch
                    {
                    }
                }

                return (false, fileLocation);
            }
            catch (TimeoutException timeoutException)
            {
                Logger.Error("AbstractDownloader:DownloadBinaryFileAsync: {0}", timeoutException);
                shellService.ShowError(timeoutException, Resources.TimeoutReached, Resources.Downloading, blog.Name);
                throw;
            }
        }

        // This overload might need re-evaluation or careful adaptation if DownloadUrlList setting is still used.
        // For now, assuming the primary path is the 4-argument version.
        // If it's kept, it needs to somehow get downloadId and blogId or this path cannot update DB.
        protected virtual async Task<(bool result, string fileLocation)> DownloadBinaryFileAsync(string fileLocation, string fileLocationUrlList, string url, int downloadId, int blogId)
        {
            if (!blog.Settings.DownloadUrlList) // Access setting via blog.Settings
            {
                return await DownloadBinaryFileAsync(fileLocation, url, downloadId, blogId);
            }

            // Appending to a text file doesn't fit the new DB download tracking model well for this specific file.
            // This path might mean the URL itself is the "downloaded" content.
            // Consider how to represent this in Downloads table. For now, marking as completed.
            bool appendResult = AppendToTextFile(fileLocationUrlList, url, false);
            if (appendResult)
            {
                databaseService.UpdateDownloadStatus(downloadId, "completed", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }
            return (appendResult, fileLocation);
        }

        protected virtual bool AppendToTextFile(string fileLocation, string text, bool isJson)
        {
            try
            {
                lock (lockObjectDownload)
                {
                    StreamWriterWithInfo sw = GetTextAppenderStreamWriter(fileLocation, isJson);
                    sw.WriteLine(text);
                }
                return true;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == 0x27 || (ex.HResult & 0xFFFF) == 0x70)
            {
                Logger.Error("AbstractDownloader:AppendToTextFile: {0}", ex);
                shellService.ShowError(ex, Resources.DiskFull);
                crawlerService.StopCommand.Execute(null);
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("AbstractDownloader:AppendToTextFile: {0}", ex);
                return false;
            }
        }

        // Added downloadId and blogId for potential DB interaction in sub-downloads
        private async Task<(bool result, string fileLocation)> DownloadVideoPlaylist(string url, string fileLocation, int downloadId, int blogId)
        {
            var playlist = await DownloadPageAsync(url); // This doesn't use downloadId/blogId itself

            // extract different video sizes from playlist
            playlist = Regex.Replace(playlist, @"\r\n?|\n", Environment.NewLine);
            var lines = playlist.Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
            var videoUrlsList = new SortedList<long, string>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("#EXT-X-STREAM-INF", StringComparison.InvariantCultureIgnoreCase) && i + 1 < lines.Length)
                {
                    var m = Regex.Match(lines[i], "BANDWIDTH=([0-9]+),");
                    var bandwidth = m.Success ? int.Parse(m.Groups[1].Value) : i;
                    var newUrl = string.Join("/", url.Split('/').Take(url.Split('/').Length - 1)) + "/" + lines[i + 1];
                    videoUrlsList.Add(bandwidth, newUrl);
                }
            }

            // download playlist with video parts
            var partsPlaylistUrl = videoUrlsList.Last().Value;
            var partsPlaylist = await DownloadPageAsync(partsPlaylistUrl);
            partsPlaylist = Regex.Replace(partsPlaylist, @"\r\n?|\n", Environment.NewLine);
            lines = partsPlaylist.Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
            
            // download all video parts and concat them
            using (var fs = new FileStream(fileLocation, FileMode.Create, FileAccess.Write))
            {
                foreach (var line in lines)
                {
                    if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;
                    var newUrl = string.Join("/", partsPlaylistUrl.Split('/').Take(partsPlaylistUrl.Split('/').Length - 1)) + "/" + line;
                    if (File.Exists(fileLocation + ".tmp")) File.Delete(fileLocation + ".tmp");
                    // If DownloadFileWithResumeAsync for parts needs to update DB, it needs downloadId/blogId.
                    // This implies either one main downloadId for the playlist, or new ones for each part.
                    // For simplicity, using the main downloadId and not creating separate DB entries for parts.
                    // The progress here would be for the entire playlist file, not individual parts.
                    var result = await fileDownloader.DownloadFileWithResumeAsync(newUrl, fileLocation + ".tmp", downloadId, blogId, this.databaseService);
                    if (!result.result) return (false, result.destinationPath);
                    using (var fs2 = File.OpenRead(fileLocation + ".tmp"))
                    {
                        await fs2.CopyToAsync(fs);
                    }
                    File.Delete(fileLocation + ".tmp");
                }
            }

            return (true, fileLocation);
        }

        private StreamWriterWithInfo GetTextAppenderStreamWriter(string key, bool isJson)
        {
            if (streamWriters.ContainsKey(key))
            {
                return streamWriters[key];
            }
            StreamWriterWithInfo sw = new StreamWriterWithInfo(key, true, isJson);
            streamWriters.Add(key, sw);

            return sw;
        }

        private bool WriteToTextFile(string fileLocation, string text, bool isJson)
        {
            try
            {
                using (StreamWriterWithInfo sw = new StreamWriterWithInfo(fileLocation, false, isJson))
                {
                    sw.WriteLine(text);
                }
                return true;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == 0x27 || (ex.HResult & 0xFFFF) == 0x70)
            {
                Logger.Error("AbstractDownloader:WriteToTextFile: {0}", ex);
                shellService.ShowError(ex, Resources.DiskFull);
                crawlerService.StopCommand.Execute(null);
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("AbstractDownloader:WriteToTextFile: {0}", ex);
                return false;
            }
        }

        public virtual async Task<bool> DownloadBlogAsync()
        {
            concurrentConnectionsSemaphore = new SemaphoreSlim(shellService.Settings.ConcurrentConnections / crawlerService.ActiveItems.Count);
            concurrentVideoConnectionsSemaphore = new SemaphoreSlim(shellService.Settings.ConcurrentVideoConnections / crawlerService.ActiveItems.Count);
            var trackedTasks = new List<Task>();
            var completeDownload = true;

            blog.CreateDataFolder();

            await Task.Run(() => Task.CompletedTask);

            try
            {
                while (await postQueue.OutputAvailableAsync(ct))
                {
                    TumblrPost downloadItem = (TumblrPost)await postQueue.ReceiveAsync();

                    if (downloadItem.GetType() == typeof(VideoPost))
                    {
                        await concurrentVideoConnectionsSemaphore.WaitAsync();
                    }

                    await concurrentConnectionsSemaphore.WaitAsync();

                    if (CheckIfShouldStop()) break;

                    CheckIfShouldPause();

                    trackedTasks.Add(DownloadPostAsync(downloadItem));
                }
            }
            catch (OperationCanceledException e)
            {
                System.Diagnostics.Debug.WriteLine(e.ToString());
            }

            // TODO: Is this even right?
            try
            {
                await Task.WhenAll(trackedTasks);
            }
            catch
            {
                completeDownload = false;
            }

            blog.LastDownloadedPhoto = null;
            blog.LastDownloadedPhoto = null; // UI property, no DB save here
            blog.LastDownloadedVideo = null; // UI property, no DB save here

            // files.Save(); // Removed

            return completeDownload;
        }

        private async Task DownloadPostAsync(TumblrPost downloadItem)
        {
            try
            {
                await DownloadPostCoreAsync(downloadItem);
            }
            catch (Exception e)
            {
                Logger.Error("AbstractDownloader.DownloadPostAsync: {0}", e);
            }
            finally
            {
                concurrentConnectionsSemaphore.Release();
                if (downloadItem.GetType() == typeof(VideoPost))
                {
                    concurrentVideoConnectionsSemaphore.Release();
                }
            }
        }

        private async Task DownloadPostCoreAsync(TumblrPost downloadItem)
        {
            // TODO: Refactor, should be polymorphism
            if (downloadItem.PostType == PostType.Binary)
            {
                await DownloadBinaryPostAsync(downloadItem);
            }
            else
            {
                DownloadTextPost(downloadItem);
            }
        }

        public virtual async Task<string> DownloadPageAsync(string url)
        {
            return await fileDownloader.ReadFromUrlAsStringAsync(url);
        }

        protected bool CheckIfLinkRestored(TumblrPost downloadItem)
        {
            if (!blog.ForceRescan || blog.FilenameTemplate != "%f") return false;
            lock (diskFilesLock)
            {
                if (diskFiles == null)
                {
                    diskFiles = new HashSet<string>();
                    foreach (var item in Directory.EnumerateFiles(blog.DownloadLocation(), "*", SearchOption.TopDirectoryOnly))
                    {
                        if (!string.Equals(Path.GetExtension(item), ".json", StringComparison.OrdinalIgnoreCase))
                            diskFiles.Add(Path.GetFileName(item).ToLower());
                    }
                }
                var filename = downloadItem.Url.Split('/').Last().ToLower();
                return diskFiles.Contains(filename);
            }
        }

        protected virtual async Task<bool> DownloadBinaryPostAsync(TumblrPost downloadItem)
        {
            if (blog?.BlogId == 0) 
            {
                Logger.Error($"AbstractDownloader:DownloadBinaryPostAsync: BlogId is not set for blog {blog?.Name}. Cannot proceed.");
                return false;
            }

            int downloadId;
            FileDto fileEntry;
            string actualDownloadUrl = Url(downloadItem); // URL for the actual binary content
            string uniqueFileKeyUrl = FileNameUrl(downloadItem); // URL part used as unique ID for the file in Files table
            string originalPostPageUrl = FileNameOriginalUrl(downloadItem); // URL of the post page, for OriginalLink

            if (downloadItem.ResumedDownloadId > 0)
            {
                downloadId = downloadItem.ResumedDownloadId;
                var downloadDto = databaseService.GetDownload(downloadId);
                if (downloadDto == null || downloadDto.FileId == 0) {
                    Logger.Error($"Resumed downloadId {downloadId} not found or invalid. Skipping.");
                    return false; 
                }
                fileEntry = databaseService.GetFile(downloadDto.FileId);
                if (fileEntry == null) {
                   Logger.Error($"FileId {downloadDto.FileId} for resumed downloadId {downloadId} not found. Skipping.");
                   return false;
                }
                // Ensure filename from DTO is used if it's more accurate for resumed download
                // And potentially the downloadItem.Url if it was stored in DownloadUrl
                downloadItem.Filename = fileEntry.Filename; 
                if (!string.IsNullOrEmpty(downloadDto.DownloadUrl)) actualDownloadUrl = downloadDto.DownloadUrl;

                databaseService.UpdateDownloadStatus(downloadId, "downloading", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                Logger.Information($"Resuming download for {downloadItem.Filename}, DownloadId {downloadId}, FileId {fileEntry.FileId}");
            }
            else // New download
            {
                fileEntry = databaseService.GetFileByBlogIdAndLink(blog.BlogId, uniqueFileKeyUrl);
                if (fileEntry == null)
                {
                    fileEntry = new FileDto 
                    { 
                        BlogId = blog.BlogId, 
                        Link = uniqueFileKeyUrl, 
                        OriginalLink = originalPostPageUrl, 
                        Filename = downloadItem.Filename, 
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 
                        FileSize = 0 // Will be updated after download
                    };
                    fileEntry.FileId = databaseService.AddFile(fileEntry);
                    if (fileEntry.FileId == 0)
                    {
                        Logger.Error($"AbstractDownloader:DownloadBinaryPostAsync: Failed to add new file to DB. Blog: {blog.Name}, URL Key: {uniqueFileKeyUrl}");
                        return false;
                    }
                }
                else if (fileEntry.Filename != downloadItem.Filename || 
                         (!string.IsNullOrEmpty(originalPostPageUrl) && fileEntry.OriginalLink != originalPostPageUrl))
                {
                    // Update filename or original link if they changed for an existing file entry
                    fileEntry.Filename = downloadItem.Filename;
                    if (!string.IsNullOrEmpty(originalPostPageUrl)) fileEntry.OriginalLink = originalPostPageUrl;
                    databaseService.UpdateFile(fileEntry);
                    Logger.Information($"AbstractDownloader:DownloadBinaryPostAsync: Updated metadata for existing file. Blog: {blog.Name}, FileId: {fileEntry.FileId}");
                }

                DownloadDto newDownload = new DownloadDto 
                { 
                    FileId = fileEntry.FileId, 
                    DownloadUrl = actualDownloadUrl, 
                    Status = "pending", // Will be set to "downloading" just before actual download attempt
                    TotalBytes = 0, // Will be updated by FileDownloader or after download
                    LastAttemptTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 
                    RetryCount = 0, 
                    DownloadPriority = 0 
                };
                downloadId = databaseService.AddDownload(newDownload);
                if (downloadId == 0)
                {
                    Logger.Error($"AbstractDownloader:DownloadBinaryPostAsync: Failed to add new download entry to DB. Blog: {blog.Name}, FileId: {fileEntry.FileId}");
                    return false;
                }
                databaseService.UpdateDownloadStatus(downloadId, "downloading", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }

            string blogDownloadLocation = blog.DownloadLocation();
            string targetFileLocation = FileLocation(blogDownloadLocation, fileEntry.Filename); // Use filename from FileDto
            DateTime postDate = PostDate(downloadItem);
            UpdateProgressQueueInformation(Resources.ProgressDownloadImage, fileEntry.Filename); // Use filename from FileDto

            bool downloadResult;
            string finalFileLocation;

            // Use actualDownloadUrl for the download call
            if (blog.Settings.DownloadUrlList)
            {
                // This path for DownloadUrlList might need more specific handling for what 'blogDownloadLocation' means here.
                // Assuming it's a path to a list file, not the download directory for the binary.
                // The actual binary isn't downloaded, just its URL is listed.
                (downloadResult, finalFileLocation) = await DownloadBinaryFileAsync(targetFileLocation, blogDownloadLocation /* text file path */, actualDownloadUrl, downloadId, blog.BlogId);
            }
            else
            {
                (downloadResult, finalFileLocation) = await DownloadBinaryFileAsync(targetFileLocation, actualDownloadUrl, downloadId, blog.BlogId);
            }

            if (!downloadResult)
            {
                return false; // DB status should have been updated by FileDownloader or DownloadBinaryFileAsync for UrlList
            }

            // For non-UrlList downloads, update FileSize and set file date.
            if (!blog.Settings.DownloadUrlList && File.Exists(finalFileLocation))
            {
                databaseService.UpdateFileSizeInFilesTable(fileEntry.FileId, new FileInfo(finalFileLocation).Length);
                SetFileDate(finalFileLocation, postDate); 
            }
            // If it was DownloadUrlList, the download success means URL was appended. Status already set to 'completed' by that path.
            
            UpdateBlogDB(downloadItem.DbType);

            if (shellService.Settings.EnablePreview && !blog.Settings.DownloadUrlList)
            {
                if (suffixes.Any(suffix => fileEntry.Filename.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                {
                    blog.LastDownloadedPhoto = Path.GetFullPath(finalFileLocation);
                }
                else
                {
                    blog.LastDownloadedVideo = Path.GetFullPath(finalFileLocation);
                }
                blog.LastPreviewShown = DateTime.Now.Ticks;
            }
            return true;
        }

        // AddTextToDb removed

        // AddFileToDb (protected virtual string AddFileToDb(TumblrPost downloadItem)) removed

        // CheckIfFileExistsInDB(string filenameUrl) - this specific public signature might be gone or changed if not used elsewhere.
        // The CheckIfFileExistsInDB(TumblrPost downloadItem) overload is what's being directly refactored.
        public bool CheckIfFileExistsInDB(string filenameUrl) // Kept for now if other parts of code use it directly with a simple URL.
        {
             if (blog?.BlogId == 0) return false;
             return databaseService.GetFileByBlogIdAndLink(blog.BlogId, filenameUrl) != null;
        }

        protected bool CheckIfFileExistsInDB(TumblrPost downloadItem) // Refactored
        {
            if (blog?.BlogId == 0) return false; // BlogId must be valid
            
            string uniqueFileUrl = FileNameUrl(downloadItem);
            var fileEntry = databaseService.GetFileByBlogIdAndLink(blog.BlogId, uniqueFileUrl);
            
            if (fileEntry != null)
            {
                // File with this specific URL exists.
                // Optional: Update OriginalLink if it's different and provided in downloadItem
                string originalPostUrl = FileNameOriginalUrl(downloadItem);
                if (!string.IsNullOrEmpty(originalPostUrl) && fileEntry.OriginalLink != originalPostUrl)
                {
                    fileEntry.OriginalLink = originalPostUrl;
                    databaseService.UpdateFile(fileEntry); // Update if changed
                }
                return true;
            }
            
            // Check by OriginalLink if uniqueFileUrl not found, for robustness if Link format changed for same content
            string filenameOrgUrl = FileNameOriginalUrl(downloadItem);
            if (!string.IsNullOrEmpty(filenameOrgUrl))
            {
                var fileByOriginalLink = databaseService.GetFileByBlogIdAndLink(blog.BlogId, filenameOrgUrl);
                if (fileByOriginalLink != null)
                {
                    // Found by original link. This means the primary 'Link' might have changed or wasn't the key before.
                    // Potentially update this entry to use the new 'uniqueFileUrl' as its primary Link if logic dictates.
                    // For now, just confirm existence.
                    return true;
                }
            }
            return false;
        }

        // UpdateLinkIfNeeded removed as its logic is integrated into CheckIfFileExistsInDB or Add/Update paths.

        private delegate bool OutputToTextFileDel(string fileLocation, string text, bool isJson);

        private void DownloadTextPost(TumblrPost downloadItem)
        {
            if (blog?.BlogId == 0)
            {
                Logger.Error($"AbstractDownloader:DownloadTextPost: BlogId is not set for blog {blog?.Name}. Cannot track text post.");
                return;
            }

            string postId = PostId(downloadItem); // Unique ID for the text post content itself.
            string linkForTextPost = $"textpost_{blog.BlogId}_{postId}"; // Construct a unique link for DB
            string textContent = Url(downloadItem); // This is the actual text content for text posts.

            FileDto fileEntry = databaseService.GetFileByBlogIdAndLink(blog.BlogId, linkForTextPost);

            if (fileEntry != null)
            {
                UpdateProgressQueueInformation(Resources.ProgressSkipFile, postId); // Already recorded
                return;
            }

            // Text post not in DB, add it.
            string blogDownloadLocation = blog.DownloadLocation();
            string actualFilenameOnDisk = string.IsNullOrEmpty(downloadItem.Filename) ? downloadItem.TextFileLocation : downloadItem.Filename;
            string fileLocationOnDisk = FileLocation(blogDownloadLocation, actualFilenameOnDisk);
            
            OutputToTextFileDel outputToTextFile = string.IsNullOrEmpty(downloadItem.Filename) 
                ? new OutputToTextFileDel(AppendToTextFile) 
                : new OutputToTextFileDel(WriteToTextFile);

            UpdateProgressQueueInformation(Resources.ProgressDownloadImage, postId); // Using generic message

            if (outputToTextFile(fileLocationOnDisk, textContent, blog.Settings.MetadataFormat == Domain.Models.MetadataType.Json))
            {
                long fileSize = new FileInfo(fileLocationOnDisk).Length; // Get actual file size after writing

                fileEntry = new FileDto
                {
                    BlogId = blog.BlogId,
                    Link = linkForTextPost, // Use the constructed unique link
                    Filename = actualFilenameOnDisk, // Actual filename on disk
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    FileSize = fileSize,
                    // OriginalLink could be the post's web URL if available, or null
                    OriginalLink = downloadItem.PostedUrl 
                };
                fileEntry.FileId = databaseService.AddFile(fileEntry);

                if (fileEntry.FileId > 0)
                {
                    DownloadDto textDownload = new DownloadDto
                    {
                        FileId = fileEntry.FileId,
                        DownloadUrl = "internal://text", // Indicates it's not a remote download
                        Status = "completed",
                        ProgressBytes = fileSize,
                        TotalBytes = fileSize,
                        LastAttemptTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };
                    databaseService.AddDownload(textDownload);
                    UpdateBlogDB(downloadItem.DbType); // UI counter update
                }
                else
                {
                     Logger.Error($"AbstractDownloader:DownloadTextPost: Failed to add FileDto to DB for text post {postId} of blog {blog.Name}.");
                }
            }
            else
            {
                Logger.Error($"AbstractDownloader:DownloadTextPost: Failed to write text post {postId} to disk for blog {blog.Name}.");
                // No DB entries created if file writing fails.
            }
        }

        protected void UpdateBlogDB(string postType) // This is for UI counters, not direct DB save of blog object
        {
            blog.UpdatePostCount(postType);
            blog.UpdateProgress(true);
        }

        protected void SetFileDate(string fileLocation, DateTime postDate)
        {
            if (blog.DownloadUrlList)
            {
                return;
            }

            // Ensure file exists before setting date, as DownloadUrlList path might not create a physical file
            if (File.Exists(fileLocation)) 
            {
                File.SetLastWriteTime(fileLocation, postDate);
            }
        }

        protected static string Url(TumblrPost downloadItem)
        {
            return downloadItem.Url;
        }

        protected virtual string FileNameUrl(TumblrPost downloadItem)
        {
            return downloadItem.Url?.Split('/').Last();
        }

        protected virtual string FileNameOriginalUrl(TumblrPost downloadItem)
        {
            return downloadItem.PostedUrl?.Split('/').Last();
        }

        protected virtual string FileName(TumblrPost downloadItem)
        {
            string filename = downloadItem.Url.Split('/').Last();
            if (Path.GetExtension(filename).ToLower() == ".gifv")
                filename = Path.GetFileNameWithoutExtension(filename) + ".gif";
            if (Path.GetExtension(filename).ToLower() == ".pnj")
                filename += ".png";
            return filename;
        }

        protected static string FileNameNew(TumblrPost downloadItem)
        {
            return downloadItem.Filename;
        }

        protected static string FileLocation(string blogDownloadLocation, string fileName)
        {
            return Path.Combine(blogDownloadLocation, fileName);
        }

        private static string PostId(TumblrPost downloadItem)
        {
            return downloadItem.Id;
        }

        protected static DateTime PostDate(TumblrPost downloadItem)
        {
            if (string.IsNullOrEmpty(downloadItem.Date))
            {
                return DateTime.Now;
            }

            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            DateTime postDate = epoch.AddSeconds(Convert.ToDouble(downloadItem.Date)).ToLocalTime();
            return postDate;
        }

        protected bool CheckIfShouldStop()
        {
            return ct.IsCancellationRequested;
        }

        protected void CheckIfShouldPause()
        {
            if (pt.IsPaused)
            {
                pt.WaitWhilePausedWithResponseAsync().Wait();
            }
        }

        // OnSaveTimedEvent method removed
        // _saveTimer field and its initialization removed

        public virtual bool CheckIfPostedUrlIsDownloaded(string url)
        {
            if (blog?.BlogId == 0 || string.IsNullOrEmpty(url)) return false;
            var filenameUrl = url.Split('/').LastOrDefault(); // Ensure not null if url is just "host/"
            if (string.IsNullOrEmpty(filenameUrl)) return false;

            // This check is against the 'Link' field in the Files table.
            // If PostedUrl is meant to be checked against 'OriginalLink', the query in DB service would need adjustment or another method.
            return databaseService.GetFileByBlogIdAndLink(blog.BlogId, filenameUrl) != null;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // lock (_saveTimerLock) // Removed
                // {
                //     _disposed = true;
                //     _saveTimer.Change(Timeout.Infinite, Timeout.Infinite); // Removed
                //     _saveTimer.Dispose(); // Removed
                // }
                _disposed = true; // Set disposed flag
                concurrentConnectionsSemaphore?.Dispose();
                concurrentVideoConnectionsSemaphore?.Dispose();

                foreach (var sw in streamWriters.Values)
                {
                    sw.Dispose();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
