using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YoutubeDownloader.Core.Downloading;

/// <summary>
/// Downloads audio and video content using yt-dlp as a fallback mechanism.
/// Used when primary download methods fail (e.g., due to 403 errors).
/// </summary>
public class YtDlpDownloader(string ytDlpPath = "yt-dlp", IProcessRunner? processRunner = null)
{
    private readonly IProcessRunner _processRunner = processRunner ?? new ProcessRunner();

    /// <summary>
    /// Downloads audio from the given URL using yt-dlp.
    /// </summary>
    /// <param name="url">The video URL to download audio from</param>
    /// <param name="outputDir">Directory where the audio file will be saved</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0)</param>
    /// <returns>Path to the downloaded audio file</returns>
    /// <exception cref="InvalidOperationException">Thrown when yt-dlp execution fails</exception>
    public async Task<string> DownloadAudioAsync(
        string url,
        string outputDir,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null
    )
    {
        // Ensure output directory exists
        Directory.CreateDirectory(outputDir);

        // Build output template path (yt-dlp will substitute %(title)s and %(ext)s)
        var outputTemplate = Path.Combine(outputDir, "%(title)s.%(ext)s");

        // Build yt-dlp process arguments
        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            Arguments =
                $"-x --audio-format m4a --no-playlist --newline --progress -o \"{outputTemplate}\" \"{url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Progress tracking
        var lastReportedProgress = 0.0;
        var lastReportTime = DateTime.MinValue;
        var progressRegex = new Regex(@"\[download\]\s+(\d{1,3}(?:\.\d+)?)\%");

        // Progress parser for streaming callbacks
        void ParseProgress(string line)
        {
            if (progress is null)
                return;

            var match = progressRegex.Match(line);
            if (!match.Success)
                return;

            if (!double.TryParse(match.Groups[1].Value, out var percentage))
                return;

            var normalizedProgress = Math.Clamp(percentage / 100.0, 0.0, 1.0);

            // Throttle: only report if delta > 0.002 OR time elapsed > 200ms
            var now = DateTime.UtcNow;
            var timeSinceLastReport = now - lastReportTime;

            if (
                normalizedProgress > lastReportedProgress + 0.002
                || timeSinceLastReport.TotalMilliseconds > 200
            )
            {
                progress.Report(normalizedProgress);
                lastReportedProgress = normalizedProgress;
                lastReportTime = now;
            }
        }

        try
        {
            var (exitCode, stdout, stderr) = await _processRunner.RunStreamingAsync(
                startInfo,
                onStdOut: ParseProgress,
                onStdErr: ParseProgress,
                cancellationToken
            );

            if (exitCode != 0)
            {
                // Extract last meaningful lines from stderr for error message
                var errorLines = stderr
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .TakeLast(5)
                    .ToArray();
                var errorMessage = string.Join(Environment.NewLine, errorLines);

                throw new InvalidOperationException(
                    $"yt-dlp failed with exit code {exitCode}. Error: {errorMessage}"
                );
            }

            // Try to parse the output filename from stdout
            // yt-dlp outputs lines like "[download] Destination: <filename>"
            var downloadedFile = ParseDownloadedFilePath(stdout, outputDir);

            if (downloadedFile is null || !File.Exists(downloadedFile))
            {
                throw new InvalidOperationException(
                    "yt-dlp completed successfully but output file was not found."
                );
            }

            // Report completion
            progress?.Report(1.0);

            return downloadedFile;
        }
        catch (OperationCanceledException)
        {
            // Clean up partial downloads on cancellation (best-effort)
            CleanupPartialFiles(outputDir);
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Wrap unexpected exceptions
            throw new InvalidOperationException($"Failed to execute yt-dlp: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Downloads video (with audio) from the given URL using yt-dlp.
    /// </summary>
    /// <param name="url">The video URL to download</param>
    /// <param name="outputDir">Directory where the video file will be saved</param>
    /// <param name="mergeFormat">Output container format (mp4, mkv, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0)</param>
    /// <returns>Path to the downloaded video file</returns>
    /// <exception cref="InvalidOperationException">Thrown when yt-dlp execution fails</exception>
    public async Task<string> DownloadVideoAsync(
        string url,
        string outputDir,
        string mergeFormat = "mp4",
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null
    )
    {
        return await DownloadVideoInternalAsync(
            url,
            outputDir,
            @"bv*+ba/b",
            mergeFormat,
            cancellationToken,
            progress
        );
    }

    /// <summary>
    /// Downloads video as a single file without merge step (ffmpeg not required).
    /// </summary>
    /// <param name="url">The video URL to download</param>
    /// <param name="outputDir">Directory where the video file will be saved</param>
    /// <param name="formatSelector">yt-dlp format selector (default: best mp4 or best available)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0)</param>
    /// <returns>Path to the downloaded video file</returns>
    /// <exception cref="InvalidOperationException">Thrown when yt-dlp execution fails</exception>
    public async Task<string> DownloadVideoNoMergeAsync(
        string url,
        string outputDir,
        string formatSelector = @"best[ext=mp4]/best",
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null
    )
    {
        return await DownloadVideoInternalAsync(
            url,
            outputDir,
            formatSelector,
            mergeFormat: null,
            cancellationToken,
            progress
        );
    }

    private async Task<string> DownloadVideoInternalAsync(
        string url,
        string outputDir,
        string formatSelector,
        string? mergeFormat,
        CancellationToken cancellationToken,
        IProgress<double>? progress
    )
    {
        // Ensure output directory exists
        Directory.CreateDirectory(outputDir);

        // Use %(id)s prefix to ensure unique filenames and avoid collisions
        var outputTemplate = Path.Combine(outputDir, "%(id)s_%(title)s.%(ext)s");

        // Build yt-dlp process arguments
        // -f "bv*+ba/b" = best video + best audio, fallback to best single file
        // --merge-output-format ensures final container type
        // --print after_move:filepath gives us the final file path reliably
        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            Arguments =
                $"--newline --progress --no-playlist -f \"{formatSelector}\"{(string.IsNullOrWhiteSpace(mergeFormat) ? "" : $" --merge-output-format {mergeFormat}")} --print after_move:filepath -o \"{outputTemplate}\" \"{url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Progress tracking
        var lastReportedProgress = 0.0;
        var lastReportTime = DateTime.MinValue;
        var progressRegex = new Regex(@"\[download\]\s+(\d{1,3}(?:\.\d+)?)\%");

        // Capture final file path from --print after_move:filepath
        string? finalFilePath = null;

        // Progress parser for streaming callbacks
        void ParseProgressAndOutput(string line)
        {
            // Parse progress
            if (progress is not null)
            {
                var match = progressRegex.Match(line);
                if (match.Success && double.TryParse(match.Groups[1].Value, out var percentage))
                {
                    var normalizedProgress = Math.Clamp(percentage / 100.0, 0.0, 1.0);

                    // Throttle: only report if delta > 0.002 OR time elapsed > 200ms
                    var now = DateTime.UtcNow;
                    var timeSinceLastReport = now - lastReportTime;

                    if (
                        normalizedProgress > lastReportedProgress + 0.002
                        || timeSinceLastReport.TotalMilliseconds > 200
                    )
                    {
                        progress.Report(normalizedProgress);
                        lastReportedProgress = normalizedProgress;
                        lastReportTime = now;
                    }
                }
            }

            // Capture final file path from --print output
            // yt-dlp prints the final path on a line by itself after merging
            if (line.Contains(Path.DirectorySeparatorChar) && !line.StartsWith('['))
            {
                var trimmedLine = line.Trim();
                if (File.Exists(trimmedLine))
                {
                    finalFilePath = trimmedLine;
                }
            }
        }

        try
        {
            var (exitCode, stdout, stderr) = await _processRunner.RunStreamingAsync(
                startInfo,
                onStdOut: ParseProgressAndOutput,
                onStdErr: ParseProgressAndOutput,
                cancellationToken
            );

            if (exitCode != 0)
            {
                // Extract last meaningful lines from stderr for error message
                var errorLines = stderr
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .TakeLast(5)
                    .ToArray();
                var errorMessage = string.Join(Environment.NewLine, errorLines);

                throw new InvalidOperationException(
                    $"yt-dlp failed with exit code {exitCode}. Error: {errorMessage}"
                );
            }

            // Try to get final file path from captured output or parse stdout
            var downloadedFile =
                finalFilePath
                ?? ParseVideoFilePath(stdout, outputDir, mergeFormat)
                ?? ParseDownloadedFilePath(stdout, outputDir);

            if (downloadedFile is null || !File.Exists(downloadedFile))
            {
                throw new InvalidOperationException(
                    "yt-dlp completed successfully but output file was not found."
                );
            }

            // Report completion
            progress?.Report(1.0);

            return downloadedFile;
        }
        catch (OperationCanceledException)
        {
            // Clean up partial downloads on cancellation (best-effort)
            CleanupPartialFiles(
                outputDir,
                string.IsNullOrWhiteSpace(mergeFormat) ? "*.mp4" : $"*.{mergeFormat}"
            );
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Wrap unexpected exceptions
            throw new InvalidOperationException($"Failed to execute yt-dlp: {ex.Message}", ex);
        }
    }

    private static string? ParseDownloadedFilePath(string stdout, string outputDir)
    {
        // Look for lines like "[download] Destination: <path>" or "[ExtractAudio] Destination: <path>"
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (
                line.Contains("[download] Destination:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("[ExtractAudio] Destination:", StringComparison.OrdinalIgnoreCase)
            )
            {
                var parts = line.Split(
                    new[] { "Destination:" },
                    StringSplitOptions.RemoveEmptyEntries
                );
                if (parts.Length > 1)
                {
                    var filePath = parts[1].Trim();
                    if (File.Exists(filePath))
                        return filePath;
                }
            }
        }

        // Fallback: Look for .m4a files in output directory
        var m4aFiles = Directory.GetFiles(outputDir, "*.m4a");
        return m4aFiles.Length > 0 ? m4aFiles[0] : null;
    }

    private static string? ParseVideoFilePath(string stdout, string outputDir, string? format)
    {
        // Look for lines like "[Merger] Merging formats into \"<path>\""
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // First, check for merger output
        foreach (var line in lines)
        {
            if (line.Contains("[Merger] Merging formats into", StringComparison.OrdinalIgnoreCase))
            {
                // Extract path between quotes
                var match = Regex.Match(line, @"""([^""]+)""");
                if (match.Success)
                {
                    var filePath = match.Groups[1].Value;
                    if (File.Exists(filePath))
                        return filePath;
                }
            }
        }

        // Fallback: Look for destination line with the correct format
        foreach (var line in lines)
        {
            if (line.Contains("[download] Destination:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(
                    new[] { "Destination:" },
                    StringSplitOptions.RemoveEmptyEntries
                );
                if (parts.Length > 1)
                {
                    var filePath = parts[1].Trim();
                    if (
                        (
                            string.IsNullOrWhiteSpace(format)
                            || filePath.EndsWith($".{format}", StringComparison.OrdinalIgnoreCase)
                        ) && File.Exists(filePath)
                    )
                        return filePath;
                }
            }
        }

        // Final fallback: Get most recent file with matching format in output directory
        try
        {
            if (!string.IsNullOrWhiteSpace(format))
            {
                var formatFiles = Directory
                    .GetFiles(outputDir, $"*.{format}")
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .ToArray();

                if (formatFiles.Length > 0)
                    return formatFiles[0];
            }

            var extensions = new[] { "*.mp4", "*.webm", "*.mkv", "*.mov", "*.avi" };
            return extensions
                .SelectMany(pattern => Directory.GetFiles(outputDir, pattern))
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static void CleanupPartialFiles(string outputDir, string pattern = "*.part")
    {
        try
        {
            // Remove .part files and recent audio/video files (best-effort)
            var patterns = new[] { "*.part", "*.m4a", pattern };
            foreach (var searchPattern in patterns)
            {
                foreach (var file in Directory.GetFiles(outputDir, searchPattern))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Ignore cleanup failures
                    }
                }
            }
        }
        catch
        {
            // Ignore if directory doesn't exist or other issues
        }
    }
}
