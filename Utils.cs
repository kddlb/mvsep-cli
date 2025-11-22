using System.Diagnostics;
using System.Net.NetworkInformation;

public static class Utils
{
    // Runs a process with the specified file name and arguments, and waits for it to exit.
    public static int RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = false, // Do not redirect standard output.
            RedirectStandardError = false, // Do not redirect standard error.
            UseShellExecute = false, // Use the operating system shell to start the process.
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start the process.");
        }

        process.WaitForExit(); // Wait for the process to exit.
        return process.ExitCode; // Return the exit code of the process.
    }

    // Fetches the content of a URL as a string asynchronously.
    public static async Task<string> GetStringFromUrlAsync(Uri url)
    {
        using var httpClient = new HttpClient();
        return await httpClient.GetStringAsync(url); // Perform an HTTP GET request.
    }

    public static string ExtensionFromOutputFormat(OutputFormat outputFormat)
    {
        return outputFormat switch
        {
            OutputFormat.FLAC => "flac",
            OutputFormat.MP3 => "mp3",
            OutputFormat.M4A => "m4a",
            OutputFormat.WAV => "wav",
            OutputFormat.WAV32 => "wav",
            OutputFormat.FLAC24 => "flac",
            _ => throw new ArgumentOutOfRangeException(nameof(outputFormat), "Unsupported output format")
        };
    }
    

    // Enum representing different progress styles for ConEmu terminal.
    public enum ConEmuProgressStyle
    {
        Clear, // Clears the progress indicator.
        Default, // Default progress style.
        Error, // Indicates an error state.
        Indeterminate, // Shows an indeterminate progress state.
        Warning // Indicates a warning state.
    }

    // Updates the progress indicator in the ConEmu terminal.
    public static void ConEmuProgress(int progress, ConEmuProgressStyle style = ConEmuProgressStyle.Default)
    {
        var styleCode = style switch
        {
            ConEmuProgressStyle.Clear => "0", // Clear progress.
            ConEmuProgressStyle.Default => "1", // Default progress style.
            ConEmuProgressStyle.Error => "2", // Error progress style.
            ConEmuProgressStyle.Indeterminate => "3", // Indeterminate progress style.
            ConEmuProgressStyle.Warning => "4", // Warning progress style.
            _ => "1" // Default to "1" if style is unrecognized.
        };

        // Write the progress update to the terminal using ANSI escape codes.
        Console.Write($"\e]9;{styleCode};{progress}\a");
    }
}