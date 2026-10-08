using OtpHarbor.IconPackBuilder.Commands;

namespace OtpHarbor.IconPackBuilder.Tests;

public sealed class ConsoleHostTests
{
    [Theory]
    [InlineData(true, false, false, 1, true)]
    [InlineData(true, false, false, 2, false)]
    [InlineData(true, true, false, 1, false)]
    [InlineData(true, false, true, 1, false)]
    [InlineData(false, false, false, 1, false)]
    public void PauseIsLimitedToDirectInteractiveWindowsLaunches(
        bool isWindows,
        bool inputRedirected,
        bool nonInteractive,
        uint consoleProcessCount,
        bool expected)
    {
        Assert.Equal(expected, ConsoleHost.ShouldPauseAtExit(
            isWindows,
            inputRedirected,
            nonInteractive,
            consoleProcessCount));
    }
}
