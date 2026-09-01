# Blockpost Legacy SDK

This is an auto-generated offset/method SDK for the key Blockpost `GameAssembly.dll` classes. It turns the obfuscated IL2Cpp names into something usable, with human-readable aliases on top.

## Regenerating

Run the full pipeline from the repo root (it reads `.tools/Il2CppDumper/dump.cs`):

```powershell
python Tools/build_sdk.py
```

Or run the individual steps:

```powershell
python Tools/dump_analyzer.py
python Tools/add_aliases.py
python Tools/auto_alias.py
python Tools/generate_sdk.py
python Tools/verify_sdk.py
dotnet build -p:AutoDeploy=false
```

You can add classes by editing `Tools/generate_sdk.py` (`TARGET_CLASSES`) or `Tools/sdk_aliases.json` for human aliases.

## Layout

- `Sdk/Generated/*.cs` — one **flat** file per game class, all in the
  `BlockpostTrainer.Sdk.Raw` namespace. `Raw` is a namespace, not a directory. Each has:
  - `TypeDefIndex` and `OriginalName` metadata.
  - `Offsets` — field offsets.
  - `Methods` — method VAs.
  - `Properties` — property names, for reflection-based access.
- `Sdk/Generated/Aliases.cs` — human aliases for the fields/methods/properties you care about.
- `Sdk/Generated/SdkIndex.cs` — lookup tables `ByOriginalName`, `ByHumanName`, and `ByTypeDefIndex`.

## Usage

Use the human aliases when possible:

```csharp
using static BlockpostTrainer.Sdk.Aliases;

var healthOffset = Player.Health;      // KBBBHJDINCB.FDOJDJLIGLF
var fireMethod   = Weapon.Fire;        // PLH.CDEGJOBLOFO
var sendHitReport = Network.SendHitReport; // Client.AHLDAPJEJNC
```

For the raw obfuscated names, use the `Raw` namespace:

```csharp
using BlockpostTrainer.Sdk;

var spread = Raw.KBBBHJDINCB.Offsets.FGFKPMPLNKO;
var fireVa = Raw.PLH.Methods.CDEGJOBLOFO;
```

## Key aliases

| Alias            | Meaning                                      |
|------------------|----------------------------------------------|
| `Player.Health`  | Main player health (`KBBBHJDINCB.FDOJDJLIGLF`) |
| `Player.Spread`  | Current weapon spread (`KBBBHJDINCB.FGFKPMPLNKO`) |
| `Player.IsMain`  | Local-player flag (`KBBBHJDINCB._LCEIAGLFFJN_k__BackingField`) |
| `Player.Team`    | Team id (`KBBBHJDINCB.MMMGPDBMOLM`) |
| `Game.SpreadVector` | `Controll.LFCCGHJKFNK` (guard vector for `PLH.CDEGJOBLOFO`) |
| `Game.CameraForward` | `Controll.JIPNKAGPCGK` |
| `Game.MuzzleForward` | `Controll.FLILDBNOFMK` |
| `Game.MainPlayer` | `Controll.HGAODFPBGLB` |
| `Weapon.Fire`    | `PLH.CDEGJOBLOFO` |
| `Network.SendHitReport` | `Client.AHLDAPJEJNC` |
| `Network.ProcessPacket` | `Client.FPIDGCHIEMJ` |
| `Network.Flush` | `Client.HKOFHOANEJD` |
| `Net.Begin`      | `NET.LPAPGKDAENI(byte, byte)` |
| `Hit.TargetId`   | `DMHBMAAFCFJ.AMGLIHOLNJE` |
| `Hit.BodyPart`   | `DMHBMAAFCFJ.KMCHFGKKICG` |
| `Hit.Point`      | `DMHBMAAFCFJ.HDAFLOCABNG` |
| `Game.IsReloading` | `Controll.DJACNOGOCKD` |
| `Game.ReloadStartTime` | `Controll.FBINCNDDPAO` |
| `Game.ReloadEndTime` | `Controll.ILGHFLMKMCO` — perfect reload subtracts from this |
| `Game.ReloadMinigameResult` | `Controll.JBKBOPCCIBM` — `0` none / `1` perfect / `2` failed |

See `PROTOCOL.md` §11 for the full reload model.

## Inventory / Loadout

The full weapon table and current loadout are in `GUIInv`:

| Offset | Type | Meaning |
|--------|------|---------|
| `Raw.GUIInv.Offsets.OIHNJCKDOIG` | `NAHLLMJMOED[]` | All weapon definitions at runtime |
| `Raw.GUIInv.Offsets.KNCJNHILDLJ` | `List<FPNENMKEFBB>` | Current loadout entries |
| `Raw.GUIInv.Offsets.KAOCDKAKFEF` | `CGJPBNDDPIN` | Currently selected weapon instance |
| `Raw.GUIInv.Offsets.PJMELMGMNDO` | `FPNENMKEFBB` | Currently selected loadout entry |

Key `GUIInv` methods (signature from `Sdk/Generated/GUIInv.cs`):

| Method | Signature | Likely purpose |
|--------|-----------|----------------|
| `Raw.GUIInv.Methods.FPIJPCOKIEC` | `NAHLLMJMOED FPIJPCOKIEC(int)` | Get weapon data by id |
| `Raw.GUIInv.Methods.FPIJPCOKIEC_2` | `NAHLLMJMOED FPIJPCOKIEC(string)` | Get weapon data by codename |
| `Raw.GUIInv.Methods.CAMMBHLEFOG` | `NAHLLMJMOED CAMMBHLEFOG(string)` | Get weapon data by name |
| `Raw.GUIInv.Methods.NJDNGJNPHNE` | `NAHLLMJMOED NJDNGJNPHNE(int)` | Get weapon data by id (alt) |
| `Raw.GUIInv.Methods.PNNEFOPFCHF` | `FPNENMKEFBB PNNEFOPFCHF(int)` | Get/create loadout entry by id |
| `Raw.GUIInv.Methods.IKFBJEFBBLH` | `FPNENMKEFBB IKFBJEFBBLH(int)` | Get loadout entry by id (alt) |
| `Raw.GUIInv.Methods.KHABBLDBFKK` | `FPNENMKEFBB KHABBLDBFKK(ulong)` | Get loadout entry by unique id |
| `Raw.GUIInv.Methods.OHNGCKOFHFB` | `void OHNGCKOFHFB(ulong, int)` | Likely add/change loadout entry |
| `Raw.GUIInv.Methods.HPLIKAOFIJE` | `void HPLIKAOFIJE(ulong, int)` | Likely remove/change loadout entry |
| `Raw.GUIInv.Methods.AJHBAOEHHOC` | `void AJHBAOEHHOC()` | Likely refresh/apply loadout |
| `Raw.GUIInv.Methods.BMEBBEIKMCP` | `void BMEBBEIKMCP()` | Likely refresh/apply loadout (alt) |
| `Raw.GUIInv.Methods.PBNDHAJICEH` | `void PBNDHAJICEH()` | Likely refresh/apply loadout (alt) |
| `Raw.GUIInv.Methods.FKBPKKPKOEK` | `void FKBPKKPKOEK()` | Likely refresh/apply loadout (alt) |

Loadout/weapon network senders (in `Client`):

| Method | Signature | Likely purpose |
|--------|-----------|----------------|
| `Raw.Client.Methods.MGPBPDIGDBO` | `void MGPBPDIGDBO(NAHLLMJMOED)` | Likely send `0x08 weapondata` |
| `Raw.Client.Methods.ANICPIFFOIK` | `void ANICPIFFOIK(int, int)` | Likely send `0x09 loadout` |

## Method overloads

If a class has overloaded methods, the second/third overload is named with a `_2`, `_3` suffix (e.g. `LPAPGKDAENI_2`). The first overload keeps the original name.

## Negative results are named too

An alias is not only for fields you want to *use*. Anything **identified** gets a name and a note,
including fields that turned out to be irrelevant — otherwise the next person re-investigates them.

`Player.FootstepIndex` is the example: it cycles `0-4` and changes on every step, so it looks like a
magazine counter at a glance. It cost a capture and an analysis pass to rule out. It is now named,
annotated, and muted in `FieldWatch`, so that cost is paid once.

The same applies to traps: `Player.ActiveWeaponId` and `Player.StatsArray` carry notes saying what
happens if you write them, because both have already corrupted game state.

Rule of thumb: if a session spent effort determining what a field is — or is *not* — that belongs in
`sdk_aliases.json` with a `Notes` entry, not only in a capture log or a commit message.

## Runtime helpers (hand-written, in `Sdk/`)

### `GameAccess.cs`

Central static accessors for global game state: `Players` (`PLH.BAKLNPIEHMI`),
`LocalPlayer` (`Controll.HGAODFPBGLB`), `MainCamera` (`Controll.CDFACGAFFFH`),
`Game` (`Controll.LPCJFAOOIKA`), `AllWeapons` (`GUIInv.OIHNJCKDOIG`),
`LoadoutEntries` (`GUIInv.KNCJNHILDLJ`), `IsInMatch`.

Provenance: the native ImGui menu (`imgui new menu/ImGui DirectX 11 Kiero Hook`) reaches the
same data through raw GameAssembly static pointer chains (`Utils/Offsets.hpp`:
`entityListOffsets = {0x00C7BA08, 0x5C, 0xC}`, `controllOffsets = {0x00C7B94C, 0x5C}`).
Under BepInEx we read the statics through the interop layer by name instead — no absolute
module addresses, survives any module layout change.

### `Validator.cs`

Runtime self-checks that catch game updates before they corrupt state:

- `Validator.CheckInteropShape(Log)` — runs at plugin load. Verifies every SDK anchor
  member (19 anchors across `Controll`/`KBBBHJDINCB`/`PLH`/`GUIInv`) still exists on its
  interop type. After a game update, interop regeneration drops renamed members and this
  fails loudly instead of the trainer misbehaving mid-match.
- `Validator.CheckFieldOffsets(Log, player)` — runs on every `F10`/`F11` field probe.
  Unsafe-reads anchor fields at their SDK offsets and compares with the interop property
  reads; any drift is logged with both values. Also logs `Player` offsets `0x44` vs `0x60`
  side by side (see below).

## Cross-validation against the native ImGui menu (2026-09)

The C++ menu's `Utils/Offsets.hpp` was checked against `dump.cs`. Agreements:

| Native offset | Our dump field | Match |
|---|---|---|
| `isMainPlayer 0x8` | `KBBBHJDINCB.<LCEIAGLFFJN>k__BackingField` | yes |
| `team 0x24` | `MMMGPDBMOLM` | yes |
| `health 0x38` | `FDOJDJLIGLF` | yes |
| `yaw 0x3C` / `pitch 0x40` (Controll) | `NAKNALFCOIF` / `IGLCENGMMMJ` | yes |
| `name 0x2C` | `HOOJGPCGFNB` — now aliased `Player.Nickname` | yes (vs `NHHBNNBDDIA` 0x14 — possibly login vs display name) |

Open question: the native menu reads **`0x60` (`FLILDBNOFMK`) as the player position** for
ESP/aimbot, while our alias calls that field `MuzzleForward` and uses `0x44`
(`OOMJGHCFODI`) as position. Both are `Vector3`. The validator's position probe logs both
values on every field probe — next in-game run settles it.

## Voxel build/mine system (the Minecraft side)

Blockpost is a Minecraft-style voxel world wrapped in a shooter. The full place/break API is
static on `VoxelMap` (aliased `VoxelWorld`) — no raycast plumbing needed:

| Alias | Signature | Minecraft equivalent |
|---|---|---|
| `VoxelWorld.GetBlock` | `int GetBlock(int x, int y, int z)` | block state probe (`0` = air) |
| `VoxelWorld.SetBlock` | `bool SetBlock(float x, float y, float z, Color color, int flag)` | setblock + paint |
| `VoxelWorld.SetBlockNearDirtyUpdate` | `void(int x, int y, int z)` | re-render chunk |
| `VoxelWorld.RenderDirty` | `void()` | rebuild all dirty chunk meshes |
| `VoxelWorld.BlockAtWorldPos` | `int NLMJPADEKOB(Vector3 worldPos)` | block under a world position |
| `VoxelWorld.SetEnt` / `DelEnt` | entity markers embedded in the map | armor stands |

World storage: `VoxelMap.chunks` is a `[,,]` of `VoxelChunk` (`LOMEPCOKKCB`), each holding
`int[,,] BlockIds` and a parallel `Color[,,] BlockColors` — direct writes + `RenderDirty` are
the "creative mode" path. `VoxelMap.mapdata` is the raw compressed map byte blob.

`GameAccess` exposes one-call wrappers: `BlockAt`, `PlaceBlock`, `RefreshBlock`,
`RefreshDirtyChunks`, `MapBuilder` (`Builder.cs` singleton: `ToolMode`, `CurrentBlock`,
`BlockCursor`).

Destruction FX/timing goes through `DestructionManager` (`DM`): `DestroyList` plus 32
`ScheduleDestroy` statics, all `void(GameObject, float delay)`.

Weapon `WeaponData` (`NAHLLMJMOED`) carries 10 integer stats (`Stat1`..`Stat10`, offsets
`0x14`–`0x3C`) — among them damage, fire-rate and magazine size. Remove-attack-cooldown can
be done today by clamping `Controll.LCMOBPPHLLM` per frame (already implemented); editing the
fire-rate stat itself needs one correlation run against known weapons (see `HANDOFF.md`).
