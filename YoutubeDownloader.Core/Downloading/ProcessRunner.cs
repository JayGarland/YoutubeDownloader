using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YoutubeDownloader.Core.Downloading;

/// <summary>
/// Default implementation of IProcessRunner that executes real processes.
/// </summary>
public class ProcessRunner : IProcessRunner
{
    public async Task<(int exitCode, string stdout, string stderr)> RunAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken = default
    )
    {
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdoutBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderrBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait for process to exit or cancellation
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }

    public async Task<(int exitCode, string stdout, string stderr)> RunStreamingAsync(
        ProcessStartInfo startInfo,
        Action<string>? onStdOut = null,
        Action<string>? onStdErr = null,
        CancellationToken cancellationToken = default
    )
    {
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdoutBuilder.AppendLine(e.Data);
                onStdOut?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderrBuilder.AppendLine(e.Data);
                onStdErr?.Invoke(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            // Wait for process to exit or cancellation
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Kill process tree on cancellation
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore if already exited
            }

            throw;
        }

        return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }
}
