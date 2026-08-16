using Jellyfin.Plugin.AttachmentOptimizer.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AttachmentOptimizer;

/// <summary>
/// Registers Attachment Optimizer services.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.AddSingleton<AttachmentStore>();
        serviceCollection.AddSingleton<IAttachmentProcessRunner, FfmpegAttachmentProcessRunner>();

        // Plugin services are registered after Jellyfin core services. The final
        // single-service registration is therefore used for IAttachmentExtractor.
        serviceCollection.AddSingleton<IAttachmentExtractor, OptimizedAttachmentExtractor>();
        serviceCollection.AddSingleton<IScheduledTask, AttachmentCleanupTask>();
    }
}
