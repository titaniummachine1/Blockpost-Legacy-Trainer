using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using BepInEx;
using BepInEx.Logging;

namespace BlockpostTrainer;

/// <summary>
/// Off-thread sink for verbose diagnostics.
///
/// BepInEx's <c>ManualLogSource</c> fans every call out to the console and the disk writer on the
/// calling thread. Verbose mode emits hundreds of lines per second, so writing through it directly
/// stalls frames. This takes the same text, drops it on a bounded queue, and lets a background
/// thread do the I/O.
///
/// The queue drops rather than blocks: a diagnostic that costs frames defeats its own purpose.
/// Dropped lines are counted and reported, so a gap in the log is never silent.
/// </summary>
internal static class AsyncLog
{
    private const int QueueLimit = 40000;

    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static ManualLogSource? log;
    private static Thread? writer;
    private static volatile bool running;
    private static int queued;
    private static long dropped;
    private static string path = string.Empty;

    internal static long Dropped => Interlocked.Read(ref dropped);

    internal static string Path => path;

    internal static void Start(ManualLogSource source)
    {
        if (running)
        {
            return;
        }

        log = source;

        var dir = System.IO.Path.Combine(Paths.BepInExRootPath, "captures");
        Directory.CreateDirectory(dir);
        path = System.IO.Path.Combine(dir, $"diag-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        running = true;
        writer = new Thread(Loop)
        {
            IsBackground = true,
            Name = "BlockpostAsyncLog",
            Priority = System.Threading.ThreadPriority.Lowest
        };
        writer.Start();

        source.LogInfo($"[AsyncLog] verbose diagnostics -> {path}");
    }

    /// <summary>Queue a diagnostic line. Safe from any thread; never touches disk inline.</summary>
    internal static void Write(string line)
    {
        if (!running)
        {
            return;
        }

        if (Volatile.Read(ref queued) >= QueueLimit)
        {
            Interlocked.Increment(ref dropped);
            return;
        }

        Interlocked.Increment(ref queued);
        Queue.Enqueue(line);
    }

    private static void Loop()
    {
        StreamWriter? file = null;
        var lastFlush = Clock.ElapsedMilliseconds;
        var lastDropReport = 0L;

        try
        {
            file = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite, 1 << 16))
            {
                AutoFlush = false
            };
            file.WriteLine($"# Blockpost verbose diagnostics, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            while (running || !Queue.IsEmpty)
            {
                if (!Queue.TryDequeue(out var line))
                {
                    if (Clock.ElapsedMilliseconds - lastFlush > 250)
                    {
                        file.Flush();
                        lastFlush = Clock.ElapsedMilliseconds;

                        // Surface drops so a gap in the log is never mistaken for quiet.
                        var lost = Dropped;
                        if (lost != lastDropReport)
                        {
                            file.WriteLine($"# ... {lost - lastDropReport} lines dropped (queue full)");
                            lastDropReport = lost;
                        }
                    }

                    Thread.Sleep(15);
                    continue;
                }

                Interlocked.Decrement(ref queued);
                file.WriteLine($"{Clock.ElapsedMilliseconds} {line}");
            }

            file.WriteLine($"# total dropped: {Dropped}");
            file.Flush();
        }
        catch (Exception exception)
        {
            log?.LogError($"[AsyncLog] writer thread died: {exception}");
        }
        finally
        {
            try
            {
                file?.Dispose();
            }
            catch
            {
                // nothing useful to do while tearing down
            }
        }
    }

    internal static void Shutdown()
    {
        running = false;
        writer?.Join(1500);
    }
}
