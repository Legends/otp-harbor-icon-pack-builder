using OtpHarbor.IconPackBuilder.Commands;

namespace OtpHarbor.IconPackBuilder.Tests;

public sealed class OutputFileRevealerTests
{
    [Fact]
    public void WindowsSelectsTheCompletedFileWithoutCommandLineConcatenation()
    {
        var path = Path.GetFullPath(Path.Combine("folder with spaces", "pack.otphicons"));

        var result = OutputFileRevealer.CreateStartInfo(path, DesktopPlatform.Windows);

        Assert.Equal("explorer.exe", result.FileName);
        Assert.Equal(["/select,", path], result.ArgumentList);
        Assert.False(result.UseShellExecute);
    }

    [Fact]
    public void MacOSRevealsTheCompletedFile()
    {
        var path = Path.GetFullPath(Path.Combine("folder with spaces", "pack.otphicons"));

        var result = OutputFileRevealer.CreateStartInfo(path, DesktopPlatform.MacOS);

        Assert.Equal("open", result.FileName);
        Assert.Equal(["-R", path], result.ArgumentList);
    }

    [Fact]
    public void LinuxOpensTheContainingDirectory()
    {
        var path = Path.GetFullPath(Path.Combine("folder with spaces", "pack.otphicons"));

        var result = OutputFileRevealer.CreateStartInfo(path, DesktopPlatform.Linux);

        Assert.Equal("xdg-open", result.FileName);
        Assert.Equal([Path.GetDirectoryName(path)!], result.ArgumentList);
    }
}
