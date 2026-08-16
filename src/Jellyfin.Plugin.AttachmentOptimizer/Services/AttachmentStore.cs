using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AttachmentOptimizer.Services;

internal sealed class AttachmentStore
{
    private static readonly string[] StoredExtensions =
        [".ttf", ".otf", ".ttc", ".otc", ".woff", ".woff2", ".pfb", ".blob"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ILogger<AttachmentStore> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _mediaLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _activeMediaSources =
        new(StringComparer.OrdinalIgnoreCase);

    public AttachmentStore(
        IApplicationPaths applicationPaths,
        ILogger<AttachmentStore> logger)
        : this(applicationPaths.DataPath, logger)
    {
    }

    internal AttachmentStore(string dataPath, ILogger<AttachmentStore> logger)
    {
        _logger = logger;
        RootPath = Path.Combine(dataPath, "attachment-optimizer");
        BlobRootPath = Path.Combine(RootPath, "objects", "sha256");
        ManifestRootPath = Path.Combine(RootPath, "media");
        WorkRootPath = Path.Combine(RootPath, "temp");
        AttachmentRootPath = Path.Combine(dataPath, "attachments");
    }

    public string RootPath { get; }

    public string BlobRootPath { get; }

    public string ManifestRootPath { get; }

    public string WorkRootPath { get; }

    public string AttachmentRootPath { get; }

    public async ValueTask<IDisposable> LockMediaSourceAsync(
        string mediaSourceId,
        CancellationToken cancellationToken)
    {
        var semaphore = _mediaLocks.GetOrAdd(mediaSourceId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreReleaser(semaphore);
    }

    public IDisposable AcquireLease(string mediaSourceId)
    {
        _activeMediaSources.AddOrUpdate(mediaSourceId, 1, static (_, count) => checked(count + 1));
        return new Lease(this, mediaSourceId);
    }

    public bool IsActive(string mediaSourceId) => _activeMediaSources.ContainsKey(mediaSourceId);

    public string CreateWorkDirectory(string mediaSourceId)
    {
        var safeId = GetSafeMediaSourceId(mediaSourceId);
        var path = Path.Combine(WorkRootPath, safeId + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetBlobPath(string hash, string extension)
    {
        if (!IsSha256(hash))
        {
            throw new ArgumentException("Blob hash must be a SHA-256 hexadecimal value.", nameof(hash));
        }

        var normalizedExtension = extension.StartsWith('.')
            ? extension.ToLowerInvariant()
            : "." + extension.ToLowerInvariant();
        if (!StoredExtensions.Contains(normalizedExtension, StringComparer.Ordinal))
        {
            throw new ArgumentException("Unsupported attachment storage extension.", nameof(extension));
        }

        return Path.Combine(BlobRootPath, hash[..2], hash + normalizedExtension);
    }


    public string? FindBlobPath(string hash)
    {
        if (!IsSha256(hash))
        {
            throw new ArgumentException("Blob hash must be a SHA-256 hexadecimal value.", nameof(hash));
        }

        var shardPath = Path.Combine(BlobRootPath, hash[..2]);
        foreach (var extension in StoredExtensions)
        {
            var candidate = Path.Combine(shardPath, hash + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
    public async Task<ImportedBlob> ImportAsync(
        string sourcePath,
        bool deleteSource,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var hash = Convert.ToHexStringLower(hashBytes);
        var size = stream.Length;
        await stream.DisposeAsync().ConfigureAwait(false);

        var extension = DetectAttachmentExtension(sourcePath);
        var blobPath = GetBlobPath(hash, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(blobPath)!);
        if (!File.Exists(blobPath))
        {
            var temporaryBlob = blobPath + ".import-" + Guid.NewGuid().ToString("N");
            try
            {
                if (deleteSource)
                {
                    File.Move(sourcePath, temporaryBlob, overwrite: false);
                }
                else
                {
                    File.Copy(sourcePath, temporaryBlob, overwrite: false);
                }

                try
                {
                    File.Move(temporaryBlob, blobPath, overwrite: false);
                }
                catch (IOException) when (File.Exists(blobPath))
                {
                    File.Delete(temporaryBlob);
                }
            }
            catch
            {
                TryDeleteFile(temporaryBlob);
                throw;
            }
        }

        if (deleteSource)
        {
            TryDeleteFile(sourcePath);
        }

        File.SetLastAccessTimeUtc(blobPath, DateTime.UtcNow);
        return new ImportedBlob(hash, blobPath, size);
    }

    public async Task<CacheManifest> LoadManifestAsync(
        string mediaSourceId,
        string sourceFingerprint,
        CancellationToken cancellationToken)
    {
        var path = GetManifestPath(mediaSourceId);
        if (File.Exists(path))
        {
            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    16384,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var manifest = await JsonSerializer.DeserializeAsync<CacheManifest>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                if (manifest is not null
                    && manifest.SourceFingerprint.Equals(sourceFingerprint, StringComparison.Ordinal))
                {
                    return manifest;
                }
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or JsonException)
            {
                _logger.LogWarning(exception, "Unable to read attachment manifest {ManifestPath}", path);
            }
        }

        return new CacheManifest
        {
            MediaSourceId = mediaSourceId,
            SourceFingerprint = sourceFingerprint
        };
    }

    public async Task SaveManifestAsync(
        CacheManifest manifest,
        CancellationToken cancellationToken)
    {
        var path = GetManifestPath(manifest.MediaSourceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".write-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16384,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    manifest,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    public async Task<IReadOnlyList<(string Path, CacheManifest Manifest)>> LoadAllManifestsAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(ManifestRootPath))
        {
            return [];
        }

        var result = new List<(string Path, CacheManifest Manifest)>();
        foreach (var path in Directory.EnumerateFiles(ManifestRootPath, "manifest.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    16384,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var manifest = await JsonSerializer.DeserializeAsync<CacheManifest>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                if (manifest is not null)
                {
                    result.Add((path, manifest));
                }
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or JsonException)
            {
                _logger.LogWarning(exception, "Unable to read attachment manifest {ManifestPath}", path);
            }
        }

        return result;
    }

    public string CreateSourceFingerprint(string inputPath)
    {
        var fullPath = Uri.TryCreate(inputPath, UriKind.Absolute, out var uri) && !uri.IsFile
            ? inputPath
            : Path.GetFullPath(inputPath);
        var builder = new StringBuilder(fullPath);
        if (File.Exists(inputPath))
        {
            var info = new FileInfo(inputPath);
            builder.Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public bool IsSafeCompatibilityPath(string path)
    {
        var root = Path.GetFullPath(AttachmentRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(path);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public void DeleteWorkDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(exception, "Unable to delete attachment work directory {WorkDirectory}", path);
        }
    }

    public int DeleteEmptyCompatibilityDirectories(
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(AttachmentRootPath))
        {
            return 0;
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(
                AttachmentRootPath,
                "*",
                SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "Unable to enumerate Jellyfin attachment cache directories under {AttachmentRootPath}",
                AttachmentRootPath);
            return 0;
        }

        var removed = 0;
        foreach (var path in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            if (!Guid.TryParse(directoryName, out var mediaSourceId)
                || IsActive(directoryName)
                || IsActive(mediaSourceId.ToString("D"))
                || IsActive(mediaSourceId.ToString("N")))
            {
                continue;
            }

            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                    || Directory.EnumerateFileSystemEntries(path).Any())
                {
                    continue;
                }

                if (dryRun)
                {
                    _logger.LogInformation(
                        "Cleanup dry run would remove empty attachment cache directory {Path}",
                        path);
                }
                else
                {
                    Directory.Delete(path, recursive: false);
                }

                removed++;
            }
            catch (DirectoryNotFoundException)
            {
                // Another cleanup or request removed the directory first.
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(
                    exception,
                    "Unable to remove empty attachment cache directory {Path}",
                    path);
            }
        }

        return removed;
    }

    private string GetManifestPath(string mediaSourceId) =>
        Path.Combine(ManifestRootPath, GetSafeMediaSourceId(mediaSourceId), "manifest.json");

    private static string DetectAttachmentExtension(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        Span<byte> header = stackalloc byte[16];
        var bytesRead = stream.Read(header);
        var data = header[..bytesRead];

        if (data.StartsWith(new byte[] { 0x00, 0x01, 0x00, 0x00 })
            || data.StartsWith("true"u8)
            || data.StartsWith("typ1"u8))
        {
            return ".ttf";
        }

        if (data.StartsWith("OTTO"u8))
        {
            return ".otf";
        }

        if (data.StartsWith("ttcf"u8))
        {
            if (bytesRead >= 16)
            {
                var firstFontOffset = BinaryPrimitives.ReadUInt32BigEndian(data[12..16]);
                if (firstFontOffset <= stream.Length - 4)
                {
                    stream.Position = firstFontOffset;
                    Span<byte> signature = stackalloc byte[4];
                    if (stream.Read(signature) == signature.Length && signature.SequenceEqual("OTTO"u8))
                    {
                        return ".otc";
                    }
                }
            }

            return ".ttc";
        }

        if (data.StartsWith("wOFF"u8))
        {
            return ".woff";
        }

        if (data.StartsWith("wOF2"u8))
        {
            return ".woff2";
        }

        if (data.StartsWith(new byte[] { 0x80, 0x01 }))
        {
            return ".pfb";
        }

        return ".blob";
    }

    private static string GetSafeMediaSourceId(string mediaSourceId)
    {
        if (!Guid.TryParse(mediaSourceId, out var id))
        {
            throw new ArgumentException("Media source id must be a GUID.", nameof(mediaSourceId));
        }

        return id.ToString("N");
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F');

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }

    private void ReleaseLease(string mediaSourceId)
    {
        while (true)
        {
            if (!_activeMediaSources.TryGetValue(mediaSourceId, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                if (_activeMediaSources.TryRemove(new KeyValuePair<string, int>(mediaSourceId, count)))
                {
                    return;
                }
            }
            else if (_activeMediaSources.TryUpdate(mediaSourceId, count - 1, count))
            {
                return;
            }
        }
    }

    private sealed class SemaphoreReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }

    private sealed class Lease(AttachmentStore owner, string mediaSourceId) : IDisposable
    {
        private AttachmentStore? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.ReleaseLease(mediaSourceId);
        }
    }
}
