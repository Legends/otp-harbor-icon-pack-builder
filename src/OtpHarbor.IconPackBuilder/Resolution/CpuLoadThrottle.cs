using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace OtpHarbor.IconPackBuilder.Resolution;

internal sealed class CpuLoadThrottle
{
    internal const double TargetLoad = 0.65;
    internal const double MaximumLoad = 0.70;
    private static readonly long SampleInterval = Stopwatch.Frequency / 10;
    private readonly object _gate = new();
    private CpuSnapshot? _previous;
    private long _nextSample;
    private bool _throttling;

    public void Wait(CancellationToken cancellationToken)
    {
        while (true)
        {
            var pause = false;
            lock (_gate)
            {
                var now = Stopwatch.GetTimestamp();
                if (now >= _nextSample)
                {
                    _nextSample = now + SampleInterval;
                    if (TryReadSnapshot(out var current) && _previous is { } previous)
                    {
                        var total = current.Total - previous.Total;
                        var idle = current.Idle - previous.Idle;
                        if (total > 0 && idle >= 0 && idle <= total)
                        {
                            var load = 1d - idle / (double)total;
                            _throttling = load >= MaximumLoad
                                || _throttling && load > TargetLoad;
                        }
                    }
                    _previous = current;
                }
                pause = _throttling;
            }
            if (!pause) return;
            cancellationToken.ThrowIfCancellationRequested();
            if (cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(25)))
                cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static bool TryReadSnapshot(out CpuSnapshot snapshot)
    {
        if (OperatingSystem.IsWindows())
            return TryReadWindowsSnapshot(out snapshot);
        if (OperatingSystem.IsLinux())
            return TryReadLinuxSnapshot(out snapshot);
        snapshot = default;
        return false;
    }

    private static bool TryReadWindowsSnapshot(out CpuSnapshot snapshot)
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            snapshot = default;
            return false;
        }
        snapshot = new CpuSnapshot(ToUInt64(kernel) + ToUInt64(user), ToUInt64(idle));
        return true;
    }

    private static bool TryReadLinuxSnapshot(out CpuSnapshot snapshot)
    {
        try
        {
            var line = File.ReadLines("/proc/stat").FirstOrDefault();
            if (line is null || !line.StartsWith("cpu ", StringComparison.Ordinal))
                throw new InvalidDataException();
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Select(value => ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture))
                .ToArray();
            if (fields.Length < 4) throw new InvalidDataException();
            snapshot = new CpuSnapshot(fields.Aggregate(0UL, (sum, value) => sum + value),
                fields[3] + (fields.Length > 4 ? fields[4] : 0));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or FormatException or OverflowException or InvalidDataException)
        {
            snapshot = default;
            return false;
        }
    }

    private static ulong ToUInt64(FileTime value)
        => ((ulong)value.High << 32) | value.Low;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileTime
    {
        public readonly uint Low;
        public readonly uint High;
    }

    private readonly record struct CpuSnapshot(ulong Total, ulong Idle);
}
