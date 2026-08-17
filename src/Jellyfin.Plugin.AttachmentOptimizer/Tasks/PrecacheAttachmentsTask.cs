using Jellyfin.Plugin.AttachmentOptimizer.Configuration;
using Jellyfin.Plugin.AttachmentOptimizer.Services;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AttachmentOptimizer.Tasks;

/// <summary>
/// Pre-caches attachments ahead of playback without creating compatibility files.
/// </summary>
public sealed class PrecacheAttachmentsTask : IScheduledTask
{
    private readonly LibraryExtractionTaskRunner _runner;
    private readonly LibraryExtractionService _extractionService;

    public PrecacheAttachmentsTask(
        IServiceProvider serviceProvider,
        ILocalizationManager localization)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _runner = serviceProvider.GetRequiredService<LibraryExtractionTaskRunner>();
        _extractionService = serviceProvider.GetRequiredService<LibraryExtractionService>();
        ArgumentNullException.ThrowIfNull(localization);
    }

    public string Name => "Extract Embedded Attachments";

    public string Key => "AttachmentOptimizerPrecacheAttachments";

    public string Description =>
        "Extracts embedded attachments into the deduplicated optimizer store without creating Jellyfin compatibility files.";

    public string Category => "Attachment Optimizer";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    public async Task ExecuteAsync(
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        await _runner.RunAsync(
            configuration.SelectedAttachmentLibraries,
            requireSubtitles: false,
            (item, token) => _extractionService.PrecacheAttachmentsAsync(item, token),
            progress,
            cancellationToken).ConfigureAwait(false);
    }
}
