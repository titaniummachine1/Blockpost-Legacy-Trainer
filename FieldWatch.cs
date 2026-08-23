using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BlockpostTrainer.Sdk;
using Raw = BlockpostTrainer.Sdk.Raw;
using UnityEngine;

namespace BlockpostTrainer;

/// <summary>
/// Runtime field differ.
///
/// The obfuscated names tell us nothing, so instead of guessing which field is the reload timer or
/// the magazine counter, this snapshots every numeric field each frame and reports the ones that
/// changed. Reload once with the watcher on and the timer identifies itself.
///
/// Three targets are watched, because state is split across them: the networked player entity
/// (<c>KBBBHJDINCB</c>, mostly animation and pose), and both the statics and the live instance of
/// <c>Controll</c>, which is where local weapon state actually lives.
///
/// Only fields that actually moved get formatted, and the text is handed to <see cref="NetProbe"/>'s
/// background writer, so the game thread never touches the disk.
/// </summary>
internal static class FieldWatch
{
    // Fields that churn every frame no matter what the player does. They drown out the signal, so
    // they are muted by default; F5 includes them.
    private static readonly HashSet<string> Noisy = new()
    {
        nameof(Raw.Controll.Offsets.JIPNKAGPCGK), // look vector
        nameof(Raw.Controll.Offsets.FLILDBNOFMK), // look vector
        nameof(Raw.KBBBHJDINCB.Offsets.OOMJGHCFODI), // position
        nameof(Raw.KBBBHJDINCB.Offsets.NDLMGNIMKHE), // distance-walked accumulator
        nameof(Raw.KBBBHJDINCB.Offsets.HDNNKKFCPOB), // weapon bob
        nameof(Raw.KBBBHJDINCB.Offsets.MJFMDOKEFFO), // sway / lean ramp
        nameof(Raw.KBBBHJDINCB.Offsets.NJPHKNAOEKM),
        nameof(Raw.KBBBHJDINCB.Offsets.NHJDPAAFIKO)
    };

    private sealed class Target
    {
        public Target(string label, Type type, object? instance)
        {
            Label = label;
            Type = type;
            Instance = instance;
        }

        public string Label { get; }
        public Type Type { get; }
        public object? Instance { get; set; }
        public PropertyInfo[] Props { get; set; } = Array.Empty<PropertyInfo>();
        public double[] Previous { get; set; } = Array.Empty<double>();
        public bool[] Seeded { get; set; } = Array.Empty<bool>();
    }

    private static readonly List<Target> Targets = new();
    private static bool enabled;
    private static bool includeNoisy;
    private static readonly Dictionary<string, int> complaints = new();
    private static float nextConnectionReport;
    private static float nextRebind;

    internal static bool Enabled => enabled;

    /// <summary>
    /// Call once per frame from the Controll.Update hook, passing Harmony's <c>__instance</c>.
    /// The controller must arrive this way: resolving it with the generic
    /// <c>Object.FindObjectOfType&lt;Controll&gt;()</c> throws "Method unstripping failed" under
    /// Il2CppInterop and takes the whole watcher down with it.
    /// </summary>
    internal static void Tick(Controll? controller)
    {
        if (Input.GetKeyDown(KeyCode.F6))
        {
            enabled = !enabled;
            Targets.Clear();
            NetProbe.Note(enabled ? "==== FIELDWATCH ON ====" : "==== FIELDWATCH OFF ====");
        }

        if (Input.GetKeyDown(KeyCode.F5))
        {
            includeNoisy = !includeNoisy;
            Targets.Clear();
            NetProbe.Note($"==== FIELDWATCH noisy fields {(includeNoisy ? "INCLUDED" : "MUTED")} ====");
        }

        try
        {
            ReportConnection();
        }
        catch (Exception exception)
        {
            Complain("connection probe", exception);
        }

        if (!enabled)
        {
            return;
        }

        try
        {
            Rebind(controller);
        }
        catch (Exception exception)
        {
            Complain("rebind", exception);
            return;
        }

        // One bad target must not take the others down with it.
        foreach (var target in Targets)
        {
            try
            {
                Diff(target);
            }
            catch (Exception exception)
            {
                Complain($"target {target.Label}", exception);
            }
        }
    }

    private static void Complain(string what, Exception exception)
    {
        if (complaints.TryGetValue(what, out var count) && count > 3)
        {
            return;
        }

        complaints[what] = count + 1;
        NetProbe.Note($"# fieldwatch: {what} failed: {exception.Message}");
    }

    /// <summary>
    /// The outgoing capture records packets as they are built, which happens whether or not the
    /// socket is up. Sampling the TcpClient tells us whether any of it actually left the process.
    /// </summary>
    private static void ReportConnection()
    {
        if (!enabled && !NetProbe.Capturing)
        {
            return;
        }

        if (Time.unscaledTime < nextConnectionReport)
        {
            return;
        }

        nextConnectionReport = Time.unscaledTime + 5f;

        var clientType = Type.GetType("Client, Assembly-CSharp");
        var instance = clientType?.GetProperty(nameof(Raw.Client.Offsets.LPCJFAOOIKA), BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (instance == null)
        {
            NetProbe.Note($"# net: Client.{nameof(Raw.Client.Offsets.LPCJFAOOIKA)} is null (not in a room)");
            return;
        }

        var tcp = instance.GetType().GetProperty(nameof(Raw.Client.Offsets.HPDGDLFMEKI))?.GetValue(instance);
        var connected = tcp == null ? "(no socket)" : tcp.GetType().GetProperty("Connected")?.GetValue(tcp)?.ToString();
        NetProbe.Note($"# net: TcpClient.Connected = {connected}");
    }

    private static void Rebind(Controll? controller)
    {
        if (Targets.Count > 0 && Time.unscaledTime < nextRebind)
        {
            return;
        }

        nextRebind = Time.unscaledTime + 3f;

        var player = Controll.HGAODFPBGLB;
        var weapon = ResolveActiveWeapon(player);
        var entry = ResolveLoadoutEntry(player);

        if (Targets.Count == 0)
        {
            if (player != null)
            {
                Add(new Target("player", player.GetType(), player));
            }

            Add(new Target("Controll.static", typeof(Controll), null));

            if (controller != null)
            {
                Add(new Target("Controll", controller.GetType(), controller));
            }

            // Ammo and the fire/reload gates are on neither the player entity nor Controll: at
            // every reload completion in capture net-20260823-020010.log, not one watched field
            // moved except the reload flag itself. They live on the weapon, so watch it too.
            if (weapon != null)
            {
                Add(new Target("weapon", weapon.GetType(), weapon));
            }

            if (entry != null)
            {
                Add(new Target("loadout", entry.GetType(), entry));
            }

            return;
        }

        // These objects are swapped out on respawn and on weapon switch; keep the bindings
        // pointed at whatever is live now.
        foreach (var target in Targets)
        {
            switch (target.Label)
            {
                case "player" when player != null:
                    target.Instance = player;
                    break;
                case "Controll" when controller != null:
                    target.Instance = controller;
                    break;
                case "weapon" when weapon != null:
                    target.Instance = weapon;
                    break;
                case "loadout" when entry != null:
                    target.Instance = entry;
                    break;
            }
        }

        // A weapon that only appeared after the first bind still deserves watching.
        if (weapon != null && !Targets.Exists(t => t.Label == "weapon"))
        {
            Add(new Target("weapon", weapon.GetType(), weapon));
        }

        if (entry != null && !Targets.Exists(t => t.Label == "loadout"))
        {
            Add(new Target("loadout", entry.GetType(), entry));
        }
    }

    /// <summary>The equipped <c>CGJPBNDDPIN</c>, via the player's ActiveWeapon property.</summary>
    private static object? ResolveActiveWeapon(object? player)
    {
        if (player == null)
        {
            return null;
        }

        try
        {
            return player.GetType().GetProperty("JPGGPPLOOML")?.GetValue(player);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The current <c>FPNENMKEFBB</c> loadout entry for the active slot.</summary>
    private static object? ResolveLoadoutEntry(object? player)
    {
        if (player == null)
        {
            return null;
        }

        try
        {
            var type = player.GetType();
            if (type.GetProperty("KPNAADPGNCP")?.GetValue(player) is not System.Collections.IList loadout)
            {
                return null;
            }

            var slotRaw = type.GetProperty("MOPBMENEGLN")?.GetValue(player);
            if (slotRaw == null)
            {
                return null;
            }

            var slot = Convert.ToInt32(slotRaw, CultureInfo.InvariantCulture);
            return slot >= 0 && slot < loadout.Count ? loadout[slot] : null;
        }
        catch
        {
            return null;
        }
    }

    private static void Add(Target target)
    {
        var candidates = new List<PropertyInfo>();
        var flags = BindingFlags.Public | (target.Instance == null ? BindingFlags.Static : BindingFlags.Instance);

        foreach (var property in target.Type.GetProperties(flags))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0 || !IsNumeric(property.PropertyType))
            {
                continue;
            }

            if (!includeNoisy && Noisy.Contains(property.Name))
            {
                continue;
            }

            candidates.Add(property);
        }

        target.Props = candidates.ToArray();
        target.Previous = new double[target.Props.Length];
        target.Seeded = new bool[target.Props.Length];
        Targets.Add(target);
        NetProbe.Note($"# fieldwatch {target.Label}: {target.Props.Length} numeric fields on {target.Type.Name}");
    }

    private static bool IsNumeric(Type t) =>
        t == typeof(float) || t == typeof(double) || t == typeof(int) || t == typeof(uint)
        || t == typeof(short) || t == typeof(ushort) || t == typeof(byte) || t == typeof(sbyte)
        || t == typeof(long) || t == typeof(ulong) || t == typeof(bool);

    private static void Diff(Target target)
    {
        for (var i = 0; i < target.Props.Length; i++)
        {
            double now;
            try
            {
                var raw = target.Props[i].GetValue(target.Instance);
                if (raw == null)
                {
                    continue;
                }

                now = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            }
            catch
            {
                continue;
            }

            if (!target.Seeded[i])
            {
                target.Previous[i] = now;
                target.Seeded[i] = true;
                continue;
            }

            if (Math.Abs(now - target.Previous[i]) < 0.0001)
            {
                continue;
            }

            NetProbe.Note(string.Format(
                CultureInfo.InvariantCulture,
                "fw {0,-15} {1,-14} {2,12:0.####} -> {3:0.####}",
                target.Label,
                target.Props[i].Name,
                target.Previous[i],
                now));
            target.Previous[i] = now;
        }
    }
}
