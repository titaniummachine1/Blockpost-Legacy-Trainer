using System;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using HarmonyLib;
using Raw = BlockpostTrainer.Sdk.Raw;
using UnityEngine;

namespace BlockpostTrainer.Sdk;

/// <summary>
/// Runtime self-checks for the generated SDK.
///
/// The SDK's offset tables are static snapshots of a specific GameAssembly build. After a
/// game update they can silently drift, which corrupts memory reads instead of failing.
/// Two layers of defense:
///
/// 1. CheckInteropShape (safe at plugin load, no game instances needed): verifies the interop
///    assemblies still expose the anchor fields/methods the SDK aliases point at. If the game
///    updated and fields moved or vanished, interop regeneration drops them, and this check
///    fails loudly instead of the trainer misbehaving mid-match.
///
/// 2. CheckFieldOffsets (needs a spawned player; call from a probe key): unsafe-reads each
///    anchor field at its SDK offset and compares against the interop property read. This is
///    the ground-truth check that the raw offsets still land on the same data.
/// </summary>
internal static class Validator
{
    private sealed record Anchor(string InteropType, string Member, string SdkPath);

    private static readonly Anchor[] Anchors =
    {
        // Controll (Game) — statics the trainer hooks depend on
        new("Controll", nameof(Controll.LPCJFAOOIKA), "Raw.Controll.Offsets.LPCJFAOOIKA"),
        new("Controll", nameof(Controll.HGAODFPBGLB), "Raw.Controll.Offsets.HGAODFPBGLB"),
        new("Controll", nameof(Controll.CDFACGAFFFH), "Raw.Controll.Offsets.CDFACGAFFFH"),
        new("Controll", nameof(Controll.NAKNALFCOIF), "Raw.Controll.Offsets.NAKNALFCOIF (yaw)"),
        new("Controll", nameof(Controll.IGLCENGMMMJ), "Raw.Controll.Offsets.IGLCENGMMMJ (pitch)"),
        new("Controll", nameof(Controll.FGGKANNFBDH), "Raw.Controll.Offsets.FGGKANNFBDH (ammo)"),
        new("Controll", nameof(Controll.KJOMABGHAIJ), "Raw.Controll.Offsets.KJOMABGHAIJ (reserve)"),
        new("Controll", nameof(Controll.FBINCNDDPAO), "Raw.Controll.Offsets.FBINCNDDPAO (reload start)"),
        new("Controll", nameof(Controll.ILGHFLMKMCO), "Raw.Controll.Offsets.ILGHFLMKMCO (reload end)"),
        // Player row
        new("KBBBHJDINCB", nameof(KBBBHJDINCB.FDOJDJLIGLF), "Raw.KBBBHJDINCB.Offsets.FDOJDJLIGLF (health)"),
        new("KBBBHJDINCB", nameof(KBBBHJDINCB.MMMGPDBMOLM), "Raw.KBBBHJDINCB.Offsets.MMMGPDBMOLM (team)"),
        new("KBBBHJDINCB", nameof(KBBBHJDINCB.OOMJGHCFODI), "Raw.KBBBHJDINCB.Offsets.OOMJGHCFODI (position)"),
        new("KBBBHJDINCB", nameof(KBBBHJDINCB.FLILDBNOFMK), "Raw.KBBBHJDINCB.Offsets.FLILDBNOFMK (position?/forward?)"),
        new("KBBBHJDINCB", nameof(KBBBHJDINCB.GDEMINMDJAC), "Raw.KBBBHJDINCB.Offsets.GDEMINMDJAC (ammo per slot)"),
        new("KBBBHJDINCB", nameof(KBBBHJDINCB.HOOJGPCGFNB), "Raw.KBBBHJDINCB.Offsets.HOOJGPCGFNB (nickname)"),
        // Weapon system statics
        new("PLH", nameof(PLH.BAKLNPIEHMI), "Raw.PLH.Offsets.BAKLNPIEHMI (players)"),
        new("PLH", nameof(PLH.CDEGJOBLOFO), "Raw.PLH.Methods.CDEGJOBLOFO (fire)"),
        // Inventory statics
        new("GUIInv", nameof(GUIInv.OIHNJCKDOIG), "Raw.GUIInv.Offsets.OIHNJCKDOIG (all weapons)"),
        new("GUIInv", nameof(GUIInv.KNCJNHILDLJ), "Raw.GUIInv.Offsets.KNCJNHILDLJ (loadout)"),
        // Voxel world (build/mine side)
        new("VoxelMap", nameof(VoxelMap.chunks), "Raw.VoxelMap.Offsets.chunks (voxel world)"),
        new("VoxelMap", nameof(VoxelMap.mapdata), "Raw.VoxelMap.Offsets.mapdata (raw map bytes)"),
        new("VoxelMap", nameof(VoxelMap.GetBlock), "Raw.VoxelMap.Methods.GetBlock"),
        new("VoxelMap", nameof(VoxelMap.SetBlock), "Raw.VoxelMap.Methods.SetBlock"),
        new("Builder", nameof(Builder.cs), "Raw.Builder.Offsets.cs (map builder singleton)"),
        new("DM", nameof(DM.destroylist), "Raw.DM.Offsets.destroylist (delayed destruction queue)"),
        // Inventory / auth
        new("GUIInv", nameof(GUIInv.MHLJKCMDJGG), "Raw.GUIInv.Offsets.MHLJKCMDJGG (selected loadout)"),
        new("GUIInv", nameof(GUIInv.KAOCDKAKFEF), "Raw.GUIInv.Offsets.KAOCDKAKFEF (selected weapon)"),
        new("FPNENMKEFBB", nameof(FPNENMKEFBB.ADMGNABJBNM), "Raw.FPNENMKEFBB.Offsets.ADMGNABJBNM (weapon data ref)"),
        new("GP", nameof(GP.auth), "Raw.GP.Offsets.auth (login state)"),
        new("GP", nameof(GP.token), "Raw.GP.Offsets.token (backend auth token)"),
    };

    /// <summary>
    /// Load-time shape check: every SDK anchor member must still exist on its interop type.
    /// Returns the number of failures (0 = SDK still matches the running game build).
    /// </summary>
    internal static int CheckInteropShape(ManualLogSource log)
    {
        var failures = 0;

        foreach (var anchor in Anchors)
        {
            var type = AccessTools.TypeByName(anchor.InteropType);
            if (type == null)
            {
                log?.LogWarning($"[SdkValidator] interop type {anchor.InteropType} not found (interop assemblies missing or game renamed the class)");
                failures++;
                continue;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var exists = type.GetProperty(anchor.Member, flags) != null
                         || type.GetField(anchor.Member, flags) != null
                         || type.GetMethod(anchor.Member, flags) != null;
            if (!exists)
            {
                log?.LogWarning($"[SdkValidator] {anchor.InteropType}.{anchor.Member} no longer exists — game updated, SDK is stale ({anchor.SdkPath})");
                failures++;
            }
        }

        if (failures == 0)
        {
            log?.LogInfo($"[SdkValidator] shape OK: {Anchors.Length} anchors resolved against interop assemblies");
        }
        else
        {
            log?.LogError($"[SdkValidator] {failures} anchor(s) failed — regenerate the SDK: python Tools/build_sdk.py");
        }

        return failures;
    }

    /// <summary>
    /// Ground-truth offset check against a live player row. Unsafe-reads at the SDK offsets and
    /// compares with the interop property values. Logs mismatches with both values so a drift is
    /// obvious immediately. Also logs Position (0x44) vs MuzzleForward (0x60) values to settle
    /// which one is the real position (the native ImGui menu uses 0x60).
    /// Returns the number of mismatches.
    /// </summary>
    internal static unsafe int CheckFieldOffsets(ManualLogSource? log, KBBBHJDINCB player)
    {
        if (player == null || player.Pointer == IntPtr.Zero)
        {
            log?.LogWarning("[SdkValidator] no live player row to validate against");
            return 0;
        }

        var mismatches = 0;
        var basePtr = (long)player.Pointer;

        void CompareInt(string name, int offset, int expected)
        {
            var actual = Marshal.ReadInt32((IntPtr)basePtr, offset);
            if (actual != expected)
            {
                log?.LogWarning($"[SdkValidator] {name} @ 0x{offset:X}: offset read {actual} != interop read {expected}");
                mismatches++;
            }
        }

        void CompareBool(string name, int offset, bool expected)
        {
            var actual = Marshal.ReadByte((IntPtr)basePtr, offset) != 0;
            if (actual != expected)
            {
                log?.LogWarning($"[SdkValidator] {name} @ 0x{offset:X}: offset read {actual} != interop read {expected}");
                mismatches++;
            }
        }

        try
        {
            CompareInt("Health", Raw.KBBBHJDINCB.Offsets.FDOJDJLIGLF, player.FDOJDJLIGLF);
            CompareInt("MaxHealth", Raw.KBBBHJDINCB.Offsets.EFHBKMHCMOH, player.EFHBKMHCMOH);
            CompareInt("Team", Raw.KBBBHJDINCB.Offsets.MMMGPDBMOLM, player.MMMGPDBMOLM);
            CompareInt("Slot", Raw.KBBBHJDINCB.Offsets.MOPBMENEGLN, player.MOPBMENEGLN);
            CompareBool("IsDead", Raw.KBBBHJDINCB.Offsets.CLOEJLAOIGI, player.CLOEJLAOIGI);

            // Position question: 0x44 (assumed position) vs 0x60 (native menu's position).
            var posInterop = player.OOMJGHCFODI;
            var altPosInterop = player.FLILDBNOFMK;
            var posAt44 = Marshal.PtrToStructure<Vector3>((IntPtr)(basePtr + Raw.KBBBHJDINCB.Offsets.OOMJGHCFODI));
            var posAt60 = Marshal.PtrToStructure<Vector3>((IntPtr)(basePtr + Raw.KBBBHJDINCB.Offsets.FLILDBNOFMK));
            log?.LogInfo(
                $"[SdkValidator] position probe: 0x44 via-offset {posAt44} vs interop {posInterop}; " +
                $"0x60 via-offset {posAt60} vs interop {altPosInterop}");

            if (mismatches == 0)
            {
                log?.LogInfo("[SdkValidator] field offsets OK on live player row");
            }
        }
        catch (Exception ex)
        {
            log?.LogError($"[SdkValidator] offset check crashed: {ex.Message}");
            mismatches++;
        }

        return mismatches;
    }
}
