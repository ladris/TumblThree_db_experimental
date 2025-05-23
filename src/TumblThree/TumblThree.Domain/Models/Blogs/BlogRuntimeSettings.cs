using TumblThree.Domain.Models.Files; // For PnjDownloadType, WebmshareTypes, UguuTypes, CatBoxType
using TumblThree.Domain.Models; // For MetadataType

namespace TumblThree.Domain.Models.Blogs
{
    public class BlogRuntimeSettings
    {
        // Default values are set here for clarity and explicitness.
        // Some of these might be overridden by global settings or blog-specific saved settings.

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

        public int PageSize { get; set; } = 250;
        public string FilenameTemplate { get; set; } = "%f"; // Default as per original Blog.cs
        public bool SkipGif { get; set; } = false;
        public bool DownloadRebloggedPosts { get; set; } = false;
        public bool GroupPhotoSets { get; set; } = false;

        public bool CheckDirectoryForFiles { get; set; } = false;
        public bool DownloadUrlList { get; set; } = false;
        public PnjDownloadType PnjDownloadFormat { get; set; } = PnjDownloadType.Apng;
        public MetadataType MetadataFormat { get; set; } = MetadataType.Text;

        public bool ForceRescan { get; set; } = false;
        public bool ForceSize { get; set; } = false;
        public bool DownloadVideoThumbnail { get; set; } = false;

        public bool DownloadImgur { get; set; } = false;
        public bool DownloadWebmshare { get; set; } = true;
        public WebmshareTypes WebmshareType { get; set; } = WebmshareTypes.Mp4;
        public bool DownloadUguu { get; set; } = true;
        public UguuTypes UguuType { get; set; } = UguuTypes.Original;
        public bool DownloadCatBox { get; set; } = true;
        public CatBoxType CatBoxType { get; set; } = CatBoxType.Original;

        public bool DownloadPages { get; set; } = false;
        public string DownloadFrom { get; set; } = string.Empty;
        public string DownloadTo { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public bool DumpCrawlerData { get; set; } = false;
        public string RegExPhotos { get; set; } = string.Empty;
        public string RegExVideos { get; set; } = string.Empty;

        public bool SaveTextsIndividualFiles { get; set; } = false;
        public bool ZipCrawlerData { get; set; } = false;

        public BlogRuntimeSettings()
        {
            // Constructor can be used for more complex initialization if needed,
            // but direct property initializers are often sufficient.
        }
    }
}
