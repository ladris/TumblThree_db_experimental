using System;
using System.Globalization;
using System.IO;
using System.Linq;
//using System.Runtime.Serialization; // Commented out

//using TumblThree.Domain.Models.Files; // No longer creating TumblrBlogFiles here
using TumblThree.Domain.Database; // Added for BlogDto

namespace TumblThree.Domain.Models.Blogs
{
    //[DataContract] // Commented out
    public class TumblrBlog : Blog
    {
        // New constructor
        public TumblrBlog(BlogDto dto, BlogRuntimeSettings settings = null) : base(dto, settings)
        {
            // Tumblr specific initialization from DTO if any
            // For example, if OriginalBlogType needs to be specifically set for Tumblr type
            this.OriginalBlogType = BlogTypes.tumblr;
        }

        // Adapted static Create method
        public static Blog Create(string url, string location, string filenameTemplate, bool isCustomDomain = false)
        {
            string processedUrl = isCustomDomain ? url : ExtractUrl(ConvertNewFormatUrl(url));
            var name = isCustomDomain ? ExtractCustomName(url) : ExtractName(processedUrl); // Use processedUrl for name extraction if not custom

            var dto = new BlogDto
            {
                Name = name,
                Url = processedUrl, // Use the processed URL
                BlogType = BlogTypes.tumblr.ToString(),
                DownloadLocation = location, // This is the root download path
                AddedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Version = "4.0", // Or use a shared constant for app/schema version
                OnlineStatus = true, // Default for new blogs
                // Title, Description, etc., can be populated later during a crawl/update
            };

            var runtimeSettings = new BlogRuntimeSettings
            {
                FilenameTemplate = filenameTemplate
                // Set any Tumblr-specific default settings here if needed
            };
            
            var blog = new TumblrBlog(dto, runtimeSettings);
            // blog.Settings.FilenameTemplate is already set by passing runtimeSettings to constructor.
            // No direct file system interaction here (e.g., Directory.CreateDirectory or saving TumblrBlogFiles)
            // The caller (e.g., ManagerController) will handle DB insertion.

            return blog;
        }

        private static string ConvertNewFormatUrl(string url)
        {
            if (UrlValidator.IsValidTumblrUrlInNewFormat(url))
            {
                var name = UrlValidator.GetTumblrNewUrlFormatBlogname(url);
                return $"https://{name}.tumblr.com";
            }
            else
            {
                return Blog.ExtractUrl(url);
            }
        }

        private static string ExtractCustomName(string url)
        {
            url = url.ToLower(CultureInfo.InvariantCulture).Replace("https://", string.Empty).Replace("http://", string.Empty).TrimEnd('/');
            var parts = url.Split('.');
            return parts[parts.Length - 2];
        }
    }
}
