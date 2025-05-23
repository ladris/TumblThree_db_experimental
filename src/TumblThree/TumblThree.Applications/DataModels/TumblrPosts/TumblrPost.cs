namespace TumblThree.Applications.DataModels.TumblrPosts
{
    public abstract class TumblrPost : AbstractPost
    {
        public long InitialProgressBytes { get; set; } = 0;
        public int ResumedDownloadId { get; set; } = 0;

        protected TumblrPost(string url, string postedUrl, string id, string date, string filename)
            : base(url, postedUrl, id, date, filename)
        {
        }

        public TumblrPost CloneWithAdjustedUrl(string newUrl)
        {
            var obj = (TumblrPost)MemberwiseClone();
            obj.Url = newUrl;
            return obj;
        }
    }
}
