using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AttachmentOptimizer.Configuration;

/// <summary>
/// Attachment Optimizer settings.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    private static readonly SubtitleCodecOption[] _allSubtitleCodecs =
    [
        new("ass", "ASS / SSA"),
        new("subrip", "SubRip / SRT"),
        new("webvtt", "WebVTT"),
        new("mov_text", "MOV text"),
        new("text", "Raw text"),
        new("sami", "SAMI"),
        new("eia_608", "EIA-608 closed captions"),
        new("jacosub", "JACOsub"),
        new("microdvd", "MicroDVD"),
        new("mpl2", "MPL2"),
        new("pjs", "PJS"),
        new("realtext", "RealText"),
        new("stl", "Spruce subtitle format"),
        new("subviewer", "SubViewer"),
        new("subviewer1", "SubViewer v1"),
        new("vplayer", "VPlayer"),
        new("DVDSUB", "DVD / VobSub"),
        new("PGSSUB", "Blu-ray / PGS"),
        new("DVBSUB", "DVB subtitles"),
        new("xsub", "XSUB")
    ];

    /// <summary>
    /// Gets or sets a value indicating whether embedded subtitles are extracted during library scans.
    /// </summary>
    public bool ExtractSubtitlesDuringLibraryScan { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether attachments are pre-cached during library scans.
    /// </summary>
    public bool PrecacheAttachmentsDuringLibraryScan { get; set; }

    /// <summary>
    /// Gets or sets the libraries included in subtitle extraction. Empty means all libraries.
    /// </summary>
    public string[] SelectedSubtitleLibraries { get; set; } = [];

    /// <summary>
    /// Gets or sets the libraries included in attachment pre-caching. Empty means all libraries.
    /// </summary>
    public string[] SelectedAttachmentLibraries { get; set; } = [];


    /// <summary>
    /// Gets or sets a value indicating whether individual subtitle codecs are selected.
    /// </summary>
    public bool EnableAdvancedSubtitleCodecSelection { get; set; }

    /// <summary>
    /// Gets or sets the subtitle codecs selected in advanced mode.
    /// </summary>
    public string[] SelectedSubtitleCodecs { get; set; } =
        _allSubtitleCodecs.Select(static option => option.Value).ToArray();

    /// <summary>
    /// Gets all subtitle codecs available to the configuration page.
    /// </summary>
    public SubtitleCodecOption[] AllSubtitleCodecs => _allSubtitleCodecs;

    /// <summary>
    /// Gets or sets a value indicating whether missing attachments are extracted in one FFmpeg invocation.
    /// </summary>
    public bool EnableBatchExtraction { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether identical attachment bytes are stored once by SHA-256.
    /// </summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether burn-in compatibility files use hard links when possible.
    /// </summary>
    public bool EnableHardLinks { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the daily cleanup task may remove plugin-managed cache data.
    /// </summary>
    public bool EnableAutomaticCleanup { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether cleanup only reports proposed changes.
    /// </summary>
    public bool CleanupDryRun { get; set; } = true;

    /// <summary>
    /// Gets or sets the retention period for per-media compatibility files.
    /// </summary>
    public int CompatibilityFileRetentionHours { get; set; } = 72;

    /// <summary>
    /// Gets or sets the retention period for unused content-addressed blobs.
    /// </summary>
    public int BlobRetentionDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets the maximum content-addressed cache size in GiB.
    /// </summary>
    public int MaximumBlobCacheSizeGiB { get; set; } = 10;
}

/// <summary>
/// One selectable subtitle codec.
/// </summary>
public sealed class SubtitleCodecOption
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleCodecOption"/> class.
    /// </summary>
    public SubtitleCodecOption()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleCodecOption"/> class.
    /// </summary>
    public SubtitleCodecOption(string value, string text)
    {
        Value = value;
        Text = text;
    }

    /// <summary>
    /// Gets or sets the codec value.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display text.
    /// </summary>
    public string Text { get; set; } = string.Empty;
}
