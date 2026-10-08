using System.Runtime.InteropServices;

namespace OtpHarbor.IconPackBuilder.Commands;

internal static class ConsoleHost
{
    private const int StandardOutputHandle = -11;
    private const int StandardErrorHandle = -12;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    public static bool TryEnableColor()
    {
        if (Console.IsOutputRedirected
            || Console.IsErrorRedirected
            || Environment.GetEnvironmentVariable("NO_COLOR") is not null
            || string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!OperatingSystem.IsWindows()) return true;
        return TryEnableVirtualTerminal(StandardOutputHandle)
            && TryEnableVirtualTerminal(StandardErrorHandle);
    }

    public static bool ShouldPauseAtExit(IReadOnlyList<string> args)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var processes = new uint[2];
        var processCount = GetConsoleProcessList(processes, (uint)processes.Length);
        return ShouldPauseAtExit(
            isWindows: true,
            inputRedirected: Console.IsInputRedirected,
            nonInteractive: args.Any(argument => argument.Equals("--non-interactive", StringComparison.OrdinalIgnoreCase)),
            consoleProcessCount: processCount);
    }

    internal static bool ShouldPauseAtExit(
        bool isWindows,
        bool inputRedirected,
        bool nonInteractive,
        uint consoleProcessCount)
        => isWindows
            && !inputRedirected
            && !nonInteractive
            && consoleProcessCount == 1;

    public static void PauseBeforeExit()
    {
        Console.WriteLine();
        Console.Write("Press Enter to close...");
        Console.ReadLine();
    }

    private static bool TryEnableVirtualTerminal(int standardHandle)
    {
        var handle = GetStdHandle(standardHandle);
        return handle != IntPtr.Zero
            && handle != new IntPtr(-1)
            && GetConsoleMode(handle, out var mode)
            && SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr consoleHandle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(IntPtr consoleHandle, uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList([Out] uint[] processList, uint processCount);
}
