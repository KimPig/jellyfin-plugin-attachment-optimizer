using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AttachmentOptimizer.Services;

/// <summary>
/// Enumerates video library items for extraction scheduled tasks.
/// </summary>
internal sealed class LibraryExtractionTaskRunner
{
    private const int QueryPageSize = 250;

    private static readonly BaseItemKind[] _itemTypes =
    [
        BaseItemKind.Episode,
        BaseItemKind.Movie
    ];

    private static readonly MediaType[] _mediaTypes = [MediaType.Video];
    private static readonly SourceType[] _sourceTypes = [SourceType.Library];
    private static readonly DtoOptions _dtoOptions = new(false);

    private readonly ILibraryManager _libraryManager;

    public LibraryExtractionTaskRunner(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    public async Task RunAsync(
        IReadOnlyCollection<string>? selectedLibraries,
        bool requireSubtitles,
        Func<BaseItem, CancellationToken, Task> action,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(progress);

        var query = new InternalItemsQuery
        {
            Recursive = true,
            HasSubtitles = requireSubtitles ? true : null,
            IsVirtualItem = false,
            IncludeItemTypes = _itemTypes,
            DtoOptions = _dtoOptions,
            MediaTypes = _mediaTypes,
            SourceTypes = _sourceTypes,
            Limit = QueryPageSize
        };
        var itemCount = _libraryManager.GetCount(query);
        if (itemCount == 0)
        {
            progress.Report(100);
            return;
        }

        var processed = 0;
        for (var startIndex = 0; startIndex < itemCount; startIndex += QueryPageSize)
        {
            query.StartIndex = startIndex;
            foreach (var item in _libraryManager.GetItemList(query))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ExtractionPolicy.IsLibrarySelected(
                    item,
                    selectedLibraries,
                    _libraryManager))
                {
                    await action(item, cancellationToken).ConfigureAwait(false);
                }

                processed++;
                progress.Report(Math.Min(100d, 100d * processed / itemCount));
            }
        }

        progress.Report(100);
    }
}
