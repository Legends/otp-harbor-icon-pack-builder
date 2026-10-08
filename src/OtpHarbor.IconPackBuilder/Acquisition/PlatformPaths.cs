using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace OtpHarbor.IconPackBuilder.Acquisition;

public static partial class PlatformPaths
{
    public static string GetCacheDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OTP Harbor", "IconPackBuilder", "cache");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(home, "Library", "Caches", "OTP Harbor", "IconPackBuilder");
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        return Path.Combine(!string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg) ? xdg : Path.Combine(home, ".cache"),
            "otp-harbor", "icon-pack-builder");
    }

    public static string GetDefaultOutputPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(home))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var xdg = ReadLinuxDownloadsDirectory(home);
                if (xdg is not null) candidates.Add(xdg);
            }
            candidates.Add(Path.Combine(home, "Downloads"));
            candidates.Add(home);
        }
        candidates.Add(Directory.GetCurrentDirectory());
        var directory = SelectFirstExistingDirectory(candidates, Directory.GetCurrentDirectory());
        return Path.Combine(directory, "otp-harbor-icons.otphicons");
    }

    internal static string SelectFirstExistingDirectory(IEnumerable<string> candidates, string fallback)
        => candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            ?? fallback;

    internal static string? ReadLinuxDownloadsDirectory(string home)
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var path = Path.Combine(!string.IsNullOrWhiteSpace(configHome) && Path.IsPathRooted(configHome)
            ? configHome : Path.Combine(home, ".config"), "user-dirs.dirs");
        if (!File.Exists(path)) return null;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var match = DownloadsRegex().Match(line.Trim());
                if (!match.Success) continue;
                var value = match.Groups[1].Value.Replace("$HOME", home, StringComparison.Ordinal)
                    .Replace("${HOME}", home, StringComparison.Ordinal);
                return Path.IsPathRooted(value) && Directory.Exists(value) ? value : null;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    [GeneratedRegex("^XDG_DOWNLOAD_DIR=\"([^\"]+)\"$", RegexOptions.CultureInvariant)]
    private static partial Regex DownloadsRegex();
}
