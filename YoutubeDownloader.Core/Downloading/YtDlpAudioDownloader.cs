using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YoutubeDownloader.Core.Downloading;

/// <summary>
/// Downloads audio-only content using yt-dlp as a fallback mechanism.
/// Used when primary download methods fail (e.g., due to 403 errors).
/// </summary>
public class YtDlpAudioDownloader(string ytDlpPath = "yt-dlp", IProcessRunner? processRunner = null)
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

    private static void CleanupPartialFiles(string outputDir)
    {
        try
        {
            // Remove .part files and recent .m4a files (best-effort)
            foreach (
                var partFile in Directory
                    .GetFiles(outputDir, "*.part")
                    .Concat(Directory.GetFiles(outputDir, "*.m4a"))
            )
            {
                try
                {
                    File.Delete(partFile);
                }
                catch
                {
                    // Ignore cleanup failures
                }
            }
        }
        catch
        {
            // Ignore if directory doesn't exist or other issues
        }
    }
}
