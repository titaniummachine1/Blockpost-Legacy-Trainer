using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace BlockpostTrainer;

/// <summary>
/// Whole-assembly static value scan.
///
/// Eleven hand-picked targets, 351 values, and the magazine counter was in none of them. Rather
/// than keep guessing which object owns it, this enumerates every type in the game assembly and
/// records every readable static numeric field. Two scans taken at known, different ammo counts
/// then identify the field by value, with no prior belief about where it lives.
///
/// This is the generalisation the curated FieldWatch targets should have been from the start.
/// </summary>
internal static class GlobalScan
{
    // Ammo-plausible range. Excludes the huge tick counters and the float soup, which are the
    // bulk of the assembly's statics.
    private const double MinValue = 0;
    private const double MaxValue = 500;
    private const int MaxTypes = 4000;

    private static PropertyInfo[][]? cache;
    private static Type[]? types;
    private static int scanCounter;

    /// <summary>Call once per frame from the Update hook.</summary>
    internal static void Tick()
    {
        if (!Input.GetKeyDown(KeyCode.F3))
        {
            return;
        }

        try
        {
            Run();
        }
        catch (Exception exception)
        {
            NetProbe.Note($"# globalscan failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void Build()
    {
        var assembly = typeof(Controll).Assembly;
        var found = new List<Type>();
        var props = new List<PropertyInfo[]>();

        foreach (var type in assembly.GetTypes())
        {
            if (found.Count >= MaxTypes)
            {
                break;
            }

            PropertyInfo[] all;
            try
            {
                all = type.GetProperties(BindingFlags.Public | BindingFlags.Static);
            }
            catch
            {
                continue;
            }

            var keep = new List<PropertyInfo>();
            foreach (var p in all)
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                var t = p.PropertyType;
                if (t == typeof(int) || t == typeof(short) || t == typeof(byte) || t == typeof(uint))
                {
                    keep.Add(p);
                }
            }

            if (keep.Count > 0)
            {
                found.Add(type);
                props.Add(keep.ToArray());
            }
        }

        types = found.ToArray();
        cache = props.ToArray();
        NetProbe.Note($"# globalscan: {types.Length} types with static int fields");
    }

    private static void Run()
    {
        if (cache == null || types == null)
        {
            Build();
        }

        if (cache == null || types == null)
        {
            return;
        }

        scanCounter++;
        NetProbe.Note($"#### GLOBALSCAN #{scanCounter} ####");

        var emitted = 0;
        for (var i = 0; i < types.Length; i++)
        {
            var parts = new List<string>();
            foreach (var p in cache[i])
            {
                try
                {
                    var raw = p.GetValue(null);
                    if (raw == null)
                    {
                        continue;
                    }

                    var d = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                    if (d >= MinValue && d <= MaxValue)
                    {
                        parts.Add(string.Format(CultureInfo.InvariantCulture, "{0}={1:0}", p.Name, d));
                    }
                }
                catch
                {
                    // Statics on uninitialised types throw; skip them.
                }
            }

            if (parts.Count > 0)
            {
                NetProbe.Note($"#G {types[i].Name}: {string.Join(" ", parts)}");
                emitted++;
            }
        }

        NetProbe.Note($"# globalscan #{scanCounter} emitted {emitted} types");
    }
}
