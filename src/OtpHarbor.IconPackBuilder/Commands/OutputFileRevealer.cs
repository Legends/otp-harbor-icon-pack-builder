using System.Diagnostics;

namespace OtpHarbor.IconPackBuilder.Commands;

internal enum DesktopPlatform
{
    Windows,
    MacOS,
    Linux
}

internal static class OutputFileRevealer
{
    public static bool TryReveal(string filePath)
    {
        try
        {
            var platform = OperatingSystem.IsWindows()
                ? DesktopPlatform.Windows
                : OperatingSystem.IsMacOS()
                    ? DesktopPlatform.MacOS
                    : DesktopPlatform.Linux;
            using var process = Process.Start(CreateStartInfo(filePath, platform));
            return process is not null;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or FileNotFoundException
            or DirectoryNotFoundException)
        {
            return false;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string filePath, DesktopPlatform platform)
    {
        var fullPath = Path.GetFullPath(filePath);
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        switch (platform)
        {
            case DesktopPlatform.Windows:
                startInfo.FileName = "explorer.exe";
                startInfo.ArgumentList.Add("/select,");
                startInfo.ArgumentList.Add(fullPath);
                break;
            case DesktopPlatform.MacOS:
                startInfo.FileName = "open";
                startInfo.ArgumentList.Add("-R");
                startInfo.ArgumentList.Add(fullPath);
                break;
            case DesktopPlatform.Linux:
                startInfo.FileName = "xdg-open";
                startInfo.ArgumentList.Add(Path.GetDirectoryName(fullPath)!);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(platform), platform, null);
        }

        return startInfo;
    }
}
