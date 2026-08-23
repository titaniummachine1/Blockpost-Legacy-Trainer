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
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
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
    private static string knownWeaponsPath = string.Empty;
    private static MemberInfo? clientBufferMember;
    private static MemberInfo? clientLengthMember;

    // ---- fake-hit / server-trust test ----
    private static bool fakeHitReady;
    private static object? fakeHitClient;
    private static MethodInfo? fakeHitFlush;
    private static MethodInfo? netBegin;
    private static MethodInfo? netF32;
    private static MethodInfo? netI32;
    private static MethodInfo? netU8;
    private static MethodInfo? netI16;
    private static MethodInfo? netEnd;
    private static int fakeHitSequence;

    // ---- weapon discovery from 0x08 packets ----
    private static readonly ConcurrentDictionary<int, (string Codename, string Name)> DiscoveredWeapons = new();
    private static bool parsingWeaponData;
    private static int weaponDataField;
    private static int weaponDataId;
    private static string? weaponDataCodename;

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

        clientBufferMember = AccessTools.Field(clientType, "PEGEIKDNHLL") ?? (MemberInfo?)AccessTools.Property(clientType, "PEGEIKDNHLL");
        clientLengthMember = AccessTools.Field(clientType, "FKEHEHGFNBD") ?? (MemberInfo?)AccessTools.Property(clientType, "FKEHEHGFNBD");

        // Cache method references for the server-trust fake-hit test.
        fakeHitClient = AccessTools.Field(clientType, "LPCJFAOOIKA")?.GetValue(null)
            ?? AccessTools.Property(clientType, "LPCJFAOOIKA")?.GetValue(null);
        fakeHitFlush = AccessTools.Method(clientType, "HKOFHOANEJD", Type.EmptyTypes);
        netBegin = AccessTools.Method(netType, nameof(Raw.NET.Methods.LPAPGKDAENI), new[] { typeof(byte), typeof(byte) });
        netF32 = AccessTools.Method(netType, nameof(Raw.NET.Methods.JBIICNJNHCI), new[] { typeof(float) });
        netI32 = AccessTools.Method(netType, nameof(Raw.NET.Methods.FPELFNLEPGG), new[] { typeof(int) });
        netU8 = AccessTools.Method(netType, nameof(Raw.NET.Methods.PFCLIPCCHCK), new[] { typeof(byte) });
        netI16 = AccessTools.Method(netType, nameof(Raw.NET.Methods.APNPMHBBLDG), new[] { typeof(short) })
            ?? AccessTools.Method(netType, nameof(Raw.NET.Methods.HMCNFGMBCOC), new[] { typeof(short) });
        netEnd = AccessTools.Method(netType, nameof(Raw.NET.Methods.EMJOGONJKIO));
        fakeHitReady = fakeHitClient != null && fakeHitFlush != null
            && netBegin != null && netF32 != null && netI32 != null
            && netU8 != null && netI16 != null && netEnd != null;

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

        // Scoreboard + match clock, pushed by the server on a ~5s refresh. Kept because it is
        // genuinely useful, but it is NOT ammo -- see PROTOCOL.md 23 for how it was mistaken for
        // one.
        var hudType = AccessTools.TypeByName("HUD");
        if (hudType != null)
        {
            patched += Patch(harmony, hudType, "GEGHOEFBKMO",
                new[] { typeof(int), typeof(int), typeof(int) }, nameof(OnScoreHud));

            // Ammo candidates. EMDBPLFCKOB and POMEPDFNPHE each stringify two ints into the HUD
            // string fields at 0x220 and 0x224 -- the shape of a magazine/reserve readout. Their
            // parameter names (ELFBCINKADC, HAFMINBJCGN) also match Client.ANICPIFFOIK, the
            // opcode 0x4E sender, which would tie the pair to the wire.
            patched += Patch(harmony, hudType, "EMDBPLFCKOB",
                new[] { typeof(int), typeof(int) }, nameof(OnHudPairA));
            patched += Patch(harmony, hudType, "POMEPDFNPHE",
                new[] { typeof(int), typeof(int) }, nameof(OnHudPairB));
            patched += Patch(harmony, hudType, "HENIAKEDGNK",
                new[] { typeof(int), typeof(int) }, nameof(OnHudPairC));

            // The setter methods above are not always reached (possibly inlined or from a stale
            // build). Catch the actual on-screen text as well, then read the candidate fields at
            // the same instant to see which one supplies the numbers.
            var guiType = AccessTools.TypeByName("UnityEngine.GUI") ?? typeof(GUI);
            if (guiType != null)
            {
                patched += Patch(harmony, guiType, "Label", new[] { typeof(Rect), typeof(string) }, nameof(OnGuiLabel));
                patched += Patch(harmony, guiType, "Label", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) }, nameof(OnGuiLabelStyled));
                patched += Patch(harmony, guiType, "Label", new[] { typeof(Rect), typeof(GUIContent) }, nameof(OnGuiLabelContent));
                patched += Patch(harmony, guiType, "Label", new[] { typeof(Rect), typeof(GUIContent), typeof(GUIStyle) }, nameof(OnGuiLabelContentStyled));
            }
        }
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

        // Client.Update dequeues received packets into Client.PEGEIKDNHLL / Client.FKEHEHGFNBD
        // then calls FPKEAECEOPE for each one. Reading the static fields is the way to capture inbound.
        patched += Patch(harmony, clientType, nameof(Raw.Client.Methods.FPKEAECEOPE), Type.EmptyTypes, nameof(OnRxClientStatic));

        // One file per run. FileMode.Create against a fixed name silently destroyed a capture, so
        // never reuse a path: evidence from an earlier session must survive a game restart.
        var captureDir = Path.Combine(Paths.BepInExRootPath, "captures");
        Directory.CreateDirectory(captureDir);
        logPath = Path.Combine(captureDir, $"net-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        // Persistent weapon catalog built from every 0x08 packet seen.
        knownWeaponsPath = Path.Combine(Paths.BepInExRootPath, "known_weapons.txt");
        LoadKnownWeapons();

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
    private static int lastTickFrame = -1;

    internal static void Tick()
    {
        // Called from both Controll.Update and GUIInv.OnGUI. Without this guard the key checks run
        // twice in one frame, so a toggle flips and immediately flips back.
        if (Time.frameCount == lastTickFrame)
        {
            return;
        }

        lastTickFrame = Time.frameCount;

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

    private static void OnBegin(byte __0, byte __1)
    {
        if (__1 == 0x08)
        {
            parsingWeaponData = true;
            weaponDataField = 0;
            weaponDataId = 0;
            weaponDataCodename = null;
        }
        else
        {
            parsingWeaponData = false;
        }

        Push(Kind.Begin, __1, __0);
    }

    private static void OnF32(float __0) => Push(Kind.F32, __0);

    private static void OnI32(int __0)
    {
        if (parsingWeaponData && weaponDataField == 0)
        {
            weaponDataId = __0;
            weaponDataField = 1;
        }

        Push(Kind.I32, __0);
    }

    private static void OnU8(byte __0) => Push(Kind.U8, __0);

    private static void OnI16(short __0) => Push(Kind.I16, __0);

    private static void OnU64(ulong __0) => Push(Kind.U64, __0);

    private static void OnStr(string __0)
    {
        if (parsingWeaponData)
        {
            if (weaponDataField == 1)
            {
                weaponDataCodename = __0;
                weaponDataField = 2;
            }
            else if (weaponDataField == 2 && !string.IsNullOrEmpty(weaponDataCodename))
            {
                DiscoverWeapon(weaponDataId, weaponDataCodename, __0);
                weaponDataField = 3; // stop parsing strings for this packet
            }
        }

        Push(Kind.Str, 0, 0, __0);
    }

    private static void OnEnd()
    {
        parsingWeaponData = false;
        Push(Kind.End, 0);
    }

    private static readonly Dictionary<string, (int, int)> LastPair = new();

    /// <summary>
    /// Log an int pair pushed into a HUD text field, on change only. Deliberately does NOT claim
    /// which pair is ammo -- section 22 was retracted for exactly that kind of early naming.
    /// </summary>
    private static void NotePair(string tag, int a, int b)
    {
        if (LastPair.TryGetValue(tag, out var prev) && prev == (a, b))
        {
            return;
        }

        LastPair[tag] = (a, b);
        Note($"HUDPAIR {tag} a={a} b={b}");
    }

    private static void OnHudPairA(int ELFBCINKADC, int HAFMINBJCGN) => NotePair("EMDBPLFCKOB", ELFBCINKADC, HAFMINBJCGN);

    private static void OnHudPairB(int ELFBCINKADC, int HAFMINBJCGN) => NotePair("POMEPDFNPHE", ELFBCINKADC, HAFMINBJCGN);

    private static void OnHudPairC(int BNJGJOOJCHF, int ICPDEFLDMLF) => NotePair("HENIAKEDGNK", BNJGJOOJCHF, ICPDEFLDMLF);

    private static int lastScoreA = int.MinValue;
    private static int lastScoreB;
    private static int lastClock;

    /// <summary>
    /// Scoreboard and match clock -- NOT ammo. HUD.GEGHOEFBKMO(a, b, c) stringifies a and b into
    /// the HUD text fields; a/b are team scores and c is the match clock in seconds. Calls arrive
    /// on a ~5s HUD refresh from the inbound packet path, not per shot.
    /// </summary>
    private static void OnScoreHud(int JMEFALEPJKM, int DEAOCFDEMKE, int PBCODLDLHKL)
    {
        if (JMEFALEPJKM == lastScoreA && DEAOCFDEMKE == lastScoreB && PBCODLDLHKL == lastClock)
        {
            return;
        }

        lastScoreA = JMEFALEPJKM;
        lastScoreB = DEAOCFDEMKE;
        lastClock = PBCODLDLHKL;
        Note($"SCORE a={JMEFALEPJKM} b={DEAOCFDEMKE} clock={PBCODLDLHKL}");
    }

    // ---- ammo display probe ----

    private static readonly char[] AmmoSeparators = new[] { '|', '/' };
    private static Type? ammoHudType;
    private static Type? ammoContType;

    private static void OnGuiLabel(Rect __0, string __1) => OnGuiLabelCore(__0, __1);
    private static void OnGuiLabelStyled(Rect __0, string __1, GUIStyle __2) => OnGuiLabelCore(__0, __1);
    private static void OnGuiLabelContent(Rect __0, GUIContent __1) => OnGuiLabelCore(__0, __1?.text);
    private static void OnGuiLabelContentStyled(Rect __0, GUIContent __1, GUIStyle __2) => OnGuiLabelCore(__0, __1?.text);

    private static void OnGuiLabelCore(Rect position, string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 24 || text.IndexOfAny(AmmoSeparators) < 0)
        {
            return;
        }

        var parts = text.Split(AmmoSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out _) || !int.TryParse(parts[1], out _))
        {
            return;
        }

        var hud = ammoHudType ??= Type.GetType("HUD, Assembly-CSharp") ?? AccessTools.TypeByName("HUD");
        var cont = ammoContType ??= Type.GetType("Controll, Assembly-CSharp") ?? AccessTools.TypeByName("Controll");

        var hud220 = Read(hud, null, "PGBHFOEPNBE")?.ToString() ?? "?";
        var hud224 = Read(hud, null, "MINMCMFHJNE")?.ToString() ?? "?";

        var controller = Read(cont, null, "LPCJFAOOIKA");
        var ca = controller != null ? (Read(controller.GetType(), controller, "CGNEAAGAMKC") as int?) : null;
        var cb = controller != null ? (Read(controller.GetType(), controller, "MCOGBCDKDJD") as int?) : null;

        var main = Read(cont, null, "HGAODFPBGLB");
        int? ec = null, slot = null, gdSlot = null;
        if (main != null)
        {
            var mt = main.GetType();
            ec = Read(mt, main, "ECBCOHFLJCC") as int?;
            slot = Read(mt, main, "MOPBMENEGLN") as int?;
            var gd = Read(mt, main, "GDEMINMDJAC") as Il2CppStructArray<int>;
            if (gd != null && slot.HasValue && slot.Value >= 0 && slot.Value < gd.Length)
            {
                gdSlot = gd[slot.Value];
            }
        }

        Note($"AMMO_TEXT '{text}' rect={position} hud220='{hud220}' hud224='{hud224}' contA={ca} contB={cb} playerEC={ec} playerSlot={slot} playerGDslot={gdSlot}");
    }

    private static object? Read(Type? type, object? instance, string name)
    {
        if (type == null)
        {
            return null;
        }

        try
        {
            var field = AccessTools.Field(type, name);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            var prop = AccessTools.Property(type, name);
            if (prop != null)
            {
                return prop.GetValue(instance, null);
            }
        }
        catch
        {
            // Reflection on an Il2Cpp type can throw for stripped or uninitalised fields.
        }

        return null;
    }

    private static void OnFlush()
    {
        parsingWeaponData = false;
        Push(Kind.Flush, 0);
    }

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

    private static void OnRxClientStatic(MethodBase __originalMethod)
    {
        if (!capturing || clientBufferMember == null || clientLengthMember == null)
        {
            return;
        }

        try
        {
            var raw = GetMemberValue(clientBufferMember);
            var lenObj = GetMemberValue(clientLengthMember);
            if (lenObj is not int len || len <= 0)
            {
                return;
            }

            var copy = CopyBytes(raw, Math.Min(len, RxCaptureBytes));
            if (copy == null || copy.Length == 0)
            {
                return;
            }

            Push(Kind.Rx, len, copy.Length, __originalMethod?.Name ?? "Client", copy);
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[NetProbe] OnRxClientStatic failed: {ex.Message}");
        }
    }

    private static object? GetMemberValue(MemberInfo? m)
    {
        if (m is FieldInfo f) return f.GetValue(null);
        if (m is PropertyInfo p) return p.GetValue(null);
        return null;
    }

    private static unsafe byte[]? CopyBytes(object? raw, int max)
    {
        if (raw is Il2CppStructArray<byte> arr && arr.Length > 0)
        {
            var take = Math.Min(arr.Length, max);
            var copy = new byte[take];
            for (var i = 0; i < take; i++)
            {
                copy[i] = arr[i];
            }
            return copy;
        }

        if (raw is not Il2CppObjectBase { Pointer: var ptr } || ptr == IntPtr.Zero)
        {
            return null;
        }

        var len = *(uint*)(ptr + 0x18).ToPointer();
        var take2 = Math.Min((int)len, max);
        var copy2 = new byte[take2];
        var data = (byte*)(ptr + 0x20).ToPointer();
        for (var i = 0; i < take2; i++)
        {
            copy2[i] = data[i];
        }
        return copy2;
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

    // ---- known weapons catalog ----

    private static void LoadKnownWeapons()
    {
        try
        {
            if (!File.Exists(knownWeaponsPath))
            {
                return;
            }

            foreach (var line in File.ReadLines(knownWeaponsPath))
            {
                var parts = line.Split('|', 3);
                if (parts.Length == 3
                    && int.TryParse(parts[0], out var id))
                {
                    DiscoveredWeapons[id] = (parts[1], parts[2]);
                }
            }
        }
        catch (Exception exception)
        {
            log?.LogWarning($"[NetProbe] failed to load known weapons: {exception.Message}");
        }
    }

    private static void AppendKnownWeapon(int id, string codename, string name)
    {
        try
        {
            File.AppendAllText(knownWeaponsPath, $"{id}|{codename}|{name}{Environment.NewLine}");
        }
        catch (Exception exception)
        {
            log?.LogWarning($"[NetProbe] failed to append known weapon: {exception.Message}");
        }
    }

    internal static void DiscoverWeapon(int id, string codename, string name)
    {
        if (DiscoveredWeapons.TryAdd(id, (codename, name)))
        {
            var line = $"weapon-discovered: id={id}, codename={codename}, name={name}";
            Note(line);
            AppendKnownWeapon(id, codename, name);
        }
    }

    /// <summary>
    /// Experimental server-trust test: build and send 0x06 (damage triple) and 0x04 (hit report)
    /// packets for the given target without the local client actually firing.
    /// </summary>
    internal static bool TryFakeHit(KBBBHJDINCB? target, Vector3 origin, Vector3 point, int damage)
    {
        if (!fakeHitReady || target == null)
        {
            log?.LogWarning("[NetProbe] fake-hit not ready or no target.");
            return false;
        }

        try
        {
            // Which player field the wire id actually comes from is still unresolved (backlog 5).
            // CCINALOJCNH is a guess, and if it is the wrong one the server would ignore the
            // packet for a reason that has nothing to do with trust -- which would be easy to
            // misread as "the server validates hits". Log every candidate alongside the id we
            // send, so one capture settles it against the ids in real 0x04 traffic.
            var targetId = target.CCINALOJCNH;
            var bodyPart = (byte)1; // head
            var seq = ++fakeHitSequence;

            Note($"fake-hit candidates: Id0={target.GLFBKBKFPCL} Id1={target.CCINALOJCNH} "
                 + $"Id2={target.LGOAHLMABFF} PlayerId={target.LCPGFBMKNHJ} Team={target.MMMGPDBMOLM} "
                 + $"Health={target.FDOJDJLIGLF} -> sending Id1={targetId}");

            SendPacket(0x06, writer =>
            {
                netI16?.Invoke(null, new object[] { (short)damage });
                netI16?.Invoke(null, new object[] { (short)targetId });
                netI16?.Invoke(null, new object[] { (short)0 });
            });

            SendPacket(0x04, writer =>
            {
                netF32?.Invoke(null, new object[] { origin.x });
                netF32?.Invoke(null, new object[] { origin.y });
                netF32?.Invoke(null, new object[] { origin.z });
                netI32?.Invoke(null, new object[] { seq });
                netU8?.Invoke(null, new object[] { (byte)targetId });
                netU8?.Invoke(null, new object[] { bodyPart });
                netF32?.Invoke(null, new object[] { point.x });
                netF32?.Invoke(null, new object[] { point.y });
                netF32?.Invoke(null, new object[] { point.z });
            });

            log?.LogInfo($"[NetProbe] fake-hit sent: targetId={targetId}, damage={damage}, seq={seq}, origin={origin}, point={point}");
            Note($"fake-hit: targetId={targetId} damage={damage} seq={seq}");
            return true;
        }
        catch (Exception exception)
        {
            log?.LogWarning($"[NetProbe] fake-hit failed: {exception.Message}");
            return false;
        }
    }

    private static void SendPacket(byte op, Action<object?> writer)
    {
        netBegin?.Invoke(null, new object[] { (byte)0xF5, op });
        writer(null);
        netEnd?.Invoke(null, null);
        fakeHitFlush?.Invoke(fakeHitClient, null);
    }

    internal static void Shutdown()
    {
        running = false;
        capturing = false;
        writer?.Join(1500);
    }
}
