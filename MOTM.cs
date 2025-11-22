using CliFetcher.Core;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace mvsep_cli
{
    // the meat of the matter. the main code.
    internal class MOTM
    {

        public static Task Execute(string file, int algorithm, string apiKey, int? opt1, int? opt2, int? opt3,
            OutputFormat outputFormat)
        {
            if (string.IsNullOrWhiteSpace(file))
                throw new ArgumentException("file path is required", nameof(file));

            var fullPath = Path.GetFullPath(file);
            var fileInfo = new FileInfo(fullPath);

            return !fileInfo.Exists ? throw new FileNotFoundException("Input file not found", fullPath) :
                // Return the Task directly to avoid an extra async/await state machine
                Execute(fileInfo, algorithm, apiKey, opt1, opt2, opt3, outputFormat);
        }

        public static async Task Execute(FileInfo file, int algorithm, string apiKey, int? opt1, int? opt2, int? opt3,
            OutputFormat outputFormat)
        {
            var filenameWithoutExtension = Path.GetFileNameWithoutExtension(file.Name);
            var cwd = Directory.GetCurrentDirectory();

            // Prepare the temporary file path for processing
            var tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".flac");
            //AnsiConsole.MarkupLine($"Temporary file will be created at: [blue]{tempFilePath}[/]");

            // Convert the input file to FLAC format using ffmpeg
            Utils.RunProcess("ffmpeg",
                $" -hide_banner -loglevel error -i \"{file.FullName}\" -compression_level 12 \"{tempFilePath}\"");

            AnsiConsole.MarkupLine("Uploading...");

            // Initialize the uploader and prepare parameters for the API request
            using var uploader = new Downloader();
            var paramDict = new Dictionary<string, string>
            {
                { "sep_type", algorithm.ToString() },
                { "api_token", apiKey },
                { "output_format", ((int)outputFormat).ToString() }
            };

            if (opt1.HasValue) paramDict["opt1"] = opt1.Value.ToString();
            if (opt2.HasValue) paramDict["opt2"] = opt2.Value.ToString();
            if (opt3.HasValue) paramDict["opt3"] = opt3.Value.ToString();

            // Display upload parameters (excluding sensitive data)
            var safeOutputParamDict = paramDict.Where(e => e.Key != "api_token");
            AnsiConsole.MarkupLine("Upload parameters:");
            foreach (var (key, value) in safeOutputParamDict)
            {
                AnsiConsole.MarkupLineInterpolated($"  [green]{key}[/]: [yellow]{value}[/]");
            }

            // Upload the file and get the result using Spectre.Console progress
            var uploadResult = string.Empty;
            var tempFileInfo = new FileInfo(tempFilePath);
            var fileSize = tempFileInfo.Exists ? tempFileInfo.Length : 0L;

            await AnsiConsole.Progress()
                .AutoClear(true)
                .Columns(
                [
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn(),
                ])
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("Uploading file", autoStart: true);
                    if (fileSize > 0) task.MaxValue = fileSize;

                    var progress = new Progress<DownloadProgress>(p =>
                    {
                        if (p.TotalBytes.HasValue)
                            task.MaxValue = p.TotalBytes.Value;
                        task.Value = p.BytesReceived;
                    });

                    uploadResult = await uploader.UploadFileAsync("https://mvsep.com/api/separation/create",
                        tempFilePath, "audiofile", paramDict, progress);

                    task.Value = task.MaxValue;
                });

            var uploadResultObject = MvsepUploadSuccess.FromJson(uploadResult);
            AnsiConsole.MarkupLineInterpolated($"Upload successful. Job hash: [green]{uploadResultObject.Data.Hash}[/]");

            // Poll the API for the separation result
            var url = uploadResultObject.Data.Link;
            var statusResult = await Utils.GetStringFromUrlAsync(url);
            var statusResultObject = MvsepStatus.FromJson(statusResult);

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Waiting for separation to complete...", async ctx =>
                {
                    while (statusResultObject.Status != "done")
                    {
                        ctx.Status($"Current status: {statusResultObject.Status}");
                        await Task.Delay(2500);
                        statusResult = await Utils.GetStringFromUrlAsync(url);
                        statusResultObject = MvsepStatus.FromJson(statusResult);
                    }
                });

            AnsiConsole.MarkupLine("[green]Separation done. Downloading result...[/]");

            // Download the separated audio files with Spectre progress
            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn(),
                })
                .StartAsync(async ctx =>
                {
                    var tasks = new List<ProgressTask>();
                    foreach (var (stemFile, index) in statusResultObject.Data.Files.Select((value, i) => (value, i)))
                    {
                        var filename = $"{filenameWithoutExtension}_Algo{algorithm}_{index:D2}_{stemFile.Type}.{Utils.ExtensionFromOutputFormat(outputFormat)}";
                        var destinationPath = Path.Combine(cwd, filename);
                        var task = ctx.AddTask($"Downloading {filename}", autoStart: true);
                        // subscribe progress to update spectre task
                        var progress = new Progress<DownloadProgress>(p =>
                        {
                            if (p.TotalBytes.HasValue)
                                task.MaxValue = p.TotalBytes.Value;
                            task.Value = p.BytesReceived;
                        });

                        await uploader.DownloadFileAsync(stemFile.Url.ToString(), destinationPath, progress);
                        task.Value = task.MaxValue;
                    }
                });
        }
    }
}