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

    [Fact]
    public void EmptyCompatibilityDirectoriesAreRemovedSafely()
    {
        var store = new AttachmentStore(_root, NullLogger<AttachmentStore>.Instance);
        var emptyId = Guid.NewGuid().ToString("N");
        var activeId = Guid.NewGuid().ToString("N");
        var nonEmptyId = Guid.NewGuid().ToString("N");
        var unrelatedName = "keep-me";
        var emptyPath = Path.Combine(store.AttachmentRootPath, emptyId);
        var activePath = Path.Combine(store.AttachmentRootPath, activeId);
        var nonEmptyPath = Path.Combine(store.AttachmentRootPath, nonEmptyId);
        var unrelatedPath = Path.Combine(store.AttachmentRootPath, unrelatedName);
        Directory.CreateDirectory(emptyPath);
        Directory.CreateDirectory(activePath);
        Directory.CreateDirectory(nonEmptyPath);
        Directory.CreateDirectory(unrelatedPath);
        File.WriteAllText(Path.Combine(nonEmptyPath, "font.ttf"), "font");
        using var lease = store.AcquireLease(activeId);

        var removed = store.DeleteEmptyCompatibilityDirectories(
            dryRun: false,
            CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(emptyPath));
        Assert.True(Directory.Exists(activePath));
        Assert.True(Directory.Exists(nonEmptyPath));
        Assert.True(File.Exists(Path.Combine(nonEmptyPath, "font.ttf")));
        Assert.True(Directory.Exists(unrelatedPath));
    }

    [Fact]
    public void CleanupDryRunKeepsEmptyCompatibilityDirectories()
    {
        var store = new AttachmentStore(_root, NullLogger<AttachmentStore>.Instance);
        var emptyPath = Path.Combine(
            store.AttachmentRootPath,
            Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(emptyPath);

        var removed = store.DeleteEmptyCompatibilityDirectories(
            dryRun: true,
            CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.True(Directory.Exists(emptyPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
