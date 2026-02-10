using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.ClosedCaptions;
using YoutubeExplode.Videos.Streams;

namespace YoutubeDownloader.Core.Downloading;

/// <summary>
/// Abstraction for fetching stream and caption manifests from a video source.
/// Enables testability and fallback strategies for handling API failures (e.g., 403 Forbidden).
/// </summary>
public interface IStreamManifestProvider
{
    /// <summary>
    /// Gets the stream manifest for a video, containing available audio/video streams.
    /// </summary>
    Task<StreamManifest> GetStreamManifestAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets the closed caption manifest for a video, containing available subtitle tracks.
    /// </summary>
    Task<ClosedCaptionManifest> GetClosedCaptionManifestAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default
    );
}
