using Jellyfin.Plugin.AttachmentOptimizer.Services;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AttachmentOptimizer.Tests;

public sealed class AttachmentProcessRunnerTests
{
    [Fact]
    public void BuildArgumentsExtractsAllRequestedAttachmentsInOneInvocation()
    {
        var source = new MediaSourceInfo
        {
            MediaStreams =
            [
                new MediaStream { Type = MediaStreamType.Video }
            ]
        };
        var targets = new[]
        {
            new ExtractionTarget(new MediaAttachment { Index = 3 }, @"C:\cache\font one.ttf"),
            new ExtractionTarget(new MediaAttachment { Index = 9 }, @"C:\cache\font-two.otf")
        };

        var arguments = FfmpegAttachmentProcessRunner.BuildArguments(
            "\"C:\\media\\episode.mkv\"",
            source,
            targets);

        Assert.Contains("-dump_attachment:3 \"C:\\cache\\font one.ttf\"", arguments, StringComparison.Ordinal);
        Assert.Contains("-dump_attachment:9 \"C:\\cache\\font-two.otf\"", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("-dump_attachment:t", arguments, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(arguments, " -i "));
        Assert.EndsWith(" -t 0 -f null null", arguments, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(search, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }

        return count;
    }
}
