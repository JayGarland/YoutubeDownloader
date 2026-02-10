using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace YoutubeDownloader.Core.Downloading;

/// <summary>
/// Interface for running external processes.
/// Enables testability by abstracting Process execution.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Starts a process with the given start info and waits for it to complete.
    /// </summary>
    /// <returns>
    /// A tuple containing:
    /// - exitCode: The process exit code
    /// - stdout: Standard output content
    /// - stderr: Standard error content
    /// </returns>
    Task<(int exitCode, string stdout, string stderr)> RunAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Starts a process with the given start info and streams output/error through callbacks.
    /// Useful for real-time progress monitoring.
    /// </summary>
    /// <param name="startInfo">Process start configuration</param>
    /// <param name="onStdOut">Callback invoked for each stdout line (nullable)</param>
    /// <param name="onStdErr">Callback invoked for each stderr line (nullable)</param>
    /// <param name="cancellationToken">Cancellation token to kill process tree on cancel</param>
    /// <returns>
    /// A tuple containing:
    /// - exitCode: The process exit code
    /// - stdout: Accumulated standard output content
    /// - stderr: Accumulated standard error content
    /// </returns>
    Task<(int exitCode, string stdout, string stderr)> RunStreamingAsync(
        ProcessStartInfo startInfo,
        Action<string>? onStdOut = null,
        Action<string>? onStdErr = null,
        CancellationToken cancellationToken = default
    );
}
