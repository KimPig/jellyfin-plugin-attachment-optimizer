using Jellyfin.Plugin.AttachmentOptimizer.Configuration;

namespace Jellyfin.Plugin.AttachmentOptimizer.Tests;

public sealed class PluginConfigurationTests
{
    [Fact]
    public void DefaultsFavorPerformanceWithoutEnablingDeletion()
    {
        var configuration = new PluginConfiguration();

        Assert.False(configuration.ExtractSubtitlesDuringLibraryScan);
        Assert.False(configuration.PrecacheAttachmentsDuringLibraryScan);
        Assert.Empty(configuration.SelectedSubtitleLibraries);
        Assert.Empty(configuration.SelectedAttachmentLibraries);
        Assert.False(configuration.EnableAdvancedSubtitleCodecSelection);
        Assert.NotEmpty(configuration.SelectedSubtitleCodecs);
        Assert.Equal(configuration.AllSubtitleCodecs.Length, configuration.SelectedSubtitleCodecs.Length);
        Assert.True(configuration.EnableBatchExtraction);
        Assert.True(configuration.EnableDeduplication);
        Assert.True(configuration.EnableHardLinks);
        Assert.False(configuration.EnableAutomaticCleanup);
        Assert.True(configuration.CleanupDryRun);
        Assert.Equal(72, configuration.CompatibilityFileRetentionHours);
        Assert.Equal(30, configuration.BlobRetentionDays);
        Assert.Equal(10, configuration.MaximumBlobCacheSizeGiB);
    }
    [Fact]
    public void ExtractionSettingsRoundTripThroughXml()
    {
        var expected = new PluginConfiguration
        {
            ExtractSubtitlesDuringLibraryScan = true,
            PrecacheAttachmentsDuringLibraryScan = true,
            SelectedSubtitleLibraries = ["Anime"],
            SelectedAttachmentLibraries = ["Movies"],
            EnableAdvancedSubtitleCodecSelection = true,
            SelectedSubtitleCodecs = ["ass", "subrip"]
        };
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, expected);
        using var reader = new StringReader(writer.ToString());

        var actual = Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader));

        Assert.True(actual.ExtractSubtitlesDuringLibraryScan);
        Assert.True(actual.PrecacheAttachmentsDuringLibraryScan);
        Assert.Equal(["Anime"], actual.SelectedSubtitleLibraries);
        Assert.Equal(["Movies"], actual.SelectedAttachmentLibraries);
        Assert.True(actual.EnableAdvancedSubtitleCodecSelection);
        Assert.Equal(["ass", "subrip"], actual.SelectedSubtitleCodecs);
    }


    [Fact]
    public void LegacySubtitleGroupElementsDoNotBreakConfigurationLoading()
    {
        const string Xml = """
            <PluginConfiguration>
              <IncludeTextSubtitles>false</IncludeTextSubtitles>
              <IncludeGraphicalSubtitles>true</IncludeGraphicalSubtitles>
              <EnableAdvancedSubtitleCodecSelection>false</EnableAdvancedSubtitleCodecSelection>
            </PluginConfiguration>
            """;
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(PluginConfiguration));
        using var reader = new StringReader(Xml);

        var configuration = Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader));

        Assert.False(configuration.EnableAdvancedSubtitleCodecSelection);
        Assert.Equal(configuration.AllSubtitleCodecs.Length, configuration.SelectedSubtitleCodecs.Length);
    }
}
