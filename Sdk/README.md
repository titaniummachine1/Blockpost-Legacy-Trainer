# Blockpost Legacy SDK

This is an auto-generated offset/method SDK for the key Blockpost `GameAssembly.dll` classes. It turns the obfuscated IL2Cpp names into something usable, with human-readable aliases on top.

## Regenerating

Run the generator from the repo root (it reads `.tools/Il2CppDumper/dump.cs`):

```powershell
python Tools/generate_sdk.py
```

You can add classes by editing `Tools/generate_sdk.py` (`TARGET_CLASSES`) or `Tools/sdk_aliases.json` for human aliases.

## Layout

- `Sdk/Generated/*.cs` — one **flat** file per game class, all in the
  `BlockpostTrainer.Sdk.Raw` namespace. `Raw` is a namespace, not a directory. Each has:
  - `Offsets` — field offsets.
  - `Methods` — method VAs.
  - `Properties` — property names, for reflection-based access.
- `Sdk/Generated/Aliases.cs` — human aliases for the fields/methods/properties you care about.

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
