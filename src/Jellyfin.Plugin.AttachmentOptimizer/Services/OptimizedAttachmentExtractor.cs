using System.Globalization;
using Jellyfin.Plugin.AttachmentOptimizer.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AttachmentOptimizer.Services;

/// <summary>
/// Batches attachment extraction and optionally stores content by SHA-256.
/// </summary>
internal sealed class OptimizedAttachmentExtractor : IAttachmentExtractor, IAttachmentPrecacheService
{
    private readonly ILogger<OptimizedAttachmentExtractor> _logger;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly IPathManager _pathManager;
    private readonly AttachmentStore _store;
    private readonly IAttachmentProcessRunner _processRunner;

    public OptimizedAttachmentExtractor(
        ILogger<OptimizedAttachmentExtractor> logger,
        IMediaSourceManager mediaSourceManager,
        IPathManager pathManager,
        AttachmentStore store,
        IAttachmentProcessRunner processRunner)
    {
        _logger = logger;
        _mediaSourceManager = mediaSourceManager;
        _pathManager = pathManager;
        _store = store;
        _processRunner = processRunner;
    }

    /// <inheritdoc />
    public async Task<(MediaAttachment Attachment, Stream Stream)> GetAttachment(
        BaseItem item,
        string mediaSourceId,
        int attachmentStreamIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaSourceId);

        var mediaSources = await _mediaSourceManager.GetPlaybackMediaSources(
            item,
            null,
            allowMediaProbe: true,
            enablePathSubstitution: false,
            cancellationToken).ConfigureAwait(false);
        var mediaSource = mediaSources.FirstOrDefault(source =>
            source.Id.Equals(mediaSourceId, StringComparison.OrdinalIgnoreCase));
        if (mediaSource is null)
        {
            throw new ResourceNotFoundException($"MediaSource {mediaSourceId} not found");
        }

        var attachment = mediaSource.MediaAttachments.FirstOrDefault(
            candidate => candidate.Index == attachmentStreamIndex);
        if (attachment is null)
        {
            throw new ResourceNotFoundException(
                $"MediaSource {mediaSourceId} has no attachment with stream index {attachmentStreamIndex}");
        }

        if (IsUnsupportedAttachment(attachment))
        {
            throw new ResourceNotFoundException(
                $"Attachment with stream index {attachmentStreamIndex} cannot be extracted for MediaSource {mediaSourceId}");
        }

        var options = RuntimeOptions.Current;
        var attachments = options.EnableBatchExtraction
            ? GetEligibleAttachments(mediaSource)
            : [attachment];
        var resolved = await EnsureAttachmentsAsync(
            mediaSource.Path,
            mediaSource,
            attachments,
            materializeCompatibilityFiles: false,
            options,
            cancellationToken).ConfigureAwait(false);
        if (!resolved.TryGetValue(attachment.Index, out var path) || !File.Exists(path))
        {
            throw new ResourceNotFoundException(
                $"Attachment with stream index {attachmentStreamIndex} could not be extracted for MediaSource {mediaSourceId}");
        }

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return (attachment, stream);
    }

    /// <inheritdoc />
    public async Task ExtractAllAttachments(
        string inputFile,
        MediaSourceInfo mediaSource,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputFile);
        ArgumentNullException.ThrowIfNull(mediaSource);

        if (_pathManager.GetAttachmentFolderPath(mediaSource.Id) is null)
        {
            _logger.LogDebug(
                "Skipping attachment extraction for {InputFile}: MediaSource id is not a GUID",
                inputFile);
            return;
        }

        var options = RuntimeOptions.Current;
        await EnsureAttachmentsAsync(
            inputFile,
            mediaSource,
            GetEligibleAttachments(mediaSource),
            materializeCompatibilityFiles: true,
            options,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PrecacheAllAttachmentsAsync(
        string inputFile,
        MediaSourceInfo mediaSource,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputFile);
        ArgumentNullException.ThrowIfNull(mediaSource);

        if (_pathManager.GetAttachmentFolderPath(mediaSource.Id) is null)
        {
            _logger.LogDebug(
                "Skipping attachment pre-cache for {InputFile}: MediaSource id is not a GUID",
                inputFile);
            return;
        }

        await EnsureAttachmentsAsync(
            inputFile,
            mediaSource,
            GetEligibleAttachments(mediaSource),
            materializeCompatibilityFiles: false,
            RuntimeOptions.Current,
            cancellationToken).ConfigureAwait(false);
    }


    private async Task<IReadOnlyDictionary<int, string>> EnsureAttachmentsAsync(
        string inputFile,
        MediaSourceInfo mediaSource,
        IReadOnlyList<MediaAttachment> attachments,
        bool materializeCompatibilityFiles,
        RuntimeOptions options,
        CancellationToken cancellationToken)
    {
        if (attachments.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        _ = _pathManager.GetAttachmentFolderPath(mediaSource.Id)
            ?? throw new ResourceNotFoundException(
                $"MediaSource {mediaSource.Id} has no attachment cache because its id is not a GUID.");
        using var lease = _store.AcquireLease(mediaSource.Id);
        using var mediaLock = await _store.LockMediaSourceAsync(
            mediaSource.Id,
            cancellationToken).ConfigureAwait(false);

        var sourceFingerprint = _store.CreateSourceFingerprint(inputFile);
        var manifest = options.EnableDeduplication
            ? await _store.LoadManifestAsync(mediaSource.Id, sourceFingerprint, cancellationToken)
                .ConfigureAwait(false)
            : null;
        var resolved = new Dictionary<int, string>();
        var missing = new List<AttachmentLocation>();
        var usedCompatibilityPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attachment in attachments)
        {
            var compatibilityPath = GetCompatibilityPath(mediaSource.Id, attachment, usedCompatibilityPaths);
            if (options.EnableDeduplication
                && manifest!.Entries.TryGetValue(attachment.Index, out var entry))
            {
                var blobPath = TryGetExistingBlob(entry.BlobHash);
                if (blobPath is not null)
                {
                    entry.LastAccessUtc = DateTime.UtcNow;
                    entry.CompatibilityPath = compatibilityPath;
                    if (materializeCompatibilityFiles || File.Exists(compatibilityPath))
                    {
                        Materialize(blobPath, compatibilityPath, options);
                    }

                    resolved[attachment.Index] = blobPath;
                    continue;
                }

                manifest.Entries.Remove(attachment.Index);
            }

            if (File.Exists(compatibilityPath))
            {
                if (options.EnableDeduplication)
                {
                    var imported = await _store.ImportAsync(
                        compatibilityPath,
                        deleteSource: false,
                        cancellationToken).ConfigureAwait(false);
                    manifest!.Entries[attachment.Index] = CreateEntry(
                        attachment.Index,
                        imported.Hash,
                        compatibilityPath);
                    if (options.EnableHardLinks || materializeCompatibilityFiles)
                    {
                        Materialize(imported.Path, compatibilityPath, options);
                    }

                    resolved[attachment.Index] = imported.Path;
                }
                else
                {
                    resolved[attachment.Index] = compatibilityPath;
                }

                continue;
            }

            missing.Add(new AttachmentLocation(attachment, compatibilityPath));
        }

        if (missing.Count > 0)
        {
            var workDirectory = _store.CreateWorkDirectory(mediaSource.Id);
            try
            {
                var targets = missing.Select(location => new ExtractionTarget(
                    location.Attachment,
                    Path.Combine(
                        workDirectory,
                        location.Attachment.Index.ToString(CultureInfo.InvariantCulture) + ".attachment")))
                    .ToArray();
                if (options.EnableBatchExtraction)
                {
                    await _processRunner.ExtractAsync(
                        inputFile,
                        mediaSource,
                        targets,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    foreach (var target in targets)
                    {
                        await _processRunner.ExtractAsync(
                            inputFile,
                            mediaSource,
                            [target],
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                for (var index = 0; index < missing.Count; index++)
                {
                    var location = missing[index];
                    var target = targets[index];
                    if (options.EnableDeduplication)
                    {
                        var imported = await _store.ImportAsync(
                            target.OutputPath,
                            deleteSource: true,
                            cancellationToken).ConfigureAwait(false);
                        manifest!.Entries[location.Attachment.Index] = CreateEntry(
                            location.Attachment.Index,
                            imported.Hash,
                            location.CompatibilityPath);
                        if (materializeCompatibilityFiles)
                        {
                            Materialize(imported.Path, location.CompatibilityPath, options);
                        }

                        resolved[location.Attachment.Index] = imported.Path;
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(location.CompatibilityPath)!);
                        File.Move(target.OutputPath, location.CompatibilityPath, overwrite: true);
                        resolved[location.Attachment.Index] = location.CompatibilityPath;
                    }
                }
            }
            finally
            {
                _store.DeleteWorkDirectory(workDirectory);
            }
        }

        if (manifest is not null)
        {
            await _store.SaveManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
        }

        return resolved;
    }

    private string GetCompatibilityPath(
        string mediaSourceId,
        MediaAttachment attachment,
        ISet<string> usedPaths)
    {
        var indexName = attachment.Index.ToString(CultureInfo.InvariantCulture);
        var candidate = _pathManager.GetAttachmentPath(
            mediaSourceId,
            string.IsNullOrWhiteSpace(attachment.FileName) ? indexName : attachment.FileName)
            ?? _pathManager.GetAttachmentPath(mediaSourceId, indexName)
            ?? throw new ResourceNotFoundException($"MediaSource {mediaSourceId} has no attachment cache.");

        if (!usedPaths.Add(candidate))
        {
            candidate = _pathManager.GetAttachmentPath(mediaSourceId, indexName)
                ?? throw new ResourceNotFoundException($"MediaSource {mediaSourceId} has no attachment cache.");
            usedPaths.Add(candidate);
        }

        return candidate;
    }

    private string? TryGetExistingBlob(string hash)
    {
        try
        {
            return _store.FindBlobPath(hash);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void Materialize(
        string blobPath,
        string compatibilityPath,
        RuntimeOptions options)
    {
        var hardLinked = HardLinkHelper.Materialize(
            blobPath,
            compatibilityPath,
            options.EnableHardLinks,
            _logger);
        if (options.EnableHardLinks && !hardLinked)
        {
            _logger.LogWarning(
                "Hard-link creation failed for {CompatibilityPath}; a verified copy was used instead",
                compatibilityPath);
        }
    }

    private static CacheEntry CreateEntry(
        int attachmentIndex,
        string hash,
        string compatibilityPath) =>
        new()
        {
            AttachmentIndex = attachmentIndex,
            BlobHash = hash,
            CompatibilityPath = compatibilityPath,
            LastAccessUtc = DateTime.UtcNow
        };

    private static IReadOnlyList<MediaAttachment> GetEligibleAttachments(MediaSourceInfo mediaSource) =>
        mediaSource.MediaAttachments.Where(static attachment => !IsUnsupportedAttachment(attachment)).ToArray();

    private static bool IsUnsupportedAttachment(MediaAttachment attachment) =>
        attachment.Codec?.Equals("mjpeg", StringComparison.OrdinalIgnoreCase) == true;

    private sealed record AttachmentLocation(
        MediaAttachment Attachment,
        string CompatibilityPath);
}
