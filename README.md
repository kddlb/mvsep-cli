# mvsep-cli

A .NET 9 command‑line tool to separate audio into stems using the MVSEP online API. It transcodes your input to FLAC, uploads it, polls until processing completes, and downloads each resulting stem with progress feedback.

## Features
- Simple CLI built with `System.CommandLine`.
- Transcodes input to temporary FLAC via `ffmpeg`.
- Uploads to MVSEP API with your API token.
- Polls status and downloads all stems on completion.
- Console progress with ETA and speed; emits terminal progress (OSC 9) where supported.
- Robust streaming upload/download with resume support for downloads.
- Ready for native AOT publishing.

## Prerequisites
- MVSEP account with paid credits (MVSEP’s API consumes credits).
- `ffmpeg` available on your `PATH` (used to transcode to FLAC).
- MVSEP API key exported as an environment variable `MVSEP_API_KEY`.
- .NET 9 SDK (for building from source).

## Getting Started
1) Create an MVSEP account and purchase credits.
2) Obtain your API token from your MVSEP account settings.
3) Set `MVSEP_API_KEY` in your shell:

Windows (PowerShell, current session):
```powershell
$env:MVSEP_API_KEY = "YOUR_TOKEN_HERE"
```

Windows (PowerShell, persist for your user):
```powershell
[System.Environment]::SetEnvironmentVariable("MVSEP_API_KEY", "YOUR_TOKEN_HERE", "User")
```

Windows (Command Prompt):
```winbatch
setx MVSEP_API_KEY "YOUR_TOKEN_HERE"
```

macOS/Linux (bash/zsh, current session):
```shell
export MVSEP_API_KEY="YOUR_TOKEN_HERE"
```

macOS/Linux (persist): add the above line to `~/.bashrc`, `~/.zshrc`, or the profile your shell uses.

## Install / Build
- Build debug: `dotnet build`
- Build release: `dotnet build -c Release`
- Publish native (example for Windows x64):
  - `dotnet publish -c Release -r win-x64 -p:PublishAot=true`
  - Output in `bin/Release/net9.0/publish/win-x64/`

Project file: `mvsep-cli.csproj` targets `net9.0` with `PublishAot=true`, `System.CommandLine` and `System.Text.Json` dependencies.

## Usage
Basic syntax:

```
mvsep-cli <file> -a <algorithm> [--add_opt1 <int>] [--add_opt2 <int>] [--add_opt3 <int>]
```

Examples:
- Minimal: `mvsep-cli "input.wav" -a 63`
- With extra options: `mvsep-cli "input.mp3" -a 37 --add_opt1 1 --add_opt2 2`

Behavior:
- Requires `MVSEP_API_KEY` to be set; otherwise exits with a warning.
- Transcodes the provided file to a temp FLAC (compression level 12) using `ffmpeg`.
- Uploads the temp file to `https://mvsep.com/api/separation/create` with parameters:
  - `sep_type=<algorithm>`
  - `api_token=$MVSEP_API_KEY`
  - `output_format=2` (FLAC)
  - Optional: `add_opt1/2/3` when provided
- Polls the returned status URL every 5 seconds until status is `done`.
- Downloads each stem to the current directory.

Output file naming pattern:
```
<OriginalName>_Algo<algorithm>_<index>_<Type>.flac
```
Example: `MySong_Algo63_00_Vocals.flac`, `MySong_Algo63_01_Drums.flac`, etc.

### Sample Run
```
> mvsep-cli "input.wav" -a 63
Options:
Temporary file will be created at: C:\Users\you\AppData\Local\Temp\...\.flac
Uploading...
Upload parameters:
  sep_type: 63
  output_format: 2
Upload successful. Job hash: 1234567890abcdef
Current status: processing. Checking again in 5 seconds...
Current status: processing. Checking again in 5 seconds...
Current status: done. Checking again in 5 seconds...
Separation done. Downloading result...
Downloading input_Algo63_00_Vocals.flac:    45.12% 8.2 MB/18.1 MB
Downloading input_Algo63_00_Vocals.flac:    100.00% 18.1 MB/18.1 MB
Downloading input_Algo63_01_Drums.flac:     100.00% 12.3 MB/12.3 MB
...
```

## Command Options
- `file` (argument): Path to the audio file to separate (required).
- `--algorithm`, `-a` (int, required): MVSEP separation algorithm id.
- `--add_opt1` (int, optional): Additional parameter 1 (default: -1 = omitted).
- `--add_opt2` (int, optional): Additional parameter 2 (default: -1 = omitted).
- `--add_opt3` (int, optional): Additional parameter 3 (default: -1 = omitted).

## Environment Variables
- `MVSEP_API_KEY` (required): Your MVSEP API token.

## Progress & UX
- Upload/download progress is printed on a single updating console line.
- Where supported (e.g., Windows Terminal, ConEmu), terminal progress is also sent using OSC 9 codes.

## Architecture Notes
- Entry point/orchestration: `Program.cs`.
- HTTP uploads/downloads with progress and resume: `CliFetcher.cs` (`CliFetcher.Core.Downloader`).
- MVSEP response models and System.Text.Json source‑gen context: `MvsepJson.cs`.
- Helpers for process execution and terminal progress: `Utils.cs`.

## Troubleshooting
- `ffmpeg` not found: Ensure `ffmpeg` is installed and on your `PATH`.
- Authorization errors (401/403): Verify `MVSEP_API_KEY` is set and valid.
- Network/timeout: The tool reports progress but does not currently auto‑retry; re‑run the command.

## Notes / Limitations
- Poll interval is 5 seconds and not configurable in the current version.
- No built‑in validation of algorithm ids beyond required presence.
- Output directory is the current working directory; not yet configurable.

---
This project targets .NET 9 and is compatible with native AOT publishing. Contributions to improve ergonomics (options, retries, out‑dir, validation) are welcome.
