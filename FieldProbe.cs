using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using BlockpostTrainer.Sdk;
using Raw = BlockpostTrainer.Sdk.Raw;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BlockpostTrainer;

/// <summary>
/// Runtime field probe that dumps all field values on key game classes.
/// Triggered via a hotkey to capture a snapshot of the game state, which can
/// then be used to assign true semantic names to type-prefixed aliases.
/// </summary>
internal static class FieldProbe
{
    private static ManualLogSource? log;
    private static bool enabled;
    private static float lastProbeTime;
    private static readonly Dictionary<string, object?> lastValues = new();

    internal static bool Enabled => enabled;

    internal static void Initialize(ManualLogSource source)
    {
        log = source;
    }

    internal static void Toggle()
    {
        enabled = !enabled;
        log?.LogInfo($"[FieldProbe] {(enabled ? "enabled" : "disabled")}");
        if (enabled)
        {
            ProbeAll();
        }
    }

    /// <summary>
    /// Run a one-shot probe of all key game classes and log every field.
    /// </summary>
    internal static void ProbeAll()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Field Probe Snapshot ===");
            sb.AppendLine($"Time: {DateTime.Now:HH:mm:ss.fff}");

            ProbeControll(sb);
            ProbePlayer(sb);
            ProbeMovement(sb);
            ProbePLH(sb);

            var output = sb.ToString();
            log?.LogInfo(output);
            // Also write to a file for easy analysis
            var path = System.IO.Path.Combine(
                BepInEx.Paths.BepInExRootPath, "captures",
                $"fieldprobe-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            try
            {
                System.IO.Directory.CreateDirectory(
                    System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.WriteAllText(path, output);
                log?.LogInfo($"[FieldProbe] Written to {path}");
            }
            catch { }
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[FieldProbe] Probe failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Continuous monitoring — called from Update. Logs when fields change.
    /// </summary>
    internal static void Tick()
    {
        if (!enabled)
        {
            return;
        }

        // Only probe every 2 seconds to avoid flooding the log.
        if (UnityEngine.Time.unscaledTime - lastProbeTime < 2f)
        {
            return;
        }

        lastProbeTime = UnityEngine.Time.unscaledTime;
        ProbeChanges();
    }

    /// <summary>
    /// Probe fields and log only the ones that changed since last probe.
    /// </summary>
    private static void ProbeChanges()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== Field Changes @ {UnityEngine.Time.unscaledTime:F1}s ===");

            var changes = 0;
            changes += ProbeControllChanges(sb);
            changes += ProbePlayerChanges(sb);

            if (changes > 0)
            {
                log?.LogInfo(sb.ToString());
            }
        }
        catch { }
    }

    // ---- Controll probing ----

    private static void ProbeControll(StringBuilder sb)
    {
        sb.AppendLine("\n--- Controll (static fields) ---");
        var type = typeof(Controll);
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            try
            {
                var val = f.GetValue(null);
                sb.AppendLine($"  {f.Name} ({f.FieldType.Name}) = {FormatValue(val)}");
            }
            catch { }
        }

        sb.AppendLine("\n--- Controll (instance fields) ---");
        try
        {
            var instance = Controll.LPCJFAOOIKA;
            if (instance != null)
            {
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    try
                    {
                        var val = f.GetValue(instance);
                        sb.AppendLine($"  {f.Name} ({f.FieldType.Name}) = {FormatValue(val)}");
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private static int ProbeControllChanges(StringBuilder sb)
    {
        var changes = 0;
        var type = typeof(Controll);

        // Static fields
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            try
            {
                var val = f.GetValue(null);
                var key = "Controll." + f.Name;
                var prev = lastValues.GetValueOrDefault(key);
                var curr = val?.ToString() ?? "null";
                if (prev?.ToString() != curr)
                {
                    sb.AppendLine($"  CHG Controll.{f.Name}: {prev} -> {curr}");
                    lastValues[key] = val;
                    changes++;
                }
            }
            catch { }
        }

        // Instance fields
        try
        {
            var instance = Controll.LPCJFAOOIKA;
            if (instance != null)
            {
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    try
                    {
                        var val = f.GetValue(instance);
                        var key = "Controll.inst." + f.Name;
                        var prev = lastValues.GetValueOrDefault(key);
                        var curr = val?.ToString() ?? "null";
                        if (prev?.ToString() != curr)
                        {
                            sb.AppendLine($"  CHG Controll.inst.{f.Name}: {prev} -> {curr}");
                            lastValues[key] = val;
                            changes++;
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        return changes;
    }

    // ---- Player probing ----

    private static void ProbePlayer(StringBuilder sb)
    {
        sb.AppendLine("\n--- Player (main player) ---");
        try
        {
            var player = Controll.HGAODFPBGLB;
            if (player == null)
            {
                sb.AppendLine("  (no main player)");
                return;
            }

            var type = typeof(Raw.KBBBHJDINCB);
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                try
                {
                    var val = f.GetValue(player);
                    sb.AppendLine($"  {f.Name} ({f.FieldType.Name}) = {FormatValue(val)}");
                }
                catch { }
            }
        }
        catch { }
    }

    private static int ProbePlayerChanges(StringBuilder sb)
    {
        var changes = 0;
        try
        {
            var player = Controll.HGAODFPBGLB;
            if (player == null) return 0;

            var type = typeof(Raw.KBBBHJDINCB);
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                try
                {
                    var val = f.GetValue(player);
                    var key = "Player." + f.Name;
                    var prev = lastValues.GetValueOrDefault(key);
                    var curr = val?.ToString() ?? "null";
                    if (prev?.ToString() != curr)
                    {
                        sb.AppendLine($"  CHG Player.{f.Name}: {prev} -> {curr}");
                        lastValues[key] = val;
                        changes++;
                    }
                }
                catch { }
            }
        }
        catch { }
        return changes;
    }

    // ---- Movement probing ----

    private static void ProbeMovement(StringBuilder sb)
    {
        sb.AppendLine("\n--- Movement (static fields) ---");
        var type = typeof(Movement);
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            try
            {
                var val = f.GetValue(null);
                sb.AppendLine($"  {f.Name} ({f.FieldType.Name}) = {FormatValue(val)}");
            }
            catch { }
        }
    }

    // ---- PLH probing ----

    private static void ProbePLH(StringBuilder sb)
    {
        sb.AppendLine("\n--- PLH (static fields) ---");
        var type = typeof(PLH);
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            try
            {
                var val = f.GetValue(null);
                sb.AppendLine($"  {f.Name} ({f.FieldType.Name}) = {FormatValue(val)}");
            }
            catch { }
        }

        sb.AppendLine("\n--- PLH (instance fields) ---");
        try
        {
            // PLH has a singleton — try to find it
            var instanceField = type.GetField("LPCJ", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (instanceField != null)
            {
                var instance = instanceField.GetValue(null);
                if (instance != null)
                {
                    foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        try
                        {
                            var val = f.GetValue(instance);
                            sb.AppendLine($"  {f.Name} ({f.FieldType.Name}) = {FormatValue(val)}");
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
    }

    private static string FormatValue(object? val)
    {
        if (val == null) return "null";
        if (val is UnityEngine.Vector3 v)
            return $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
        if (val is UnityEngine.Quaternion q)
            return $"({q.x:F2}, {q.y:F2}, {q.z:F2}, {q.w:F2})";
        if (val is Array arr)
            return $"[{arr.Length}]";
        // Il2Cpp arrays — just try to get Length via reflection
        var valType = val.GetType();
        if (valType.IsArray || valType.Name.Contains("Array"))
        {
            try
            {
                var lenProp = valType.GetProperty("Length");
                if (lenProp != null)
                    return $"[{lenProp.GetValue(val)}]";
            }
            catch { }
        }
        return val.ToString() ?? "null";
    }
}
