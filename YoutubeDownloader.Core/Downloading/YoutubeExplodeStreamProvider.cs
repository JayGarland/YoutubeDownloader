using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDownloader.Core.Utils;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.ClosedCaptions;
using YoutubeExplode.Videos.Streams;

namespace YoutubeDownloader.Core.Downloading;

/// <summary>
/// Production implementation that uses YoutubeExplode to fetch stream manifests.
/// </summary>
public class YoutubeExplodeStreamProvider : IStreamManifestProvider, IDisposable
{
    private readonly YoutubeClient _youtube;

    public YoutubeExplodeStreamProvider(IReadOnlyList<Cookie>? initialCookies = null)
    {
        _youtube = new YoutubeClient(Http.Client, initialCookies ?? []);
    }

    public async Task<StreamManifest> GetStreamManifestAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default
    )
    {
        return await _youtube.Videos.Streams.GetManifestAsync(videoId, cancellationToken);
    }

    public async Task<ClosedCaptionManifest> GetClosedCaptionManifestAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default
    )
    {
        return await _youtube.Videos.ClosedCaptions.GetManifestAsync(videoId, cancellationToken);
    }

    public void Dispose() => _youtube.Dispose();
}
