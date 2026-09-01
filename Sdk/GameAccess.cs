using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BlockpostTrainer.Sdk;

/// <summary>
/// Central runtime accessors for the game's global state.
///
/// The native ImGui menu ("imgui new menu") reaches these through raw GameAssembly
/// static pointer chains (Utils/Offsets.hpp: entityListOffsets 0x00C7BA08 -> +0x5C -> +0xC,
/// controllOffsets 0x00C7B94C -> +0x5C). Under BepInEx the same data is reachable directly
/// through the interop layer by static field name, which is both version-stable (the interop
/// assemblies are regenerated on every dump) and does not depend on absolute module addresses.
/// </summary>
internal static class GameAccess
{
    /// <summary>
    /// All players in the current match (PLH.BAKLNPIEHMI, static at offset 0xC).
    /// Null or empty while not in a match.
    /// </summary>
    internal static KBBBHJDINCB[]? Players
    {
        get
        {
            try
            {
                return PLH.BAKLNPIEHMI;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Local player row (Controll.HGAODFPBGLB, static at offset 0x140).
    /// Null while not spawned in.
    /// </summary>
    internal static KBBBHJDINCB? LocalPlayer
    {
        get
        {
            try
            {
                return Controll.HGAODFPBGLB;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Main camera (Controll.CDFACGAFFFH, static at offset 0x88).
    /// </summary>
    internal static Camera? MainCamera
    {
        get
        {
            try
            {
                return Controll.CDFACGAFFFH;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Controll singleton instance (Controll.LPCJFAOOIKA, static at offset 0x0).
    /// </summary>
    internal static Controll? Game
    {
        get
        {
            try
            {
                return Controll.LPCJFAOOIKA;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Full weapon catalog (GUIInv.OIHNJCKDOIG static).
    /// </summary>
    internal static NAHLLMJMOED[]? AllWeapons
    {
        get
        {
            try
            {
                return GUIInv.OIHNJCKDOIG;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Owned loadout entries (GUIInv.KNCJNHILDLJ static).
    /// </summary>
    internal static Il2CppSystem.Collections.Generic.List<FPNENMKEFBB>? LoadoutEntries
    {
        get
        {
            try
            {
                return GUIInv.KNCJNHILDLJ;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// True while spawned in a live match (players list exists and local player row exists).
    /// </summary>
    internal static bool IsInMatch
    {
        get
        {
            var players = Players;
            return players != null && players.Length > 0 && LocalPlayer != null;
        }
    }

    // ---- Voxel world (the Minecraft side) ----

    /// <summary>
    /// Map builder singleton (Builder.cs static). Editor/build tool: toolmode, current block,
    /// blockCursor. Null when the builder tool is not instantiated.
    /// </summary>
    internal static Builder? MapBuilder
    {
        get
        {
            try
            {
                return Builder.cs;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Block id at integer block coordinates. 0 = air. Wraps VoxelMap.GetBlock.
    /// </summary>
    internal static int BlockAt(int x, int y, int z)
    {
        try
        {
            return VoxelMap.GetBlock(x, y, z);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Minecraft-style setblock at world-space floats with a paint color, through the game's
    /// own write path (VoxelMap.SetBlock) so chunk meshes update.
    /// </summary>
    internal static bool PlaceBlock(float x, float y, float z, Color color)
    {
        try
        {
            return VoxelMap.SetBlock(x, y, z, color, 0);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Re-render the chunk containing the block after direct BlockIds edits
    /// (VoxelMap.SetBlockNearDirtyUpdate).
    /// </summary>
    internal static void RefreshBlock(int x, int y, int z)
    {
        try
        {
            VoxelMap.SetBlockNearDirtyUpdate(x, y, z);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Rebuild meshes for all dirty chunks (VoxelMap.RenderDirty).
    /// </summary>
    internal static void RefreshDirtyChunks()
    {
        try
        {
            VoxelMap.RenderDirty();
        }
        catch
        {
        }
    }

    // ---- Inventory / loadout ----

    /// <summary>
    /// Entry currently selected in the inventory UI (GUIInv.MHLJKCMDJGG static).
    /// </summary>
    internal static FPNENMKEFBB? SelectedLoadoutEntry
    {
        get
        {
            try
            {
                return GUIInv.MHLJKCMDJGG;
            }
            catch
            {
                return null;
            }
        }
        set
        {
            try
            {
                GUIInv.MHLJKCMDJGG = value;
                GUIInv.PJMELMGMNDO = value;
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Auth manager singleton + login state (GP statics). Token authenticates against the
    /// underdogs.ru backend over the game TCP protocol.
    /// </summary>
    internal static GP? Auth
    {
        get
        {
            try
            {
                return GP.cs;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Find a weapon definition by codename in the full catalog (GUIInv.AllWeapons).
    /// </summary>
    internal static NAHLLMJMOED? FindWeapon(string codename)
    {
        try
        {
            var all = GUIInv.OIHNJCKDOIG;
            if (all == null)
            {
                return null;
            }

            foreach (var w in all)
            {
                if (w != null && w.OJEKKFDIKMG == codename)
                {
                    return w;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// Inject a weapon the account does not own into the client-side loadout list and select
    /// it. CLIENT-SIDE ONLY: the server keeps its own inventory copy, so this survives until
    /// the next server inventory sync / may be rejected on slot switch (server validation of
    /// op 0x0F is untested — BACKLOG item 11).
    /// </summary>
    internal static FPNENMKEFBB? ForceLoadoutEntry(string codename)
    {
        try
        {
            var data = FindWeapon(codename);
            var entries = LoadoutEntries;
            if (data == null || entries == null)
            {
                return null;
            }

            foreach (var existing in entries)
            {
                if (existing != null && existing.ADMGNABJBNM == data)
                {
                    SelectedLoadoutEntry = existing;
                    return existing;
                }
            }

            var entry = new FPNENMKEFBB((ulong)data.HAFMINBJCGN, data);
            entries.Add(entry);
            SelectedLoadoutEntry = entry;
            return entry;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Force the active weapon slot on the local player row (client-declared; sent via op 0x0F).
    /// </summary>
    internal static bool ForceSlot(int slot)
    {
        try
        {
            var player = LocalPlayer;
            if (player == null)
            {
                return false;
            }

            player.MOPBMENEGLN = slot;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
