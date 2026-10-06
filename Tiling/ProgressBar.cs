using System.Diagnostics;
using System.Text;

namespace SwMapRenderer.Tiling;

/// <summary>
/// A one-line progress bar redrawn in place on stderr, safe to tick from every worker thread.
///
/// It writes to stderr so that stdout stays the plan and the summary, and draws only when
/// stderr is a terminal: a carriage-return redraw piped into a log file is one enormous line.
/// Redraws are throttled, because a slice at deep zoom finishes in milliseconds and repainting
/// the line on each one would cost more than the slice.
/// </summary>
public sealed class ProgressBar
{
    private static readonly long RedrawTicks = Stopwatch.Frequency / 10;

    private int _total;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private int _done;
    private long _lastDraw;
    private string _phase = string.Empty;
    private int _lastLength;

    /// <summary>The work is only known once the plan is, which is after the bar is built.</summary>
    public void SetTotal(int total) => Volatile.Write(ref _total, Math.Max(total, 1));

    public static bool IsSupported => !Console.IsErrorRedirected;

    /// <summary>Names the stage shown beside the bar, e.g. "level 8".</summary>
    public void SetPhase(string phase)
    {
        lock (_gate)
            _phase = phase;
    }

    public void Tick()
    {
        int n = Interlocked.Increment(ref _done);

        long now = _clock.ElapsedTicks;
        if (n < Volatile.Read(ref _total) && now - Volatile.Read(ref _lastDraw) < RedrawTicks)
            return;

        lock (_gate)
        {
            _lastDraw = now;
            Draw(n);
        }
    }

    /// <summary>Clears the bar so the next line of output starts at the left edge.</summary>
    public void Finish()
    {
        lock (_gate)
            Console.Error.Write("\r" + new string(' ', _lastLength) + "\r");
    }

    private void Draw(int n)
    {
        double fraction = Math.Min(n / (double)_total, 1);

        string tail = $" {fraction,4:P0}  {n:N0}/{_total:N0}";
        double elapsed = _clock.Elapsed.TotalSeconds;
        if (n >= 20 && n < _total)
            tail += $"  ETA {Span(TimeSpan.FromSeconds(elapsed / n * (_total - n)))}";
        if (_phase.Length > 0)
            tail += $"  {_phase}";

        int width;
        try { width = Console.WindowWidth; }
        catch (IOException) { width = 80; }

        int barWidth = Math.Clamp(width - tail.Length - 3, 10, 40);
        int filled = (int)(fraction * barWidth);

        var line = new StringBuilder("[")
            .Append('#', filled).Append('-', barWidth - filled).Append(']').Append(tail).ToString();

        // Stay one short of the width: writing the last column wraps on some terminals.
        if (width > 1 && line.Length > width - 1)
            line = line[..(width - 1)];

        Console.Error.Write("\r" + line.PadRight(_lastLength));
        _lastLength = line.Length;
    }

    private static string Span(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}h{span.Minutes:00}m" :
        span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}m{span.Seconds:00}s" :
        $"{span.Seconds}s";
}
