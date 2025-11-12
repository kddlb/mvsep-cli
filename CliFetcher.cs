// Example usage:
//
// // Download a file with console progress
// using var downloader = new CliFetcher.Core.Downloader();
// await downloader.DownloadFileAsync(
//     "https://example.com/file.zip",
//     "file.zip",
//     CliFetcher.Core.Downloader.ConsoleProgress("Downloading: "),
//     CancellationToken.None
// );
//
// // Upload a file with console progress and extra form fields
// using var downloader = new CliFetcher.Core.Downloader();
// var response = await downloader.UploadFileAsync(
//     "https://example.com/upload",
//     "file.zip",
//     formFileField: "upload",
//     formFields: new Dictionary<string, string> { { "token", "abc123" } },
//     progress: CliFetcher.Core.Downloader.ConsoleProgress("Uploading: "),
//     cancellationToken: CancellationToken.None
// );
// Console.WriteLine("Server response: " + response);

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace CliFetcher.Core;

// Represents options for configuring download behavior
public sealed class DownloadOptions
{
    /// <summary>Optional custom User-Agent product name/version (e.g., "CliFetcher/1.0").</summary>
    public string? UserAgent { get; init; } = "CliFetcher.Core/1.0";
    /// <summary>Copy buffer size in bytes. Default 64 KiB.</summary>
    public int BufferSize { get; init; } = 1 << 16;
    /// <summary>When true, will attempt to resume if file exists and server supports Range.</summary>
    public bool Resume { get; init; } = true;
    /// <summary>When true and Resume=false, will overwrite existing file; otherwise throws.</summary>
    public bool Overwrite { get; init; } = true;
    /// <summary>Optional timeout for the HttpClient.</summary>
    public TimeSpan? HttpTimeout { get; init; } = TimeSpan.FromMinutes(30);
}

// Represents progress information during a download
public readonly record struct DownloadProgress(
    long BytesReceived,
    long? TotalBytes,                 // null when unknown
    double InstantBytesPerSecond,     // raw instantaneous rate
    double SmoothedBytesPerSecond,    // moving average
    TimeSpan Elapsed,
    TimeSpan? Eta                     // null when unknown
)
{
    public double? Percent => TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value : null;
}

// Handles downloading and uploading files with progress reporting
public sealed class Downloader : IDisposable
{
    private readonly HttpClient _http;
    private bool _disposed;

    // Constructor to initialize the downloader with optional custom settings
    public Downloader(HttpMessageHandler? handler = null, DownloadOptions? options = null)
    {
        Options = options ?? new DownloadOptions();
        _http = new HttpClient(handler ?? new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        });
        if (Options.HttpTimeout is { } t) _http.Timeout = t;
    }

    public DownloadOptions Options { get; }

    /// <summary>
    /// Downloads a URL to a file with progress and cancellation. Resumes when enabled and supported.
    /// </summary>
    public async Task DownloadFileAsync(
        string url,
        string outputPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        // Compute initial resume position
        long existing = 0;
        if (File.Exists(outputPath))
        {
            if (Options.Resume)
            {
                existing = new FileInfo(outputPath).Length;
            }
            else if (Options.Overwrite)
            {
                File.Delete(outputPath);
            }
            else
            {
                throw new IOException($"File exists and Overwrite=false: {outputPath}");
            }
        }

        // Create the HTTP request
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(Options.UserAgent))
        {
            // Add User-Agent header if specified
            var ua = Options.UserAgent!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => s.Split('/', 2))
                                       .Where(p => p.Length == 2);
            foreach (var p in ua)
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue(p[0], p[1]));
        }

        // If resuming, add Range header
        if (existing > 0)
            req.Headers.Range = new RangeHeaderValue(existing, null);

        // Send the HTTP request
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (existing > 0 && resp.StatusCode == HttpStatusCode.OK)
        {
            // Server ignored Range; start fresh
            existing = 0;
            // If file exists, delete it to avoid mixing partial with fresh
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }

        if (resp.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
            resp.EnsureSuccessStatusCode();

        var totalBytes = existing + (resp.Content.Headers.ContentLength ?? 0);
        // If no Content-Length or we're starting from zero, this may be 0/unknown
        var knownTotal = resp.StatusCode == HttpStatusCode.PartialContent || resp.Content.Headers.ContentLength.HasValue
            ? (resp.Content.Headers.ContentLength.HasValue ? existing + resp.Content.Headers.ContentLength.Value : null)
            : resp.Content.Headers.ContentLength;

        await using var src = await resp.Content.ReadAsStreamAsync(cancellationToken);

        // Ensure directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");

        // Open target stream (append if resuming)
        await using var dst = new FileStream(
            outputPath,
            existing > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1 << 20,
            useAsync: true);

        var buffer = new byte[Math.Max(4096, Options.BufferSize)];
        var received = existing;
        var sw = Stopwatch.StartNew();

        // Simple moving average over last N ticks
        const int N = 20;
        var history = new Queue<(double t, long bytes)>(N);

        // Initial progress report
        Report();

        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            Report();
        }

        sw.Stop();
        Report(); // Final progress report

        void Report()
        {
            var t = sw.Elapsed.TotalSeconds;
            if (t <= 0) t = 1e-6;

            var inst = readRateInstant();
            var smooth = readRateSmoothed(inst);
            TimeSpan? eta = null;
            if (knownTotal is long kt && smooth > 1e-6)
            {
                var remaining = kt - received;
                if (remaining < 0) remaining = 0;
                eta = TimeSpan.FromSeconds(remaining / smooth);
            }

            progress?.Report(new DownloadProgress(
                BytesReceived: received,
                TotalBytes: knownTotal,
                InstantBytesPerSecond: inst,
                SmoothedBytesPerSecond: smooth,
                Elapsed: sw.Elapsed,
                Eta: eta
            ));

            double readRateInstant()
            {
                // Use last delta if available, else average since start
                if (history.Count > 0)
                {
                    var (tPrev, bytesPrev) = history.Last();
                    var dt = t - tPrev;
                    var db = received - bytesPrev;
                    if (dt <= 1e-6) return 0;
                    return db / dt;
                }
                return received / t;
            }

            double readRateSmoothed(double current)
            {
                if (history.Count == N) history.Dequeue();
                history.Enqueue((t, received));
                // Simple average of per-sample rates
                if (history.Count < 2) return current;

                double sum = 0;
                var count = 0;
                var items = history.ToArray();
                for (var i = 1; i < items.Length; i++)
                {
                    var dt = items[i].t - items[i - 1].t;
                    var db = items[i].bytes - items[i - 1].bytes;
                    if (dt > 1e-6)
                    {
                        sum += db / dt;
                        count++;
                    }
                }
                return count > 0 ? sum / count : current;
            }
        }
    }

    /// <summary>
    /// Uploads a file to a URL using multipart/form-data, with optional form fields, progress, and cancellation.
    /// Returns the response body as a string.
    /// </summary>
    public async Task<string> UploadFileAsync(
        string url,
        string filePath,
        string formFileField = "file",
        IDictionary<string, string>? formFields = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File to upload not found", filePath);

        using var content = new MultipartFormDataContent();
        // Add additional form fields
        if (formFields != null)
        {
            foreach (var kv in formFields)
                content.Add(new StringContent(kv.Value), kv.Key);
        }

        var fileInfo = new FileInfo(filePath);
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16, useAsync: true);
        var streamContent = new ProgressStreamContent(fileStream, Options.BufferSize, fileInfo.Length, progress, cancellationToken);
        content.Add(streamContent, formFileField, fileInfo.Name);

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        if (!string.IsNullOrWhiteSpace(Options.UserAgent))
        {
            var ua = Options.UserAgent!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => s.Split('/', 2))
                                       .Where(p => p.Length == 2);
            foreach (var p in ua)
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue(p[0], p[1]));
        }

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(cancellationToken);
    }

    // Helper class for progress reporting during upload
    private sealed class ProgressStreamContent : HttpContent
    {
        private readonly Stream _stream;
        private readonly int _bufferSize;
        private readonly long _totalBytes;
        private readonly IProgress<DownloadProgress>? _progress;
        private readonly CancellationToken _cancellationToken;

        public ProgressStreamContent(Stream stream, int bufferSize, long totalBytes, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            _stream = stream;
            _bufferSize = bufferSize;
            _totalBytes = totalBytes;
            _progress = progress;
            _cancellationToken = cancellationToken;
        }

        protected override async Task SerializeToStreamAsync(Stream target, TransportContext? context)
        {
            var buffer = new byte[Math.Max(4096, _bufferSize)];
            long uploaded = 0;
            var sw = Stopwatch.StartNew();
            const int N = 20;
            var history = new Queue<(double t, long bytes)>(N);

            void Report()
            {
                var t = sw.Elapsed.TotalSeconds;
                if (t <= 0) t = 1e-6;
                var inst = readRateInstant();
                var smooth = readRateSmoothed(inst);
                TimeSpan? eta = null;
                if (_totalBytes > 0 && smooth > 1e-6)
                {
                    var remaining = _totalBytes - uploaded;
                    if (remaining < 0) remaining = 0;
                    eta = TimeSpan.FromSeconds(remaining / smooth);
                }
                _progress?.Report(new DownloadProgress(
                    BytesReceived: uploaded,
                    TotalBytes: _totalBytes,
                    InstantBytesPerSecond: inst,
                    SmoothedBytesPerSecond: smooth,
                    Elapsed: sw.Elapsed,
                    Eta: eta
                ));

                double readRateInstant()
                {
                    if (history.Count > 0)
                    {
                        var (tPrev, bytesPrev) = history.Last();
                        var dt = t - tPrev;
                        var db = uploaded - bytesPrev;
                        if (dt <= 1e-6) return 0;
                        return db / dt;
                    }
                    return uploaded / t;
                }

                double readRateSmoothed(double current)
                {
                    if (history.Count == N) history.Dequeue();
                    history.Enqueue((t, uploaded));
                    if (history.Count < 2) return current;
                    double sum = 0;
                    var count = 0;
                    var items = history.ToArray();
                    for (var i = 1; i < items.Length; i++)
                    {
                        var dt = items[i].t - items[i - 1].t;
                        var db = items[i].bytes - items[i - 1].bytes;
                        if (dt > 1e-6)
                        {
                            sum += db / dt;
                            count++;
                        }
                    }
                    return count > 0 ? sum / count : current;
                }
            }

            Report(); // initial

            int read;
            while ((read = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), _cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), _cancellationToken);
                uploaded += read;
                Report();
            }
            sw.Stop();
            Report(); // final
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _totalBytes;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _stream.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Returns an IProgress that writes progress to the system console.
    /// </summary>
    public static IProgress<DownloadProgress> ConsoleProgress(string? prefix = null)
    {
        object sync = new();
        DownloadProgress? last = null;
        var finished = false;
        return new Progress<DownloadProgress>(p =>
        {
            lock (sync)
            {
                var percent = p.Percent is double d ? d * 100 : 0;
                var rate = FormatRate(p.SmoothedBytesPerSecond);
                var eta = p.Eta is TimeSpan t ? $"ETA {t:hh\\:mm\\:ss}" : "";
                var elapsed = $"{p.Elapsed:hh\\:mm\\:ss}";
                var received = FormatSize(p.BytesReceived);
                var total = p.TotalBytes is long tb ? FormatSize(tb) : "?";
                var progressMsg = $"{percent,6:F2}% {received}/{total} {rate,-12} {elapsed} {eta}".TrimEnd();
                var prefixMsg = prefix ?? string.Empty;
                var width = 80;
                try { width = Console.WindowWidth; } catch { try { width = Console.BufferWidth; } catch { width = 80; } }
                if (width < 1) width = 80;
                var available = width - prefixMsg.Length;
                if (available < 10) available = 10; // fallback
                if (progressMsg.Length > available)
                    progressMsg = progressMsg.Substring(0, available);
                else
                    progressMsg = progressMsg.PadLeft(available);
                var msg = $"{prefixMsg}{progressMsg}";
                Console.Write($"\r{msg}");

                // OSC 9;4 support for progress
                Console.Write($"\u001b]9;4;{(int)percent}\u0007");

                last = p;
                if (!finished && p.TotalBytes.HasValue && p.BytesReceived >= p.TotalBytes.Value)
                {
                    Console.WriteLine();
                    Console.Write("\u001b]9;4;-1\u0007"); // Reset progress
                    finished = true;
                }
            }
        });

        static string FormatRate(double bytesPerSec)
        {
            if (bytesPerSec < 1) return "";
            return $"{FormatSize((long)bytesPerSec)}/s";
        }
        static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            var i = 0;
            while (v >= 1024 && i < units.Length - 1)
            {
                v /= 1024;
                i++;
            }
            return $"{v:0.##} {units[i]}";
        }
    }

    /// <summary>
    /// Returns an IProgress that writes only percent and bytes to the system console (no ETA or speed).
    /// </summary>
    public static IProgress<DownloadProgress> ConsoleProgressSimple(string? prefix = null)
    {
        object sync = new();
        var finished = false;
        return new Progress<DownloadProgress>(p =>
        {
            lock (sync)
            {
                var percent = p.Percent is double d ? d * 100 : 0;
                var received = FormatSize(p.BytesReceived);
                var total = p.TotalBytes is long tb ? FormatSize(tb) : "?";
                var progressMsg = $"{percent,6:F2}% {received}/{total}";
                var prefixMsg = prefix ?? string.Empty;
                var width = 40;
                try { width = Console.WindowWidth; } catch { try { width = Console.BufferWidth; } catch { width = 40; } }
                if (width < 1) width = 40;
                var available = width - prefixMsg.Length;
                if (available < 10) available = 10;
                if (progressMsg.Length > available)
                    progressMsg = progressMsg.Substring(0, available);
                else
                    progressMsg = progressMsg.PadLeft(available);
                var msg = $"{prefixMsg}{progressMsg}";
                Console.Write($"\r{msg}");

                // OSC 9;4 support for progress
                Utils.ConEmuProgress((int)percent);

                if (!finished && p.TotalBytes.HasValue && p.BytesReceived >= p.TotalBytes.Value)
                {
                    Console.WriteLine();
                    Utils.ConEmuProgress(0, Utils.ConEmuProgressStyle.Clear);
                    finished = true;
                }
            }
        });

        static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            var i = 0;
            while (v >= 1024 && i < units.Length - 1)
            {
                v /= 1024;
                i++;
            }
            return $"{v:0.##} {units[i]}";
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
