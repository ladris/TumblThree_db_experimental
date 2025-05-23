using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TumblThree.Applications.Extensions;
using TumblThree.Applications.Properties;
using TumblThree.Applications.Services;
using TumblThree.Domain.Database; // Added for DatabaseService

namespace TumblThree.Applications.Downloader
{
    public class FileDownloader
    {
        public static readonly int BufferSize = 512 * 4096;
        private readonly AppSettings settings;
        private readonly CancellationToken ct;
        private readonly IWebRequestFactory webRequestFactory;
        private readonly ISharedCookieService cookieService;

        public FileDownloader(AppSettings settings, CancellationToken ct, IWebRequestFactory webRequestFactory, ISharedCookieService cookieService)
        {
            this.settings = settings;
            this.ct = ct;
            this.webRequestFactory = webRequestFactory;
            this.cookieService = cookieService;
        }

        public event EventHandler Completed;

        public event EventHandler<DownloadProgressChangedEventArgs> ProgressChanged;

        public async Task<(bool result, string destinationPath)> DownloadFileWithResumeAsync(string url, string destinationPath, int downloadId, int blogId, DatabaseService databaseService)
        {
            long totalBytesReceived = 0;
            var attemptCount = 0;
            var bufferSize = settings.BufferSize * 4096;

            if (File.Exists(destinationPath))
            {
                var fileInfo = new FileInfo(destinationPath);
                totalBytesReceived = fileInfo.Length;
                var result = await CheckDownloadSizeAsync(url, destinationPath).TimeoutAfter(settings.TimeOut);
                if (totalBytesReceived >= result.contentLength) return (true, result.destinationPath);
                if (destinationPath != result.destinationPath)
                {
                    File.Delete(destinationPath);
                    destinationPath = result.destinationPath;
                    fileInfo = new FileInfo(destinationPath);
                    totalBytesReceived = fileInfo.Length;
                }
            }

            if (ct.IsCancellationRequested)
            {
                databaseService.UpdateDownloadStatus(downloadId, "paused", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                return (false, destinationPath);
            }

            var fileMode = totalBytesReceived > 0 ? FileMode.Append : FileMode.Create;

            var fileStream = new FileStream(destinationPath, fileMode, FileAccess.Write, FileShare.Read, bufferSize, true);
            try
            {
                while (true)
                {
                    attemptCount += 1;

                    if (attemptCount > settings.MaxNumberOfRetries) return (false, destinationPath);

                    var requestRegistration = new CancellationTokenRegistration();

                    try
                    {
                        var request = webRequestFactory.CreateGetRequest(url);
                        requestRegistration = ct.Register(() => request.Abort());
                        request.AddRange(totalBytesReceived);

                        long totalBytesToReceive = 0;
                        bool isChunked = false;
                        using (var response = await request.GetResponseAsync().TimeoutAfter(settings.TimeOut))
                        {
                            if (url.Contains("tumblr.com") && (url.Contains(".png") || url.Contains(".pnj"))
                                && Path.GetExtension(destinationPath).ToLower() == ".png" && (response.Headers["Content-Type"]?.Contains("jpeg") ?? false))
                            {
                                fileStream.Dispose();
                                File.Delete(destinationPath);
                                destinationPath = Path.Combine(Path.GetDirectoryName(destinationPath), Path.GetFileNameWithoutExtension(destinationPath) + ".jpg");
                                fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize, true);
                            }

                            isChunked = response.Headers.ToString().Contains("chunked");
                            totalBytesToReceive = totalBytesReceived + (response.ContentLength == -1 ? 0 : response.ContentLength);

                            using (var responseStream = response.GetResponseStream())
                            using (var throttledStream = GetStreamForDownload(responseStream))
                            {
                                var buffer = new byte[4096];
                                var bytesRead = 0;
                                //Stopwatch sw = Stopwatch.StartNew();

                                while ((bytesRead = await throttledStream
                                           .ReadAsync(buffer, 0, buffer.Length, ct)
                                           .TimeoutAfter(settings.TimeOut)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                                    totalBytesReceived += bytesRead;

                                    //float currentSpeed = totalBytesReceived / (float)sw.Elapsed.TotalSeconds;
                                    //OnProgressChanged(new DownloadProgressChangedEventArgs(totalBytesReceived,
                                    //    totalBytesToReceive, (long)currentSpeed));
                                    
                                    // DB Progress Update
                                    databaseService.UpdateDownloadProgressAndStatus(downloadId, totalBytesReceived, totalBytesToReceive, "downloading", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                                }
                            }
                            isChunked = isChunked && response.Headers.ToString().Contains("Content-Range");
                        }

                        if (!isChunked && totalBytesReceived >= totalBytesToReceive)
                        {
                            databaseService.UpdateDownloadStatus(downloadId, "completed", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                            break;
                        }
                        if (isChunked && totalBytesToReceive == 0 && totalBytesReceived > 0) // Chunked and received some data, assume completed for now
                        {
                             databaseService.UpdateDownloadStatus(downloadId, "completed", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                             break;
                        }
                        if (isChunked) attemptCount = 0; // Reset attempts for chunked successful reads
                    }
                    catch (IOException ioException)
                    {
                        long win32ErrorCode = ioException.HResult & 0xFFFF;
                        if (win32ErrorCode == 0x21 || win32ErrorCode == 0x20) // file in use
                        {
                            databaseService.UpdateDownloadStatus(downloadId, "error", DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // Or a specific status like "file_in_use"
                            databaseService.IncrementDownloadRetry(downloadId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                            return (false, destinationPath);
                        }
                        // For other IOExceptions (like unexpected EOF), retry logic below handles it.
                        // Consider logging this specific type of IOException if it's frequent.
                        databaseService.UpdateDownloadStatus(downloadId, "error_io", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                        databaseService.IncrementDownloadRetry(downloadId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    }
                    catch (WebException webException)
                    {
                        databaseService.UpdateDownloadStatus(downloadId, "error_web", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                        databaseService.IncrementDownloadRetry(downloadId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                        if (webException.Status == WebExceptionStatus.ConnectionClosed)
                        {
                            // retry is handled by the loop
                        }
                        else
                        {
                            throw; // Re-throw if it's not a connection closed error that we want to retry within the loop
                        }
                    }
                    catch (TimeoutException) // Specifically from TimeoutAfter extension
                    {
                        databaseService.UpdateDownloadStatus(downloadId, "error_timeout", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                        databaseService.IncrementDownloadRetry(downloadId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                        // Retry is handled by the loop
                    }
                    catch (Exception ex) // Catch-all for other unexpected errors during download attempt
                    {
                        databaseService.UpdateDownloadStatus(downloadId, "error_unknown", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                        databaseService.IncrementDownloadRetry(downloadId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                        Logger.Error("FileDownloader:DownloadFileWithResumeAsync: Unknown error during download loop: {0}", ex);
                        // Depending on policy, might re-throw or let retry logic handle.
                    }
                    finally
                    {
                        requestRegistration.Dispose();
                    }
                }
                return (true, destinationPath); // Success
            }
            finally
            {
                fileStream?.Dispose();
            }
        }

        private async Task<(long contentLength, string destinationPath)> CheckDownloadSizeAsync(string url, string destinationPath) // No downloadId needed here, just checking size
        {
            var requestRegistration = new CancellationTokenRegistration();
            try
            {
                var request = webRequestFactory.CreateGetRequest(url);
                requestRegistration = ct.Register(() => request.Abort());

                using (var response = await request.GetResponseAsync())
                {
                    if (url.Contains("tumblr.com") && (url.Contains(".png") || url.Contains(".pnj"))
                        && Path.GetExtension(destinationPath).ToLower() == ".png" && (response.Headers["Content-Type"]?.Contains("jpeg") ?? false))
                    {
                        destinationPath = Path.Combine(Path.GetDirectoryName(destinationPath), Path.GetFileNameWithoutExtension(destinationPath) + ".jpg");
                    }
                    return (response.ContentLength, destinationPath);
                }
            }
            finally
            {
                requestRegistration.Dispose();
            }
        }

        public async Task<Stream> ReadFromUrlIntoStreamAsync(string url)
        {
            var request = webRequestFactory.CreateGetRequest(url);

            using (var response = await request.GetResponseAsync() as HttpWebResponse)
            {
                if (response?.StatusCode == HttpStatusCode.OK)
                {
                    var responseStream = response.GetResponseStream();
                    return GetStreamForDownload(responseStream);
                }

                return null;
            }
        }

        public async Task<string> ReadFromUrlAsStringAsync(string url)
        {
            var request = webRequestFactory.CreateGetRequest(url);

            using (var response = await request.GetResponseAsync() as HttpWebResponse)
            {
                if (response?.StatusCode == HttpStatusCode.OK)
                {
                    using (var streamReader = new StreamReader(response.GetResponseStream(), true))
                    {
                        return await streamReader.ReadToEndAsync();
                    }
                }

                return null;
            }
        }

        private Stream GetStreamForDownload(Stream stream)
        {
            return settings.Bandwidth == 0 ? stream : new ThrottledStream(stream, settings.Bandwidth / settings.ConcurrentConnections * 1024);
        }

        public static async Task<bool> SaveStreamToDiskAsync(Stream input, string destinationFileName, CancellationToken ct)
        {
            using (var stream = new FileStream(destinationFileName, FileMode.Create, FileAccess.Write, FileShare.Read, BufferSize, true))
            {
                var buf = new byte[4096];
                int bytesRead;
                while ((bytesRead = await input.ReadAsync(buf, 0, buf.Length, ct)) > 0) await stream.WriteAsync(buf, 0, bytesRead, ct);
            }

            return true;
        }

        protected void OnProgressChanged(DownloadProgressChangedEventArgs e)
        {
            ProgressChanged?.Invoke(this, e);
        }

        protected void OnCompleted(EventArgs e)
        {
            Completed?.Invoke(this, e);
        }
    }

    public class DownloadProgressChangedEventArgs : EventArgs
    {
        public DownloadProgressChangedEventArgs(long totalReceived, long fileSize, long currentSpeed)
        {
            BytesReceived = totalReceived;
            TotalBytesToReceive = fileSize;
            CurrentSpeed = currentSpeed;
        }

        public long BytesReceived { get; }

        public long TotalBytesToReceive { get; }

        public float ProgressPercentage => BytesReceived / (float)TotalBytesToReceive * 100;

        public float CurrentSpeed { get; } // in bytes

        public TimeSpan TimeLeft
        {
            get
            {
                var bytesRemainingtoBeReceived = TotalBytesToReceive - BytesReceived;
                return TimeSpan.FromSeconds(bytesRemainingtoBeReceived / CurrentSpeed);
            }
        }
    }
}
