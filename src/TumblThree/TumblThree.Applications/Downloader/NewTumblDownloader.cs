using System;
using System.Threading;
using TumblThree.Applications.DataModels;
using TumblThree.Applications.DataModels.TumblrPosts;
using TumblThree.Applications.Services;
using TumblThree.Domain.Database; // Added
using TumblThree.Domain.Models.Blogs;
//using TumblThree.Domain.Models.Files; // Removed

namespace TumblThree.Applications.Downloader
{
    public class NewTumblDownloader : AbstractDownloader
    {
        public NewTumblDownloader(IShellService shellService, IManagerService managerService, CancellationToken ct, PauseToken pt, IProgress<DownloadProgress> progress,
            IPostQueue<AbstractPost> postQueue, FileDownloader fileDownloader, DatabaseService databaseService, /* Added */
            ICrawlerService crawlerService = null, IBlog blog = null /* IFiles files = null Removed */)
            : base(shellService, managerService, ct, pt, progress, postQueue, fileDownloader, databaseService, crawlerService, blog) // Pass databaseService, files removed
        {
        }

        // AddFileToDb and CheckIfFileExistsInDB are removed from AbstractDownloader and its derivatives.
        // The logic is now centralized in AbstractDownloader using DatabaseService.
        // If NewTumblDownloader had specific overrides for these, that logic needs to be
        // re-evaluated:
        // - Does it need to influence how FileDto is created in AbstractDownloader.DownloadBinaryPostAsync?
        // - Does it need a different way of checking file existence? (The DB check is by Link)

        // The old AddFileToDb used FileNameUrl (m<ID>) as the Link, and "" as OriginalLink.
        // The old CheckIfFileExistsInDB used FileNameUrl (m<ID>) as the Link.
        // This specific URL generation is kept in FileNameUrl.

        protected override string FileNameUrl(TumblrPost downloadItem)
        {
            return "m" + downloadItem.Id;
        }
    }
}
