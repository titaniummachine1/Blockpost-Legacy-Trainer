# Blockpost Legacy — Ammo Field Analysis

Deep static analysis of ammo-related fields and methods in `dump.cs`.
Goal: identify the correct field(s) for infinite ammo and rapid fire without
relying on dynamic guessing.

---

## 1. Summary of Candidates

Three classes carry ammo-relevant state. None has a single obvious scalar
"ammo in magazine" field — the game stores ammo per-slot in arrays and
replicates display values across Controll, Player, and UIAmmo.

| Class | Field | Offset | Type | Confidence | Notes |
|-------|-------|--------|------|------------|-------|
| Controll | FGGKANNFBDH | 0xC0 | int | HIGH | Ammo in magazine (displayed value) |
| Controll | ILFOFIOFBAM | 0xC8 | int | HIGH | Max ammo (-1 = no weapon) |
| Controll | KJOMABGHAIJ | 0xCC | int | MEDIUM | Reserve ammo |
| Player | GDEMINMDJAC | 0xA8 | int[] | HIGH | Ammo per slot (array indexed by slot) |
| Player | PELNEJDOBKH | 0xCC | int | MEDIUM | Per-weapon ammo (from demo recording) |
| Player | GEDMGLAMGMD | 0x180 | int | MEDIUM | Paired with MHCOJFIAGLP |
| Player | MHCOJFIAGLP | 0x184 | int | MEDIUM | Paired with GEDMGLAMGMD |
| UIAmmo | _ammo | 0x10 | Text | DISPLAY | UI text showing ammo count |
| UIAmmo | _backpack | 0x14 | Text | DISPLAY | UI text showing reserve |

---

## 2. Controll Class Ammo Fields

### FGGKANNFBDH (0xC0) — Ammo in Magazine

This is the **primary candidate** for the ammo-in-magazine field on the
Controll singleton. Evidence:

- Located in a cluster of weapon-state fields (fire timer, spread, fire input)
- Type is `int`, consistent with a bullet counter
- The field is read by the fire logic (PLH.CDEGJOBLOFO) and decremented per shot
- UIAmmo reads this value to update the `_ammo` Text display

**Infinite ammo strategy**: Set `FGGKANNFBDH` to a large value (e.g. 999) in
the Controll.Update postfix. This persists to the next frame and the game
will display and use the inflated value.

### ILFOFIOFBAM (0xC8) — Max Ammo

- Type `int`, value `-1` when no weapon is equipped
- Defines the magazine capacity for the current weapon
- Used to clamp FGGKANNFBDH after reload

**Rapid fire strategy**: Set `ILFOFIOFBAM` to a large value so the fire
logic never thinks the magazine is empty.

### KJOMABGHAIJ (0xCC) — Reserve Ammo

- Type `int`, the backpack/reserve ammo count
- Decremented when reloading (transferred to magazine)

---

## 3. Player Class (KBBBHJDINCB) Ammo Fields

### GDEMINMDJAC (0xA8) — Ammo Per Slot (int[])

This is the **per-slot ammo array**. Evidence:

- Type is `int[]` — the only array field in the weapon/ammo region
- Indexed by `MOPBMENEGLN` (current weapon slot, 0xA0)
- The fire logic reads `GDEMINMDJAC[Slot]` to check remaining ammo
- Reload writes `GDEMINMDJAC[Slot]` with the new magazine count

**Infinite ammo strategy**: Write 999 to `GDEMINMDJAC[Slot]` each frame.
This is the authoritative server-side value — the Controll fields are
display mirrors.

### PELNEJDOBKH (0xCC) / GEDMGLAMGMD (0x180) / MHCOJFIAGLP (0x184)

These three int fields are secondary candidates observed in demo recording
traces. They appear to be per-weapon ammo snapshots used by the demo
playback system, not the live fire logic. **Do not write these for
infinite ammo** — they are presentation-only.

---

## 4. PLH (Weapon System) Ammo Methods

The PLH class contains the weapon system logic. Key ammo-related methods
identified by signature analysis:

### Methods taking (KBBBHJDINCB, int) — Player + Ammo Count

| Method | VA | Likely Purpose |
|--------|----|----------------|
| JNCPAMGFJOM | 0x10AFF810 | Set player ammo (player, count) |
| CGJJEPDJIEJ | 0x10AEFCF0 | Add/consume ammo (player, delta) |
| DJJHCLDHJOM | 0x10AF2620 | Set ammo alt (player, count) |
| JBJFFJLJNAF | 0x10AFEF30 | Set player slot (player, slotIndex) |
| KEIHIPEFKMH | 0x10AFFE70 | Set player slot alt (player, slotIndex) |

### Methods taking (KBBBHJDINCB, int, int) — Player + Slot + Ammo

| Method | VA | Likely Purpose |
|--------|----|----------------|
| GKCKAEFMPKM | 0x10AF9FD0 | Set slot ammo (player, slot, ammo) |
| MJFMOHDJKHB | 0x10B02B50 | Set slot ammo alt (player, slot, ammo) |
| MLCHKDDMNOI | 0x10B02E10 | Set slot ammo alt2 (player, slot, ammo) |
| MDGFHHELIMC | 0x10B023D0 | Set slot ammo alt3 (player, slot, ammo) |

The repeated (player, slot, ammo) signature across 4 methods suggests
different weapon categories (primary, secondary, melee, grenade) or
different code paths (local vs network replication).

### Fire Method

| Method | VA | Signature |
|--------|----|-----------|
| CDEGJOBLOFO | 0x10AEE0F0 | void Fire(KBBBHJDINCB player, float fireRate, bool a, bool b) |
| MFHJFPPOHLC | 0x10B027F0 | void FireAlt(KBBBHJDINCB player, float fireRate, bool a, bool b) |

The fire method takes the player and fireRate but NOT an ammo parameter —
it reads ammo from the player's fields directly. This confirms ammo is
stored on the Player object, not passed as a parameter.

---

## 5. UIAmmo Class — Display Layer

UIAmmo is the HUD element that displays ammo. Key fields:

| Field | Offset | Type | Purpose |
|-------|--------|------|---------|
| _ammo | 0x10 | Text | Magazine ammo display |
| _backpack | 0x14 | Text | Reserve ammo display |
| _weaponName | 0x18 | Text | Weapon name display |
| _weaponIcon | 0x1C | Image | Weapon icon |
| _reloadGO | 0x28 | GameObject | Reload indicator |
| _reloadingProgress | 0x2C | Image | Reload progress bar |
| _miniGameMarkerRT | 0x30 | RectTransform | Reload minigame marker |

### UIAmmo Methods Taking CGJPBNDDPIN (WeaponItem)

| Method | VA | Purpose |
|--------|----|---------|
| NOGDEACOPFI | 0x103672A0 | Update weapon display (weapon) |
| JMLBJJDIGKO | 0x103656F0 | Update weapon display alt (weapon) |
| MOAIACHLJDF | 0x103669B0 | Update weapon display alt2 (weapon) |
| LOLOAPEHEHJ | 0x10366350 | Update weapon display alt3 (weapon) |

### UIAmmo Methods Taking int (Ammo Count)

| Method | VA | Purpose |
|--------|----|---------|
| LCEEFFIBEBO | 0x10365E60 | Set ammo display (count) |
| ICIDFONKLMG | 0x10364E10 | Set ammo display alt (count) |
| AFHDGDDHPGC | 0x10362BB0 | Set ammo display alt2 (count) |
| CABCAEKDHOI | 0x103635C0 | Set ammo display alt3 (count) |
| PFBJDHPMIJP | 0x103682F0 | Set ammo + reserve (count, reserve) |

The `PFBJDHPMIJP(int, int)` method is particularly interesting — it takes
two int parameters, likely (ammoInMag, reserveAmmo). This is the method
that updates both displays at once.

---

## 6. Network Protocol — Ammo Replication

Ammo is sent to the server via the Client class. Relevant methods:

| Method | VA | Signature | Purpose |
|--------|----|-----------|---------|
| LGFGPAJMOLA | 0x10B56FA0 | void(int playerId, Vector3 pos, int weaponId) | Player position + weapon |
| DGMAFLPDKMD | 0x10B4A760 | void(int teamId) | Set team |
| MGPBPDIGDBO | 0x10B58B00 | void(NAHLLMJMOED weaponData) | Send weapon data |
| FLFBOKOFCHN | 0x10B4E370 | void(NAHLLMJMOED weaponData) | Send weapon data alt |

The server likely validates ammo counts against the weapon's magazine size
(defined in NAHLLMJMOED/WeaponData). Pure ammo injection may be rejected
if the server enforces magSize limits.

**Safer approach**: Hook the fire method (PLH.CDEGJOBLOFO) and restore
ammo after each shot. This way the server sees a valid fire event but the
local ammo count never decreases.

---

## 7. Recommended Implementation

### Option A: Controll Field Override (Simplest)

```csharp
// In Controll.Update postfix
int ammo = Il2CppInteropUtils.LoadField(controllInstance, Controll.Fields.FGGKANNFBDH);
if (ammo < 999)
    Il2CppInteropUtils.StoreField(controllInstance, Controll.Fields.FGGKANNFBDH, 999);
```

**Risk**: Server may detect impossible ammo counts. Works on self-hosted
server where server validation is lenient.

### Option B: Player Array Override (Server-Accurate)

```csharp
// In Controll.Update postfix
var player = Controll.MainPlayer;
int slot = Il2CppInteropUtils.LoadField(player, Player.Fields.Slot);
var ammoArray = Il2CppInteropUtils.LoadArrayField(player, Player.Fields.AmmoPerSlot);
if (ammoArray != null && slot < ammoArray.Length)
    ammoArray[slot] = 999;
```

**Risk**: Modifying the array directly may not trigger the UI update.
May need to also call UIAmmo.PFBJDHPMIJP to refresh display.

### Option C: Fire Method Hook (Stealthiest)

```csharp
// Harmony postfix on PLH.CDEGJOBLOFO
[HarmonyPatch(typeof(PLH), nameof(PLH.CDEGJOBLOFO))]
static void Postfix(KBBBHJDINCB player)
{
    // Restore ammo after each shot
    int slot = Il2CppInteropUtils.LoadField(player, Player.Fields.Slot);
    var ammoArray = Il2CppInteropUtils.LoadArrayField(player, Player.Fields.AmmoPerSlot);
    if (ammoArray != null && slot < ammoArray.Length)
        ammoArray[slot] = originalAmmo; // restore to pre-shot value
}
```

**Risk**: Server still sees ammo decrement in the fire packet. May need
to also hook the network send to patch the ammo value in the outgoing
packet.

---

## 8. Dead Ends (Verified Non-Ammo)

| Field | Class | Why Not Ammo |
|-------|-------|-------------|
| ECBCOHFLJCC (0xAC) | Player | Active weapon ID. Writing causes "no weapon" state. |
| OCDNCKANJPB (0x5C) | WeaponItem | Weapon ID, not ammo count. Writing causes "no weapon". |
| IGBIBDAMMLE (0x...) | Player | Footstep sound index (cycles 0-4). |
| BCHEAICMFGH (0x...) | Player | Footstep distance accumulator. |
| HDNNKKFCPOB (0x...) | Player | Weapon bob (oscillates 0-10). |
| MJFMDOKEFFO (0x...) | Player | Sway lean ramp (0-45 then decays). |
| PPOOANLEBNI (0x...) | Player | Stats array — DO NOT WRITE, corrupts state. |
