# Exhaustive Codebase Report

## Project Overview

**Project Name:** mvsep-cli  
**Type:** Command-Line Interface (CLI) Application  
**Language:** C# (.NET)  
**Target Framework:** .NET 10.0  
**Purpose:** Audio separation tool using the MVSEP API to separate audio files into individual stems (vocals, drums, guitar, etc.)

## Repository Structure

```
mvsep-cli/
├── .git/                          # Git repository metadata
├── .github/                       # GitHub-specific files
│   └── upgrades/                  # .NET upgrade documentation
│       ├── dotnet-upgrade-plan.md
│       └── dotnet-upgrade-report.md
├── .gitignore                     # Git ignore rules
├── .vscode/                       # VS Code configuration
│   └── launch.json               # Debug launch configuration
├── CliFetcher.cs                 # HTTP download/upload utilities
├── MOTM.cs                       # Main separation logic
├── MvsepJson.cs                  # JSON serialization models
├── Program.cs                    # Entry point and CLI definition
├── Properties/                   # Project properties
│   └── launchSettings.json       # Launch profiles
├── Utils.cs                      # Utility functions
├── mvsep-cli.csproj             # Project file
└── mvsep-cli.sln                # Solution file
```

## Core Source Files Analysis

### 1. Program.cs (294 lines)

**Purpose:** Entry point of the application, defines the CLI interface and command structure.

**Key Components:**

#### Command Structure
- **Root Command:** "Audio Separator CLI"
  - **single command:** Runs a single separation on a file
  - **full command:** Runs a batched separation with multiple algorithms

#### Options & Arguments

**Global Options:**
- `--api-key` / `-k`: API key for MVSEP (falls back to MVSEP_API_KEY environment variable)

**Single Command Options:**
- `file`: Audio file argument (required, exactly one)
- `--algorithm` / `-a`: Separation algorithm ID (required, integer)
- `--add_opt1` / `-o1`: First optional parameter (default: -1)
- `--add_opt2` / `-o2`: Second optional parameter (default: -1)
- `--add_opt3` / `-o3`: Third optional parameter (default: -1)
- `--output-format` / `-f`: Output format (default: FLAC, options: MP3, WAV, FLAC, M4A, WAV32, FLAC24)

**Full Command Options:**
- `file`: Audio file to process (required)
- `--drums-on` / `-d`: Channel for drums separation (Left/Right, default: Left)
- `--separate-tambourine` / `-t`: Enable tambourine separation (boolean, default: false)
- `--karaoke-on-vocals` / `-v`: Enable lead-back vocals separation (boolean, default: false)
- `--acoustic-on` / `-a`: Channel for acoustic guitar separation (optional: Left/Right)
- `--dump-options` / `-x`: Dump selected options and exit (boolean, default: false)

#### Enums
- **OutputFormat:** MP3, WAV, FLAC, M4A, WAV32, FLAC24
- **ChannelWhere:** Left, Right

#### Command Handlers

**Single Command Handler:**
1. Validates file argument
2. Retrieves API key (CLI option > environment variable)
3. Calls `MOTM.Execute()` with parameters
4. Handles error cases (missing file, missing API key)

**Full Command Handler:**
1. Optional dump mode - displays options in a formatted table
2. Validates file existence
3. Splits stereo audio into left/right channels using ffmpeg
4. Processes both channels with algorithm 63
5. Conditionally processes:
   - Drums separation on selected channel
   - Tambourine separation (algorithm 76)
   - Drumkit components (algorithm 37)
   - Acoustic guitar separation (algorithm 66) on selected channel
   - Lead/backing vocals separation (algorithm 49)

**Dependencies:**
- System.CommandLine: CLI parsing framework
- Spectre.Console: Rich console UI (tables, colors, markup)

**Design Patterns:**
- Command pattern for CLI structure
- Builder pattern for options
- Async/await for asynchronous operations

---

### 2. MOTM.cs (153 lines)

**Purpose:** "Meat of the matter" - Core logic for executing separation tasks.

**Namespace:** mvsep_cli

**Class:** MOTM (internal)

#### Methods

**Execute(string file, ...)**
- Validates file path
- Converts to FileInfo
- Delegates to FileInfo overload

**Execute(FileInfo file, int algorithm, string apiKey, int? opt1, int? opt2, int? opt3, OutputFormat outputFormat)**
- Main execution method
- **Steps:**
  1. **Preparation**
     - Extracts filename without extension
     - Gets current working directory
     - Creates temporary FLAC file in system temp directory
  
  2. **Conversion**
     - Converts input file to FLAC using ffmpeg
     - Uses compression level 12
     - Hides ffmpeg banner and error output
  
  3. **Upload**
     - Displays upload status with Spectre.Console
     - Prepares upload parameters:
       - `sep_type`: algorithm ID
       - `api_token`: API key
       - `output_format`: format code
       - `opt1`, `opt2`, `opt3`: optional parameters
     - Uses CliFetcher.Core.Downloader for upload
     - Shows progress bar during upload
     - Posts to: https://mvsep.com/api/separation/create
  
  4. **Polling**
     - Parses upload response to get job hash and status URL
     - Polls status endpoint every 2.5 seconds
     - Uses Spectre.Console spinner for visual feedback
     - Waits until status becomes "done"
  
  5. **Download**
     - Downloads all separated stems
     - Names files: `{original}_Algo{algorithm}_{index:02}_{type}.flac`
     - Shows individual progress bars for each file
     - Saves to current working directory

**Error Handling:**
- Throws ArgumentException for null/whitespace file path
- Throws FileNotFoundException if input file doesn't exist

**UI Features:**
- Console markup for colored output
- Progress bars for upload/download
- Spinner during processing
- Parameter display (excluding sensitive API token)

**Dependencies:**
- CliFetcher.Core: HTTP operations
- Spectre.Console: UI components
- System.Text.Json: JSON parsing via MvsepJson models

---

### 3. CliFetcher.cs (534 lines)

**Purpose:** HTTP download and upload functionality with progress reporting.

**Namespace:** CliFetcher.Core

#### Classes

**DownloadOptions (sealed record)**
- `UserAgent`: Custom user agent string (default: "CliFetcher.Core/1.0")
- `BufferSize`: Copy buffer size in bytes (default: 64 KiB)
- `Resume`: Attempt to resume partial downloads (default: true)
- `Overwrite`: Overwrite existing files when Resume=false (default: true)
- `HttpTimeout`: HTTP client timeout (default: 30 minutes)

**DownloadProgress (readonly record struct)**
- `BytesReceived`: Total bytes transferred
- `TotalBytes`: Total file size (nullable, may be unknown)
- `InstantBytesPerSecond`: Raw instantaneous transfer rate
- `SmoothedBytesPerSecond`: Moving average transfer rate
- `Elapsed`: Time elapsed
- `Eta`: Estimated time to completion (nullable)
- `Percent`: Calculated percentage (0-1, nullable)

**Downloader (sealed class, IDisposable)**

**Constructor:**
- Accepts optional HttpMessageHandler and DownloadOptions
- Configures automatic decompression (GZip, Deflate, Brotli)
- Sets timeout from options

**DownloadFileAsync() Method:**
- Downloads URL to file with progress reporting
- **Features:**
  - Resume support via HTTP Range headers
  - Progress callbacks
  - Cancellation token support
  - Moving average speed calculation (20-sample window)
  - Automatic directory creation
  - ETA calculation
- **Process:**
  1. Check for existing file
  2. Add Range header if resuming
  3. Send HTTP GET request
  4. Handle server response (OK or PartialContent)
  5. Stream data to file with progress updates
  6. Report final statistics

**UploadFileAsync() Method:**
- Uploads file using multipart/form-data
- **Parameters:**
  - `url`: Upload endpoint
  - `filePath`: File to upload
  - `formFileField`: Form field name (default: "file")
  - `formFields`: Additional form data
  - `progress`: Progress callback
  - `cancellationToken`: Cancellation support
- **Returns:** Server response as string
- **Process:**
  1. Validate file exists
  2. Create MultipartFormDataContent
  3. Add form fields
  4. Stream file with progress
  5. Return server response

**ConsoleProgress() Static Method:**
- Creates IProgress<DownloadProgress> for console output
- **Displays:**
  - Percentage complete
  - Bytes received / total bytes
  - Transfer rate (formatted: B, KB, MB, GB, TB)
  - Elapsed time
  - ETA
- **Features:**
  - Terminal width detection
  - ConEmu/Windows Terminal progress bar support (OSC 9;4)
  - Carriage return for in-place updates
  - Thread-safe with lock

**ConsoleProgressSimple() Static Method:**
- Simplified progress output
- Shows only percentage and bytes
- No speed or ETA
- Uses Utils.ConEmuProgress for terminal integration

**ProgressStreamContent (private sealed class)**
- Wraps Stream for upload progress
- Inherits from HttpContent
- Tracks bytes written
- Calculates progress with same algorithm as download

**Technical Details:**
- Uses 64 KiB buffer for I/O operations
- 20-sample moving average for speed smoothing
- Async/await throughout
- IDisposable pattern for resource cleanup
- Product/Version parsing for User-Agent headers

---

### 4. MvsepJson.cs (350 lines)

**Purpose:** JSON data models for MVSEP API responses, with auto-generated serialization code.

**Namespace:** mvsep_cli

**Note:** Auto-generated file with nullable reference type warnings disabled.

#### Data Models

**MvsepStatus**
- Root response object for status queries
- Properties:
  - `Success`: bool
  - `Status`: string (e.g., "done", "processing")
  - `Data`: MvsepStatusData

**MvsepStatusData**
- Contains job details and results
- Properties:
  - `Message`: string (optional)
  - `Hash`: string - job identifier (optional)
  - `Algorithm`: string (optional)
  - `AlgorithmDescription`: string (optional)
  - `OutputFormat`: string (optional)
  - `Tags`: DataTags - audio metadata (optional)
  - `InputFile`: File (optional)
  - `Files`: File[] - separated stem files (optional)
  - `Date`: DateTimeOffset? with custom converter (optional)
  - `Transcription`: object[] (optional)

**File**
- Represents a downloadable audio file
- Properties:
  - `Type`: string - stem type (e.g., "Drums", "Vocals")
  - `Url`: Uri - download URL
  - `Size`: string - file size
  - `Image`: object - waveform image
  - `Download`: string - filename

**DataTags**
- Audio file metadata container
- Properties:
  - `Audio`: Audio
  - `Tags`: TagsTags

**Audio**
- Detailed audio format information
- Properties:
  - `Dataformat`: string
  - `BitrateMode`: string
  - `Lossless`: bool
  - `SampleRate`: long
  - `Channels`: long
  - `BitsPerSample`: long
  - `Bitrate`: double
  - `Encoder`: string
  - `Channelmode`: string
  - `CompressionRatio`: double
  - `Streams`: Audio[] (optional, nested)

**TagsTags & Vorbiscomment**
- Vorbis comment tag structure
- Contains encoder information

**MvsepUploadSuccess**
- Response from file upload
- Properties:
  - `Success`: bool
  - `Data`: MvsepUploadSuccessData

**MvsepUploadSuccessData**
- Upload result details
- Properties:
  - `Link`: Uri - status polling URL
  - `Hash`: string - job identifier

#### Serialization Infrastructure

**Static Methods:**
- `MvsepStatus.FromJson(string)`: Deserialize status response
- `MvsepUploadSuccess.FromJson(string)`: Deserialize upload response
- `ToJson()`: Extension methods for serialization

**Custom Converters:**
- `DateOnlyConverter`: Handles DateOnly serialization (format: yyyy-MM-dd)
- `TimeOnlyConverter`: Handles TimeOnly serialization (format: HH:mm:ss.fff)
- `IsoDateTimeOffsetConverter`: ISO 8601 datetime handling
- `NullableDateTimeOffsetConverter`: AOT-friendly nullable datetime

**MvsepJsonContext**
- Source-generated JSON serializer context
- AOT-compatible
- Registers MvsepStatus and MvsepUploadSuccess types

**Converter Settings:**
- Uses JsonSerializerDefaults.General
- Includes custom date/time converters
- Supports roundtrip kind for DateTimeOffset

**Null Handling:**
- JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull) for optional properties
- Nullable reference types enabled

---

### 5. Utils.cs (61 lines)

**Purpose:** Utility functions for process execution, HTTP requests, and terminal progress.

**Static Class**

#### Methods

**RunProcess(string fileName, string arguments)**
- Executes external process synchronously
- **Parameters:**
  - `fileName`: Executable name (e.g., "ffmpeg")
  - `arguments`: Command-line arguments
- **Returns:** Exit code (int)
- **Configuration:**
  - No output redirection (prints to console)
  - No shell execution
- **Error Handling:**
  - Throws InvalidOperationException if process fails to start
- **Usage in codebase:**
  - ffmpeg audio conversion
  - Channel splitting

**GetStringFromUrlAsync(Uri url)**
- Fetches URL content as string
- **Async method**
- Creates new HttpClient per call
- No error handling (exceptions propagate)
- Used for MVSEP API status polling

**ConEmuProgress(int progress, ConEmuProgressStyle style)**
- Updates terminal progress indicator
- **Parameters:**
  - `progress`: Progress percentage (0-100)
  - `style`: Visual style (default: Default)
- **ConEmuProgressStyle enum:**
  - `Clear`: Remove progress indicator (code: "0")
  - `Default`: Normal progress (code: "1")
  - `Error`: Error state (code: "2")
  - `Indeterminate`: Unknown duration (code: "3")
  - `Warning`: Warning state (code: "4")
- **Technical Details:**
  - Uses ANSI escape sequences: `\e]9;{style};{progress}\a`
  - Compatible with ConEmu and Windows Terminal
  - OSC 9;4 protocol

**Design Notes:**
- No resource cleanup for HttpClient (could be improved)
- Minimal error handling (delegates to caller)
- Terminal-specific features (ConEmu)

---

## Project Configuration

### mvsep-cli.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>mvsep_cli</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.0.0" />
    <PackageReference Include="Spectre.Console" Version="0.54.0" />
  </ItemGroup>
</Project>
```

**Configuration Details:**
- **Output Type:** Executable (console application)
- **Target Framework:** .NET 10.0
- **Root Namespace:** mvsep_cli (underscore, not hyphen)
- **Implicit Usings:** Enabled (common namespaces auto-imported)
- **Nullable:** Enabled (nullable reference types)
- **Invariant Globalization:** True (no culture-specific operations, smaller deployment)

**Dependencies:**
- **System.CommandLine v2.0.0:** Modern CLI framework
  - Argument parsing
  - Option binding
  - Command hierarchy
  - Help generation
- **Spectre.Console v0.54.0:** Rich console UI
  - Progress bars
  - Spinners
  - Tables
  - Markup/colors
  - Text paths

### mvsep-cli.sln

**Solution File Format:** Visual Studio 2017 (v12.00)
- **Projects:** 1 (mvsep-cli.csproj)
- **Configurations:** Debug|Any CPU, Release|Any CPU
- **GUID:** {61D8CFA3-3FF0-4AFA-8397-18BD4458BA2F}
- **Solution GUID:** {4E0CC544-19A8-4DD2-88EC-8BA2C5A451A1}

---

## Build & Development Configuration

### .vscode/launch.json

**Debug Configuration:**
- **Name:** "C#: mvsep-cli Debug"
- **Type:** dotnet
- **Request:** launch
- **Project Path:** ${workspaceFolder}/mvsep-cli.csproj
- **Example Arguments:**
  - File: Beatles FLAC file (Windows path)
  - Algorithm: 37
  - opt1: 7
  - opt2: 1

### Properties/launchSettings.json

**Profile:** mvsep-cli
- **Command Name:** Project
- **Command Line Args:** Same as VS Code configuration
- Uses Windows-specific file path

---

## .NET Upgrade History

### Upgrade Plan (from .github/upgrades/)

**Source Framework:** .NET 9.0  
**Target Framework:** .NET 10.0

**Changes Required:**
1. Update TargetFramework from `net9.0` to `net10.0`
2. Update System.Text.Json from 9.0.10 to 10.0.0
3. Validate SDK installation

**Projects Affected:**
- mvsep-cli.csproj

**NuGet Package Changes:**
- System.Text.Json: 9.0.10 → 10.0.0 (later removed as provided by platform)

### Upgrade Report

**Commits:**
- af27871f: Update mvsep-cli.csproj to target .NET 10.0
- ed66c44c: Commit upgrade plan
- 221e1ac3: Update System.Text.Json to v10.0.0
- 50481645: Remove redundant System.Text.Json reference

**Status:** Complete
**Next Steps:** Build and test with .NET 10 SDK

---

## Dependencies & External Integrations

### NuGet Packages

1. **System.CommandLine 2.0.0**
   - CLI framework
   - Argument/option parsing
   - Subcommand support
   - Help text generation

2. **Spectre.Console 0.54.0**
   - Rich console output
   - Progress bars
   - Spinners
   - Tables
   - Colored markup
   - Terminal detection

### External Tools

1. **ffmpeg**
   - Required for audio processing
   - Operations:
     - Format conversion to FLAC
     - Stereo channel splitting
     - Audio filtering
   - Must be in PATH

### External APIs

**MVSEP API (https://mvsep.com)**

**Endpoints:**
- `POST /api/separation/create`: Upload audio for separation
  - Multipart form data
  - Parameters: audiofile, sep_type, api_token, output_format, opt1-3
  - Returns: job hash and status URL

- `GET {status_url}`: Poll job status
  - Returns: MvsepStatus JSON
  - Status values: "processing", "done", etc.
  - Includes download URLs when complete

**Authentication:**
- API key required
- Can be provided via:
  1. `--api-key` CLI option (priority)
  2. `MVSEP_API_KEY` environment variable (fallback)
- Get key at: https://mvsep.com/user-api

**Algorithms:** (Referenced in code)
- 37: Drumkit components (with opts)
- 49: Lead/backing vocals separation (opt1: 6, opt2: 0)
- 63: Main separation (4-stem: drums, vocals, bass, other)
- 66: Acoustic guitar separation (opt2: 0)
- 76: Tambourine separation

**Output Formats:**
- 0: MP3
- 1: WAV
- 2: FLAC (default)
- 3: M4A
- 4: WAV32
- 5: FLAC24

---

## Architecture & Design

### Application Flow

**Single Command:**
```
User Input → Parse CLI Args → Validate → MOTM.Execute() → Upload → Poll → Download
```

**Full Command:**
```
User Input → Parse CLI Args → Validate → Split Channels →
  → Separate L/R (Algo 63) →
  → Drums (Algo 63 output) →
  → [Optional] Tambourine (Algo 76) →
  → Drumkit Components (Algo 37) →
  → [Optional] Acoustic (Algo 66) →
  → [Optional] Vocals (Algo 49) →
  → Complete
```

### Key Design Patterns

1. **Command Pattern**
   - System.CommandLine structure
   - Separate handlers for each command
   - Option/argument encapsulation

2. **Strategy Pattern**
   - OutputFormat enum
   - Different separation algorithms
   - Channel selection

3. **Async/Await**
   - Non-blocking I/O
   - Parallel downloads possible
   - Progress reporting

4. **Progress Reporting**
   - IProgress<T> interface
   - Decoupled UI from logic
   - Real-time feedback

5. **Dependency Injection (minimal)**
   - Options passed to constructors
   - HttpMessageHandler injection support

### Error Handling Strategy

**Current Approach:**
- Exceptions propagate to caller
- Validation at entry points
- User-friendly error messages via Spectre.Console markup
- API errors not explicitly caught

**Areas for Improvement:**
- HTTP error handling
- Retry logic for network failures
- API rate limiting
- Disk space validation

### Code Organization

**Separation of Concerns:**
- `Program.cs`: CLI definition and routing
- `MOTM.cs`: Business logic
- `CliFetcher.cs`: HTTP operations
- `MvsepJson.cs`: Data models
- `Utils.cs`: Shared utilities

**Namespace Strategy:**
- `mvsep_cli`: Main application
- `CliFetcher.Core`: Reusable download library
- No deep nesting

---

## Performance Considerations

### I/O Operations

**Buffering:**
- 64 KiB default buffer for HTTP transfers
- 1 MiB file stream buffer for disk writes
- Async I/O throughout

**Progress Reporting:**
- Moving average over 20 samples
- Reduces noise in speed calculations
- Minimal overhead

### Network Operations

**Download/Upload:**
- Streaming (not loading entire file in memory)
- Automatic decompression support
- Resume capability reduces redundant transfers
- 30-minute timeout (configurable)

**Polling:**
- 2.5 second interval
- Could be optimized with exponential backoff
- No polling cancellation mechanism

### Temporary Files

**Location:** System temp directory
- UUID-based filenames (collision-free)
- FLAC compression level 12 (smaller uploads)
- Not automatically cleaned up (potential issue)

---

## Security Considerations

### API Key Management

**Current Implementation:**
- Environment variable support (good)
- CLI argument support (visible in process list - risk)
- No encryption at rest
- Not sanitized from console output in debug mode

**Recommendations:**
- Consider credential manager integration
- Warn users about process visibility
- Sanitize from debug output

### File Handling

**Input Validation:**
- File existence checked
- FileInfo used (path traversal resistant)
- No size limits (DoS risk)

**Output:**
- Writes to current directory
- Filename based on input + algorithm (safe)
- No path validation beyond FileInfo

### Network Security

**HTTPS:**
- Uses HTTPS for API calls (secure)
- No certificate validation overrides
- No proxy support

**Dependencies:**
- System.CommandLine 2.0.0 (check for CVEs)
- Spectre.Console 0.54.0 (check for CVEs)

---

## Testing

**Current State:**
- No test project in repository
- No unit tests
- No integration tests
- Launch configurations for manual testing

**Test Coverage Opportunities:**
1. CLI parsing (System.CommandLine)
2. Progress calculation (CliFetcher)
3. JSON deserialization (MvsepJson)
4. File naming logic (MOTM)
5. Error handling paths

---

## Build Instructions

**Prerequisites:**
- .NET 10.0 SDK (currently requires .NET 9 available)
- ffmpeg in PATH

**Build Commands:**
```bash
dotnet restore
dotnet build
dotnet run -- <args>
```

**Publish:**
```bash
dotnet publish -c Release -r <runtime-id> --self-contained
```

**Runtime IDs:**
- `win-x64`: Windows 64-bit
- `linux-x64`: Linux 64-bit
- `osx-x64`: macOS Intel
- `osx-arm64`: macOS Apple Silicon

---

## Usage Examples

### Single Separation

```bash
# Basic usage
mvsep-cli single audio.mp3 --algorithm=63 --api-key=YOUR_KEY

# With optional parameters
mvsep-cli single drums.flac -a 37 -o1 7 -o2 1 -k YOUR_KEY

# Using environment variable
export MVSEP_API_KEY=YOUR_KEY
mvsep-cli single vocals.wav -a 49 -o1 6 -o2 0
```

### Full Separation

```bash
# Default (drums on left channel)
mvsep-cli full song.flac -k YOUR_KEY

# Drums on right, with tambourine separation
mvsep-cli full song.flac -d Right -t -k YOUR_KEY

# Complete processing with all options
mvsep-cli full song.flac -d Left -t -v -a Left -k YOUR_KEY

# Dump options without processing
mvsep-cli full song.flac -x
```

---

## Output Files

**Naming Convention:**
```
{original_filename}_Algo{algorithm}_{index:02}_{stem_type}.flac
```

**Examples:**
- `song_Algo63_00_Drums.flac`
- `song_Algo63_01_Vocals.flac`
- `song_Algo63_02_Bass.flac`
- `song_Algo63_03_Other.flac`

**Location:** Current working directory

---

## Known Issues & Limitations

### Current Limitations

1. **No Cleanup:**
   - Temporary files not deleted
   - Intermediate files from full command retained

2. **No Parallel Processing:**
   - Sequential API calls in full command
   - Could batch upload similar files

3. **Limited Error Recovery:**
   - No retry logic
   - No resume for interrupted separations

4. **Platform Dependencies:**
   - Requires ffmpeg in PATH
   - ConEmu-specific progress (not universal)

5. **API Polling:**
   - Fixed 2.5 second interval
   - No timeout for long-running jobs

### Future Improvements

1. **Feature Enhancements:**
   - Batch processing multiple files
   - Output directory option
   - Dry-run mode
   - Verbose logging option

2. **Robustness:**
   - Retry with exponential backoff
   - Network error handling
   - Disk space validation
   - Cleanup on failure

3. **Performance:**
   - Parallel downloads
   - Caching/resume for interrupted jobs
   - Streaming conversion (avoid temp file)

4. **UX:**
   - Better error messages
   - Cancel support (Ctrl+C)
   - Configuration file support
   - Progress persistence

---

## Code Metrics

**Total Lines of Code:** ~1,456 lines (excluding generated code comments)

**File Breakdown:**
- Program.cs: 294 lines
- CliFetcher.cs: 534 lines (largest, reusable component)
- MvsepJson.cs: 350 lines (mostly auto-generated)
- MOTM.cs: 153 lines (core business logic)
- Utils.cs: 61 lines (smallest)

**Code Characteristics:**
- Heavy use of async/await
- Minimal comments (self-documenting code style)
- Modern C# features (records, nullable reference types)
- Functional style in places (LINQ)

---

## Maintenance Notes

### Code Quality

**Strengths:**
- Clear separation of concerns
- Modern C# idioms
- Strong typing
- Async throughout

**Areas for Improvement:**
- Add XML documentation comments
- Implement unit tests
- Add logging framework
- Resource cleanup (IDisposable pattern completion)

### Upgrade Path

**Recent History:**
- Upgraded from .NET 9.0 to .NET 10.0
- Removed explicit System.Text.Json (now in platform)

**Future Considerations:**
- Monitor System.CommandLine (currently preview)
- Consider native AOT compilation
- Evaluate newer Spectre.Console features

---

## License & Attribution

**License:** Not specified in repository

**Dependencies Licenses:**
- System.CommandLine: MIT License
- Spectre.Console: MIT License

**External Service:**
- MVSEP API: Proprietary, requires API key

---

## Glossary

**Stem:** Individual instrument or vocal track separated from a mix

**MVSEP:** Music Voice Separation - cloud-based audio separation service

**FLAC:** Free Lossless Audio Codec - compressed audio format

**ffmpeg:** Multimedia framework for audio/video processing

**CLI:** Command-Line Interface

**AOT:** Ahead-of-Time compilation (native executables)

**OSC:** Operating System Command (ANSI escape sequence)

**ConEmu:** Console emulator for Windows

---

## Summary

This codebase is a well-structured, modern C# CLI application for audio source separation using the MVSEP cloud service. It demonstrates:

- Clean architecture with separated concerns
- Modern .NET features and async patterns
- Rich console UI with Spectre.Console
- Robust HTTP operations with progress tracking
- Flexible command structure for different use cases

The code is production-ready for personal use but could benefit from:
- Comprehensive error handling
- Automated testing
- Resource cleanup improvements
- Better temporary file management

The upgrade to .NET 10.0 keeps the codebase modern, though this requires users to have the latest SDK installed.
