using Jellyfin.Plugin.AttachmentOptimizer.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AttachmentOptimizer.Providers;

/// <summary>
/// Extracts embedded subtitles while Jellyfin scans a library.
/// </summary>
public sealed class SubtitleExtractionProvider :
    ICustomMetadataProvider<Episode>,
    ICustomMetadataProvider<Movie>,
    ICustomMetadataProvider<Video>,
    IHasItemChangeMonitor,
    IHasOrder,
    IForcedProvider
{
    private readonly ILibraryManager _libraryManager;
    private readonly LibraryExtractionService _extractionService;
    private readonly ILogger<SubtitleExtractionProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleExtractionProvider"/> class.
    /// </summary>
    public SubtitleExtractionProvider(
        ILibraryManager libraryManager,
        LibraryExtractionService extractionService,
        ILogger<SubtitleExtractionProvider> logger)
    {
        _libraryManager = libraryManager;
        _extractionService = extractionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Attachment Optimizer - Subtitle Extraction";

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
            || !configuration.ExtractSubtitlesDuringLibraryScan
            || !ExtractionPolicy.IsLibrarySelected(
                item,
                configuration.SelectedSubtitleLibraries,
                _libraryManager))
        {
            return ItemUpdateType.None;
        }

        _logger.LogDebug("Extracting embedded subtitles during library scan for {Path}", item.Path);
        await _extractionService.ExtractSubtitlesAsync(
            item,
            configuration,
            cancellationToken).ConfigureAwait(false);
        return ItemUpdateType.None;
    }
}
