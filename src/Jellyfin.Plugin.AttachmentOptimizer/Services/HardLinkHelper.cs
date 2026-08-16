using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AttachmentOptimizer.Services;

internal static partial class HardLinkHelper
{
    public static bool Materialize(
        string blobPath,
        string compatibilityPath,
        bool enableHardLinks,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(blobPath);
        ArgumentException.ThrowIfNullOrEmpty(compatibilityPath);

        var directory = Path.GetDirectoryName(compatibilityPath)
            ?? throw new ArgumentException("Compatibility path cannot be a root directory.", nameof(compatibilityPath));
        Directory.CreateDirectory(directory);
        var temporaryPath = compatibilityPath + ".attachment-optimizer-" + Guid.NewGuid().ToString("N");
        var hardLinked = false;

        try
        {
            if (enableHardLinks)
            {
                hardLinked = TryCreateHardLink(temporaryPath, blobPath);
            }

            if (!hardLinked)
            {
                File.Copy(blobPath, temporaryPath, overwrite: false);
            }

            File.Move(temporaryPath, compatibilityPath, overwrite: true);
            return hardLinked;
        }
        catch
        {
            TryDelete(temporaryPath, logger);
            throw;
        }
    }

    private static bool TryCreateHardLink(string linkPath, string existingPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateHardLinkWindows(linkPath, existingPath, IntPtr.Zero);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            return CreateHardLinkUnix(existingPath, linkPath) == 0;
        }

        return false;
    }

    private static void TryDelete(string path, ILogger logger)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "Unable to delete temporary compatibility file {Path}", path);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(
        string fileName,
        string existingFileName,
        IntPtr securityAttributes);

    [DllImport("libc", EntryPoint = "link", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int CreateHardLinkUnix(string existingPath, string linkPath);
}
