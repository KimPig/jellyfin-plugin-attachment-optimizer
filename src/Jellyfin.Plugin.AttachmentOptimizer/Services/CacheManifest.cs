namespace Jellyfin.Plugin.AttachmentOptimizer.Services;

internal sealed class CacheManifest
{
    public string MediaSourceId { get; set; } = string.Empty;

    public string SourceFingerprint { get; set; } = string.Empty;

    public Dictionary<int, CacheEntry> Entries { get; set; } = [];
}

internal sealed class CacheEntry
{
    public int AttachmentIndex { get; set; }

    public string BlobHash { get; set; } = string.Empty;

    public string CompatibilityPath { get; set; } = string.Empty;

    public DateTime LastAccessUtc { get; set; }
}

internal sealed record ImportedBlob(
    string Hash,
    string Path,
    long Size);
