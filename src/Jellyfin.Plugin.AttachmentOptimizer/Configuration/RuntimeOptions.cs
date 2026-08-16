namespace Jellyfin.Plugin.AttachmentOptimizer.Configuration;

/// <summary>
/// Validated settings snapshot used by one operation.
/// </summary>
internal sealed record RuntimeOptions(
    bool EnableBatchExtraction,
    bool EnableDeduplication,
    bool EnableHardLinks,
    bool EnableAutomaticCleanup,
    bool CleanupDryRun,
    TimeSpan CompatibilityFileRetention,
    TimeSpan BlobRetention,
    long MaximumBlobCacheBytes)
{
    public static RuntimeOptions Current
    {
        get
        {
            var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            return new RuntimeOptions(
                configuration.EnableBatchExtraction,
                configuration.EnableDeduplication,
                configuration.EnableHardLinks,
                configuration.EnableAutomaticCleanup,
                configuration.CleanupDryRun,
                TimeSpan.FromHours(Math.Clamp(configuration.CompatibilityFileRetentionHours, 1, 24 * 365)),
                TimeSpan.FromDays(Math.Clamp(configuration.BlobRetentionDays, 1, 3650)),
                checked((long)Math.Clamp(configuration.MaximumBlobCacheSizeGiB, 1, 1024) * 1024 * 1024 * 1024));
        }
    }
}
