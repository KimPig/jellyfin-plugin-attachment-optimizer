using Jellyfin.Plugin.AttachmentOptimizer.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AttachmentOptimizer.Services;

/// <summary>
/// Runs subtitle extraction and attachment pre-caching for library items.
/// </summary>
public sealed class LibraryExtractionService
{
    private readonly ISubtitleEncoder _subtitleEncoder;
    private readonly IAttachmentPrecacheService _attachmentPrecacheService;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryExtractionService"/> class.
    /// </summary>
    public LibraryExtractionService(
        ISubtitleEncoder subtitleEncoder,
        IAttachmentPrecacheService attachmentPrecacheService)
    {
        _subtitleEncoder = subtitleEncoder;
        _attachmentPrecacheService = attachmentPrecacheService;
    }

    /// <summary>
    /// Extracts configured embedded subtitles for one item.
    /// </summary>
    public async Task ExtractSubtitlesAsync(
        BaseItem item,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(configuration);

        foreach (var mediaSource in item.GetMediaSources(false)
                     .Where(source => ExtractionPolicy.ShouldExtractSubtitles(source, configuration)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _subtitleEncoder.ExtractAllExtractableSubtitles(
                mediaSource,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Pre-caches attachments for one item without materializing compatibility files.
    /// </summary>
    public async Task PrecacheAttachmentsAsync(
        BaseItem item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        foreach (var mediaSource in item.GetMediaSources(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var subtitleStreams = mediaSource.MediaStreams
                .Where(static stream => stream.Type == MediaStreamType.Subtitle)
                .ToArray();
            var mksPaths = subtitleStreams
                .Select(static stream => stream.Path)
                .Where(static path =>
                    !string.IsNullOrWhiteSpace(path)
                    && path.EndsWith(".mks", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var mksPath in mksPaths)
            {
                await _attachmentPrecacheService.PrecacheAllAttachmentsAsync(
                    mksPath!,
                    mediaSource,
                    cancellationToken).ConfigureAwait(false);
            }

            if (subtitleStreams.Length != mksPaths.Length
                && !string.IsNullOrWhiteSpace(mediaSource.Path))
            {
                await _attachmentPrecacheService.PrecacheAllAttachmentsAsync(
                    mediaSource.Path,
                    mediaSource,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
