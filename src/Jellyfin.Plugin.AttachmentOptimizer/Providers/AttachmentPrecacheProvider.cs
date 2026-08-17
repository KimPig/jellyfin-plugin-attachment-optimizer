using Jellyfin.Plugin.AttachmentOptimizer.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AttachmentOptimizer.Providers;

/// <summary>
/// Pre-caches embedded attachments while Jellyfin scans a library.
/// </summary>
public sealed class AttachmentPrecacheProvider :
    ICustomMetadataProvider<Episode>,
    ICustomMetadataProvider<Movie>,
    ICustomMetadataProvider<Video>,
    IHasItemChangeMonitor,
    IHasOrder,
    IForcedProvider
{
    private readonly ILibraryManager _libraryManager;
    private readonly LibraryExtractionService _extractionService;
    private readonly ILogger<AttachmentPrecacheProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AttachmentPrecacheProvider"/> class.
    /// </summary>
    public AttachmentPrecacheProvider(
        ILibraryManager libraryManager,
        LibraryExtractionService extractionService,
        ILogger<AttachmentPrecacheProvider> logger)
    {
        _libraryManager = libraryManager;
        _extractionService = extractionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Attachment Optimizer - Embedded Attachment Extraction";

    /// <inheritdoc />
    public int Order => 1000;

    /// <inheritdoc />
    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        if (!item.IsFileProtocol)
        {
            return false;
        }

        var file = directoryService.GetFile(item.Path);
        return file is not null && item.HasChanged(file.LastWriteTimeUtc);
    }

    /// <inheritdoc />
    public Task<ItemUpdateType> FetchAsync(
        Episode item,
        MetadataRefreshOptions options,
        CancellationToken cancellationToken) =>
        FetchAsync((BaseItem)item, cancellationToken);

    /// <inheritdoc />
    public Task<ItemUpdateType> FetchAsync(
        Movie item,
        MetadataRefreshOptions options,
        CancellationToken cancellationToken) =>
        FetchAsync((BaseItem)item, cancellationToken);

    /// <inheritdoc />
    public Task<ItemUpdateType> FetchAsync(
        Video item,
        MetadataRefreshOptions options,
        CancellationToken cancellationToken) =>
        FetchAsync((BaseItem)item, cancellationToken);

    private async Task<ItemUpdateType> FetchAsync(
        BaseItem item,
        CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null
            || !configuration.PrecacheAttachmentsDuringLibraryScan
            || !ExtractionPolicy.IsLibrarySelected(
                item,
                configuration.SelectedAttachmentLibraries,
                _libraryManager))
        {
            return ItemUpdateType.None;
        }

        _logger.LogDebug("Pre-caching attachments during library scan for {Path}", item.Path);
        await _extractionService.PrecacheAttachmentsAsync(
            item,
            cancellationToken).ConfigureAwait(false);
        return ItemUpdateType.None;
    }
}
