using Jellyfin.Plugin.AttachmentOptimizer.Configuration;
using Jellyfin.Plugin.AttachmentOptimizer.Services;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AttachmentOptimizer.Tests;

public sealed class ExtractionPolicyTests
{
    [Fact]
    public void UnrestrictedModeAcceptsAnyEmbeddedSubtitleMix()
    {
        var source = SourceWithSubtitles("ass", "PGSSUB", "unknown");

        Assert.True(ExtractionPolicy.ShouldExtractSubtitles(
            source,
            new PluginConfiguration()));
    }

    [Fact]
    public void RestrictedModeRequiresEveryEmbeddedSubtitleCodecToMatch()
    {
        var source = SourceWithSubtitles("ass", "subrip");
        var configuration = new PluginConfiguration
        {
            EnableAdvancedSubtitleCodecSelection = true,
            SelectedSubtitleCodecs = ["ass"]
        };

        Assert.False(ExtractionPolicy.ShouldExtractSubtitles(source, configuration));

        configuration.SelectedSubtitleCodecs = ["ASS", "SUBRIP"];

        Assert.True(ExtractionPolicy.ShouldExtractSubtitles(source, configuration));
    }

    [Fact]
    public void RestrictedModeWithNoSelectedCodecsExtractsNothing()
    {
        var configuration = new PluginConfiguration
        {
            EnableAdvancedSubtitleCodecSelection = true,
            SelectedSubtitleCodecs = []
        };

        Assert.False(ExtractionPolicy.ShouldExtractSubtitles(
            SourceWithSubtitles("ass"),
            configuration));
    }

    [Fact]
    public void ExternalSubtitleStreamsDoNotTriggerExtraction()
    {
        var source = new MediaSourceInfo
        {
            MediaStreams =
            [
                new MediaStream
                {
                    Type = MediaStreamType.Subtitle,
                    Codec = "ass",
                    IsExternal = true
                }
            ]
        };

        Assert.False(ExtractionPolicy.ShouldExtractSubtitles(
            source,
            new PluginConfiguration()));
    }

    private static MediaSourceInfo SourceWithSubtitles(params string[] codecs) =>
        new()
        {
            MediaStreams = codecs
                .Select(codec => new MediaStream
                {
                    Type = MediaStreamType.Subtitle,
                    Codec = codec
                })
                .ToList()
        };
}
