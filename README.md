# Attachment Optimizer

A Jellyfin 12 plugin that pre-extracts embedded subtitles, batches attachment extraction, deduplicates identical files, and manages its cache safely.

Download the ready-to-install ZIP from [GitHub Releases](https://github.com/KimPig/jellyfin-plugin-attachment-optimizer/releases).

## Catalog installation

Add the shared KimPig plugin repository in **Dashboard > Plugins > Repositories**:

```text
https://raw.githubusercontent.com/KimPig/jellyfin-plugin-repository/main/manifest.json
```

Use `KimPig Jellyfin Plugins` as the repository name. After saving it, open the
plugin catalog, install Attachment Optimizer, and restart Jellyfin Server.
The same catalog also provides Subtitle Font Bridge.

## What it does

- Optionally extracts embedded subtitles during library scans.
- Provides a manual **Extract Embedded Subtitles** scheduled task.
- Optionally extracts attachments during library scans without creating Jellyfin compatibility files.
- Provides a manual **Extract Embedded Attachments** scheduled task using the same optimized path.
- Filters subtitle extraction by library and can optionally limit it to selected codecs.
- Filters attachment extraction by library.
- Extracts all currently missing attachments from one media source with a single FFmpeg process.
- Stores identical attachment bytes once in a SHA-256 content-addressed cache.
- Identifies common font formats from their file signatures and stores them as TTF, OTF, TTC, OTC, WOFF, WOFF2, or PFB; other attachments use `.blob`.
- Serves Web attachment requests directly from the shared blob cache.
- Creates Jellyfin-compatible per-media attachment files for burn-in workflows, using hard links when possible and copies as a fallback.
- Provides a scheduled cleanup task that only manages files recorded by this plugin.

The plugin replaces Jellyfin's internal `IAttachmentExtractor` service. It does not modify media files and does not remove attachments embedded in MKV files.

## Do not combine attachment extraction tasks

Do not use Subtitle Extract's **Extract Attachments** task or its attachment
extraction during library scans together with Attachment Optimizer. Subtitle
Extract calls Jellyfin's compatibility extraction path, which can create
additional per-media entries under `data/attachments`.

Use Attachment Optimizer's **Extract Embedded Attachments** task and
**Extract embedded attachments during library scan** option instead. They populate the
deduplicated optimizer store without materializing standard compatibility files.
Actual server workflows that require a filesystem `fontsdir`, such as subtitle
burn-in, can still materialize hard links on demand.

Because Attachment Optimizer now includes embedded subtitle extraction, the
separate Subtitle Extract plugin is normally unnecessary when these features are
enabled here.

## Settings

All behavior can be changed from **Dashboard > Plugins > Attachment Optimizer**.

### Library scan

- **Extract embedded subtitles during library scan**: disabled by default.
- **Extract embedded attachments during library scan**: disabled by default.
- **Subtitle libraries**: empty means all movie and TV libraries.
- **Attachment libraries**: empty means all movie and TV libraries.

Library-scan switches do not disable the corresponding scheduled tasks. Library
selections apply to both scan providers and scheduled tasks.

### Subtitle extraction

- All embedded subtitle codecs are eligible by default.
- **Limit subtitle formats**: disabled by default. Enable it to choose the accepted codecs precisely.
- When a format limit is enabled, every embedded subtitle stream in a media file must match the selected codecs.

### Attachment optimization

- **Batch missing attachments**: enabled by default. One FFmpeg invocation receives one `-dump_attachment:<index>` option for every missing attachment.
- **Store identical attachment content once**: enabled by default. Identical bytes share one blob even when they came from different media files.
- **Use hard links for compatibility files**: enabled by default. A verified copy is used when hard links are unavailable.

### Cache cleanup

- **Automatic cleanup**: disabled by default.
- **Cleanup dry run**: enabled by default.
- **Compatibility file retention**: 72 hours by default.
- **Unused blob retention**: 30 days by default.
- **Maximum blob cache size**: 10 GiB by default.

Three tasks are available under **Dashboard > Scheduled Tasks**. The two extraction tasks are grouped under **Attachment Optimizer**; cleanup remains under **Maintenance**:

- **Extract Embedded Subtitles**
- **Extract Embedded Attachments**
- **Clean Attachment Optimizer Cache**

None of the extraction tasks has a default schedule. Run them manually or add a
schedule in Jellyfin. Cleanup runs daily but performs no deletion unless
automatic cleanup is enabled; dry-run remains enabled by default.

## Data layout

Plugin-managed data is stored below Jellyfin's data directory:

```text
attachment-optimizer/
  objects/
    sha256/     content-addressed files sharded by the first two hash characters
  media/
    <id>/       per-media manifest and last-access metadata
  temp/         unique temporary extraction directories
```

Jellyfin-compatible files are materialized below Jellyfin's normal `attachments`
directory only when a server workflow requires a filesystem `fontsdir`. Empty
per-media directories are not created during ordinary attachment delivery or
scheduled extraction, and stale empty directories are removed by cache cleanup.

## Compatibility and limitations

- Target: Jellyfin Server 12.0.0-rc5 / .NET 10.
- The implementation depends on Jellyfin's internal attachment, subtitle, and metadata-provider contracts. Rebuild and validation may be required for a later Jellyfin release.
- Only missing attachments are extracted. Existing compatible files are imported into the shared cache instead of being extracted again.
- SHA-256 deduplication saves physical storage across media sources. Per-media hard links may still appear as separate directory entries while sharing the same underlying file data.
- Automatic cleanup is deliberately opt-in. Review a dry-run log before disabling dry-run mode.
- Direct-play clients such as native players normally read attachments from the original media and do not invoke this plugin. Server-side attachment delivery and subtitle burn-in do.

## Build and test

```powershell
dotnet build Jellyfin.Plugin.AttachmentOptimizer.slnx -c Release
dotnet test Jellyfin.Plugin.AttachmentOptimizer.slnx -c Release
./build/Package.ps1
```

The package script creates `artifacts/AttachmentOptimizer_<version>.zip`
containing the plugin DLL and `meta.json`.

## Acknowledgements

The library-scan provider, scheduled-task, and subtitle filtering behavior is
independently implemented for Attachment Optimizer with reference to Jellyfin's
[Subtitle Extract](https://github.com/jellyfin/jellyfin-plugin-subtitleextract)
plugin.


## License

GPL-3.0-or-later.
