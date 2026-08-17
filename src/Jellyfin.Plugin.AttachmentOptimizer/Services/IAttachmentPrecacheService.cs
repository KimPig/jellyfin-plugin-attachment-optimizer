using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.AttachmentOptimizer.Services;

/// <summary>
/// Pre-caches attachments in the optimizer store without creating Jellyfin compatibility files.
/// </summary>
public interface IAttachmentPrecacheService
{
    /// <summary>
    /// Pre-caches all supported attachments for one media source.
    /// </summary>
    Task PrecacheAllAttachmentsAsync(
        string inputFile,
        MediaSourceInfo mediaSource,
        CancellationToken cancellationToken);
}
