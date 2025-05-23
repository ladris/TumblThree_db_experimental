namespace TumblThree.Domain.Database
{
    public class BlogDto
    {
        public int BlogId { get; set; }
        public string Name { get; set; }
        public string Title { get; set; } // Added
        public string Description { get; set; } // Added
        public string Url { get; set; }
        public string BlogType { get; set; }
        public string DownloadLocation { get; set; }
        public string LastCrawledPostId { get; set; }
        public long? LastCrawledTimestamp { get; set; } // Nullable Unix timestamp
        public string SettingsJson { get; set; }
        public string Notes { get; set; }
        public bool OnlineStatus { get; set; } // Will be stored as INTEGER 0 or 1
        public string Version { get; set; }
        public long AddedTimestamp { get; set; } // Unix timestamp
    }
}
