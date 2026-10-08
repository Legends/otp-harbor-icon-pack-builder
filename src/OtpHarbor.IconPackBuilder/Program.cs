using OtpHarbor.IconPackBuilder.Commands;

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;
int exitCode;
try
{
    exitCode = await CliApplication.RunAsync(
        args,
        Console.Out,
        Console.Error,
        cancellation.Token,
        useColor: ConsoleHost.TryEnableColor());
}
catch (Exception exception)
{
    var previousColor = Console.ForegroundColor;
    try
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[X] Unexpected failure: {exception.Message}");
    }
    finally
    {
        Console.ForegroundColor = previousColor;
    }

    exitCode = 3;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}

if (ConsoleHost.ShouldPauseAtExit(args))
    ConsoleHost.PauseBeforeExit();

return exitCode;
