using System.Diagnostics;
using System.Globalization;
using OtpHarbor.IconPackBuilder.Resolution;

namespace OtpHarbor.IconPackBuilder.Commands;

internal sealed class CatalogProgressRenderer(TextWriter output, bool enabled)
{
    private const int BarWidth = 20;
    private const int BrandWidth = 18;
    private static readonly char[] SpinnerFrames = ['|', '/', '-', '\\'];
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private CatalogBuildProgress? _latest;
    private long _lastRenderMilliseconds = -100;
    private int _lastLineLength;
    private int _spinnerIndex;
    private bool _rendered;

    public void Report(CatalogBuildProgress progress)
    {
        _latest = progress;
        if (!enabled) return;
        var force = progress.CompletedBrands == 0 || progress.CompletedBrands == progress.TotalBrands;
        if (!force && _stopwatch.ElapsedMilliseconds - _lastRenderMilliseconds < 50) return;
        Render(progress, completed: false);
    }

    public void Complete()
    {
        if (!enabled || _latest is null) return;
        Render(_latest with { CompletedBrands = _latest.TotalBrands }, completed: true);
        output.WriteLine();
        output.Flush();
        _rendered = false;
        _lastLineLength = 0;
    }

    public void Clear()
    {
        if (!enabled || !_rendered) return;
        output.Write('\r');
        output.Write(new string(' ', _lastLineLength));
        output.Write('\r');
        output.Flush();
        _rendered = false;
        _lastLineLength = 0;
    }

    private void Render(CatalogBuildProgress progress, bool completed)
    {
        var ratio = progress.TotalBrands == 0
            ? 1d
            : Math.Clamp(progress.CompletedBrands / (double)progress.TotalBrands, 0d, 1d);
        var filled = Math.Clamp((int)Math.Round(ratio * BarWidth), 0, BarWidth);
        var bar = new string('=', filled) + new string('-', BarWidth - filled);
        var marker = completed ? "+" : SpinnerFrames[_spinnerIndex++ % SpinnerFrames.Length].ToString();
        var percentage = (ratio * 100).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5);
        var counts = $"{progress.CompletedBrands:N0}/{progress.TotalBrands:N0}";
        var brand = Fit(progress.CurrentBrandId, BrandWidth).PadRight(BrandWidth);
        var elapsed = $"{(int)_stopwatch.Elapsed.TotalMinutes:00}:{_stopwatch.Elapsed.Seconds:00}";
        var line = $"{marker} [{bar}] {percentage}%  {counts,11}  {brand}  {elapsed}";

        output.Write('\r');
        output.Write(line);
        if (_lastLineLength > line.Length) output.Write(new string(' ', _lastLineLength - line.Length));
        output.Flush();
        _lastLineLength = line.Length;
        _lastRenderMilliseconds = _stopwatch.ElapsedMilliseconds;
        _rendered = true;
    }

    private static string Fit(string value, int width)
        => value.Length <= width ? value : value[..(width - 1)] + "~";
}
