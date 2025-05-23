using Newtonsoft.Json; // Added
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
//using System.Runtime.Serialization; // Commented out
//using System.Runtime.Serialization.Json; // Commented out
using System.Text;
using System.Threading;
using System.Waf.Foundation;
//using System.Xml; // Commented out
using TumblThree.Domain.Database; // Added for BlogDto

namespace TumblThree.Domain.Models.Blogs
{
#pragma warning disable SX1309 // Field names should begin with underscore
    //[DataContract] // Commented out
    public class Blog : Model, IBlog
    {
        // Fields moved to BlogRuntimeSettings are removed.
        // Backing fields for properties that remain in IBlog but are now auto-properties or populated from DTO.
        private string lastDownloadedPhoto;
        private string lastDownloadedVideo;
        private string notes;
        private int rating;
        private string tags;
        private int duplicatePhotos;
        private int duplicateVideos;
        private int duplicateAudios;
        private int totalCount;
        private int posts;
        private int texts;
        private int answers;
        private int photos;
        private int numberOfLinks;
        private int conversations;
        private int videos;
        private int audios;
        private int photoMetas;
        private int videoMetas;
        private int audioMetas;
        private int downloadedTexts;
        private int downloadedQuotes;
        private int downloadedPhotos;
        private int downloadedLinks;
        private int downloadedAnswers;
        private int downloadedConversations;
        private int downloadedVideos;
        private int downloadedAudios;
        private int downloadedPhotoMetas;
        private int downloadedVideoMetas;
        private int downloadedAudioMetas;
        private DateTime dateAdded;
        private DateTime lastCompleteCrawl;
        private DateTime latestPost;
        private bool online;
        private int settingsTabIndex;
        private int progress;
        private int quotes;
        private BlogTypes blogType;
        private PostType states; // Retained as it seems like runtime state not setting
        private int collectionId;
        private int downloadedItemsNew;
        private bool downloadReplies; // Kept as per IBlog, might move later if it's a setting

        // New properties from IBlog
        public int BlogId { get; protected set; }
        public BlogRuntimeSettings Settings { get; set; }

        // Retained lock objects
        private object lockObjectProgress = new object();
        private object lockObjectPostCount = new object();
        // private object lockObjectDb = new object(); // Removed as DB interaction is changing
        private object lockObjectDirectory = new object();

        // private int bufferSizeKB = 4; // No longer used for file load/save here

        // Enum for runtime state, distinct from settings.
        public enum PostType
        {
            Photo,
            Video
        }

        //[DataMember] // Commented out
        public PostType States
        {
            get => states;
            set => SetProperty(ref states, value);
        }

        //[DataMember] // Commented out
        public string Version { get; set; }

        //[DataMember] // Commented out
        public BlogTypes OriginalBlogType { get; set; } // This might be derived or set during DTO mapping/construction

        //[DataMember] // Commented out
        public int DuplicatePhotos
        {
            get => duplicatePhotos;
            set => SetProperty(ref duplicatePhotos, value);
        }

        //[DataMember] // Commented out
        public int DuplicateVideos
        {
            get => duplicateVideos;
            set => SetProperty(ref duplicateVideos, value);
        }

        //[DataMember] // Commented out
        public int DuplicateAudios
        {
            get => duplicateAudios;
            set => SetProperty(ref duplicateAudios, value);
        }

        // Properties moved to BlogRuntimeSettings are removed.
        // Example: DownloadText, DownloadQuote, DumpCrawlerData, FileDownloadLocation, etc.

        //[DataMember] // Commented out
        public bool DownloadReplies // Retained as per IBlog
        {
            get => downloadReplies;
            set => SetProperty(ref downloadReplies, value);
        }

        //[DataMember] // Commented out
        public string Name { get; set; }

        //[DataMember] // Commented out
        public string Url { get; set; }

        //[DataMember] // Commented out
        public string Location { get; set; } // Root download path

        // ChildId removed

        //[DataMember] // Commented out
        public BlogTypes BlogType
        {
            get => blogType;
            set => SetProperty(ref blogType, value);
        }

        //[DataMember] // Commented out
        public int DownloadedItemsNew
        {
            get => downloadedItemsNew;
            set => SetProperty(ref downloadedItemsNew, value);
        }

        public int DownloadedItems
        {
            get
            {
                return downloadedAnswers + downloadedAudioMetas + downloadedAudios + downloadedConversations + downloadedLinks
                    + downloadedPhotoMetas + downloadedPhotos + downloadedQuotes + downloadedTexts + downloadedVideoMetas + downloadedVideos;
            }
        }

        //[DataMember] // Commented out
        public int TotalCount
        {
            get => totalCount;
            set => SetProperty(ref totalCount, value);
        }

        //[DataMember] // Commented out
        public string Tags
        {
            get => tags;
            set => SetProperty(ref tags, value);
        }

        //[DataMember] // Commented out
        public int Rating
        {
            get => rating;
            set => SetProperty(ref rating, value);
        }

        //[DataMember] // Commented out
        public int Posts
        {
            get => posts;
            set => SetProperty(ref posts, value);
        }

        //[DataMember] // Commented out
        public int Texts
        {
            get => texts;
            set => SetProperty(ref texts, value);
        }

        //[DataMember] // Commented out
        public int Answers
        {
            get => answers;
            set => SetProperty(ref answers, value);
        }

        //[DataMember] // Commented out
        public int Quotes
        {
            get => quotes;
            set => SetProperty(ref quotes, value);
        }

        //[DataMember] // Commented out
        public int Photos
        {
            get => photos;
            set => SetProperty(ref photos, value);
        }

        //[DataMember] // Commented out
        public int NumberOfLinks
        {
            get => numberOfLinks;
            set => SetProperty(ref numberOfLinks, value);
        }

        //[DataMember] // Commented out
        public int Conversations
        {
            get => conversations;
            set => SetProperty(ref conversations, value);
        }

        //[DataMember] // Commented out
        public int Videos
        {
            get => videos;
            set => SetProperty(ref videos, value);
        }

        //[DataMember] // Commented out
        public int Audios
        {
            get => audios;
            set => SetProperty(ref audios, value);
        }

        //[DataMember] // Commented out
        public int PhotoMetas
        {
            get => photoMetas;
            set => SetProperty(ref photoMetas, value);
        }

        //[DataMember] // Commented out
        public int VideoMetas
        {
            get => videoMetas;
            set => SetProperty(ref videoMetas, value);
        }

        //[DataMember] // Commented out
        public int AudioMetas
        {
            get => audioMetas;
            set => SetProperty(ref audioMetas, value);
        }

        //[DataMember] // Commented out
        public int DownloadedTexts
        {
            get => downloadedTexts;
            set
            {
                if (SetProperty(ref downloadedTexts, value))
                {
                    RaisePropertyChanged(nameof(DownloadedItems));
                }
            }
        }

        //[DataMember] // Commented out
        public int DownloadedQuotes
        {
            get => downloadedQuotes;
            set
            {
                if (SetProperty(ref downloadedQuotes, value))
                {
                    RaisePropertyChanged(nameof(DownloadedItems));
                }
            }
        }

        //[DataMember] // Commented out
        public int DownloadedPhotos
        {
            get => downloadedPhotos;
            set
            {
                if (SetProperty(ref downloadedPhotos, value))
                {
                    RaisePropertyChanged(nameof(DownloadedItems));
                }
            }
        }

        //[DataMember] // Commented out
        public int DownloadedLinks
        {
            get => downloadedLinks;
            set
            {
                if (SetProperty(ref downloadedLinks, value))
                {
                    RaisePropertyChanged(nameof(DownloadedItems));
                }
            }
        }

        //[DataMember] // Commented out
        public int DownloadedConversations
        {
            get => downloadedConversations;
            set
            {
                if (SetProperty(ref downloadedConversations, value))
                {
                    RaisePropertyChanged(nameof(DownloadedItems));
                }
            }
        }

        //[DataMember] // Commented out
        public int DownloadedAnswers
        {
            get => downloadedAnswers;
            set
            {
                if (SetProperty(ref downloadedAnswers, value))
                {
                    RaisePropertyChanged(nameof(DownloadedItems));
                }
            }
        }

        //[DataMember] // Commented out
        public int DownloadedVideos
        {
            get => downloadedVideos;
            set
            {
                if (SetProperty(ref downloadedVideos, value))
                {
                    RaisePropertyChanged(nameof(DownloadedItems));
                }
            }
        }

        //[DataMember] // Commented out
        public int DownloadedAudios
        {
            get => downloadedAudios;
            set
            {
                if (SetProperty(ref downloadedAudios, value))
                {
                    RaisePropertyChanged(nameof(DownloadedItems));
                }
            }
        }

        //[DataMember] // Commented out
        public int DownloadedPhotoMetas
        {
            get => downloadedPhotoMetas;
            set
            {
                if (SetProperty(ref downloadedPhotoMetas, value))
                {
                    RaisePropertyChanged(nameof(DownloadedItems));
                }
            }
        }

        //[DataMember] // Commented out
        public int DownloadedVideoMetas
        {
            get => downloadedVideoMetas;
            set
            {
                if (SetProperty(ref downloadedVideoMetas, value))
                {
                    RaisePropertyChanged(nameof(DownloadedItems));
                }
            }
        }

        //[DataMember] // Commented out
        public int DownloadedAudioMetas
        {
            get => downloadedAudioMetas;
            set
            {
                if (SetProperty(ref downloadedAudioMetas, value))
                {
                    RaisePropertyChanged(nameof(DownloadedItems));
                }
            }
        }

        // Properties moved to BlogRuntimeSettings (e.g., MetadataFormat, DownloadImgur, etc.) are removed.

        //[DataMember] // Commented out
        public DateTime DateAdded
        {
            get => dateAdded;
            set => SetProperty(ref dateAdded, value);
        }

        //[DataMember(IsRequired = false, EmitDefaultValue = false)] // Commented out
        public DateTime LastCompleteCrawl
        {
            get => lastCompleteCrawl;
            set => SetProperty(ref lastCompleteCrawl, value);
        }

        //[DataMember(IsRequired = false, EmitDefaultValue = false)] // Commented out
        public DateTime LatestPost
        {
            get => latestPost;
            set => SetProperty(ref latestPost, value);
        }

        // FilenameTemplate moved to BlogRuntimeSettings

        //[DataMember] // Commented out
        public bool Online
        {
            get => online;
            set => SetProperty(ref online, value);
        }

        //[DataMember] // Commented out
        public int SettingsTabIndex
        {
            get => settingsTabIndex;
            set => SetProperty(ref settingsTabIndex, value);
        }

        //[DataMember] // Commented out
        public int Progress
        {
            get => progress;
            set => SetProperty(ref progress, value);
        }

        //[DataMember] // Commented out
        public string Notes
        {
            get => notes;
            set => SetProperty(ref notes, value);
        }

        // CheckDirectoryForFiles moved to BlogRuntimeSettings
        // DownloadUrlList moved to BlogRuntimeSettings

        public bool Dirty { get; set; } // Retained for now, though its update logic might change

        public Exception LoadError { get; set; } // Retained

        // links field and Links property removed

        public string LastDownloadedPhoto
        {
            get => lastDownloadedPhoto;
            set
            {
                SetProperty(ref lastDownloadedPhoto, value);
                States = PostType.Photo;
            }
        }

        public string LastDownloadedVideo
        {
            get => lastDownloadedVideo;
            set
            {
                SetProperty(ref lastDownloadedVideo, value);
                States = PostType.Video;
            }
        }

        //[DataMember] // Commented out
        public string Description { get; set; }

        //[DataMember] // Commented out
        public string Title { get; set; }

        //[DataMember] // Commented out
        public ulong LastId { get; set; }

        // SkipGif, DownloadVideoThumbnail, ForceSize, ForceRescan moved to BlogRuntimeSettings
        // GroupPhotoSets moved to BlogRuntimeSettings

        //[DataMember] // Commented out
        public int CollectionId
        {
            get => collectionId;
            set => SetProperty(ref collectionId, value);
        }

        // PnjDownloadFormat, SaveTextsIndividualFiles, ZipCrawlerData moved to BlogRuntimeSettings

        //[IgnoreDataMember] // Commented out
        public long LastPreviewShown { get; set; }


        // NEW CONSTRUCTOR
        public Blog(BlogDto dto, BlogRuntimeSettings settings = null)
        {
            this.BlogId = dto.BlogId;
            this.Name = dto.Name;
            this.Url = dto.Url;
            this.BlogType = Enum.TryParse<BlogTypes>(dto.BlogType, true, out var type) ? type : default;
            this.Location = dto.DownloadLocation; // This is the root download path
            this.LastId = Convert.ToUInt64(dto.LastCrawledPostId ?? "0");
            this.Title = dto.Title;
            this.Description = dto.Description;
            this.Notes = dto.Notes;
            this.Online = dto.OnlineStatus;
            this.Version = dto.Version;
            this.DateAdded = DateTimeOffset.FromUnixTimeSeconds(dto.AddedTimestamp).DateTime;
            this.LastCompleteCrawl = dto.LastCrawledTimestamp.HasValue ? DateTimeOffset.FromUnixTimeSeconds(dto.LastCrawledTimestamp.Value).DateTime : default;

            if (settings != null) 
            {
                this.Settings = settings;
            }
            else
            {
                this.Settings = string.IsNullOrEmpty(dto.SettingsJson) 
                    ? new BlogRuntimeSettings() 
                    : JsonConvert.DeserializeObject<BlogRuntimeSettings>(dto.SettingsJson) ?? new BlogRuntimeSettings();
            }
            // Initialize other non-setting fields if necessary, e.g. progress counters
            // this.Dirty = false; // Initial state from DB is not dirty
        }

        protected Blog() // Default constructor for derived classes or older deserialization paths if any remain temporarily
        {
             this.Settings = new BlogRuntimeSettings(); // Ensure settings is initialized
        }


        // ToDto Method
        public virtual BlogDto ToDto()
        {
            var newDto = new BlogDto
            {
                BlogId = this.BlogId,
                Name = this.Name,
                Url = this.Url,
                BlogType = this.BlogType.ToString(),
                DownloadLocation = this.Location, // Location is the root path
                LastCrawledPostId = this.LastId.ToString(),
                Notes = this.Notes,
                OnlineStatus = this.Online,
                Version = this.Version,
                AddedTimestamp = new DateTimeOffset(this.DateAdded.ToUniversalTime()).ToUnixTimeSeconds(), // Ensure UTC for consistency
                LastCrawledTimestamp = (this.LastCompleteCrawl == default(DateTime)) ? (long?)null : new DateTimeOffset(this.LastCompleteCrawl.ToUniversalTime()).ToUnixTimeSeconds(),
                Title = this.Title,
                Description = this.Description,
                SettingsJson = JsonConvert.SerializeObject(this.Settings, Newtonsoft.Json.Formatting.None)
            };
            return newDto;
        }


        private new bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false; // Added to prevent marking dirty if value hasn't changed
            
            field = value;
            RaisePropertyChanged(propertyName);
            Dirty = true; // Mark as dirty if a property changes
            return true;
        }

        public void UpdateProgress(bool doCount)
        {
            lock (lockObjectProgress)
            {
                if (doCount) { DownloadedItemsNew++; }
                if (TotalCount > 0) // Avoid division by zero
                    Progress = (int)(DownloadedItems / (double)TotalCount * 100);
                else
                    Progress = 0;
            }
        }

        public void UpdatePostCount(string propertyName)
        {
            lock (lockObjectPostCount)
            {
                PropertyInfo property = typeof(IBlog).GetProperty(propertyName);
                if (property != null && property.CanWrite)
                {
                    var postCounter = (int)property.GetValue(this);
                    postCounter++;
                    property.SetValue(this, postCounter, null);
                }
            }
        }

        // AddFileToDb removed

        public bool CreateDataFolder()
        {
            string loc = DownloadLocation(); // Use the method
            if (!Directory.Exists(loc))
            {
                Directory.CreateDirectory(loc);
                return true;
            }
            return false;
        }

        // CheckIfFileExistsInDB removed

        public virtual bool CheckIfBlogShouldCheckDirectory(string filename, string filenameNew)
        {
            // Access CheckDirectoryForFiles via Settings property
            return Settings.CheckDirectoryForFiles && CheckIfFileExistsInDirectory(filename, filenameNew);
        }

        public virtual bool CheckIfFileExistsInDirectory(string filename, string filenameNew)
        {
            Monitor.Enter(lockObjectDirectory);
            string blogPath = DownloadLocation(); // Use the method
            try
            {
                string filepath = Path.Combine(blogPath, filename);
                string filepathNew = Path.Combine(blogPath, filenameNew);
                bool result = File.Exists(filepath);
                if (result && !string.IsNullOrEmpty(filenameNew))
                {
                    if (File.Exists(filepathNew))
                    {
                        // Logger.Warning is not available directly, consider how to log or handle this
                    }
                    else
                    {
                        File.Move(filepath, filepathNew);
                    }
                }
                if (result || string.IsNullOrEmpty(filenameNew)) return result;
                return File.Exists(filepathNew);
            }
            finally
            {
                Monitor.Exit(lockObjectDirectory);
            }
        }

        public string DownloadLocation() // Now simply returns Location
        {
            // Old logic:
            // if (string.IsNullOrWhiteSpace(FileDownloadLocation)) // FileDownloadLocation is now in Settings
            // {
            //     return Path.Combine(Directory.GetParent(Location).FullName, Name); // This assumed Location was index path
            // }
            // return FileDownloadLocation;
            return this.Location; // Location is now the root download path
        }

        // Load method removed
        // Save method removed
        // SaveBlog method removed
        // SaveCore method removed

        protected static string ExtractSubDomain(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var uri = new Uri(url);
            var parts = uri.Host.Split('.');
            if (parts.Length > 1) return parts[0]; // Simplified, might need adjustment for complex domains
            return uri.Host;
        }

        public static string ExtractName(string url) // Remains for utility
        {
            return ExtractSubDomain(url);
        }

        protected static string ExtractUrl(string url) // Remains for utility
        {
            if (string.IsNullOrEmpty(url)) return null;
            var subDomain = ExtractSubDomain(url);
            if (string.IsNullOrEmpty(subDomain)) return null; // Or handle error
            return $"https://{subDomain}.tumblr.com/";
        }

        // OnDeserialized removed as DataContract is removed
    }
#pragma warning restore SX1309 // Field names should begin with underscore
}
