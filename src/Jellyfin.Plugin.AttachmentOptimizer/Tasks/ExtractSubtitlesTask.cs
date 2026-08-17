using Jellyfin.Plugin.AttachmentOptimizer.Configuration;
using Jellyfin.Plugin.AttachmentOptimizer.Services;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AttachmentOptimizer.Tasks;

/// <summary>
/// Extracts embedded subtitles ahead of playback.
/// </summary>
public sealed class ExtractSubtitlesTask : IScheduledTask
{
    private readonly LibraryExtractionTaskRunner _runner;
    private readonly LibraryExtractionService _extractionService;

    public ExtractSubtitlesTask(
        IServiceProvider serviceProvider,
        ILocalizationManager localization)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _runner = serviceProvider.GetRequiredService<LibraryExtractionTaskRunner>();
        _extractionService = serviceProvider.GetRequiredService<LibraryExtractionService>();
        ArgumentNullException.ThrowIfNull(localization);
    }

    public string Name => "Extract Embedded Subtitles";

    public string Key => "AttachmentOptimizerExtractSubtitles";

    public string Description =>
        "Extracts configured embedded subtitle streams ahead of playback.";

    public string Category => "Attachment Optimizer";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    public async Task ExecuteAsync(
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        await _runner.RunAsync(
            configuration.SelectedSubtitleLibraries,
            requireSubtitles: true,
            (item, token) => _extractionService.ExtractSubtitlesAsync(
                item,
                configuration,
                token),
            progress,
            cancellationToken).ConfigureAwait(false);
    }
}
