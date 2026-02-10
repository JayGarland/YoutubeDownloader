using System.IO;
using YoutubeExplode.Videos.Streams;

namespace YoutubeDownloader.Core.Downloading;

public record CompatibilityModeDownloadPlan(bool IsAudio, string AudioFormat)
{
    public static CompatibilityModeDownloadPlan FromSelection(
        VideoDownloadOption? selectedOption,
        VideoDownloadPreference? selectedPreference,
        string? reservedFilePath = null
    )
    {
        var container =
            selectedOption?.Container
            ?? selectedPreference?.PreferredContainer
            ?? TryGetContainerFromFilePath(reservedFilePath);

        var isAudioSelection =
            selectedOption?.IsAudioOnly == true
            || (container.HasValue && IsAudioContainer(container.Value));

        return new CompatibilityModeDownloadPlan(isAudioSelection, ResolveAudioFormat(container));
    }

    public static bool IsAudioContainer(Container container)
    {
        if (container.IsAudioOnly)
            return true;

        return container.Name.ToLowerInvariant() is "mp3" or "m4a" or "ogg" or "opus" or "vorbis";
    }

    public static string ResolveAudioFormat(Container? container)
    {
        if (!container.HasValue)
            return "m4a";

        return container.Value.Name.ToLowerInvariant() switch
        {
            "mp3" => "mp3",
            "m4a" => "m4a",
            "ogg" => "vorbis",
            "vorbis" => "vorbis",
            "opus" => "opus",
            "webm" => "opus",
            _ => "m4a",
        };
    }

    private static Container? TryGetContainerFromFilePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var extension = Path.GetExtension(filePath).TrimStart('.');
        return string.IsNullOrWhiteSpace(extension) ? null : new Container(extension);
    }
}
