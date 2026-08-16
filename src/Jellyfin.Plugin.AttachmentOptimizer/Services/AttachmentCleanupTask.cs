using Jellyfin.Plugin.AttachmentOptimizer.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AttachmentOptimizer.Services;

/// <summary>
/// Cleans cache data managed by Attachment Optimizer.
/// </summary>
internal sealed class AttachmentCleanupTask : IScheduledTask
{
    private readonly ILogger<AttachmentCleanupTask> _logger;
    private readonly AttachmentStore _store;

    public AttachmentCleanupTask(
        ILogger<AttachmentCleanupTask> logger,
        AttachmentStore store)
    {
        _logger = logger;
        _store = store;
    }

    public string Name => "Clean Attachment Optimizer Cache";

    public string Key => "AttachmentOptimizerCleanup";

    public string Description =>
        "Removes expired cache files and empty Jellyfin attachment cache directories.";

    public string Category => "Maintenance";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(24).Ticks
        };
    }

    public async Task ExecuteAsync(
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var options = RuntimeOptions.Current;
        if (!options.EnableAutomaticCleanup)
        {
            _logger.LogInformation("Attachment Optimizer cleanup is disabled");
            progress.Report(100);
            return;
        }

        var now = DateTime.UtcNow;
        var manifests = (await _store.LoadAllManifestsAsync(cancellationToken).ConfigureAwait(false))
            .Select(static item => new ManifestState(item.Path, item.Manifest))
            .ToArray();
        var removedCompatibilityFiles = 0;
        var removedBlobs = 0;
        var removedBlobPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long reclaimedBytes = 0;

        for (var manifestIndex = 0; manifestIndex < manifests.Length; manifestIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = manifests[manifestIndex];
            if (_store.IsActive(state.Manifest.MediaSourceId))
            {
                continue;
            }

            foreach (var entry in state.Manifest.Entries.Values.ToArray())
            {
                if (now - entry.LastAccessUtc >= options.CompatibilityFileRetention
                    && TryDeleteCompatibility(entry, options.CleanupDryRun, out var compatibilitySize))
                {
                    removedCompatibilityFiles++;
                    reclaimedBytes += compatibilitySize;
                }

                if (now - entry.LastAccessUtc >= options.BlobRetention)
                {
                    state.Manifest.Entries.Remove(entry.AttachmentIndex);
                    state.Changed = true;
                }
            }

            progress.Report(manifests.Length == 0 ? 40 : 40d * (manifestIndex + 1) / manifests.Length);
        }

        var lastAccessByHash = manifests
            .SelectMany(static state => state.Manifest.Entries.Values)
            .GroupBy(static entry => entry.BlobHash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Max(static entry => entry.LastAccessUtc),
                StringComparer.OrdinalIgnoreCase);
        var activeHashes = manifests
            .Where(state => _store.IsActive(state.Manifest.MediaSourceId))
            .SelectMany(static state => state.Manifest.Entries.Values)
            .Select(static entry => entry.BlobHash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blobs = EnumerateBlobs().ToArray();

        foreach (var blob in blobs.Where(blob => !lastAccessByHash.ContainsKey(blob.Hash)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (now - blob.LastWriteTimeUtc < options.BlobRetention)
            {
                continue;
            }

            if (TryDeleteBlob(blob, options.CleanupDryRun))
            {
                removedBlobs++;
                removedBlobPaths.Add(blob.Path);
                reclaimedBytes += blob.Size;
            }
        }

        var existingBlobs = blobs
            .Where(blob => !removedBlobPaths.Contains(blob.Path) && File.Exists(blob.Path))
            .ToArray();
        var totalBlobBytes = existingBlobs.Sum(static blob => blob.Size);
        if (totalBlobBytes > options.MaximumBlobCacheBytes)
        {
            var evictionCandidates = existingBlobs
                .Where(blob => !activeHashes.Contains(blob.Hash))
                .OrderBy(blob => lastAccessByHash.GetValueOrDefault(blob.Hash, blob.LastWriteTimeUtc))
                .ThenBy(static blob => blob.Hash, StringComparer.OrdinalIgnoreCase);
            foreach (var blob in evictionCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (totalBlobBytes <= options.MaximumBlobCacheBytes)
                {
                    break;
                }

                foreach (var state in manifests)
                {
                    if (_store.IsActive(state.Manifest.MediaSourceId))
                    {
                        continue;
                    }

                    foreach (var entry in state.Manifest.Entries.Values
                                 .Where(entry => entry.BlobHash.Equals(blob.Hash, StringComparison.OrdinalIgnoreCase))
                                 .ToArray())
                    {
                        TryDeleteCompatibility(entry, options.CleanupDryRun, out _);
                        state.Manifest.Entries.Remove(entry.AttachmentIndex);
                        state.Changed = true;
                    }
                }

                if (TryDeleteBlob(blob, options.CleanupDryRun))
                {
                    removedBlobs++;
                    reclaimedBytes += blob.Size;
                }

                totalBlobBytes -= blob.Size;
            }
        }

        if (!options.CleanupDryRun)
        {
            foreach (var state in manifests.Where(static state => state.Changed))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _store.SaveManifestAsync(state.Manifest, cancellationToken).ConfigureAwait(false);
            }
        }

        var removedEmptyDirectories = _store.DeleteEmptyCompatibilityDirectories(
            options.CleanupDryRun,
            cancellationToken);

        progress.Report(100);
        _logger.LogInformation(
            "Attachment Optimizer cleanup {Mode}: {CompatibilityCount} compatibility files, {BlobCount} blobs, {EmptyDirectoryCount} empty directories, {ReclaimedBytes} bytes",
            options.CleanupDryRun ? "dry run" : "completed",
            removedCompatibilityFiles,
            removedBlobs,
            removedEmptyDirectories,
            reclaimedBytes);
    }

    private IEnumerable<BlobInfo> EnumerateBlobs()
    {
        if (!Directory.Exists(_store.BlobRootPath))
        {
            return [];
        }

        return Directory.EnumerateFiles(_store.BlobRootPath, "*", SearchOption.AllDirectories)
            .Select(static path => new FileInfo(path))
            .Where(static info => Path.GetFileNameWithoutExtension(info.Name) is { Length: 64 } hash
                                  && hash.All(static character =>
                                      character is >= '0' and <= '9'
                                      or >= 'a' and <= 'f'
                                      or >= 'A' and <= 'F'))
            .Select(static info => new BlobInfo(
                Path.GetFileNameWithoutExtension(info.Name),
                info.FullName,
                info.Length,
                info.LastWriteTimeUtc))
            .ToArray();
    }

    private bool TryDeleteCompatibility(
        CacheEntry entry,
        bool dryRun,
        out long size)
    {
        size = 0;
        if (string.IsNullOrWhiteSpace(entry.CompatibilityPath)
            || !_store.IsSafeCompatibilityPath(entry.CompatibilityPath)
            || !File.Exists(entry.CompatibilityPath))
        {
            return false;
        }

        size = new FileInfo(entry.CompatibilityPath).Length;
        if (dryRun)
        {
            _logger.LogInformation(
                "Cleanup dry run would remove compatibility file {Path}",
                entry.CompatibilityPath);
            return true;
        }

        try
        {
            File.Delete(entry.CompatibilityPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Unable to remove compatibility file {Path}", entry.CompatibilityPath);
            return false;
        }
    }

    private bool TryDeleteBlob(BlobInfo blob, bool dryRun)
    {
        if (dryRun)
        {
            _logger.LogInformation("Cleanup dry run would remove blob {Hash}", blob.Hash);
            return true;
        }

        try
        {
            File.Delete(blob.Path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Unable to remove attachment blob {Hash}", blob.Hash);
            return false;
        }
    }

    private sealed class ManifestState(string path, CacheManifest manifest)
    {
        public string Path { get; } = path;

        public CacheManifest Manifest { get; } = manifest;

        public bool Changed { get; set; }
    }

    private sealed record BlobInfo(
        string Hash,
        string Path,
        long Size,
        DateTime LastWriteTimeUtc);
}
