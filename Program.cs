using System.CommandLine;
using CliFetcher.Core;
using mvsep_cli;

// Define the file argument for the CLI
Argument<FileInfo> fileOption = new("file")
{
    Description = "The audio file to separate.",
    Arity = ArgumentArity.ExactlyOne
};

// Define the algorithm option for the CLI
Option<int> algorithmOption = new("--algorithm", "-a")
{
    Description = "The separation algorithm to use.",
    Required = true
};

// Define API key option (CLI takes priority over environment variable)
Option<string> apiKeyOption = new("--api-key", "-k")
{
    Description = "API key for MVSEP. If not provided, MVSEP_API_KEY environment variable will be used. Get your API key at https://mvsep.com/user-api.",
};

// Define additional optional parameters
Option<int> addOpt1 = new("--add_opt1", "-o1")
{
    Description = "First optional parameter.",
    DefaultValueFactory = _ => -1
};
Option<int> addOpt2 = new("--add_opt2", "-o2")
{
    Description = "Second optional parameter.",
    DefaultValueFactory = _ => -1
};
Option<int> addOpt3 = new("--add_opt3", "-o3")
{
    Description = "Third optional parameter.",
    DefaultValueFactory = _ => -1
};

// Create the root command for the CLI
RootCommand rootCommand = new("Audio Separator CLI")
{
    fileOption,
    algorithmOption,
    apiKeyOption,
    addOpt1,
    addOpt2,
    addOpt3
};

// Set the action to be performed when the command is executed
rootCommand.SetAction(async parseResult =>
{
    // Retrieve values from the parsed result
    var file = parseResult.GetValue(fileOption);
    if (file == null)
    {
        Console.WriteLine("Error: File argument is missing or invalid.");
        return;
    }
    var filenameWithoutExtension = Path.GetFileNameWithoutExtension(file.Name);
    var cwd = Directory.GetCurrentDirectory();
    var algorithm = parseResult.GetValue(algorithmOption);
    var opt1 = parseResult.GetValue(addOpt1);
    var opt2 = parseResult.GetValue(addOpt2);
    var opt3 = parseResult.GetValue(addOpt3);

    // Determine API key: CLI option takes priority over environment variable
    var cliApiKey = parseResult.GetValue(apiKeyOption);
    var envApiKey = Environment.GetEnvironmentVariable("MVSEP_API_KEY");
    var apiKey = !string.IsNullOrEmpty(cliApiKey) ? cliApiKey : envApiKey;

    if (string.IsNullOrEmpty(apiKey))
    {
        Console.WriteLine("Error: API key not provided. Set MVSEP_API_KEY environment variable or pass --api-key <key>.");
        return;
    }

    // Indicate progress using ConEmu
    Utils.ConEmuProgress(0, Utils.ConEmuProgressStyle.Indeterminate);

    // Prepare the temporary file path for processing
    var tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".flac");
    Console.WriteLine($"Temporary file will be created at: {tempFilePath}");

    // Convert the input file to FLAC format using ffmpeg
    Utils.RunProcess("ffmpeg", $" -hide_banner -loglevel error -i \"{file.FullName}\" -compression_level 12 \"{tempFilePath}\"");

    Console.WriteLine("Uploading...");

    // Initialize the uploader and prepare parameters for the API request
    using var uploader = new Downloader();
    var paramDict = new Dictionary<string, string>
    {
        { "sep_type", algorithm.ToString() },
        { "api_token", apiKey },
        { "output_format", "2" }
    };

    // Add optional parameters if provided
    if (opt1 != -1)
        paramDict.Add("add_opt1", opt1.ToString());

    if (opt2 != -1)
        paramDict.Add("add_opt2", opt2.ToString());

    if (opt3 != -1)
        paramDict.Add("add_opt3", opt3.ToString());

    // Display upload parameters (excluding sensitive data)
    var safeOutputParamDict = paramDict.Where(e => e.Key != "api_token");
    Console.WriteLine("Upload parameters:");
    foreach (var (key, value) in safeOutputParamDict)
    {
        Console.WriteLine($"  {key}: {value}");
    }

    // Upload the file and get the result
    var uploadResult = await uploader.UploadFileAsync("https://mvsep.com/api/separation/create", tempFilePath, "audiofile", paramDict, Downloader.ConsoleProgressSimple("Upload Progress: "));

    var uploadResultObject = MvsepUploadSuccess.FromJson(uploadResult);
    Console.WriteLine($"Upload successful. Job hash: {uploadResultObject.Data.Hash}");

    // Poll the API for the separation result
    var url = uploadResultObject.Data.Link;
    var statusResult = await Utils.GetStringFromUrlAsync(url);
    var statusResultObject = MvsepStatus.FromJson(statusResult);
    while (statusResultObject.Status != "done")
    {
        Utils.ConEmuProgress(0, Utils.ConEmuProgressStyle.Indeterminate);
        var statusMsg = $"Current status: {statusResultObject.Status}. Checking again in 5 seconds...";
        var timeMsg = $"[{DateTime.Now:HH:mm:ss}]".PadLeft(Console.WindowWidth - statusMsg.Length - 1);
        Console.Write($"\r{statusMsg}{timeMsg}");
        await Task.Delay(5000);
        statusResult = await Utils.GetStringFromUrlAsync(url);
        statusResultObject = MvsepStatus.FromJson(statusResult);
    }

    // Clear the progress indicator
    Utils.ConEmuProgress(0, Utils.ConEmuProgressStyle.Clear);

    Console.WriteLine("Separation done. Downloading result...");

    // Download the separated audio files
    foreach (var (stemFile, index) in statusResultObject.Data.Files.Select((value, i) => (value, i)))
    {
        var filename = $"{filenameWithoutExtension}_Algo{algorithm}_{index:D2}_{stemFile.Type}.flac";
        var destinationPath = Path.Combine(cwd, filename);
        await uploader.DownloadFileAsync(stemFile.Url.ToString(), destinationPath, Downloader.ConsoleProgressSimple($"Downloading {filename}: "));
    }

});

// Parse the command-line arguments and invoke the root command
var parseResult = rootCommand.Parse(args);
parseResult.InvokeAsync().Wait();