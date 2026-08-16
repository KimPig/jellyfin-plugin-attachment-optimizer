using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AttachmentOptimizer.Configuration;

/// <summary>
/// Attachment Optimizer settings.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether missing attachments are extracted in one FFmpeg invocation.
    /// </summary>
    public bool EnableBatchExtraction { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether identical attachment bytes are stored once by SHA-256.
    /// </summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether burn-in compatibility files use hard links when possible.
    /// </summary>
    public bool EnableHardLinks { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the daily cleanup task may remove plugin-managed cache data.
    /// </summary>
    public bool EnableAutomaticCleanup { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether cleanup only reports proposed changes.
    /// </summary>
    public bool CleanupDryRun { get; set; } = true;

    /// <summary>
    /// Gets or sets the retention period for per-media compatibility files.
    /// </summary>
    public int CompatibilityFileRetentionHours { get; set; } = 72;

    /// <summary>
    /// Gets or sets the retention period for unused content-addressed blobs.
    /// </summary>
    public int BlobRetentionDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets the maximum content-addressed cache size in GiB.
    /// </summary>
    public int MaximumBlobCacheSizeGiB { get; set; } = 10;
}
