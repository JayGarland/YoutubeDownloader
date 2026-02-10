using FluentAssertions;
using YoutubeDownloader.Core.Downloading;
using YoutubeExplode.Videos.Streams;
using Xunit;

namespace YoutubeDownloader.Core.Tests.Downloading;

public class CompatibilityModeDownloadPlanTests
{
    [Fact]
    public void FromSelection_WithMp3Preference_ShouldSelectAudioWithMp3Format()
    {
        // Arrange
        var preference = new VideoDownloadPreference(new Container("mp3"), VideoQualityPreference.Highest);

        // Act
        var plan = CompatibilityModeDownloadPlan.FromSelection(
            selectedOption: null,
            selectedPreference: preference
        );

        // Assert
        plan.IsAudio.Should().BeTrue();
        plan.AudioFormat.Should().Be("mp3");
    }

    [Fact]
    public void FromSelection_WithMp4Preference_ShouldSelectVideo()
    {
        // Arrange
        var preference = new VideoDownloadPreference(Container.Mp4, VideoQualityPreference.Highest);

        // Act
        var plan = CompatibilityModeDownloadPlan.FromSelection(
            selectedOption: null,
            selectedPreference: preference
        );

        // Assert
        plan.IsAudio.Should().BeFalse();
        plan.AudioFormat.Should().Be("m4a");
    }
}
