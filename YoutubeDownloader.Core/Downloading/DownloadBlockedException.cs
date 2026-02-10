using System;
using System.Net;

namespace YoutubeDownloader.Core.Downloading;

/// <summary>
/// Exception thrown when a video download is blocked by the service provider (e.g., 403 Forbidden).
/// Provides a user-friendly message and preserves the underlying HTTP error details.
/// </summary>
public class DownloadBlockedException : Exception
{
    /// <summary>
    /// The HTTP status code that caused the download to be blocked.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    public DownloadBlockedException(
        string message,
        Exception innerException,
        HttpStatusCode statusCode
    )
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public DownloadBlockedException(string message, HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
