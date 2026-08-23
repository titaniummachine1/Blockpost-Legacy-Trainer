# Blockpost Legacy SDK

This is an auto-generated offset/method SDK for the key Blockpost `GameAssembly.dll` classes. It turns the obfuscated IL2Cpp names into something usable, with human-readable aliases on top.

## Regenerating

Run the generator from the repo root (it reads `Il2CppDumper/dump.cs`):

```powershell
python Tools/generate_sdk.py
```

You can add classes by editing `Tools/generate_sdk.py` (`TARGET_CLASSES`) or `Tools/sdk_aliases.json` for human aliases.

## Layout

- `Sdk/Generated/` — raw, obfuscated classes and their offsets/method VAs.
  - `Sdk/Generated/Raw/*.cs` — one file per game class. Each has:
    - `Offsets` static class with field offsets.
    - `Methods` static class with method VAs.
  - `Sdk/Generated/Aliases.cs` — human aliases for the fields/methods you care about.

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

## Method overloads

If a class has overloaded methods, the second/third overload is named with a `_2`, `_3` suffix (e.g. `LPAPGKDAENI_2`). The first overload keeps the original name.
