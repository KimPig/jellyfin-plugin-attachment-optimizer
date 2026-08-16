using System.Diagnostics;
using System.Globalization;
using System.Text;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AttachmentOptimizer.Services;

internal sealed record ExtractionTarget(
    MediaAttachment Attachment,
    string OutputPath);

internal interface IAttachmentProcessRunner
{
    Task ExtractAsync(
        string inputFile,
        MediaSourceInfo mediaSource,
        IReadOnlyList<ExtractionTarget> targets,
        CancellationToken cancellationToken);
}

internal sealed class FfmpegAttachmentProcessRunner : IAttachmentProcessRunner
{
    private readonly ILogger<FfmpegAttachmentProcessRunner> _logger;
    private readonly IMediaEncoder _mediaEncoder;

    public FfmpegAttachmentProcessRunner(
        ILogger<FfmpegAttachmentProcessRunner> logger,
        IMediaEncoder mediaEncoder)
    {
        _logger = logger;
        _mediaEncoder = mediaEncoder;
    }

    public async Task ExtractAsync(
        string inputFile,
        MediaSourceInfo mediaSource,
        IReadOnlyList<ExtractionTarget> targets,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputFile);
        ArgumentNullException.ThrowIfNull(mediaSource);
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Count == 0)
        {
            return;
        }

        foreach (var target in targets)
        {
            var directory = Path.GetDirectoryName(target.OutputPath)
                ?? throw new ArgumentException("Attachment output path cannot be a root directory.", nameof(targets));
            Directory.CreateDirectory(directory);
        }

        var inputArgument = _mediaEncoder.GetInputArgument(inputFile, mediaSource);
        ArgumentException.ThrowIfNullOrEmpty(inputArgument);
        var arguments = BuildArguments(inputArgument, mediaSource, targets);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                Arguments = arguments,
                FileName = _mediaEncoder.EncoderPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                ErrorDialog = false
            },
            EnableRaisingEvents = true
        };

        _logger.LogInformation(
            "Extracting {AttachmentCount} missing attachments with one FFmpeg process: {File} {Arguments}",
            targets.Count,
            process.StartInfo.FileName,
            process.StartInfo.Arguments);

        process.Start();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            DeleteOutputs(targets);
            throw;
        }

        var hasVideoOrAudioStream = mediaSource.MediaStreams.Any(
            static stream => stream.Type is MediaStreamType.Video or MediaStreamType.Audio);
        var exitCodeAccepted = process.ExitCode == 0 || (!hasVideoOrAudioStream && process.ExitCode == 1);
        var outputsComplete = targets.All(static target => File.Exists(target.OutputPath));
        if (!exitCodeAccepted || !outputsComplete)
        {
            DeleteOutputs(targets);
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"FFmpeg attachment extraction failed with exit code {process.ExitCode}."));
        }
    }

    internal static string BuildArguments(
        string inputArgument,
        MediaSourceInfo mediaSource,
        IReadOnlyList<ExtractionTarget> targets)
    {
        var builder = new StringBuilder("-nostdin ");
        foreach (var target in targets)
        {
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "-dump_attachment:{0} \"{1}\" ",
                target.Attachment.Index,
                EscapeQuotedArgument(target.OutputPath));
        }

        if (inputArgument.EndsWith(".concat\"", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("-f concat -safe 0 ");
        }

        builder.Append("-i ");
        builder.Append(inputArgument);
        if (mediaSource.MediaStreams.Any(
                static stream => stream.Type is MediaStreamType.Video or MediaStreamType.Audio))
        {
            builder.Append(" -t 0 -f null null");
        }

        return builder.ToString();
    }

    private static string EscapeQuotedArgument(string value) =>
        value.Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void DeleteOutputs(IEnumerable<ExtractionTarget> targets)
    {
        foreach (var target in targets)
        {
            try
            {
                File.Delete(target.OutputPath);
            }
            catch (IOException)
            {
                // Cleanup is best effort; the unique work directory is retried later.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup is best effort; the unique work directory is retried later.
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }
    }
}
