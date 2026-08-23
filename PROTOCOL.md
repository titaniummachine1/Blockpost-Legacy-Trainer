# Blockpost Legacy — protocol and internals

Findings from static analysis of `GameAssembly.dll` plus live capture. Everything here is
reproducible with the tooling in `.tools/analysis/`.

Context: single-player against bots on a **self-hosted server**. The retail service is dead; the
developer supplied the client binary only, no source.

---

## 1. Tooling

`.tools/Il2CppDumper/dump.cs` is a **metadata dump** — classes, field offsets, method RVA/VA/file
offsets, signatures. **No method bodies.**

`.tools/decompiled/*.cs` is a decompilation of the **Il2CppInterop proxy assembly**, not the game.
Every body there is `il2cpp_runtime_invoke` plumbing. Useful only for exact managed signatures when
writing plugin code. Do not go looking for game logic in it.

`.tools/analysis/` pairs `dump.cs` with capstone to give symbolised x86 for any of the ~42,000
methods. The build is **32-bit x86**; image delta (VA − file offset) is `0x10000C00`.

```bash
python .tools/analysis/bpdis.py find:AHLDAPJEJNC     # name -> VA
python .tools/analysis/bpdis.py va:0x10B47670 150    # disassemble, call targets symbolised
python .tools/analysis/opcodes.py                    # every outgoing message type
```

`vamap.json` is a generated cache; delete it to force a rebuild after re-dumping.

> Gotcha: never name a script in that folder `dis.py`. It shadows the stdlib module capstone
> imports and fails with a misleading circular-import error.

---

## 2. Transport

Raw **TCP**, hand-rolled binary framing. `websocket-sharp.dll` ships with the game but is a WebGL
leftover — the standalone build does not use it.

Three client classes, each with its own `TcpClient`:

| Class | Role |
|---|---|
| `MasterClient` | master/lobby server |
| `Client` | **the game room** — singleton at `Client.LPCJFAOOIKA` |
| `DevClient`, `DropClient` | secondary channels, share the `NEGGNDFJMAK` transport helper |

`NET` is a static serialiser, not a manager: a shared buffer (`GLPMIOHOEOG`) plus a cursor
(`JGDCFADACPP`), with typed writers and readers.

### Choke points (these are what the probe hooks)

| Method | Role |
|---|---|
| `NET.LPAPGKDAENI(byte hdr, byte op)` | open packet — **`hdr` is always `0xF5`**, `op` is the message id |
| `NET.DMFHJJDMAEJ(byte, byte)` | second packet-open variant (seen with `hdr=0x33`) |
| `NET.JBIICNJNHCI(float)` | write f32 |
| `NET.FPELFNLEPGG(int)` | write i32 |
| `NET.PFCLIPCCHCK(byte)` | write u8 |
| `NET.APNPMHBBLDG(short)` | write i16 |
| `NET.EMJOGONJKIO()` | close packet |
| `Client.HKOFHOANEJD()` | **flush → `TcpClient.GetStream()`** — the real send |
| `Client.FPIDGCHIEMJ(byte[], int)` | enqueues an inbound buffer under a lock — **but see §6** |

x86 calling convention note: il2cpp appends a `MethodInfo*` argument, and cdecl pushes
right-to-left. So `push 0; push 0x2D; push 0xF5; call` reads as `LPAPGKDAENI(0xF5, 0x2D)`.

---

## 3. Outgoing opcodes

Extracted by byte-scanning for `push 0xF5` followed by a call to a packet-open function — 100
callsites, 75 distinct opcodes across all three clients. Run `opcodes.py` for the full list.

`Client` (game room) senders:

| Op | Sender | Payload |
|---|---|---|
| `0x00` | `HHHPAHPJMFK` | keepalive |
| `0x02` | `DGMAFLPDKMD(int)` | |
| **`0x04`** | **`AHLDAPJEJNC(Vector3, uint, List<DMHBMAAFCFJ>)`** | **hit report — see §4** |
| **`0x06`** | **`KHIBHNBHACE(int, int, int)`** | **three i16 — see §4** |
| `0x07` | `LAJNHBDGLPK(int,int,int)` | three i16 |
| `0x08` | `FLFBOKOFCHN(NAHLLMJMOED)` | weapon data |
| `0x09` | `EEKLOPBNDAC` / `HLHODPPHCIP(List<FPNENMKEFBB>)` | loadout |
| `0x0D` | `CCHIPIPCCPC(int, string)` | |
| `0x0E` | `FFFFNOGILAB(int)` | weapon select |
| `0x0F` | `KNBMPAGJBMO(int)` | slot |
| `0x10` | `HBEGNLMENMD()` | ready |
| `0x11` | `BOACAFDCFJE()` | spawn |
| `0x16` | `KDHBJEJINMH(Vector3, Vector3, float)` | throw |
| `0x17` | `ILACKIKMKHE(int, Vector3)` | impact |
| `0x1B` | `IGBENOGKFHK(string)` | |
| **`0x2D`** | **`CHIKDFPGGFC(f,f,f,f,f,byte,uint)`** | **movement — see §4** |
| `0x35` | `MKPOLBIKPPA` / `GINPPBIJOCA(byte[], int)` | raw passthrough |
| `0x39` | `NONOLDAPPLO(int,int,int)` | |
| `0x3D` | `HLHAOKJGJKF(int)` | |
| `0x42` | `MLIMAJJPIGM()` | |
| `0x4E` | `ANICPIFFOIK(int, int)` | |
| `0x50` | `BBPEDFGGEPN(int, string)` | |
| `0x51` | `CICCJMCODCA(int, uint, string)` | |
| `0x52` | `KBIGGPBBBFH(int, string, string)` | |
| `0x55` | `HGCKLOEJJAH(Vector3, Vector3, float)` | shot/tracer — **never observed firing** |
| `0x56` | `LGFGPAJMOLA(int, Vector3, int)` | hit effect |
| `0x57` | `MGOGBHLJHAJ(int)` | |

### There is no ammo, reload, or health opcode

Exhaustive over the outgoing set. The server is **never told** your magazine count, that you are
reloading, or the outcome of the perfect-reload minigame. All of it is client-local simulation.

---

## 4. Key packet layouts

### `0x04` — hit report (`Client.AHLDAPJEJNC`, VA `0x10B47670`)

```
LPAPGKDAENI(0xF5, 0x04)
if (hits == null || hits.Count == 0) goto end     // <-- position/seq skipped entirely
  f32 pos.x, f32 pos.y, f32 pos.z
  i32 seq
  foreach hit in hits:
      u8  targetId          (DMHBMAAFCFJ.AMGLIHOLNJE, truncated to a byte)
      u8  bodyPart          (DMHBMAAFCFJ.KMCHFGKKICG, truncated to a byte)
      f32 point.x, f32 point.y, f32 point.z
end: EMJOGONJKIO(); HKOFHOANEJD()
```

**The client declares its own hits.** No damage value, no ray, no time, no proof of line of sight —
just "I hit player N in part B at point P". The server can only sanity-check; nothing in the packet
lets it verify.

The early-out matters for reading captures: a shot that hits nothing sends `0x04` with an
**empty body**. Empty `0x04` = miss, populated = hit.

### `0x06` — damage triple (`Client.KHIBHNBHACE`, VA `0x10B55620`)

Three i16 and nothing else. Sent alongside hits. Observed values `(45,4,61)`, `(76,12,50)`,
`(28,4,56)`, `(21,5,58)`, `(74,3,47)`, `(82,4,51)`. The middle value tracks the small target ids
seen in `0x04`. Working hypothesis: **`(damage, targetId, healthRemaining)`** — i.e. the client
computes and reports the damage number too. **Not yet confirmed.**

### `0x2D` — movement (`Client.CHIKDFPGGFC`, VA `0x10B498C0`)

```
LPAPGKDAENI(0xF5, 0x2D)
f32 x, f32 y, f32 z, f32 rotX, f32 rotY, u8 state, i32 tick
```

Sent ~20 Hz. The guard at the top of the function is a null/state check on an unrelated object, not
a position sanity check — **the client simply declares where it is**.

---

## 5. Field identifications

`Controll.HGAODFPBGLB` → the local `KBBBHJDINCB`. This is the **networked player entity**;
everything on it that moves is presentation state. Local weapon state is on `Controll`, not here.

| Field | Offset | Meaning |
|---|---|---|
| `FDOJDJLIGLF` | `0x38` | health |
| `EFHBKMHCMOH` | `0x3C` | max health |
| `NDLMGNIMKHE` | `0x7C` | distance-walked accumulator |
| `FGFKPMPLNKO` | `0x84` | spread / recoil accumulator |
| `HDNNKKFCPOB` | `0x90` | weapon bob (oscillates 0→~10→0, driven by `NDLMGNIMKHE`) |
| `MOPBMENEGLN` | `0xA0` | active weapon slot |
| `GDEMINMDJAC` | `0xA8` | `int[]` |
| `ECBCOHFLJCC` | `0xAC` | active weapon id — **writing 999 here causes "NO WEAPON"** |
| `MJFMDOKEFFO` | `0x160` | sway/lean ramp, 0→45 then decay |
| `LCMOBPPHLLM` | `0x178` | fire-rate / damage float |
| `NJPHKNAOEKM` `BDHNEDFEHBA` `NHJDPAAFIKO` `LNIDBHIAJJI` `ACAIGMNLLEC` `LCPGFBMKNHJ` | `0x1D4`–`0x1E8` | six consecutive ints, drift with movement — **not** magazine counters |
| `PPOOANLEBNI` | `0x1F0` | `int[]`, stats — do not touch |

`CGJPBNDDPIN` is a weapon **definition** (prefabs, clips, textures), not runtime state. `OCDNCKANJPB`
is the weapon id, not ammo.

### Do not write

`ECBCOHFLJCC`, `GDEMINMDJAC`, `PPOOANLEBNI`, `JPGGPPLOOML`, `OCDNCKANJPB` — corrupts weapon state.

---

## 6. Open questions

1. **Zero inbound packets across two captures**, with `TcpClient.Connected == True` throughout.
   Implausible for a live match — bot positions have to arrive somehow. `Client.FPIDGCHIEMJ` looked
   like the read path but evidently is not the one carrying traffic. Find the real one before
   drawing any conclusion about what the server sends back or whether it ever corrects the client.
2. **Reload timer and magazine counter not yet located.** Confirmed absent from `KBBBHJDINCB`.
   Next place to look is `Controll` (188 fields, many numeric statics).
3. `0x06` field meanings unconfirmed.
4. `0x55` (shot/tracer) never fired in capture despite kills — the primary weapon path does not use
   it. Worth knowing what does.
5. Whether the server validates anything at all. Nothing observed so far suggests it does, but
   absence of inbound data means this is **untested**, not disproven.

---

## 7. Probe usage

Built into the plugin (`NetProbe.cs`, `FieldWatch.cs`). Hooks push small records onto a bounded
queue; a background thread does all formatting and I/O, so the game thread never blocks.

| Key | Effect |
|---|---|
| `F5` | include/mute the per-frame churn fields |
| `F6` | field watcher on/off |
| `F7` | packet capture on/off |
| `F8` | numbered marker |

Keys are read from the `Controll.Update` hook, so they **only work while spawned in a match**.

Logs go to `BepInEx/captures/net-<timestamp>.log` — **one file per run, never overwritten**.
An earlier fixed filename with `FileMode.Create` destroyed a capture on game restart; don't
reintroduce that.

Worth preserving captures into `captures/` in this repo — `.gitignore` has an exception for it.

---

## 8. Update: 23 Aug 2026 capture

New log: `BepInEx/captures/net-20260823-021230.log` (plugin 0.7.1, 25/25 NetProbe hooks working).

- **1005 outgoing packets, 0 incoming packets.** `Client.FPIDGCHIEMJ` is still not the live read path — still need to find the real receive method.
- **Packet distribution:** `0x2D` MOVE 840, `0x04` HIT_REPORT 90, `0x06` 69, `0x10` 3, `0x11` 1, `0x65` 1.
- **`0x04` HIT_REPORT structure** (from live data, matches disassembly):
  - `f32 pos.x, pos.y, pos.z`
  - `i32 seq/tick`
  - foreach hit: `u8 targetId`, `u8 bodyPart`, `f32 hitPoint.x, hitPoint.y, hitPoint.z`
  - Empty body means the shot missed.
- **`0x06` shot/damage packet:** three `short` values, always sent just before a `0x04`. Examples: `s79 s2 s16`, `s87 s3 s5`. Field meanings still unconfirmed.
- **`0x2D` MOVE:** `f32 x, y, z, rotX, rotY, u8 state, i32 tick`. State `0xB0` seen while standing/aiming.
- **`0x55` shot/tracer:** still never observed. The main weapon path appears to use `0x06` instead.
- **`0x10`, `0x11`, `0x65`:** rare — likely weapon/ready/spawn/loadout events. Need captures of reload, switch, grenade, and death to map them.

---

## 9. Update: 23 Aug 2026 capture #2

New log: `BepInEx/captures/net-20260823-025026.log` (~113 KB, latest plugin).

- **Still 0 incoming packets.** The first three `Client` (byte[], int) methods (`FPIDGCHIEMJ`, `MKPOLBIKPPA`, `GINPPBIJOCA`) did not fire on the receive path. Three more candidates were found in `Client` with the same signature: `KPBPDBDDOFG`, `BMJJCBAPAHP`, `KHPHBCBOMML` — these are now patched.
- **Packet distribution:** `0x2D` 1060, `0x04` 12, `0x08` 10, `0x0F` 9, `0x42` 2, `0x06` 2, `0x65` 1, `0x00` 1, `0x09` 1.
- **`0x0F` slot/15:** one `byte`, the active slot (`b0`, `b1`, `b2`). Sent on weapon switch.
- **`0x42` req/66:** sent twice with no obvious payload. Possibly a ready/request handshake.
- **`0x08` weapondata/8:** weapon definition list. One entry per weapon:
  ```
  i32 weaponId, string codename, string displayName, ..., string hash32
  ```
  Observed weapons in this capture: `kriss_vector`, `beretta92`, `shovel`, `block`, `grenade`, `sl8`.
- **`0x09` loadout/9:** loadout snapshot. Long list of `L<guid> i<weaponId> b...` triples; needs more captures to pin down the exact structure.
- **`0x00` keepalive/0:** heavy heartbeat/player-info packet with id strings, many flags, `i1350`, SteamID, and hashes.
- **`0x06` shot/damage triple:** two examples captured: `s16 s1 s81` and `s17 s2 s81`. The third short is consistently `81`; possibly max/remaining health or a fixed seed. Still unconfirmed.
- **`0x04` HIT_REPORT:** populated examples now show the full layout:
  ```
  26,738 3,397 61,758 i297660631 b0 b0 19,248 2,597 74,454
  ```
  i.e. `pos(x,y,z), i32 seq, u8 targetId, u8 bodyPart, point(x,y,z)`.
- **No 0x55 shot/tracer packets observed in this session either.**

---

## 10. Live security observation

A player was observed killing the local client **after the local client had already killed that player**.
If the killer was not an admin/host, this confirms the game trusts client-authored kill/damage state:

- The client already declares its own hits (`0x04`) and computes the resulting damage triple (`0x06`).
- If another client can force a kill on demand, either `0x06`/`0x04` is accepted without server validation,
  or there is an unmapped admin/kill opcode (candidates: `0x07`, `0x0D`, `0x39`, `0x3D`, `0x4E`, `0x50`,
  `0x51`, `0x52`, `0x57`).

`infiniteHealth` sets local `FDOJDJLIGLF` to `1000` every frame, but if the server accepts a client kill
packet it may still register death before the local cheat can overwrite it.

---

## 11. Reload system — fully mapped

Recovered offline from `captures/net-20260823-020010.log` (5533 fieldwatch lines, five reloads
covering all three minigame outcomes). Every field below is a `Controll` member.

| Alias | Raw | Offset | Type | Meaning |
|---|---|---|---|---|
| `Game.ReloadStartTime` | `FBINCNDDPAO` | `0x1A8` | float | `Time.time` when the reload began |
| `Game.ReloadEndTime` | `ILGHFLMKMCO` | `0x1AC` | float | completion stamp — **normally `start + 2.0`** |
| `Game.ReloadMarkerPos` | `JADIDAJFOGK` | `0x1B8` | float | minigame marker position (~0.31–0.40 observed) |
| `Game.IsReloading` | `DJACNOGOCKD` | `0xB7` | bool | true for the duration |
| `Game.ReloadPromptActive` | `KOPOBDGHLFL` | `0x94` | bool | instance; true ~250 ms before the reload starts |
| `Game.ReloadRequestTime` | `CLEHDNFKJPB` | `0x98` | float | instance; mirrors the start stamp |
| `Game.ReloadMinigameResult` | `JBKBOPCCIBM` | property | int | `0` no attempt · `1` perfect · `2` failed |

`ILGHFLMKMCO − FBINCNDDPAO` is exactly `2.0` at reload start, every time.

### How the perfect reload actually works

**It subtracts from the completion stamp.** In the capture, reload #1 was a successful minigame:

```
19442.1  IsReloading      0 -> 1          reload begins
19442.1  ReloadEndTime    0 -> 17.8536    = start(15.8536) + 2.0
20634.8  MinigameResult   0 -> 1          perfect hit registered
20634.8  ReloadEndTime    17.8536 -> 17.1449    <-- 0.71s removed
20737.3  IsReloading      1 -> 0          finished after 1295 ms, not 2000
```

Reload #4 was a failed minigame: `MinigameResult 0 -> 2`, **no change to `ReloadEndTime`**, and it
ran the full 2005 ms. Runs with no participation leave the result at `0`, also full duration.

So all three outcomes differ only in how much is subtracted from one float.

### Instant reload

Implemented in `Plugin.cs` (`ApplyInstantReload`, menu toggle). Pulling `ReloadEndTime` back to
`ReloadStartTime` finishes the reload immediately — the same mechanism the perfect reload uses,
taken to its limit rather than a new code path:

```csharp
if (Controll.DJACNOGOCKD && Controll.ILGHFLMKMCO > Controll.FBINCNDDPAO)
{
    Controll.ILGHFLMKMCO = Controll.FBINCNDDPAO;
}
```

No packet is involved and none is needed — see §3, there is no reload or ammo opcode, so the
server is never told and there is nothing to stay synchronised with. **Untested in game.**

### Corrected alias

`KBBBHJDINCB.ECBCOHFLJCC` (`0xAC`) previously had two contradictory aliases, `TotalAmmo` and
`ActiveWeaponIndex`. It is the **active weapon id** — writing an ammo count here is exactly the
documented "NO WEAPON" corruption. Now a single alias, `Player.ActiveWeaponId`.

### Identified as presentation-only (muted in FieldWatch)

`GIPNIMALPEG` (3001 changes/run), `MKFDGFOCKNO`, `LEKCBKLAILO`, `OFJKHAFJIMA`, `NAKNALFCOIF`,
`IGLCENGMMMJ`, `MNHBPCOOMLE`. The group `DGNDKAKOEPD` / `BEJBLGGGOCC` / `JELGPAKAJPE` /
`EENJPDHGGGC` updates once per second and is network/timing telemetry (`EENJPDHGGGC` is seconds
elapsed), **not** ammo — the "25 changes" in a 25-second capture was a coincidence of duration.

---

## 12. Correction: what instant reload actually does

Reported in game: instant reload **hides the active-reload minigame animation but does not feel
faster**, and rapid fire / the server-trust test do not work.

The timer model in §11 is confirmed correct — the clocks just differ. Capture timestamps are
Stopwatch milliseconds since plugin load; `ILGHFLMKMCO` is `Time.time` seconds. Offset ≈ 3.59 s.
Reload #1 ended at log `20737.3` → `Time.time` `17.144`, matching `ILGHFLMKMCO = 17.1449`; reload #2
ended at `26176.4` → `22.586`, matching `22.5873`. So the reload **does** end when `Time.time`
reaches `ILGHFLMKMCO`.

### The real gap: ammo is not on any object we were watching

At all five reload completions in `captures/net-20260823-020010.log`, the only fields that moved
were:

| Field | Note |
|---|---|
| `DJACNOGOCKD` | reload flag, 1 → 0 |
| `_JFKCDODJALP_k__BackingField` (`0x1C0`, int) | clears with the reload |
| `JBKBOPCCIBM` | minigame result, reset to 0 |
| `GIPNIMALPEG`, `OFJKHAFJIMA`, `MKFDGFOCKNO`, `LEKCBKLAILO` | per-frame churn |

**No ammo counter changed anywhere.** FieldWatch covered the player entity, `Controll` statics and
the `Controll` instance — so ammo and the fire/reload gates must live on the **weapon**
(`CGJPBNDDPIN`) or the **loadout entry** (`FPNENMKEFBB`), neither of which was watched.

That one blind spot plausibly explains all three failures: clearing the timer ends the bar (hence
the missing animation) while the refill and the fire gate, which live elsewhere, are untouched.

FieldWatch now watches two more targets, `weapon` and `loadout`, re-resolved on weapon switch.

### Server-trust test: do not read "doesn't work" as "server validates"

`TryFakeHit` sends `target.CCINALOJCNH` (`Id1`) as the wire target id, but **which player field
maps to the `u8` id in `0x04` is still unresolved** (backlog 5). Real captures show ids `0`, `3`,
`4`; `0x06` middles show `3`, `4`, `5`, `12`. If `Id1` is the wrong field, the server ignores the
packet for a reason unrelated to trust — which would be very easy to misread as validation.

`TryFakeHit` now logs `Id0`/`Id1`/`Id2`/`PlayerId`/`Team`/`Health` for the target next to the id it
sends, so a single capture settles which field matches the ids in genuine `0x04` traffic. **Until
that is resolved, the server-trust question remains open, not answered.**
