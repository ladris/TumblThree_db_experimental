namespace TumblThree.Domain.Database
{
    public class DownloadDto
    {
        public int DownloadId { get; set; }
        public int FileId { get; set; }
        public string Status { get; set; }
        public long ProgressBytes { get; set; }
        public long TotalBytes { get; set; }
        public long LastAttemptTimestamp { get; set; } // Unix timestamp
        public int RetryCount { get; set; }
        public int DownloadPriority { get; set; }
        public string DownloadUrl { get; set; }

        // Fields from joined Files table
        public string Filename { get; set; } 
        public string Link { get; set; } 
        public int BlogId { get; set; } 
    }
}
