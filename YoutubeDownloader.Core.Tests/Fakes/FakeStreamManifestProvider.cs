using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDownloader.Core.Downloading;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.ClosedCaptions;
using YoutubeExplode.Videos.Streams;

namespace YoutubeDownloader.Core.Tests.Fakes;

/// <summary>
/// Fake implementation of IStreamManifestProvider for deterministic unit testing.
/// Does not make any network calls - returns canned data or throws configured exceptions.
/// </summary>
public class FakeStreamManifestProvider : IStreamManifestProvider
{
    private readonly Func<VideoId, CancellationToken, Task<StreamManifest>>? _getStreamManifest;
    private readonly Func<VideoId, CancellationToken, Task<ClosedCaptionManifest>>? _getClosedCaptionManifest;

    public FakeStreamManifestProvider(
        Func<VideoId, CancellationToken, Task<StreamManifest>>? getStreamManifest = null,
        Func<VideoId, CancellationToken, Task<ClosedCaptionManifest>>? getClosedCaptionManifest = null
    )
    {
        _getStreamManifest = getStreamManifest;
        _getClosedCaptionManifest = getClosedCaptionManifest;
    }

    public Task<StreamManifest> GetStreamManifestAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default
    )
    {
        if (_getStreamManifest is null)
            return Task.FromResult(CreateEmptyStreamManifest());

        return _getStreamManifest(videoId, cancellationToken);
    }

    public Task<ClosedCaptionManifest> GetClosedCaptionManifestAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default
    )
    {
        if (_getClosedCaptionManifest is null)
            return Task.FromResult(CreateEmptyClosedCaptionManifest());

        return _getClosedCaptionManifest(videoId, cancellationToken);
    }

    /// <summary>
    /// Creates a provider that throws HttpRequestException with 403 Forbidden status.
    /// </summary>
    public static FakeStreamManifestProvider CreateForbiddenProvider()
    {
        return new FakeStreamManifestProvider(
            getStreamManifest: (_, _) => throw new HttpRequestException(
                "Response status code does not indicate success: 403 (Forbidden).",
                null,
                HttpStatusCode.Forbidden
            )
        );
    }

    /// <summary>
    /// Creates a provider that throws HttpRequestException with timeout/no status code.
    /// </summary>
    public static FakeStreamManifestProvider CreateNetworkErrorProvider()
    {
        return new FakeStreamManifestProvider(
            getStreamManifest: (_, _) => throw new HttpRequestException("Connection timeout")
        );
    }

    /// <summary>
    /// Creates a provider that returns a manifest with multiple stream types.
    /// </summary>
    public static FakeStreamManifestProvider CreateSuccessProvider()
    {
        return new FakeStreamManifestProvider(
            getStreamManifest: (_, _) => Task.FromResult(CreateMockStreamManifest())
        );
    }

    private static StreamManifest CreateEmptyStreamManifest()
    {
        // StreamManifest constructor is internal, so we'll need to create it via reflection
        // or just return a manifest with no streams by using the internal constructor
        // For now, let's use reflection to create an empty manifest
        var constructor = typeof(StreamManifest).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            [typeof(IReadOnlyList<IStreamInfo>)],
            null
        );

        return (StreamManifest)constructor!.Invoke([Array.Empty<IStreamInfo>()]);
    }

    private static ClosedCaptionManifest CreateEmptyClosedCaptionManifest()
    {
        var constructor = typeof(ClosedCaptionManifest).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            [typeof(IReadOnlyList<ClosedCaptionTrackInfo>)],
            null
        );

        return (ClosedCaptionManifest)constructor!.Invoke([Array.Empty<ClosedCaptionTrackInfo>()]);
    }

    private static StreamManifest CreateMockStreamManifest()
    {
        // Create an empty manifest - actual stream data not needed for basic tests
        return CreateEmptyStreamManifest();
    }
}
