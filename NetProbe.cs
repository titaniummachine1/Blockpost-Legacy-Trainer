using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using BlockpostTrainer.Sdk;
using Raw = BlockpostTrainer.Sdk.Raw;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BlockpostTrainer;

/// <summary>
/// Passive wire tap for the Blockpost room protocol.
///
/// Every outgoing message is assembled by the static <c>NET</c> serializer and pushed to the
/// socket by <c>Client.HKOFHOANEJD</c>; every incoming buffer arrives through
/// one of <c>Client.FPIDGCHIEMJ</c>, <c>Client.MKPOLBIKPPA</c>, or <c>Client.GINPPBIJOCA</c>.
/// Hooking the typed <c>NET</c> writers gives a decoded field stream
/// without having to know the byte layout up front.
///
/// Nothing is formatted on the game thread: hooks push a small record onto a bounded queue and a
/// background thread does the string work and the disk I/O. If the queue backs up, records are
/// dropped and counted rather than allowed to stall a frame.
/// </summary>
internal static class NetProbe
{
    private const int QueueLimit = 20000;
    private const int RxCaptureBytes = 96;

    private enum Kind : byte
    {
        Begin,
        F32,
        I32,
        U8,
        I16,
        U64,
        Str,
        End,
        Flush,
        Rx,
        Mark,
        Text
    }

    private readonly struct Rec
    {
        public Rec(Kind kind, long ticks, double value, int extra, string? text, byte[]? blob)
        {
            Kind = kind;
            Ticks = ticks;
            Value = value;
            Extra = extra;
            Text = text;
            Blob = blob;
        }

        public Kind Kind { get; }
        public long Ticks { get; }
        public double Value { get; }
        public int Extra { get; }
        public string? Text { get; }
        public byte[]? Blob { get; }
    }

    // Outgoing message ids, read off the NET.LPAPGKDAENI(0xF5, op) callsites inside Client.
    private static readonly Dictionary<int, string> TxNames = new()
    {
        [0x00] = "keepalive",
        [0x02] = "req/2",
        [0x04] = "HIT_REPORT(pos,seq,hits[])",
        [0x06] = "int3/6",
        [0x07] = "int3/7",
        [0x08] = "weapondata/8",
        [0x09] = "loadout/9",
        [0x0D] = "chat?/13",
        [0x0E] = "weapon_select/14",
        [0x0F] = "slot/15",
        [0x10] = "ready/16",
        [0x11] = "spawn?/17",
        [0x16] = "throw(pos,dir,f)/22",
        [0x17] = "impact(id,pos)/23",
        [0x1B] = "string/27",
        [0x2D] = "MOVE(x,y,z,rx,ry,state,tick)",
        [0x35] = "raw/53",
        [0x39] = "int3/57",
        [0x3D] = "int/61",
        [0x42] = "req/66",
        [0x4E] = "int2/78",
        [0x50] = "int+str/80",
        [0x51] = "int+uint+str/81",
        [0x52] = "int+str+str/82",
        [0x55] = "SHOT(origin,dir,spread)",
        [0x56] = "hit_effect(id,pos,i)/86",
        [0x57] = "int/87"
    };

    private static readonly ConcurrentQueue<Rec> Queue = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static ManualLogSource? log;
    private static Thread? writer;
    private static volatile bool running;
    private static volatile bool capturing;
    private static int queued;
    private static long dropped;
    private static int markCounter;
    private static int failureCount;
    private static string logPath = string.Empty;

    internal static bool Capturing => capturing;

    internal static long Dropped => Interlocked.Read(ref dropped);

    internal static string LogPath => logPath;

    internal static void Install(Harmony harmony, ManualLogSource source)
    {
        log = source;

        var netType = AccessTools.TypeByName("NET");
        var clientType = AccessTools.TypeByName("Client");
        if (netType == null || clientType == null)
        {
            source.LogWarning("[NetProbe] NET/Client types not found; wire tap disabled.");
            return;
        }

        var patched = 0;
        // Method name strings come from the generated SDK so the obfuscated names live in one place.
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.LPAPGKDAENI), new[] { typeof(byte), typeof(byte) }, nameof(OnBegin));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.DMFHJJDMAEJ), new[] { typeof(byte), typeof(byte) }, nameof(OnBegin));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.JBIICNJNHCI), new[] { typeof(float) }, nameof(OnF32));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.HIPPJGAHHPC), new[] { typeof(float) }, nameof(OnF32));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.PIMOAOKDDCC), new[] { typeof(float) }, nameof(OnF32));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.FPELFNLEPGG), new[] { typeof(int) }, nameof(OnI32));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.GJBAJNCFBLB), new[] { typeof(int) }, nameof(OnI32));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.KLPOMLKDPAL), new[] { typeof(int) }, nameof(OnI32));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.LHMNDGLMOFO), new[] { typeof(int) }, nameof(OnI32));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.CHIOALKDHOC), new[] { typeof(int) }, nameof(OnI32));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.PFCLIPCCHCK), new[] { typeof(byte) }, nameof(OnU8));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.LMKOIABBCNK), new[] { typeof(byte) }, nameof(OnU8));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.HMCNFGMBCOC), new[] { typeof(short) }, nameof(OnI16));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.IFINMFCPGIB), new[] { typeof(short) }, nameof(OnI16));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.IHLNBLGFGLF), new[] { typeof(short) }, nameof(OnI16));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.APNPMHBBLDG), new[] { typeof(short) }, nameof(OnI16));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.MJDOMFPOPMK), new[] { typeof(ulong) }, nameof(OnU64));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.EKDBCDKOJAO), new[] { typeof(ulong) }, nameof(OnU64));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.EDICJCKFAMN), new[] { typeof(ulong) }, nameof(OnU64));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.KOIHHCOBIEJ), new[] { typeof(string) }, nameof(OnStr));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.KMEFAPEEHHN), new[] { typeof(string) }, nameof(OnStr));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.PJFMOLFBKHM), new[] { typeof(string) }, nameof(OnStr));
        patched += Patch(harmony, netType, nameof(Raw.NET.Methods.EMJOGONJKIO), Type.EmptyTypes, nameof(OnEnd));
        patched += Patch(harmony, clientType, nameof(Raw.Client.Methods.HKOFHOANEJD), Type.EmptyTypes, nameof(OnFlush));
        // Room client has three byte[]+int methods; only FPIDGCHIEMJ was patched before.
        // Capture from all three so we can see which one actually carries inbound traffic.
        // Client has several (byte[], int) methods. FPIDGCHIEMJ was the obvious candidate,
        // but it never fired. The ones below are other internal methods with the same signature;
        // one of them should be the actual receive path.
        patched += Patch(harmony, clientType, nameof(Raw.Client.Methods.FPIDGCHIEMJ), new[] { typeof(Il2CppStructArray<byte>), typeof(int) }, nameof(OnRx));
        patched += Patch(harmony, clientType, nameof(Raw.Client.Methods.MKPOLBIKPPA), new[] { typeof(Il2CppStructArray<byte>), typeof(int) }, nameof(OnRx));
        patched += Patch(harmony, clientType, nameof(Raw.Client.Methods.GINPPBIJOCA), new[] { typeof(Il2CppStructArray<byte>), typeof(int) }, nameof(OnRx));
        patched += Patch(harmony, clientType, nameof(Raw.Client.Methods.KPBPDBDDOFG), new[] { typeof(Il2CppStructArray<byte>), typeof(int) }, nameof(OnRx));
        patched += Patch(harmony, clientType, nameof(Raw.Client.Methods.BMJJCBAPAHP), new[] { typeof(Il2CppStructArray<byte>), typeof(int) }, nameof(OnRx));
        patched += Patch(harmony, clientType, nameof(Raw.Client.Methods.KHPHBCBOMML), new[] { typeof(Il2CppStructArray<byte>), typeof(int) }, nameof(OnRx));

        // One file per run. FileMode.Create against a fixed name silently destroyed a capture, so
        // never reuse a path: evidence from an earlier session must survive a game restart.
        var captureDir = Path.Combine(Paths.BepInExRootPath, "captures");
        Directory.CreateDirectory(captureDir);
        logPath = Path.Combine(captureDir, $"net-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        running = true;
        writer = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "BlockpostNetProbe",
            Priority = System.Threading.ThreadPriority.BelowNormal
        };
        writer.Start();

        source.LogInfo($"[NetProbe] {patched} hooks installed. F7 starts/stops capture, F8 drops a marker. Log: {logPath}");
    }

    private static int Patch(Harmony harmony, Type type, string name, Type[] args, string handler)
    {
        try
        {
            var method = AccessTools.Method(type, name, args);
            if (method == null)
            {
                log?.LogWarning($"[NetProbe] {type.Name}.{name} not found; skipped.");
                return 0;
            }

            harmony.Patch(method, prefix: new HarmonyMethod(typeof(NetProbe), handler));
            return 1;
        }
        catch (Exception exception)
        {
            log?.LogWarning($"[NetProbe] failed to hook {type.Name}.{name}: {exception.Message}");
            return 0;
        }
    }

    /// <summary>Call once per frame from an existing Update hook.</summary>
    internal static void Tick()
    {
        if (Input.GetKeyDown(KeyCode.F7))
        {
            capturing = !capturing;
            Push(Kind.Mark, 0, 0, capturing ? "==== CAPTURE ON ====" : "==== CAPTURE OFF ====", null, force: true);
            log?.LogInfo($"[NetProbe] capture {(capturing ? "started" : "stopped")} (dropped so far: {Dropped}).");
        }

        if (Input.GetKeyDown(KeyCode.F8))
        {
            var n = Interlocked.Increment(ref markCounter);
            Push(Kind.Mark, 0, 0, $"---- MARKER #{n} ----", null, force: true);
            log?.LogInfo($"[NetProbe] marker #{n}");
        }
    }

    /// <summary>Write a free-form line into the capture log, independent of packet capture.</summary>
    internal static void Note(string text) => Push(Kind.Text, 0, 0, text, null, force: true);

    private static void Push(Kind kind, double value, int extra = 0, string? text = null, byte[]? blob = null, bool force = false)
    {
        if (!capturing && !force)
        {
            return;
        }

        try
        {
            if (Volatile.Read(ref queued) >= QueueLimit)
            {
                Interlocked.Increment(ref dropped);
                return;
            }

            Interlocked.Increment(ref queued);
            Queue.Enqueue(new Rec(kind, Clock.ElapsedTicks, value, extra, text, blob));
        }
        catch (Exception exception)
        {
            if (Interlocked.Increment(ref failureCount) < 5)
            {
                log?.LogWarning($"[NetProbe] record failed: {exception.Message}");
            }
        }
    }

    // ---- hook bodies: no formatting, no I/O, no Unity calls ----

    private static void OnBegin(byte __0, byte __1) => Push(Kind.Begin, __1, __0);

    private static void OnF32(float __0) => Push(Kind.F32, __0);

    private static void OnI32(int __0) => Push(Kind.I32, __0);

    private static void OnU8(byte __0) => Push(Kind.U8, __0);

    private static void OnI16(short __0) => Push(Kind.I16, __0);

    private static void OnU64(ulong __0) => Push(Kind.U64, __0);

    private static void OnStr(string __0) => Push(Kind.Str, 0, 0, __0);

    private static void OnEnd() => Push(Kind.End, 0);

    private static void OnFlush() => Push(Kind.Flush, 0);

    private static void OnRx(Il2CppStructArray<byte> __0, int __1, MethodBase __originalMethod)
    {
        if (!capturing || __0 == null || __1 <= 0)
        {
            return;
        }

        var take = Math.Min(__1, Math.Min(RxCaptureBytes, __0.Length));
        var copy = new byte[take];
        for (var i = 0; i < take; i++)
        {
            copy[i] = __0[i];
        }

        var source = __originalMethod?.Name ?? "rx";
        Push(Kind.Rx, __1, take, source, copy);
    }

    // ---- background writer ----

    private static void WriterLoop()
    {
        StreamWriter? file = null;
        var line = new StringBuilder(256);
        var packet = new StringBuilder(512);
        var inPacket = false;
        var lastFlush = Clock.ElapsedMilliseconds;

        try
        {
            file = new StreamWriter(new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite, 1 << 16))
            {
                AutoFlush = false
            };
            file.WriteLine($"# Blockpost room-protocol capture, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            file.WriteLine("# tx: <ms> tx hdr=0xNN op=0xNN <name> : <fields>    rx: <ms> rx len=N <hex>");

            while (running || !Queue.IsEmpty)
            {
                if (!Queue.TryDequeue(out var rec))
                {
                    if (Clock.ElapsedMilliseconds - lastFlush > 250)
                    {
                        file.Flush();
                        lastFlush = Clock.ElapsedMilliseconds;
                    }

                    Thread.Sleep(15);
                    continue;
                }

                Interlocked.Decrement(ref queued);
                var ms = rec.Ticks * 1000.0 / Stopwatch.Frequency;

                switch (rec.Kind)
                {
                    case Kind.Begin:
                        if (inPacket)
                        {
                            file.WriteLine(packet.ToString());
                        }

                        packet.Clear();
                        var op = (int)rec.Value;
                        var name = TxNames.TryGetValue(op, out var known) ? known : "?";
                        packet.Append(ms.ToString("F1")).Append(" tx hdr=0x").Append(rec.Extra.ToString("X2"))
                              .Append(" op=0x").Append(op.ToString("X2")).Append(' ').Append(name).Append(" :");
                        inPacket = true;
                        break;

                    case Kind.F32:
                        packet.Append(' ').Append(((float)rec.Value).ToString("0.###"));
                        break;

                    case Kind.I32:
                        packet.Append(" i").Append((int)rec.Value);
                        break;

                    case Kind.U8:
                        packet.Append(" b").Append((int)rec.Value);
                        break;

                    case Kind.I16:
                        packet.Append(" s").Append((int)rec.Value);
                        break;

                    case Kind.U64:
                        packet.Append(" L").Append((ulong)rec.Value);
                        break;

                    case Kind.Str:
                        packet.Append(" \"").Append(rec.Text).Append('"');
                        break;

                    case Kind.End:
                    case Kind.Flush:
                        if (inPacket)
                        {
                            file.WriteLine(packet.ToString());
                            packet.Clear();
                            inPacket = false;
                        }

                        break;

                    case Kind.Rx:
                        line.Clear();
                        line.Append(ms.ToString("F1")).Append(" rx src=").Append(rec.Text ?? "?")
                            .Append(" len=").Append((int)rec.Value).Append(' ');
                        if (rec.Blob != null)
                        {
                            for (var i = 0; i < rec.Blob.Length; i++)
                            {
                                line.Append(rec.Blob[i].ToString("X2")).Append(' ');
                            }
                        }

                        file.WriteLine(line.ToString());
                        break;

                    case Kind.Text:
                        if (inPacket)
                        {
                            file.WriteLine(packet.ToString());
                            packet.Clear();
                            inPacket = false;
                        }

                        file.WriteLine($"{ms:F1} {rec.Text}");
                        break;

                    case Kind.Mark:
                        if (inPacket)
                        {
                            file.WriteLine(packet.ToString());
                            packet.Clear();
                            inPacket = false;
                        }

                        file.WriteLine();
                        file.WriteLine($"{ms:F1} {rec.Text}");
                        file.Flush();
                        lastFlush = Clock.ElapsedMilliseconds;
                        break;
                }
            }

            file.WriteLine($"# dropped records: {Dropped}");
            file.Flush();
        }
        catch (Exception exception)
        {
            log?.LogError($"[NetProbe] writer thread died: {exception}");
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
        capturing = false;
        writer?.Join(1500);
    }
}
