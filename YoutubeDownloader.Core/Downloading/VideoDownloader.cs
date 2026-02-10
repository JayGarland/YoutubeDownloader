using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Gress;
using YoutubeDownloader.Core.Utils;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.ClosedCaptions;

namespace YoutubeDownloader.Core.Downloading;

public class VideoDownloader : IDisposable
{
    private readonly YoutubeClient _youtube;
    private readonly IStreamManifestProvider _streamProvider;
    private readonly bool _ownsStreamProvider;

    public VideoDownloader(
        IReadOnlyList<Cookie>? initialCookies = null,
        IStreamManifestProvider? streamProvider = null
    )
    {
        _youtube = new YoutubeClient(Http.Client, initialCookies ?? []);

        if (streamProvider is not null)
        {
            _streamProvider = streamProvider;
            _ownsStreamProvider = false;
        }
        else
        {
            _streamProvider = new YoutubeExplodeStreamProvider(initialCookies);
            _ownsStreamProvider = true;
        }
    }

    public async Task<IReadOnlyList<VideoDownloadOption>> GetDownloadOptionsAsync(
        VideoId videoId,
        bool includeLanguageSpecificAudioStreams = true,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var manifest = await _streamProvider.GetStreamManifestAsync(videoId, cancellationToken);
            return VideoDownloadOption.ResolveAll(manifest, includeLanguageSpecificAudioStreams);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new DownloadBlockedException(
                "YouTube refused the request (403 Forbidden). This may be due to rate limiting or regional restrictions. Try again later or check your network settings.",
                ex,
                HttpStatusCode.Forbidden
            );
        }
    }

    public async Task<VideoDownloadOption> GetBestDownloadOptionAsync(
        VideoId videoId,
        VideoDownloadPreference preference,
        bool includeLanguageSpecificAudioStreams = true,
        CancellationToken cancellationToken = default
    )
    {
        var options = await GetDownloadOptionsAsync(
            videoId,
            includeLanguageSpecificAudioStreams,
            cancellationToken
        );

        return preference.TryGetBestOption(options)
            ?? throw new InvalidOperationException("No suitable download option found.");
    }

    public async Task DownloadVideoAsync(
        string filePath,
        IVideo video,
        VideoDownloadOption downloadOption,
        bool includeSubtitles = true,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        // Include subtitles in the output container
        var trackInfos = new List<ClosedCaptionTrackInfo>();
        if (includeSubtitles && !downloadOption.Container.IsAudioOnly)
        {
            try
            {
                var manifest = await _streamProvider.GetClosedCaptionManifestAsync(
                    video.Id,
                    cancellationToken
                );

                trackInfos.AddRange(manifest.Tracks);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                // Captions are non-critical - continue download without them
                // The 403 on captions shouldn't fail the entire download
            }
        }

        var dirPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dirPath))
            Directory.CreateDirectory(dirPath);

        await _youtube.Videos.DownloadAsync(
            downloadOption.StreamInfos,
            trackInfos,
            new ConversionRequestBuilder(filePath)
                .SetFFmpegPath(FFmpeg.TryGetCliFilePath() ?? "ffmpeg")
                .SetContainer(downloadOption.Container)
                .SetPreset(ConversionPreset.Medium)
                .Build(),
            progress?.ToDoubleBased(),
            cancellationToken
        );
    }

    public void Dispose()
    {
        _youtube.Dispose();

        if (_ownsStreamProvider && _streamProvider is IDisposable disposable)
            disposable.Dispose();
    }
}
