using Jellyfin.Plugin.AttachmentOptimizer.Configuration;

namespace Jellyfin.Plugin.AttachmentOptimizer.Tests;

public sealed class PluginConfigurationTests
{
    [Fact]
    public void DefaultsFavorPerformanceWithoutEnablingDeletion()
    {
        var configuration = new PluginConfiguration();

        Assert.True(configuration.EnableBatchExtraction);
        Assert.True(configuration.EnableDeduplication);
        Assert.True(configuration.EnableHardLinks);
        Assert.False(configuration.EnableAutomaticCleanup);
        Assert.True(configuration.CleanupDryRun);
        Assert.Equal(72, configuration.CompatibilityFileRetentionHours);
        Assert.Equal(30, configuration.BlobRetentionDays);
        Assert.Equal(10, configuration.MaximumBlobCacheSizeGiB);
    }
}
