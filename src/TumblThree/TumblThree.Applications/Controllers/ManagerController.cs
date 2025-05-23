using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Waf.Applications;
using System.Waf.Applications.Services;
using System.Windows.Forms;
using System.Windows.Input;
using System.Xml;
using TumblThree.Applications.Crawler;
using TumblThree.Applications.DataModels;
using TumblThree.Applications.Properties;
using TumblThree.Applications.Services;
using TumblThree.Applications.ViewModels;
using TumblThree.Domain;
using TumblThree.Domain.Models;
using TumblThree.Domain.Models.Blogs;
using TumblThree.Domain.Models.Files;
using TumblThree.Domain.Queue;
using TumblThree.Domain.Database; // Added for DatabaseService

using Clipboard = System.Windows.Clipboard;

namespace TumblThree.Applications.Controllers
{
    [Export]
    internal class ManagerController : IDisposable
    {
        private readonly IBlogFactory _blogFactory;
        private readonly ICrawlerService _crawlerService;
        private readonly IClipboardService _clipboardService;
        private readonly ICrawlerFactory _crawlerFactory;
        private readonly IManagerService _managerService;
        private readonly Lazy<ManagerViewModel> _managerViewModel;
        private readonly IMessageService _messageService;
        private readonly ISelectionService _selectionService;
        private readonly IShellService _shellService;
        private readonly ISettingsService _settingsService;
        private readonly ITumblrBlogDetector _tumblrBlogDetector;
        private readonly DatabaseService _databaseService; // Added

        private readonly AsyncDelegateCommand _checkStatusCommand;
        private readonly DelegateCommand _copyUrlCommand;
        private readonly DelegateCommand _checkIfDatabasesCompleteCommand;
        private readonly AsyncDelegateCommand _importBlogsCommand;
        private readonly AsyncDelegateCommand _addBlogCommand;
        private readonly DelegateCommand _autoDownloadCommand;
        private readonly DelegateCommand _enqueueSelectedCommand;
        private readonly DelegateCommand _dequeueSelectedCommand;
        private readonly DelegateCommand _listenClipboardCommand;
        private readonly AsyncDelegateCommand _loadLibraryCommand;
        private readonly AsyncDelegateCommand _loadAllDatabasesCommand;
        private readonly AsyncDelegateCommand _loadArchiveCommand;
        private readonly DelegateCommand _removeBlogCommand;
        private readonly DelegateCommand _showDetailsCommand;
        private readonly DelegateCommand _showFilesCommand;
        private readonly DelegateCommand _visitBlogCommand;
        private readonly DelegateCommand _visitBlogOnTumbexCommand;

        private readonly SemaphoreSlim _addBlogSemaphoreSlim = new SemaphoreSlim(1);
        private readonly object _lockObject = new object();

        public delegate void BlogManagerFinishedLoadingLibraryHandler(object sender, EventArgs e);

        public delegate void BlogManagerFinishedLoadingDatabasesHandler(object sender, EventArgs e);

        public delegate void BlogManagerFinishedLoadingArchiveHandler(object sender, EventArgs e);

        public delegate void FinishedCrawlingLastBlogEventHandler(object sender, EventArgs e);

        [ImportingConstructor]
        public ManagerController(IShellService shellService, ISelectionService selectionService, ICrawlerService crawlerService,
            ISettingsService settingsService, IClipboardService clipboardService, IManagerService managerService,
            ICrawlerFactory crawlerFactory, IBlogFactory blogFactory, ITumblrBlogDetector tumblrBlogDetector,
            IMessageService messageService, Lazy<ManagerViewModel> managerViewModel)
        {
            _shellService = shellService;
            _selectionService = selectionService;
            _clipboardService = clipboardService;
            _crawlerService = crawlerService;
            _managerService = managerService;
            _managerViewModel = managerViewModel;
            _settingsService = settingsService;
            _messageService = messageService;
            _crawlerFactory = crawlerFactory;
            _blogFactory = blogFactory;
            _tumblrBlogDetector = tumblrBlogDetector;

            // Initialize DatabaseService
            // Ensure IShellService.Settings.DownloadLocation is available and valid.
            // Using a "Metadata" subfolder within the main download path for the database.
            string metadataPath = Path.Combine(shellService.Settings.DownloadLocation, "Metadata");
            if (!Directory.Exists(metadataPath))
            {
                Directory.CreateDirectory(metadataPath);
            }
            string dbPath = Path.Combine(metadataPath, "TumblThreeData.sqlite");
            _databaseService = new DatabaseService(dbPath);
            DatabaseService.Logger = Logger.Information; // Assign logger

            _importBlogsCommand = new AsyncDelegateCommand(ImportBlogs);
            _addBlogCommand = new AsyncDelegateCommand(AddBlog, CanAddBlog);
            _removeBlogCommand = new DelegateCommand(RemoveBlog, CanRemoveBlog);
            _showFilesCommand = new DelegateCommand(ShowFiles, CanShowFiles);
            _visitBlogCommand = new DelegateCommand(VisitBlog, CanVisitBlog);
            _visitBlogOnTumbexCommand = new DelegateCommand(VisitBlogOnTumbex, CanVisitBlog);
            _enqueueSelectedCommand = new DelegateCommand(EnqueueSelected, CanEnqueueSelected);
            _dequeueSelectedCommand = new DelegateCommand(DequeueSelected, CanDequeueSelected);
            _loadLibraryCommand = new AsyncDelegateCommand(LoadLibraryAsync, CanLoadLibrary);
            _loadAllDatabasesCommand = new AsyncDelegateCommand(LoadAllDatabasesAsync, CanLoadAllDatbases);
            _loadArchiveCommand = new AsyncDelegateCommand(LoadArchiveAsync, CanLoadArchive);
            _checkIfDatabasesCompleteCommand = new DelegateCommand(CheckIfDatabasesComplete, CanCheckIfDatabasesComplete);
            _listenClipboardCommand = new DelegateCommand(ListenClipboard);
            _autoDownloadCommand = new DelegateCommand(EnqueueAutoDownload, CanEnqueueAutoDownload);
            _showDetailsCommand = new DelegateCommand(ShowDetailsCommand);
            _copyUrlCommand = new DelegateCommand(CopyUrl, CanCopyUrl);
            _checkStatusCommand = new AsyncDelegateCommand(CheckStatusAsync, CanCheckStatus);
        }

        private ManagerViewModel ManagerViewModel => _managerViewModel.Value;

        public ManagerSettings ManagerSettings { get; set; }

        public QueueManager QueueManager { get; set; }

        public event BlogManagerFinishedLoadingLibraryHandler BlogManagerFinishedLoadingLibrary;

        public event BlogManagerFinishedLoadingDatabasesHandler BlogManagerFinishedLoadingDatabases;

        public event BlogManagerFinishedLoadingArchiveHandler BlogManagerFinishedLoadingArchive;

        public event FinishedCrawlingLastBlogEventHandler FinishedCrawlingLastBlog;

        public async Task InitializeAsync()
        {
            _crawlerService.ImportBlogsCommand = _importBlogsCommand;
            _crawlerService.AddBlogCommand = _addBlogCommand;
            _crawlerService.RemoveBlogCommand = _removeBlogCommand;
            _crawlerService.ShowFilesCommand = _showFilesCommand;
            _crawlerService.EnqueueSelectedCommand = _enqueueSelectedCommand;
            _crawlerService.DequeueSelectedCommand = _dequeueSelectedCommand;
            _crawlerService.LoadLibraryCommand = _loadLibraryCommand;
            _crawlerService.LoadAllDatabasesCommand = _loadAllDatabasesCommand;
            _crawlerService.LoadArchiveCommand = _loadArchiveCommand;
            _crawlerService.CheckIfDatabasesCompleteCommand = _checkIfDatabasesCompleteCommand;
            _crawlerService.AutoDownloadCommand = _autoDownloadCommand;
            _crawlerService.ListenClipboardCommand = _listenClipboardCommand;
            _crawlerService.PropertyChanged += CrawlerServicePropertyChanged;

            // Set the logger for DatabaseService if it's static and needs to be set from here.
            // If it's instance-based, it should be passed to its constructor.
            // DatabaseService.Logger = Logger.Information; // Already did this in constructor adjustment

            ManagerViewModel.ShowFilesCommand = _showFilesCommand;
            ManagerViewModel.VisitBlogCommand = _visitBlogCommand;
            ManagerViewModel.VisitBlogOnTumbexCommand = _visitBlogOnTumbexCommand;
            ManagerViewModel.ShowDetailsCommand = _showDetailsCommand;
            ManagerViewModel.CopyUrlCommand = _copyUrlCommand;
            ManagerViewModel.CheckStatusCommand = _checkStatusCommand;

            ManagerViewModel.PropertyChanged += ManagerViewModelPropertyChanged;

            ManagerViewModel.QueueItems = QueueManager.Items;
            QueueManager.Items.CollectionChanged += QueueItemsCollectionChanged;
            ManagerViewModel.QueueItems.CollectionChanged += ManagerViewModel.QueueItemsCollectionChanged;
            BlogManagerFinishedLoadingLibrary += OnBlogManagerFinishedLoadingLibrary;
            BlogManagerFinishedLoadingDatabases += OnBlogManagerFinishedLoadingDatabases;
            BlogManagerFinishedLoadingArchive += OnBlogManagerFinishedLoadingArchive;

            _shellService.ContentView = ManagerViewModel.View;

            // Refresh command availability on selection change.
            ManagerViewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName != nameof(ManagerViewModel.SelectedBlogFile))
                {
                    return;
                }

                _showFilesCommand.RaiseCanExecuteChanged();
                _visitBlogCommand.RaiseCanExecuteChanged();
                _visitBlogOnTumbexCommand.RaiseCanExecuteChanged();
                _showDetailsCommand.RaiseCanExecuteChanged();
                _copyUrlCommand.RaiseCanExecuteChanged();
                _checkStatusCommand.RaiseCanExecuteChanged();
            };

            if (_shellService.Settings.CheckClipboard)
            {
                _shellService.ClipboardMonitor.OnClipboardContentChanged += OnClipboardContentChanged;
            }

            using (_shellService.SetApplicationBusy())
            {
                await LoadDataBasesAsync();
            }
        }

        /// <summary>
        /// Ask the controller if a shutdown can be executed.
        /// </summary>
        /// <returns>
        /// true  - can be executed,
        /// false - shall be postponed
        /// </returns>
        public bool QueryShutdown()
        {
            return true;
        }

        public void Shutdown()
        {
        }

        private void OnBlogManagerFinishedLoadingLibrary(object sender, EventArgs e) =>
            _crawlerService.LibraryLoaded.SetResult(true);

        private void OnBlogManagerFinishedLoadingDatabases(object sender, EventArgs e) =>
            _crawlerService.DatabasesLoaded.SetResult(true);

        private void OnBlogManagerFinishedLoadingArchive(object sender, EventArgs e) =>
            _crawlerService.ArchiveLoaded.SetResult(true);

        private void QueueItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Remove)
            {
                ManagerViewModel.QueueItems = QueueManager.Items;
            }
            if (e.Action == NotifyCollectionChangedAction.Remove && QueueManager.Items.Count == 0)
            {
                FinishedCrawlingLastBlogEventHandler handler = FinishedCrawlingLastBlog;
                handler?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task LoadDataBasesAsync()
        {
            Logger.Verbose("ManagerController.LoadDataBasesAsync:Start");
            _managerService.BlogFiles.Clear();
            // _managerService.ClearDatabases(); // No longer using per-blog IFiles for active blogs
            // _managerService.ClearArchive(); // Archive logic might need separate handling if it remains file-based

            try
            {
                var blogDtos = _databaseService.GetAllBlogs();
                foreach (var dto in blogDtos)
                {
                    IBlog blog;
                    // Attempt to parse BlogType string to enum
                    if (!Enum.TryParse<BlogTypes>(dto.BlogType, true, out var type))
                    {
                        Logger.Warning($"Unsupported blog type encountered: {dto.BlogType} for blog {dto.Name}. Skipping.");
                        continue; 
                    }

                    // Instantiate IBlog based on BlogType
                    // This assumes _blogFactory.GetBlog(dto) is not yet available or implemented.
                    // Also assumes concrete blog types like TumblrBlog now have a constructor that accepts BlogDto.
                    switch (type)
                    {
                        case BlogTypes.tumblr:
                        case BlogTypes.tmblrpriv: // Assuming TumblrHiddenBlog maps to tmblrpriv or similar
                        case BlogTypes.tlb:       // Assuming TumblrLikedByBlog maps to tlb
                            // For simplicity, mapping all these to TumblrBlog for now.
                            // Specific types like TumblrHiddenBlog, TumblrLikedByBlog would need their own classes
                            // inheriting from Blog and having constructors accepting BlogDto.
                            // If TumblrHiddenBlog, etc. are distinct classes, use them here.
                            // Example: blog = new TumblrHiddenBlog(dto);
                            blog = new TumblrBlog(dto); 
                            break;
                        case BlogTypes.twitter:
                            // blog = new TwitterBlog(dto); // Example for Twitter
                            Logger.Warning($"Blog type Twitter for blog {dto.Name} not fully implemented for DB loading. Using generic Blog.");
                            blog = new Blog(dto); // Fallback to generic Blog if specific not ready
                            break;
                        case BlogTypes.newtumbl:
                            // blog = new NewTumblBlog(dto); // Example for NewTumbl
                            Logger.Warning($"Blog type NewTumbl for blog {dto.Name} not fully implemented for DB loading. Using generic Blog.");
                            blog = new Blog(dto);
                            break;
                        // Add cases for BlueskyBlog, etc., as they are created/refactored
                        default:
                            Logger.Warning($"Unsupported or unhandled blog type: {dto.BlogType} for blog {dto.Name}. Using generic Blog instance.");
                            // Fallback to a generic Blog instance if specific type not handled.
                            // This requires Blog.cs to have a public constructor accepting BlogDto.
                            blog = new Blog(dto); 
                            break;
                    }
                    _managerService.BlogFiles.Add(blog);
                }

                // Signal that the library (blogs from DB) has been loaded
                BlogManagerFinishedLoadingLibrary?.Invoke(this, EventArgs.Empty);
                // These might also be relevant depending on how "Databases" and "Archive" are re-interpreted
                // BlogManagerFinishedLoadingDatabases?.Invoke(this, EventArgs.Empty);
                // BlogManagerFinishedLoadingArchive?.Invoke(this, EventArgs.Empty);


                _crawlerService.UpdateCollectionsList(false); // This seems to update UI based on collections/blogs
                await CheckBlogsOnlineStatusAsync(); // This likely iterates _managerService.BlogFiles
            }
            catch (Exception e)
            {
                Logger.Error("ManagerController.LoadDataBasesAsync: Error loading blogs from database: {0}", e);
                _shellService.ShowError(e, Resources.CouldNotLoadLibrary, e.Message); // Or a new resource string for DB error
            }

            // Enqueue pending downloads
            Logger.Information("Checking for pending downloads to resume...");
            IEnumerable<Database.DownloadDto> pendingDownloads = _databaseService.GetPendingDownloads(blogId: null);
            int resumedCount = 0;
            foreach (var dto in pendingDownloads)
            {
                IBlog blogForDownload = _managerService.BlogFiles.FirstOrDefault(b => b.BlogId == dto.BlogId);
                if (blogForDownload == null)
                {
                    Logger.Warning($"Cannot resume downloadId {dto.DownloadId}: BlogId {dto.BlogId} not loaded/found in UI.");
                    continue;
                }

                // Determine PostType
                TumblrPost.PostType postType = TumblrPost.PostType.Binary; // Default, AbstractPost.PostType might be better if it exists
                if (!string.IsNullOrEmpty(dto.Filename)) {
                    string ext = Path.GetExtension(dto.Filename).ToLowerInvariant();
                    if (ext == ".mp4" || ext == ".webm" || ext == ".mov") postType = TumblrPost.PostType.Video; // Assumes TumblrPost.PostType enum
                    else if (ext == ".jpg" || ext == ".png" || ext == ".gif" || ext == ".jpeg" || ext == ".bmp") postType = TumblrPost.PostType.Photo; // Assumes TumblrPost.PostType enum
                    // Text/Audio posts are less likely to be resumable in this way, but can be added if needed.
                    // else if (ext == ".mp3" || ext == ".ogg") postType = TumblrPost.PostType.Audio;
                    // else if (ext == ".txt" && (dto.DownloadUrl == "internal://text" || string.IsNullOrEmpty(dto.DownloadUrl) )) postType = TumblrPost.PostType.Text;
                }

                // Create a concrete TumblrPost instance. This might need a factory if other AbstractPost derivatives are common.
                // For now, PhotoPost and VideoPost are common concrete types that inherit from TumblrPost.
                // We use a generic Url for now; specific post types might refine this.
                // Using FileId as Post.Id for mapping.
                // Date from LastAttemptTimestamp for sorting or info.
                AbstractPost postToResume; // Use AbstractPost for the queue
                
                // Heuristic to choose between PhotoPost and VideoPost based on determined postType
                // This is a simplification. Ideally, the actual post type from original crawl would be stored.
                if (postType == TumblrPost.PostType.Video) 
                {
                    postToResume = new VideoPost(
                        url: dto.DownloadUrl ?? dto.Link, 
                        id: dto.FileId.ToString(), 
                        date: DateTimeOffset.FromUnixTimeSeconds(dto.LastAttemptTimestamp).ToString("yyyyMMddHHmmss"),
                        filename: dto.Filename // Added filename
                    );
                }
                else if (postType == TumblrPost.PostType.Photo)
                {
                     postToResume = new PhotoPost(
                        url: dto.DownloadUrl ?? dto.Link,
                        id: dto.FileId.ToString(),
                        date: DateTimeOffset.FromUnixTimeSeconds(dto.LastAttemptTimestamp).ToString("yyyyMMddHHmmss"),
                        filename: dto.Filename // Added filename
                    );
                }
                else // Fallback for Binary or other types not specifically Photo/Video
                {
                    // Need a concrete type. If TumblrPost itself is not abstract and can be instantiated:
                    // For now, let's assume a generic PhotoPost or VideoPost can handle "Binary" if no specific fields needed.
                    // Or, if AbstractPost can be instantiated directly (not typical).
                    // This indicates a potential need for a generic "BinaryFilePost" or similar if TumblrPost is abstract.
                    // Let's use PhotoPost as a fallback for now, assuming it can handle generic binary downloads.
                     postToResume = new PhotoPost( // Using PhotoPost as a placeholder for general binary.
                        url: dto.DownloadUrl ?? dto.Link,
                        id: dto.FileId.ToString(),
                        date: DateTimeOffset.FromUnixTimeSeconds(dto.LastAttemptTimestamp).ToString("yyyyMMddHHmmss"),
                        filename: dto.Filename
                    );
                }
                
                // Set common properties from AbstractPost/TumblrPost
                ((TumblrPost)postToResume).InitialProgressBytes = dto.ProgressBytes;
                ((TumblrPost)postToResume).ResumedDownloadId = dto.DownloadId;
                postToResume.Blog = blogForDownload; // Set the Blog context

                _crawlerService.PostQueue.Add(postToResume);
                _databaseService.UpdateDownloadStatus(dto.DownloadId, "queued", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                resumedCount++;
            }
            if (resumedCount > 0)
            {
                Logger.Information($"Enqueued {resumedCount} pending downloads for resumption.");
                // Optional: Trigger crawl. This logic needs to be confirmed based on app behavior.
                // if (!_crawlerService.IsCrawl && _shellService.Settings.AutoDownloadOnStartup && QueueManager.Items.Any())
                // {
                //    _crawlerService.CrawlCommand.Execute(null);
                // }
            }
            else
            {
                Logger.Information("No pending downloads found to resume.");
            }
            Logger.Verbose("ManagerController.LoadDataBasesAsync:End");
        }

        // private async Task LoadLibraryAsync() { /* Removed / Content moved to LoadDataBasesAsync */ }
        // private Task<IReadOnlyList<IBlog>> GetIBlogsAsync(string directory) { /* Removed */ }
        // private IReadOnlyList<IBlog> GetIBlogsCore(string directory) { /* Removed */ }
        // private async Task LoadAllDatabasesAsync() { /* Removed - IFiles logic is gone for active blogs */ }
        // private async Task LoadArchiveAsync() { /* TODO: Archive logic needs review. For now, removing old content. */ }
        // async Task<bool> ProcessFolder(int collectionId, string folder) { /* Removed, part of old archive logic */ }
        // private bool SkipFolder(int currentCollectionId, string folder, bool loadArchives) { /* Removed, part of old archive logic */ }
        // private Task<IReadOnlyList<IFiles>> GetIFilesAsync(string directory, bool isArchive) { /* Removed */ }
        // private IReadOnlyList<IFiles> GetIFilesCore(string directory, bool isArchive) { /* Removed */ }
        // private void AddDatabaseToList(List<IFiles> databases, IFiles database, bool isArchive, bool loadAllDatabasesSetting) { /* Removed */ }
        // private void CheckIfDatabasesComplete() { /* Removed - ChildId logic is gone */ }

        private async Task CheckBlogsOnlineStatusAsync()
        {
            if (_shellService.Settings.CheckOnlineStatusOnStartup)
            {
                IEnumerable<IBlog> blogs = _managerService.BlogFiles.ToArray<IBlog>();
                await Task.Run(() => ThrottledCheckStatusOfBlogsAsync(blogs));
            }
        }

        private async Task CheckStatusAsync()
        {
            IEnumerable<IBlog> blogs = _selectionService.SelectedBlogFiles.ToArray();
            await Task.Run(() => ThrottledCheckStatusOfBlogsAsync(blogs));
        }

        private async Task ThrottledCheckStatusOfBlogsAsync(IEnumerable<IBlog> blogs)
        {
            using (var semaphoreSlim = new SemaphoreSlim(25))
            {
                IEnumerable<Task> tasks = blogs.Select(async blog => await CheckStatusOfBlogsAsync(semaphoreSlim, blog));
                await Task.WhenAll(tasks);
            }
        }

        private async Task CheckStatusOfBlogsAsync(SemaphoreSlim semaphoreSlim, IBlog blog)
        {
            await semaphoreSlim.WaitAsync();
            ICrawler crawler = null;
            try
            {
                bool isHiddenTumblrBlog = false;
                if (blog.BlogType == BlogTypes.tumblr)
                    isHiddenTumblrBlog = await _tumblrBlogDetector.IsHiddenTumblrBlogAsync(blog.Url);
                if (isHiddenTumblrBlog)
                    blog.BlogType = BlogTypes.tmblrpriv;
                crawler = _crawlerFactory.GetCrawler(blog, new Progress<DownloadProgress>(), new PauseToken(), new CancellationToken());
                await crawler.IsBlogOnlineAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("ManagerController.CheckStatusOfBlogsAsync: {0}", ex);
                _shellService.ShowError(ex, $"Online check for '{blog.Name}' failed: {ex.Message}");
                blog.Online = false;
            }
            finally
            {
                crawler?.Dispose();
                try
                {
                    semaphoreSlim.Release();
                }
                catch (ObjectDisposedException)
                { }
            }
        }

        private bool CanLoadLibrary() => !_crawlerService.IsCrawl;

        private bool CanLoadAllDatbases() => !_crawlerService.IsCrawl;

        private bool CanLoadArchive() => !_crawlerService.IsCrawl;

        private bool CanCheckIfDatabasesComplete() => _crawlerService.DatabasesLoaded.Task.GetAwaiter().IsCompleted &&
                                                      _crawlerService.LibraryLoaded.Task.GetAwaiter().IsCompleted;

        private bool CanEnqueueSelected() => ManagerViewModel.SelectedBlogFile != null && ManagerViewModel.SelectedBlogFile.Online;

        private bool CanDequeueSelected() => ManagerViewModel.SelectedBlogFile != null && true;

        private void EnqueueSelected() => Enqueue(_selectionService.SelectedBlogFiles.Where(blog => blog.Online).ToArray());

        private void DequeueSelected() => Dequeue(_selectionService.SelectedBlogFiles.ToArray());

        private void Enqueue(IEnumerable<IBlog> blogFiles) => QueueManager.AddItems(blogFiles.Select(x => new QueueListItem(x)));

        private void Dequeue(IEnumerable<IBlog> blogFiles)
        {
            List<QueueListItem> toBeRemoved = new List<QueueListItem>();
            foreach (var blog in blogFiles)
            {
                toBeRemoved.AddRange(QueueManager.Items.Where(x => x.Blog.Name == blog.Name && x.Blog.BlogType == blog.BlogType).ToArray());
            }
            _crawlerService.RemoveBlogSelectionFromQueueCommand.Execute(toBeRemoved);
        }

        private bool CanEnqueueAutoDownload() => _managerService.BlogFiles.Any();

        private void EnqueueAutoDownload()
        {
            //if (_shellService.Settings.BlogType == _shellService.Settings.BlogTypes.ElementAtOrDefault(0))
            //{
            //}

            if (_shellService.Settings.BlogType == AppSettings.BlogTypes.ElementAtOrDefault(1))
            {
                Enqueue(_managerService.BlogFiles.Where(blog => blog.Online).ToArray());
            }

            if (_shellService.Settings.BlogType == AppSettings.BlogTypes.ElementAtOrDefault(2))
            {
                Enqueue(
                    _managerService
                        .BlogFiles.Where(blog => blog.Online && blog.LastCompleteCrawl != new DateTime(0L, DateTimeKind.Utc))
                        .ToArray());
            }

            if (_shellService.Settings.BlogType == AppSettings.BlogTypes.ElementAtOrDefault(3))
            {
                Enqueue(
                    _managerService
                        .BlogFiles.Where(blog => blog.Online && blog.LastCompleteCrawl == new DateTime(0L, DateTimeKind.Utc))
                        .ToArray());
            }

            if (_crawlerService.IsCrawl && _crawlerService.IsPaused)
            {
                _crawlerService.ResumeCommand.CanExecute(null);
                _crawlerService.ResumeCommand.Execute(null);
            }
            else if (!_crawlerService.IsCrawl)
            {
                _crawlerService.CrawlCommand.CanExecute(null);
                _crawlerService.CrawlCommand.Execute(null);
            }
        }

        private bool CanAddBlog() => _blogFactory.IsValidBlogUrl(_crawlerService.NewBlogUrl) || _blogFactory.IsValidUrl(_crawlerService.NewBlogUrl);

        private async Task AddBlog()
        {
            try
            {
                await AddBlogAsync(_crawlerService.NewBlogUrl, false);
            }
            catch (WebException we)
            {
                if (we.Response != null && ((HttpWebResponse)we.Response).StatusCode == HttpStatusCode.NotFound)
                {
                    Logger.Error($"ManagerController:AddBlog WebException (Not Found): {_crawlerService.NewBlogUrl}, {we.Message}");
                    _shellService.ShowError(we, Resources.CouldNotAddBlog, $"{_crawlerService.NewBlogUrl} not found.");
                    // CleanFailedAddBlog logic might need to be re-evaluated as it relied on old IBlog structure.
                    // For now, the primary issue is informing the user. DB won't have a failed entry.
                }
                else
                {
                    Logger.Error($"ManagerController:AddBlog WebException: {_crawlerService.NewBlogUrl}, {we}");
                    _shellService.ShowError(we, Resources.CouldNotAddBlog, $"{_crawlerService.NewBlogUrl}: {we.Message}");
                }
            }
            catch (ArgumentException ae) // Can be thrown by new Blog(dto) if BlogType is weird
            {
                 Logger.Error($"ManagerController:AddBlog ArgumentException: {_crawlerService.NewBlogUrl}, {ae.Message}");
                _shellService.ShowError(ae, Resources.CouldNotAddBlog, $"{_crawlerService.NewBlogUrl}: Invalid blog type or data. {ae.Message}");
            }
            catch (Exception e)
            {
                Logger.Error($"ManagerController:AddBlog Exception: {_crawlerService.NewBlogUrl}, {e}");
                _shellService.ShowError(e, Resources.CouldNotAddBlog, $"{_crawlerService.NewBlogUrl}: {e.Message}");
            }
            finally
            {
                _crawlerService.NewBlogUrl = ""; // Clear the URL input field
            }
        }

        private void CleanFailedAddBlog() // TODO: Review if this is still needed or how it should work
        {
            // This method was designed to clean up file system artifacts (empty folders, index files)
            // from the old file-based system if a blog addition failed AFTER some files were created.
            // With the DB-first approach for AddBlogAsync, fewer artifacts might be created before DB insert.
            // If EnsureUniqueFolder creates a folder and then DB insert fails, that folder might remain.
            // For now, this method's old logic based on blog.ChildId is no longer valid.
            Logger.Warning("ManagerController:CleanFailedAddBlog: Review needed for this method's functionality with DB persistence.");
            // Example: if a directory was created by EnsureUniqueFolder but AddBlog failed
            // string potentialDirPath = Path.Combine(_shellService.Settings.DownloadLocation, Path.GetFileName(_crawlerService.NewBlogUrl)); // This is a guess
            // if (Directory.Exists(potentialDirPath) && !Directory.EnumerateFileSystemEntries(potentialDirPath).Any())
            // {
            //    Directory.Delete(potentialDirPath);
            // }
        }

        private async Task ImportBlogs()
        {
            try
            {
                string path = "";
                using (var fileBrowser = new OpenFileDialog() { Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*" })
                {
                    if (fileBrowser.ShowDialog() != DialogResult.OK)
                    {
                        return;
                    }

                    path = fileBrowser.FileName;
                }

                if (!File.Exists(path))
                {
                    Logger.Warning("ManagerController:ImportBlogs: An attempt was made to import blogs from a file which doesn't exist.");
                    return;
                }

                string fileContent;

                using (var streamReader = new StreamReader(path))
                {
                    fileContent = await streamReader.ReadToEndAsync();
                }

                var blogUris = fileContent.Split().Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x));

                await Task.Run(() => AddBlogBatchedAsync(blogUris, false));
            }
            catch (Exception ex)
            {
                Logger.Error($"ManagerController:ImportBlogs: {ex}");
            }
        }

        private bool CanRemoveBlog() => ManagerViewModel.SelectedBlogFile != null;

        private void RemoveBlog()
        {
            IBlog[] blogs = _selectionService.SelectedBlogFiles.ToArray();

            if (_shellService.Settings.DisplayConfirmationDialog)
            {
                var blogNames = string.Join(", ", blogs.Select(blog => blog.Name));
                var message = string.Format(
                    _shellService.Settings.DeleteOnlyIndex ? Resources.DeleteBlogsDialog : Resources.DeleteBlogsAndFilesDialog,
                    blogNames);

                if (!_messageService.ShowYesNoQuestion(message))
                {
                    return;
                }
            }

            RemoveBlog(blogs, true);
        }

        private void RemoveBlog(IEnumerable<IBlog> blogs, bool doArchive)
        {
            foreach (IBlog blog in blogs)
            {
                // Step 1: Delete blog from database
                if (!_databaseService.DeleteBlog(blog.BlogId))
                {
                    Logger.Error($"ManagerController:RemoveBlog: Failed to delete blog {blog.Name} (ID: {blog.BlogId}) from database.");
                    _shellService.ShowError(null, Resources.CouldNotRemoveBlog, $"Failed to delete {blog.Name} from database.");
                    // Decide if we should proceed with file deletion or stop.
                    // For now, let's stop if DB deletion fails.
                    return; 
                }

                // Step 2: Delete blog files from disk (optional, based on settings)
                if (!_shellService.Settings.DeleteOnlyIndex) // This setting name might be misleading now.
                                                             // It should probably be "DeleteBlogFilesFromDisk" or similar.
                                                             // Assuming for now it means "delete files from disk".
                {
                    try
                    {
                        string blogDownloadPath = blog.DownloadLocation(); // Uses the DownloadLocation() method from IBlog
                        if (Directory.Exists(blogDownloadPath))
                        {
                            Directory.Delete(blogDownloadPath, true);
                            Logger.Information($"ManagerController:RemoveBlog: Deleted directory {blogDownloadPath} for blog {blog.Name}.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"ManagerController:RemoveBlog: Error deleting files for blog {blog.Name} at {blog.DownloadLocation()}: {ex.Message}");
                        _shellService.ShowError(ex, Resources.CouldNotRemoveBlog, $"Error deleting files for {blog.Name}: {ex.Message}");
                        // Continue to remove from UI even if file deletion fails.
                    }
                }

                // Step 3: Remove from UI collections
                _managerService.BlogFiles.Remove(blog);
                QueueManager.RemoveItems(QueueManager.Items.Where(item => item.Blog.Equals(blog)));

                // Old logic for removing from _managerService.Databases (IFiles list) is no longer needed.
                // Old logic for archiving/deleting individual blog index files (e.g., MyBlog.tumblr, MyBlog_files.tumblr) is removed.
            }
        }

        private bool CanShowFiles() => ManagerViewModel.SelectedBlogFile != null;

        private void ShowFiles()
        {
            foreach (IBlog blog in _selectionService.SelectedBlogFiles.ToArray())
            {
                try
                {
                    Process.Start("explorer.exe", blog.DownloadLocation());
                }
                catch (Exception ex)
                {
                    _shellService.ShowError(ex, Resources.ErrorShowingBlogFiles, blog.Name);
                }
            }
        }

        private bool CanVisitBlog() => ManagerViewModel.SelectedBlogFile != null;

        private void VisitBlog()
        {
            foreach (IBlog blog in _selectionService.SelectedBlogFiles.ToArray())
            {
                try
                {
                    Process.Start(blog.Url);
                }
                catch (Exception ex)
                {
                    _shellService.ShowError(ex, Resources.ErrorOpeningBlogUrl, blog.Name);
                }
            }
        }

        private void VisitBlogOnTumbex()
        {
            foreach (IBlog blog in _selectionService.SelectedBlogFiles.ToArray())
            {
                string tumbexUrl = $"https://www.tumbex.com/{blog.Name}.tumblr/";
                Process.Start(tumbexUrl);
            }
        }

        private void ShowDetailsCommand() => _shellService.ShowDetailsView();

        private void CopyUrl()
        {
            List<string> urls = _selectionService.SelectedBlogFiles.Select(blog => blog.Url).ToList();
            urls.Sort();
            _clipboardService.SetText(string.Join(Environment.NewLine, urls));
        }

        private bool CanCopyUrl() => ManagerViewModel.SelectedBlogFile != null;

        private bool CanCheckStatus() => ManagerViewModel.SelectedBlogFile != null;

        private async Task AddBlogAsync(string blogUrl, bool fromClipboard)
        {
            string currentBlogUrl = string.IsNullOrEmpty(blogUrl) ? _crawlerService.NewBlogUrl : blogUrl;
            if (string.IsNullOrEmpty(currentBlogUrl)) return;

            IBlog tempNewBlog = await CheckIfCrawlableBlog(currentBlogUrl, fromClipboard);
            if (tempNewBlog == null)
            {
                Logger.Warning($"ManagerController:AddBlogAsync:CheckIfCrawlableBlog returned null for {currentBlogUrl}");
                if (!fromClipboard) _shellService.ShowError(null, Resources.CouldNotAddBlog, $"{currentBlogUrl} is not a valid blog type or URL.");
                return;
            }
            
            // tempNewBlog here is a transient IBlog, not yet saved to DB. Its Location should be set.
            // tempNewBlog.Settings should have defaults from _settingsService.GetDefaultBlogSettings() or similar after _settingsService.TransferGlobalSettingsToBlog below.

            // Check if blog already exists in DB by name
            var existingBlogDtoByName = _databaseService.GetBlogByName(tempNewBlog.Name);
            if (existingBlogDtoByName != null)
            {
                _shellService.ShowError(null, Resources.BlogAlreadyExist, tempNewBlog.Name);
                return;
            }

            // Apply global settings to the transient blog's Settings object
            // Note: _settingsService.TransferGlobalSettingsToBlog used to return a modified IBlog.
            // Now, it should ideally modify tempNewBlog.Settings directly, or we adapt.
            // For now, let's assume it can modify tempNewBlog.Settings or we extract settings from its return value.
            tempNewBlog = _settingsService.TransferGlobalSettingsToBlog(tempNewBlog); // This populates tempNewBlog.Settings

            // Set default Tumblr crawler type if applicable (modifies tempNewBlog.BlogType and potentially tempNewBlog.Settings)
            SetDefaultTumblrBlogCrawler(tempNewBlog); 

            // Ensure download directory exists and name is unique for the file system.
            // This also sets tempNewBlog.Location correctly if it was modified for uniqueness.
            _managerService.EnsureUniqueFolder(tempNewBlog);

            // Convert transient IBlog to DTO for saving
            BlogDto dtoToSave = tempNewBlog.ToDto();
            // Ensure the (potentially modified by EnsureUniqueFolder) Location is in the DTO
            dtoToSave.DownloadLocation = tempNewBlog.Location; 
                                                          
            int newBlogId = _databaseService.AddBlog(dtoToSave);
            if (newBlogId == 0)
            {
                Logger.Error($"ManagerController:AddBlogAsync: Failed to add blog {tempNewBlog.Name} to database.");
                _shellService.ShowError(null, Resources.CouldNotAddBlog, $"Failed to save {tempNewBlog.Name} to the database.");
                return;
            }

            // Fetch the DTO back to get all DB-generated fields (like BlogId, default timestamps)
            BlogDto newCreatedDto = _databaseService.GetBlog(newBlogId);
            if (newCreatedDto == null)
            {
                 Logger.Error($"ManagerController:AddBlogAsync: Failed to retrieve newly added blog {tempNewBlog.Name} (ID: {newBlogId}) from database.");
                _shellService.ShowError(null, Resources.CouldNotAddBlog, $"Failed to load {tempNewBlog.Name} from database after adding.");
                return;
            }

            // Instantiate the final IBlog instance for UI and further operations
            IBlog finalBlogForUI;
            if (!Enum.TryParse<BlogTypes>(newCreatedDto.BlogType, true, out var typeEnum))
            {
                Logger.Error($"ManagerController:AddBlogAsync: Invalid blog type string '{newCreatedDto.BlogType}' from DB for blog {newCreatedDto.Name}.");
                 _shellService.ShowError(null, Resources.CouldNotAddBlog, $"Invalid blog type for {newCreatedDto.Name} after saving.");
                return;
            }

            // Use _blogFactory if it's adapted for DTOs, otherwise manual instantiation
            // finalBlogForUI = _blogFactory.GetBlog(newCreatedDto, tempNewBlog.Settings); // Ideal
            switch (typeEnum)
            {
                case BlogTypes.tumblr:
                    finalBlogForUI = new TumblrBlog(newCreatedDto, tempNewBlog.Settings);
                    break;
                // Add cases for TwitterBlog, NewTumblBlog, etc.
                // case BlogTypes.twitter:
                //    finalBlogForUI = new TwitterBlog(newCreatedDto, tempNewBlog.Settings);
                //    break;
                default:
                    Logger.Error($"ManagerController:AddBlogAsync: Failed to create IBlog instance for newly added blog: {newCreatedDto.Name} of type {typeEnum}.");
                    _shellService.ShowError(null, Resources.CouldNotAddBlog, $"Unsupported blog type {typeEnum} for {newCreatedDto.Name}.");
                    // Consider deleting the DB entry if UI object cannot be created: _databaseService.DeleteBlog(newBlogId);
                    return;
            }
            
            // Add to UI collection
            QueueOnDispatcher.CheckBeginInvokeOnUI(() => _managerService.BlogFiles.Add(finalBlogForUI));
            
            // Clear URL from input only on successful addition through UI (not for clipboard/import)
            if (!fromClipboard) // Assuming direct calls to AddBlogAsync with null blogUrl come from UI
            {
                 // _crawlerService.NewBlogUrl = ""; // Moved to finally block of AddBlog()
            }

            await UpdateMetaInformationAsync(finalBlogForUI); // Update metadata for the DB-backed IBlog
        }

        private void SetDefaultTumblrBlogCrawler(IBlog blog)
        {
            // This method modifies blog.BlogType and potentially blog.Settings if settings are associated with BlogType changes.
            // The caller (AddBlogAsync) is responsible for persisting these changes via blog.ToDto() if this is a new blog,
            // or calling _databaseService.UpdateBlog(blog.ToDto()) if it's an existing blog being modified.
            if (_shellService.Settings.OverrideTumblrBlogCrawler)
            {
                if (blog.BlogType == BlogTypes.tumblr || blog.BlogType == BlogTypes.tmblrpriv)
                {
                    BlogTypes newType = _shellService.Settings.TumblrBlogCrawlerType.MapToBlogType();
                    if (blog.BlogType != newType)
                    {
                        blog.BlogType = newType;
                        // If blog.Settings needs adjustment based on newType, do it here.
                        // For example: blog.Settings.SomeSetting = newDefaultBasedOnType;
                        blog.Dirty = true; // Mark as dirty if changes are made.
                    }
                }
            }
        }

        // SaveBlog method removed, replaced by direct DB calls.
        // AddToManager method removed, logic integrated into AddBlogAsync.
        // CheckIfBlogAlreadyExists method removed (functionality in AddBlogAsync).
        // CheckifBlogsAreTumblrBlogs method removed (functionality in AddBlogAsync if needed, or no longer applicable with DB check by name).

        private async Task UpdateMetaInformationAsync(IBlog blog) // Now accepts IBlog (which is DB backed)
        {
            if (blog == null) return;

            ICrawler crawler = null;
            try
            {
                // The crawler updates the properties of the passed 'blog' instance (e.g. Title, Description, Posts count etc.)
                crawler = _crawlerFactory.GetCrawler(blog, new Progress<DownloadProgress>(), new PauseToken(), new CancellationToken());
                await crawler.UpdateMetaInformationAsync();

                // After crawler updates 'blog' instance, persist these changes to the database.
                if (blog.Dirty) // Check if crawler actually made changes
                {
                    _databaseService.UpdateBlog(blog.ToDto());
                    blog.Dirty = false; // Reset dirty flag
                }
            }
            catch(Exception ex)
            {
                Logger.Error($"ManagerController:UpdateMetaInformationAsync: Error updating metadata for blog {blog.Name}. {ex}");
                // Optionally show error to user, or just log.
            }
            finally
            {
                crawler?.Dispose();
            }
        }

        private string GetIndexFolderPath(int CollectionId = 0) // TODO: Review if this is still needed. Location is now directly in BlogDto.DownloadLocation
        {
            var downloadLocation = (CollectionId == 0) ? _shellService.Settings.DownloadLocation : _shellService.Settings.GetCollection(CollectionId).DownloadLocation;
            return Path.Combine(downloadLocation, "Index");
        }

        private async Task<IBlog> CheckIfCrawlableBlog(string blogUrl, bool fromClipboard)
        {
            if (!_blogFactory.IsValidBlogUrl(blogUrl))
            {
                if (fromClipboard) throw new Exception();
                if (_blogFactory.IsValidUrl(blogUrl) && await _tumblrBlogDetector.IsTumblrBlogWithCustomDomainAsync(blogUrl))
                    return TumblrBlog.Create(blogUrl, GetIndexFolderPath(_shellService.Settings.ActiveCollectionId), _shellService.Settings.FilenameTemplate, true);
                throw new Exception($"The url '{blogUrl}' cannot be recognized as valid blog!");
            }
            // This creates a transient IBlog instance. Its Location should be the root download path for the blog.
            // FilenameTemplate is now part of BlogRuntimeSettings, which _blogFactory should handle.
            string downloadLocation = _shellService.Settings.GetCollection(_shellService.Settings.ActiveCollectionId).DownloadLocation;
            return _blogFactory.GetBlog(blogUrl, downloadLocation, _shellService.Settings.FilenameTemplate);
        }

        // AddToManager removed, logic integrated into AddBlogAsync.

        private async Task<IBlog> CheckIfBlogIsHiddenTumblrBlogAsync(IBlog blog) // Operates on transient IBlog
        {
            if (blog.GetType() == typeof(TumblrBlog) && await _tumblrBlogDetector.IsHiddenTumblrBlogAsync(blog.Url))
            {
                RemoveBlog(new[] { blog }, false);
                blog = TumblrHiddenBlog.Create("https://www.tumblr.com/dashboard/blog/" + blog.Name,
                    GetIndexFolderPath(_shellService.Settings.ActiveCollectionId), _shellService.Settings.FilenameTemplate);
            }

            return blog;
        }

        private static string oldContent;

        private void OnClipboardContentChanged(object sender, EventArgs e)
        {
            try
            {
                if (!Clipboard.ContainsText()) return;

                // Count each whitespace as new url
                string content = Clipboard.GetText();
                if (content == null || oldContent == content) return;
                oldContent = content;
                string[] urls = content.Split();

                Task.Run(() => AddBlogBatchedAsync(urls, true));
            }
            catch (Exception ex)
            {
                Logger.Error($"ManagerController:OnClipboardContentChanged: {ex}");
                _shellService.ShowError(new ClipboardContentException(ex), "error getting clipboard content");
            }
        }

        private async Task AddBlogBatchedAsync(IEnumerable<string> urls, bool fromClipboard)
        {
            var semaphoreSlim = new SemaphoreSlim(25);

            await _addBlogSemaphoreSlim.WaitAsync();
            QueueOnDispatcher.CheckBeginInvokeOnUI(() => Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait);
            try
            {
                IEnumerable<Task> tasks = urls.Select(async url => await AddBlogsAsync(semaphoreSlim, url, fromClipboard));
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Logger.Error($"ManagerController:AddBlogBatchedAsync: {ex}");
                _shellService.ShowError(new ClipboardContentException(ex), "error getting clipboard content");
            }
            finally
            {
                _addBlogSemaphoreSlim.Release();
                semaphoreSlim.Dispose();
                QueueOnDispatcher.CheckBeginInvokeOnUI(() => Mouse.OverrideCursor = null);
            }
        }

        private async Task AddBlogsAsync(SemaphoreSlim semaphoreSlim, string url, bool fromClipboard)
        {
            try
            {
                await semaphoreSlim.WaitAsync();
                await AddBlogAsync(url, fromClipboard);
            }
            catch (Exception e)
            {
                if (!fromClipboard)
                {
                    Logger.Error("ManagerController.AddBlogsAsync: {0}", e);
                    _shellService.ShowError(e, Resources.CouldNotAddBlog, e.Message);
                }
            }
            finally
            {
                semaphoreSlim.Release();
            }
        }

        private void ListenClipboard()
        {
            if (_shellService.Settings.CheckClipboard)
            {
                _shellService.ClipboardMonitor.OnClipboardContentChanged += OnClipboardContentChanged;
            }
            else
            {
                _shellService.ClipboardMonitor.OnClipboardContentChanged -= OnClipboardContentChanged;
            }
        }

        private void CrawlerServicePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_crawlerService.NewBlogUrl))
            {
                QueueOnDispatcher.CheckBeginInvokeOnUI(() => { _addBlogCommand.RaiseCanExecuteChanged(); });
            }
        }

        private void ManagerViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ManagerViewModel.SelectedBlogFile))
            {
                UpdateCommands();
            }
        }

        private void UpdateCommands()
        {
            _enqueueSelectedCommand.RaiseCanExecuteChanged();
            _dequeueSelectedCommand.RaiseCanExecuteChanged();
            _removeBlogCommand.RaiseCanExecuteChanged();
            _showFilesCommand.RaiseCanExecuteChanged();
        }

        public void RestoreColumn() => ManagerViewModel.DataGridColumnRestore();

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _addBlogSemaphoreSlim.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
