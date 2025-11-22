using System.CommandLine;
using mvsep_cli;
using Spectre.Console;

// Define API key option (CLI takes priority over environment variable)
Option<string> apiKeyOption = new("--api-key", "-k")
{
    Description = "API key for MVSEP. If not provided, MVSEP_API_KEY environment variable will be used. Get your API key at https://mvsep.com/user-api.",
    
};

var defaultFormat = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) ? OutputFormat.WAV : OutputFormat.FLAC;

#region single command options

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

Option<OutputFormat> outputFormatOption = new("--output-format", "-f")
{
    Description = "Output format for separated files.",
    DefaultValueFactory = _ => defaultFormat
};

#endregion

#region full command options

Argument<FileInfo> fileArgument = new("file")
{
    Arity = ArgumentArity.ExactlyOne,
    Description = "The file to process."
};


Option<ChannelWhere> drumsSeparationChannelOption = new("--drums-on", "-d")
{
    Description = "Select the channel for drums separation.",
    DefaultValueFactory = result => ChannelWhere.Left
};

Option<bool> tambourineSeparationOption = new("--separate-tambourine", "-t")
{
    Description = "Enable tambourine separation.",
    DefaultValueFactory = result => false
};

Option<bool> doLeadBackSeparationOnVocalsOption = new("--karaoke-on-vocals", "-v")
{
    Description = "Enable lead-back vocals separation.",
    DefaultValueFactory = result => false
};

Option<ChannelWhere?> acousticGuitarSeparationChannelOption = new("--acoustic-on", "-a")
{
    Description = "Select the channel for acoustic guitar separation."
};

Option<bool> dumpOptionsAndExitOption = new("--dump-options", "-x")
{
    Description = "Dump the selected options and exit.",
    DefaultValueFactory = result => false
};


#endregion

// Create the root command for the CLI
RootCommand rootCommand = new("Audio Separator CLI")
{
    apiKeyOption
};



Command singleCommand = new("single", "Runs a single separation")
{
    fileOption,
    algorithmOption,
    apiKeyOption,
    addOpt1,
    addOpt2,
    addOpt3,
    outputFormatOption

};

// Set the action to be performed when the command is executed
singleCommand.SetAction(async parseResult =>
{
    // Retrieve values from the parsed result
    var file = parseResult.GetValue(fileOption);
    if (file == null)
    {
        AnsiConsole.MarkupLine("[red]Error: File argument is missing or invalid.[/]");
        return;
    }
    var algorithm = parseResult.GetValue(algorithmOption);
    var opt1 = parseResult.GetValue(addOpt1);
    var opt2 = parseResult.GetValue(addOpt2);
    var opt3 = parseResult.GetValue(addOpt3);
    var outputFormat = parseResult.GetValue(outputFormatOption);

    // Determine API key: CLI option takes priority over environment variable
    var cliApiKey = parseResult.GetValue(apiKeyOption);
    var envApiKey = Environment.GetEnvironmentVariable("MVSEP_API_KEY");
    var apiKey = !string.IsNullOrEmpty(cliApiKey) ? cliApiKey : envApiKey;

    if (string.IsNullOrEmpty(apiKey))
    {
        AnsiConsole.MarkupLine("[red]Error: API key not provided. Set MVSEP_API_KEY environment variable or pass --api-key <key>.[/]");
        return;
    }

    await MOTM.Execute(file, 
        algorithm, 
        apiKey, 
        opt1 != -1 ? opt1 : null, 
        opt2 != -1 ? opt2 : null, 
        opt3 != -1 ? opt3 : null, 
        outputFormat);


});

rootCommand.Add(singleCommand);

Command batchedCommand = new("full", "Runs a batched separation")
{
    fileArgument,
    drumsSeparationChannelOption,
    doLeadBackSeparationOnVocalsOption,
    acousticGuitarSeparationChannelOption,
    tambourineSeparationOption,
    dumpOptionsAndExitOption,
    apiKeyOption,
};

batchedCommand.SetAction(async parseResult =>
{
    var file = parseResult.GetValue(fileArgument);
    var drumsOnChannel = parseResult.GetValue(drumsSeparationChannelOption);
    var karaokeOnVocals = parseResult.GetValue(doLeadBackSeparationOnVocalsOption);
    var acousticOnChannel = parseResult.GetValue(acousticGuitarSeparationChannelOption);
    var separateTambourine = parseResult.GetValue(tambourineSeparationOption);
    var dumpOptionsAndExit = parseResult.GetValue(dumpOptionsAndExitOption);

    var cliApiKey = parseResult.GetValue(apiKeyOption);
    var envApiKey = Environment.GetEnvironmentVariable("MVSEP_API_KEY");
    var apiKey = !string.IsNullOrEmpty(cliApiKey) ? cliApiKey : envApiKey;


    if (dumpOptionsAndExit)
    {

        var textPath = new TextPath(file.FullName)
            .RootColor(Color.Red)
            .SeparatorColor(Color.Green)
            .StemColor(Color.Blue)
            .LeafColor(Color.Yellow);

        var table = new Table()
            .AddColumn(new TableColumn("[u]Option[/]"))
            .AddColumn(new TableColumn("[u]Value[/]"));

        table.AddRow(new Markup("File"), textPath);
        table.AddRow("Drums Separation Channel", drumsOnChannel.ToString());
        table.AddRow("Karaoke on Vocals", karaokeOnVocals ? "[green]Yes[/]" : "[red]No[/]");
        table.AddRow("Acoustic Guitar Separation Channel", acousticOnChannel.HasValue ? acousticOnChannel.ToString() : "[grey]None[/]");
        table.AddRow("Separate Tambourine", separateTambourine ? "[green]Yes[/]" : "[red]No[/]");

        AnsiConsole.Write(new Rule("[yellow]Selected Options[/]"));
        AnsiConsole.Write(table);

        return;
    }

    //check if file exists
    if (!file.Exists)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]Error: File '{file.FullName}' does not exist.[/]");
        return;
    }

    AnsiConsole.Write(new Rule( $"[yellow]Processing File: [blue]{file.FullName}[/][/]"));
    Utils.RunProcess("ffmpeg", $" -hide_banner -loglevel error -i \"{file.FullName}\" -filter_complex \"[0:a]channelsplit=channel_layout=stereo:channels=FL[left];[0:a]channelsplit=channel_layout=stereo:channels=FR[right]\" -map \"[left]\" left.flac -map \"[right]\" right.flac");

    AnsiConsole.Write(new Rule("[yellow]Starting separation on [bold]left[/] channel...[/]"));
    await MOTM.Execute("left.flac", 63, apiKey, null, null, null, defaultFormat);
    AnsiConsole.Write(new Rule("[yellow]Starting separation on [bold]right[/] channel...[/]"));
    await MOTM.Execute("right.flac", 63, apiKey, null, null, null, defaultFormat);
    var drumsFileName = "";
    switch (drumsOnChannel)
    {
        case ChannelWhere.Left:
            AnsiConsole.Write(new Rule("[yellow]Starting drums separation on [bold]left[/] channel...[/]"));
            drumsFileName = "left_Algo63_01_Drums.flac";
            break;
        case ChannelWhere.Right:
            AnsiConsole.Write(new Rule("[yellow]Starting drums separation on [bold]right[/] channel...[/]"));
            drumsFileName = "right_Algo63_01_Drums.flac";
            break;
    }
    var drumsFileNameWithoutExtension = Path.GetFileNameWithoutExtension(drumsFileName);

    Console.WriteLine(new string('-', Console.WindowWidth));
    if (separateTambourine) //--algorithm=76
    {
        AnsiConsole.Write(new Rule("[yellow]Starting [bold]tambourine[/] separation...[/]"));
        await MOTM.Execute(drumsFileName, 76, apiKey, null, null, null, defaultFormat);
        AnsiConsole.Write(new Rule("[yellow]Starting [bold]drumkit components[/] separation...[/]"));
        await MOTM.Execute($"{drumsFileNameWithoutExtension}_Algo76_01_Other.flac", 37, apiKey, 7, 1, null, defaultFormat);

    }
    else
    {
        AnsiConsole.Write(new Rule("[yellow]Starting [bold]drumkit components[/] separation...[/]"));
        await MOTM.Execute(drumsFileName, 37, apiKey, 7, 1, null, defaultFormat);
    }


    if (acousticOnChannel.HasValue)
    {
        switch (acousticOnChannel.Value)
        {
            case ChannelWhere.Left:
                AnsiConsole.Write(new Rule("[yellow]Starting acoustic separation on [bold]left[/] channel...[/]"));
                await MOTM.Execute("left_Algo63_04_Guitar.flac", 66, apiKey, null, 0, null, defaultFormat);
                break;
            case ChannelWhere.Right:
                AnsiConsole.Write(new Rule("[yellow]Starting acoustic separation on [bold]right[/] channel...[/]"));
                await MOTM.Execute("right_Algo63_04_Guitar.flac", 66, apiKey, null, 0, null, defaultFormat);
                break;
        }
    }

    if (karaokeOnVocals)
    {
        AnsiConsole.Write(new Rule("[yellow]Starting lead/backing vocals separation...[/]"));
        await MOTM.Execute("right_Algo63_03_Vocals.flac", 49, apiKey, 6, 0, null, defaultFormat);
    }

});

rootCommand.Add(batchedCommand);

// Parse the command-line arguments and invoke the root command
var parseResult = rootCommand.Parse(args);
parseResult.InvokeAsync().Wait();

public enum OutputFormat
{
    MP3,
    WAV,
    FLAC,
    M4A,
    WAV32,
    FLAC24
};


public enum ChannelWhere
{
    Left,
    Right
};
