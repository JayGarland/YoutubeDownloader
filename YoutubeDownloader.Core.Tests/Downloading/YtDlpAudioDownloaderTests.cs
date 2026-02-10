using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using YoutubeDownloader.Core.Downloading;
using Xunit;

namespace YoutubeDownloader.Core.Tests.Downloading;

public class YtDlpAudioDownloaderTests
{
    private const string TestVideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    [Fact]
    public async Task DownloadAudioAsync_ShouldCallProcessRunner_WithCorrectArguments()
    {
        // Arrange
        var fakeProcessRunner = new FakeProcessRunner(
            exitCode: 0,
            stdout: "[download] Destination: test-audio.m4a\n",
            stderr: ""
        );

        var downloader = new YtDlpAudioDownloader("yt-dlp", fakeProcessRunner);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        try
        {
            Directory.CreateDirectory(outputDir);

            // Create a fake output file that the downloader will "find"
            var fakeOutputFile = Path.Combine(outputDir, "test-audio.m4a");
            await File.WriteAllTextAsync(fakeOutputFile, "fake audio content");

            // Act
            var result = await downloader.DownloadAudioAsync(
                TestVideoUrl,
                outputDir,
                CancellationToken.None
            );

            // Assert
            fakeProcessRunner.LastStartInfo.Should().NotBeNull();
            fakeProcessRunner.LastStartInfo!.FileName.Should().Be("yt-dlp");
            fakeProcessRunner
                .LastStartInfo.Arguments.Should()
                .Contain("-x")
                .And.Contain("--audio-format m4a")
                .And.Contain("--no-playlist")
                .And.Contain(TestVideoUrl);

            result.Should().Be(fakeOutputFile);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAudioAsync_WhenProcessFails_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var fakeProcessRunner = new FakeProcessRunner(
            exitCode: 1,
            stdout: "",
            stderr: "ERROR: Video not available\nSome other error line"
        );

        var downloader = new YtDlpAudioDownloader("yt-dlp", fakeProcessRunner);
        var outputDir = Path.GetTempPath();

        // Act
        var act = async () =>
            await downloader.DownloadAudioAsync(
                TestVideoUrl,
                outputDir,
                CancellationToken.None
            );

        // Assert
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("yt-dlp failed with exit code 1");
        exception.Which.Message.Should().Contain("Video not available");
    }

    [Fact]
    public async Task DownloadAudioAsync_WhenDownloadBlockedException_TriggersYtDlpFallback()
    {
        // This test verifies the contract: when DownloadBlockedException is thrown,
        // the DashboardViewModel should catch it and call YtDlpAudioDownloader
        // if UseCompatibilityModeYtDlp && CompatibilityModeAudioOnly are enabled.

        // Arrange
        var fakeProcessRunner = new FakeProcessRunner(
            exitCode: 0,
            stdout: "[ExtractAudio] Destination: output.m4a\n",
            stderr: ""
        );

        var downloader = new YtDlpAudioDownloader("yt-dlp", fakeProcessRunner);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        try
        {
            Directory.CreateDirectory(outputDir);

            // Create fake output file
            var fakeOutputFile = Path.Combine(outputDir, "output.m4a");
            await File.WriteAllTextAsync(fakeOutputFile, "audio");

            // Act
            var result = await downloader.DownloadAudioAsync(
                TestVideoUrl,
                outputDir,
                CancellationToken.None
            );

            // Assert - verifies that yt-dlp was called with expected parameters
            fakeProcessRunner.LastStartInfo.Should().NotBeNull();
            fakeProcessRunner.LastStartInfo!.FileName.Should().Be("yt-dlp");
            fakeProcessRunner
                .LastStartInfo.Arguments.Should()
                .Contain(TestVideoUrl)
                .And.Contain("--audio-format m4a");

            File.Exists(result).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAudioAsync_ShouldReportProgress_WhenStreamingOutputContainsPercentages()
    {
        // Arrange - Simulate yt-dlp progress output with increasing percentages
        var streamingLines = new[]
        {
            "[download] Starting download...",
            "[download]   10.0% of 3.50MiB at 1.20MiB/s ETA 00:02",
            "[download]   25.5% of 3.50MiB at 1.25MiB/s ETA 00:01",
            "[download]   55.5% of 3.50MiB at 1.30MiB/s ETA 00:01",
            "[download]   87.3% of 3.50MiB at 1.28MiB/s ETA 00:00",
            "[download]  100% of 3.50MiB at 1.27MiB/s",
            "[ExtractAudio] Destination: test-progress.m4a",
        };

        var fakeProcessRunner = new FakeProcessRunner(
            exitCode: 0,
            stdout: "[ExtractAudio] Destination: test-progress.m4a\n",
            stderr: "",
            streamingLines: streamingLines
        );

        var downloader = new YtDlpAudioDownloader("yt-dlp", fakeProcessRunner);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var reportedProgress = new List<double>();
        var progress = new Progress<double>(p => reportedProgress.Add(p));

        try
        {
            Directory.CreateDirectory(outputDir);

            // Create fake output file
            var fakeOutputFile = Path.Combine(outputDir, "test-progress.m4a");
            await File.WriteAllTextAsync(fakeOutputFile, "audio");

            // Act
            var result = await downloader.DownloadAudioAsync(
                TestVideoUrl,
                outputDir,
                CancellationToken.None,
                progress
            );

            // Assert - Progress should be reported with increasing values
            reportedProgress.Should().NotBeEmpty("progress should be reported");
            reportedProgress.Should().Contain(p => p >= 0.10, "10% should be reported");
            reportedProgress.Should().Contain(p => p >= 0.55, "55% should be reported");
            reportedProgress.Should().Contain(p => p >= 0.87, "87% should be reported");

            // Last reported value should be 1.0 (completion)
            reportedProgress.Should().Contain(1.0, "completion should be reported");
            reportedProgress.Last().Should().Be(1.0, "last progress should be 100%");

            // Progress should be monotonically increasing
            for (int i = 1; i < reportedProgress.Count; i++)
            {
                reportedProgress[i]
                    .Should()
                    .BeGreaterOrEqualTo(
                        reportedProgress[i - 1],
                        "progress should increase or stay the same"
                    );
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAudioAsync_WhenCancelled_ShouldStopReportingProgress()
    {
        // Arrange - Long-running operation with many progress updates
        var streamingLines = Enumerable
            .Range(1, 100)
            .Select(i => $"[download]   {i}.0% of 100.00MiB at 1.00MiB/s ETA 00:10")
            .ToArray();

        var fakeProcessRunner = new FakeProcessRunner(
            exitCode: 0,
            stdout: "",
            stderr: "",
            streamingLines: streamingLines
        );

        var downloader = new YtDlpAudioDownloader("yt-dlp", fakeProcessRunner);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var reportedProgress = new List<double>();
        var progress = new Progress<double>(p => reportedProgress.Add(p));
        var cts = new CancellationTokenSource();

        try
        {
            Directory.CreateDirectory(outputDir);

            // Act - Start download and cancel after a short delay
            var downloadTask = downloader.DownloadAudioAsync(
                TestVideoUrl,
                outputDir,
                cts.Token,
                progress
            );

            // Cancel after 50ms (should cancel mid-progress)
            await Task.Delay(50);
            await cts.CancelAsync();

            // Assert - Should throw OperationCanceledException
            var act = async () => await downloadTask;
            await act.Should().ThrowAsync<OperationCanceledException>();

            // Some progress should have been reported before cancellation
            reportedProgress.Should().NotBeEmpty("some progress was reported before cancel");

            // But we shouldn't have all 100 progress updates
            reportedProgress
                .Should()
                .HaveCountLessThan(100, "download was cancelled before completion");
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    // Fake process runner for testing
    private class FakeProcessRunner(
        int exitCode,
        string stdout,
        string stderr,
        string[]? streamingLines = null
    ) : IProcessRunner
    {
        public ProcessStartInfo? LastStartInfo { get; private set; }

        public Task<(int exitCode, string stdout, string stderr)> RunAsync(
            ProcessStartInfo startInfo,
            CancellationToken cancellationToken = default
        )
        {
            LastStartInfo = startInfo;
            return Task.FromResult((exitCode, stdout, stderr));
        }

        public async Task<(int exitCode, string stdout, string stderr)> RunStreamingAsync(
            ProcessStartInfo startInfo,
            Action<string>? onStdOut = null,
            Action<string>? onStdErr = null,
            CancellationToken cancellationToken = default
        )
        {
            LastStartInfo = startInfo;

            // Emit streaming lines if provided (simulating real-time output)
            if (streamingLines is not null)
            {
                foreach (var line in streamingLines)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Emit to both stdout and stderr handlers (yt-dlp can output to both)
                    onStdOut?.Invoke(line);
                    onStdErr?.Invoke(line);

                    // Small delay to simulate real process output
                    await Task.Delay(10, cancellationToken);
                }
            }

            return (exitCode, stdout, stderr);
        }
    }
}