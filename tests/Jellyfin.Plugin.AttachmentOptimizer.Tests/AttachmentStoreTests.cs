using System.Security.Cryptography;
using Jellyfin.Plugin.AttachmentOptimizer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AttachmentOptimizer.Tests;

public sealed class AttachmentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "attachment-optimizer-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task IdenticalFilesShareOneSha256Blob()
    {
        Directory.CreateDirectory(_root);
        var first = Path.Combine(_root, "first.ttf");
        var second = Path.Combine(_root, "second.otf");
        byte[] fontBytes = [0x00, 0x01, 0x00, 0x00, 0x53, 0x75, 0x62, 0x4d, 0x75, 0x78];
        await File.WriteAllBytesAsync(first, fontBytes);
        await File.WriteAllBytesAsync(second, fontBytes);
        var store = new AttachmentStore(_root, NullLogger<AttachmentStore>.Instance);

        var firstBlob = await store.ImportAsync(first, deleteSource: false, CancellationToken.None);
        var secondBlob = await store.ImportAsync(second, deleteSource: false, CancellationToken.None);

        Assert.Equal(firstBlob.Hash, secondBlob.Hash);
        Assert.Equal(firstBlob.Path, secondBlob.Path);
        Assert.Single(Directory.EnumerateFiles(store.BlobRootPath, "*", SearchOption.AllDirectories));
        Assert.EndsWith(".ttf", firstBlob.Path, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("objects", "sha256"), firstBlob.Path, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("00010000", ".ttf")]
    [InlineData("4F54544F", ".otf")]
    [InlineData("7474636600010000000000010000001000010000", ".ttc")]
    [InlineData("747463660001000000000001000000104F54544F", ".otc")]
    [InlineData("774F4646", ".woff")]
    [InlineData("774F4632", ".woff2")]
    [InlineData("8001", ".pfb")]
    [InlineData("5355424D5558", ".blob")]
    public async Task StoredExtensionMatchesDetectedAttachmentFormat(string headerHex, string expectedExtension)
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "extracted.attachment");
        await File.WriteAllBytesAsync(source, Convert.FromHexString(headerHex));
        var store = new AttachmentStore(_root, NullLogger<AttachmentStore>.Instance);

        var blob = await store.ImportAsync(source, deleteSource: false, CancellationToken.None);

        Assert.EndsWith(expectedExtension, blob.Path, StringComparison.Ordinal);
        Assert.Equal(blob.Path, store.FindBlobPath(blob.Hash));
    }
    [Fact]
    public void LegacyBlobLayoutIsMigratedAndTyped()
    {
        byte[] fontBytes = [0x00, 0x01, 0x00, 0x00, 0x53, 0x75, 0x62, 0x4d, 0x75, 0x78];
        var hash = Convert.ToHexStringLower(SHA256.HashData(fontBytes));
        var legacyPath = Path.Combine(_root, "attachment-optimizer", "blobs", hash[..2], hash);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllBytes(legacyPath, fontBytes);

        var store = new AttachmentStore(_root, NullLogger<AttachmentStore>.Instance);
        var migratedPath = store.FindBlobPath(hash);

        Assert.NotNull(migratedPath);
        Assert.EndsWith(".ttf", migratedPath, StringComparison.Ordinal);
        Assert.True(File.Exists(migratedPath));
        Assert.False(File.Exists(legacyPath));
    }

    [Fact]
    public async Task ManifestIsReusedOnlyForTheSameSourceFingerprint()
    {
        Directory.CreateDirectory(_root);
        var store = new AttachmentStore(_root, NullLogger<AttachmentStore>.Instance);
        var mediaSourceId = Guid.NewGuid().ToString("D");
        var manifest = new CacheManifest
        {
            MediaSourceId = mediaSourceId,
            SourceFingerprint = "fingerprint-one",
            Entries =
            {
                [2] = new CacheEntry
                {
                    AttachmentIndex = 2,
                    BlobHash = new string('a', 64),
                    LastAccessUtc = DateTime.UtcNow
                }
            }
        };
        await store.SaveManifestAsync(manifest, CancellationToken.None);

        var reused = await store.LoadManifestAsync(mediaSourceId, "fingerprint-one", CancellationToken.None);
        var invalidated = await store.LoadManifestAsync(mediaSourceId, "fingerprint-two", CancellationToken.None);

        Assert.Single(reused.Entries);
        Assert.Empty(invalidated.Entries);
        Assert.Equal("fingerprint-two", invalidated.SourceFingerprint);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
