namespace TumblThree.Domain.Database
{
    public class FileDto
    {
        public int FileId { get; set; }
        public int BlogId { get; set; }
        public string Link { get; set; }
        public string OriginalLink { get; set; }
        public string Filename { get; set; }
        public long Timestamp { get; set; } // Unix timestamp
        public string Md5Hash { get; set; }
        public long FileSize { get; set; }
        public string AdditionalProperties { get; set; } // JSON
    }
}
