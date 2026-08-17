using Jellyfin.Plugin.AttachmentOptimizer.Services;
using Jellyfin.Plugin.AttachmentOptimizer.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.AttachmentOptimizer.Tests;

public sealed class ScheduledTaskDiscoveryTests
{
    [Fact]
    public void ScheduledTasksArePubliclyExportedForJellyfinDiscovery()
    {
        var exportedTasks = typeof(Plugin).Assembly
            .GetExportedTypes()
            .Where(static type =>
                !type.IsAbstract
                && typeof(IScheduledTask).IsAssignableFrom(type))
            .ToHashSet();

        Assert.Contains(typeof(ExtractSubtitlesTask), exportedTasks);
        Assert.Contains(typeof(PrecacheAttachmentsTask), exportedTasks);
        Assert.Contains(typeof(AttachmentCleanupTask), exportedTasks);
    }
}
