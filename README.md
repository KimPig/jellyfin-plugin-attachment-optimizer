# Attachment Optimizer

A Jellyfin 12 plugin that batches embedded attachment extraction, deduplicates identical files, and manages its cache safely.

Download the ready-to-install ZIP from [GitHub Releases](https://github.com/KimPig/jellyfin-plugin-attachment-optimizer/releases).

## What it does

- Extracts all currently missing attachments from one media source with a single FFmpeg process.
- Stores identical attachment bytes once in a SHA-256 content-addressed cache.
- Identifies common font formats from their file signatures and stores them as TTF, OTF, TTC, OTC, WOFF, WOFF2, or PFB; other attachments use `.blob`.
- Serves Web attachment requests directly from that shared blob cache.
- Creates Jellyfin-compatible per-media attachment files for burn-in workflows, using hard links when possible and copies as a fallback.
- Provides a scheduled cleanup task that only manages files recorded by this plugin.

The plugin replaces Jellyfin's internal `IAttachmentExtractor` service. It does not modify media files and it does not remove attachments embedded in MKV files.

## Settings

All behavior can be changed from Dashboard > Plugins > Attachment Optimizer:

- **Batch missing attachments**: enabled by default. One FFmpeg invocation receives one `-dump_attachment:<index>` option for every missing attachment.
- **Deduplicate by SHA-256**: enabled by default. Identical bytes share one blob even when they came from different media files.
- **Use hard links**: enabled by default. A normal file copy is used when the source and destination do not support hard links.
- **Automatic cleanup**: disabled by default.
- **Cleanup dry run**: enabled by default. The task reports what it would remove without deleting it.
- **Compatibility file retention**: 72 hours by default.
- **Unused blob retention**: 30 days by default.
- **Maximum blob cache size**: 10 GiB by default.

The cleanup task is also available manually under Dashboard > Scheduled Tasks. It skips media sources currently being handled by the plugin and never scans arbitrary user directories for deletion.

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

Jellyfin-compatible files may also be materialized below Jellyfin's normal `attachments` directory when a server workflow requires a filesystem `fontsdir`. Those paths are recorded in the manifest before they become eligible for cleanup.

## Compatibility and limitations

- Target: Jellyfin Server 12.0.0-rc5 / .NET 10.
- The implementation depends on Jellyfin's internal attachment service contract. Rebuild and validation may be required for a later Jellyfin release.
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

The package script creates `artifacts/AttachmentOptimizer_<version>.zip` containing the plugin DLL and `meta.json`.

## License

GPL-3.0-or-later.
