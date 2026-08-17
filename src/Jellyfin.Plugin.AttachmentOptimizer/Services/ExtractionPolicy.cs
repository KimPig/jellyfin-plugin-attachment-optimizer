using Jellyfin.Plugin.AttachmentOptimizer.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AttachmentOptimizer.Services;

/// <summary>
/// Shared extraction selection rules for providers and scheduled tasks.
/// </summary>
internal static class ExtractionPolicy
{
    public static bool IsLibrarySelected(
        BaseItem item,
        IReadOnlyCollection<string>? selectedLibraries,
        ILibraryManager libraryManager)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(libraryManager);

        if (selectedLibraries is null || selectedLibraries.Count == 0)
        {
            return true;
        }

        return libraryManager.GetCollectionFolders(item).Any(folder =>
            selectedLibraries.Contains(folder.Name, StringComparer.OrdinalIgnoreCase));
    }

    public static bool ShouldExtractSubtitles(
        MediaSourceInfo mediaSource,
        PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(mediaSource);
        ArgumentNullException.ThrowIfNull(configuration);

        var subtitleStreams = mediaSource.MediaStreams
            .Where(static stream =>
                stream.Type == MediaStreamType.Subtitle
                && !stream.IsExternal)
            .ToArray();
        if (subtitleStreams.Length == 0)
        {
            return false;
        }

        if (!configuration.EnableAdvancedSubtitleCodecSelection)
        {
            return true;
        }

        var selectedCodecs = configuration.SelectedSubtitleCodecs ?? [];
        return selectedCodecs.Length > 0
            && subtitleStreams.All(stream =>
                !string.IsNullOrWhiteSpace(stream.Codec)
                && selectedCodecs.Contains(stream.Codec, StringComparer.OrdinalIgnoreCase));
    }
}
