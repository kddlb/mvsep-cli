using System.Diagnostics;
using System.Net.NetworkInformation;

public static class Utils
{
    public static int RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        process.WaitForExit();
        return process.ExitCode;
    }

    public static async Task<string> GetStringFromUrlAsync(Uri url)
    {
        using var httpClient = new HttpClient();
        return await httpClient.GetStringAsync(url);
    }

    public enum ConEmuProgressStyle
    {
        Clear, Default, Error, Indeterminate, Warning
    }

    public static void ConEmuProgress(int progress, ConEmuProgressStyle style = ConEmuProgressStyle.Default)
    {
        string styleCode = style switch
        {
            ConEmuProgressStyle.Clear => "0",
            ConEmuProgressStyle.Default => "1",
            ConEmuProgressStyle.Error => "2",
            ConEmuProgressStyle.Indeterminate => "3",
            ConEmuProgressStyle.Warning => "4",
            _ => "1"
        };

        Console.Write($"\x1b]9;{styleCode};{progress}\x07");
    }

}