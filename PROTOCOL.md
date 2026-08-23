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

---

## 13. Instant reload: what it really touches

Second in-game report: with instant reload on, the reload takes **about as long as a normal
non-participating reload**, the minigame disappears, and **reloading can be re-triggered
repeatedly, resetting the previous reload**.

So writing `ILGHFLMKMCO` moves the *minigame bar*, not the reload completion. Setting end = start
collapses the bar to zero width (animation vanishes) and reopens the reload input (hence the
re-trigger), while the actual refill runs on its own schedule.

That does not contradict §11 — a genuine perfect reload both shortens the bar **and** shortens the
reload. The perfect-reload handler must do a second thing that this write does not.

### Where the reload actually goes

`PLH.JLAALPNBABH(KBBBHJDINCB)` at VA `0x10AFF330` is the reload entry point. It does **not** use
`KPNAADPGNCP`. It walks:

```
player.JDIHHMABLAJ            0x9C   BIMFEOACIDM[]
      [ player.MOPBMENEGLN ]  0xA0   active slot
      .DBMOPKGMECL            0x0C   FPNENMKEFBB[]
      [ 1 ]                          <-- element 1, not the slot index
```

and calls `PLH.EMHOOBODDCM(player, string)` (VA `0x10AF4D30`) to resolve the weapon by codename.

`FPNENMKEFBB` carries five candidate ints — `NIBLMFFHJHK` `0x14`, `PICIILNDDJO` `0x18`,
`GLGJDNLBAOB` `0x24`, `NIKINLIKGCP` `0x28`, `MOGDFDEMPLE` `0x2C` — one or two of which should be
magazine and reserve ammo.

FieldWatch now binds `activeEntry` along exactly this path, plus `weapon` and `loadout`, so the
next capture covers the object the reload code actually mutates. Watching `KPNAADPGNCP[slot]`
alone would have missed the counter a second time.

The menu toggle is relabelled **EXPERIMENTAL** and describes what it really does, so it is not
mistaken for a working feature. It is a mild footgun in its current state (the reload re-trigger),
so leave it off unless capturing.

---

## 14. Performance incident: verbose diagnostics froze the game

Reported: with verbose diagnostics on, the game froze ~5 s every 1 s and the machine became
unresponsive when trying to close it. The earlier "moved logging to a background thread" change did
**not** fix this, because the file I/O was never the bottleneck.

### Measured from `captures/diag-20260823-133719.log`

33,201 lines over ~2 minutes, 8.4 MB total across all logs with 164 GB free — so **not** disk.
Per one-second tick, with **40 players in the match**:

| Source | Lines | Real cost |
|---|---|---|
| `[PlayerField]` | 15,904 | `GetProperties()` re-enumerated **per player per tick**, then ~130 `GetValue` calls each |
| `[Controll]` | 8,428 | ~188 static `GetValue` calls |
| `[Object]` | 4,795 | per-player object/screen-position work |
| `[Player]` | 3,920 | all 40 players, every tick |

Every one of those `GetValue` calls is an **IL2CPP `runtime_invoke` through the interop layer**, not
a managed reflection call — and `FormatDiagnosticValue` marshals native strings for `.name` on
`Camera`/`Transform`. On top of that, `ResolveCamera()` ran `FindObjectsOfType<Camera>()` — a full
scene scan — once per tick.

The routine could not complete within its own one-second interval, so each tick ran into the next.

### Fixes

- `DiagnosticInterval` 1 s → **5 s**.
- **Per-tick budget** of 150 IL2CPP reads (`DiagTake`). A sweep that runs out stops and *resumes*
  next tick rather than blocking the frame until it finishes.
- Player scan walks a **rotating 3-player window** instead of all 40; the roster is still covered,
  just spread over ticks.
- `GetProperties()` cached for both `KBBBHJDINCB` and `Controll` statics.
- Diagnostics use the **cached camera**; no scene scan.
- The heavy field sweep is now a **separate opt-in** under verbose, labelled as costly. Plain
  verbose is the one-line summary plus ammo status only.
- `Plugin.Unload()` now calls `NetProbe.Shutdown()` / `AsyncLog.Shutdown()` so writer threads flush
  and exit instead of being killed at process teardown — the likely cause of the hang on close.

### Unrelated bug found while fixing this

`NetProbe.Tick()` is called from **both** `Controll.Update` and `GUIInv.OnGUI`. `GetKeyDown` is true
for one frame, so with the inventory GUI open every F7/F8 press was sampled twice and the toggle
flipped straight back. Now guarded on `Time.frameCount`.

### Rule going forward

On IL2CPP, **the cost is the property reads, not the logging**. Moving output to another thread does
nothing for a routine whose expense is thousands of `runtime_invoke` calls on the game thread. Any
new diagnostic must be budgeted per tick, not merely written asynchronously.

---

## 15. Capture `net-20260823-140511` — reload reconfirmed, two bugs found

Clean baseline run, verbose off, budgeted probe. No lag reported.

### Reload model reconfirmed exactly

```
13491.0  IsReloading    0 -> 1
13491.1  ReloadStart    0 -> 10.0278
13491.1  ReloadEnd      0 -> 12.0278      = start + 2.0
13495.9  MarkerPos      0 -> 0.35
14682.9  MinigameResult 0 -> 1            perfect
14696.1  ReloadEnd      12.0278 -> 11.3205   -0.7073
14785.7  IsReloading    1 -> 0            1294.7 ms  == 2.0 - 0.705
```

Second reload: `MinigameResult -> 2` (failed), `ReloadEnd` untouched, ran 1998.8 ms. So
`ILGHFLMKMCO` **is** the completion stamp, and §13's doubt about that was wrong — the mechanism was
right, the write was landing at the wrong time.

### Bug 1 — instant reload wrote too late in the frame

`ApplyInstantReload` ran from the `Controll.Update` **postfix**, after the game had already
evaluated the stamp for that frame. That matches the reported symptom exactly: the bar collapses
(it is drawn later) but the reload is not shortened. Moved to the **prefix**.

### Bug 2 — `loadout` / `activeEntry` never bound

Only four targets bound: `player` (53), `Controll.static` (74), `Controll` (28), `weapon` (3).
`weapon` having just 3 numeric fields reconfirms `CGJPBNDDPIN` is a definition, not runtime state.

Cause: **`Il2CppReferenceArray<T>` implements `IList<T>` but not the non-generic
`System.Collections.IList`**, so `is not System.Collections.IList` silently returned null and both
lookups bailed without error. Replaced with reflection helpers (`CountOf` / `ElementAt`) that work
off `Length`/`Count` and the indexer.

> Rule: never type-test an Il2Cpp collection against a non-generic BCL interface. It fails silently
> and looks like "the field doesn't exist".

### New field

`Controll.MJPKOOHJOPA` (instance, float) — monotonically rising, moves in steps while active.
Unidentified; not reload-related.

---

## 16. Capture `net-20260823-141003` — ammo is not a scalar

44 s, 9194 lines, no lag. Five targets bound including `activeEntry` (the `IList` fix worked).

### Result: 166 scalar fields watched, none is ammo

| Target | Numeric scalars |
|---|---|
| `player` (`KBBBHJDINCB`) | 53 |
| `Controll.static` | 74 |
| `Controll` | 28 |
| `weapon` (`CGJPBNDDPIN`) | 3 |
| `activeEntry` (`FPNENMKEFBB`) | 8 |

`activeEntry` recorded **zero changes** across the whole run — firing and reloading do not touch any
scalar on `FPNENMKEFBB`. `weapon` likewise. So ammo is on neither.

### Why: it is an array element

`KBBBHJDINCB` carries six numeric arrays that a scalar differ cannot see:

| Field | Offset | Type |
|---|---|---|
| `GPBAJMJILMA` | `0x6C` | `float[]` |
| `JNBMIDFBOHD` | `0x74` | `float[]` |
| `JPDHFNADBKI` | `0x78` | `float[]` |
| **`GDEMINMDJAC`** | **`0xA8`** | **`int[]`** — the existing ammo diagnostic already reads `[slot]` |
| `DDKPBGMMNIA` | `0x1AC` | `int[]` |
| `PPOOANLEBNI` | `0x1F0` | `int[]` — stats, never write |

`GDEMINMDJAC[slot]` is the prime candidate. FieldWatch now diffs array elements too (first 12 per
array, sharing the frame budget), reported as `fw <target> <ARRAY>[i] old -> new`.

### Dead end recorded so anyone re-walking it stops early

`player.IGBIBDAMMLE` (`0x16C`, int) cycles `0→1→2→3→4→0` in lockstep with `BCHEAICMFGH` (`0x170`,
float, monotonically rising) — 240 changes each. That is a **footstep sound index plus a distance
accumulator**, not a magazine counter. The equal change counts and small cyclic range make it look
like ammo at a glance; it isn't.

### Reload note

A second reload at `137416.5` lasted only **164 ms** with `ILGHFLMKMCO` left at `start + 2.0`
(`134.2007`, unmodified). Either instant reload was enabled and its write was restored by the game
before FieldWatch sampled that frame, or the reload was interrupted. **Unresolved** — needs a run
where it is known whether the toggle was on.

---

## 17. Ammo hunt: still not found, search space narrowed

`net-20260823-142205` — magazines emptied, minigame attempted and failed several times. All arrays
bound this time:

| Target | Scalars | Arrays |
|---|---|---|
| `player` | 51 | `GPBAJMJILMA`, `JNBMIDFBOHD`, `JPDHFNADBKI`, **`GDEMINMDJAC`**, `DDKPBGMMNIA`, `PPOOANLEBNI` |
| `Controll.static` | 74 | `DDOHELGGICN` |
| `Controll` | 28 | — |
| `weapon` (`CGJPBNDDPIN`) | 3 | — |
| `activeEntry` (`FPNENMKEFBB`) | 8 | `COAAKMDBKJM` (byte[]) |

**Zero array element changes** across the run, `GDEMINMDJAC` included — despite several magazines
being emptied.

### Ruled out so far

- Every scalar on `KBBBHJDINCB`, `Controll` (static and instance), `CGJPBNDDPIN`, `FPNENMKEFBB`
- Every element (first 12) of all eight bound arrays

### Open ambiguity, and the fix for it

"No array lines" is currently indistinguishable from "`ElementAt` throws and the catch swallows
it". Silent failure has already cost two runs in this hunt (the `IList` cast in §15, the starved
budget in §16), so FieldWatch now **dumps each array's contents once at bind time**:

```
#   player.GDEMINMDJAC len=4 [30, 12, 1, 0]
#   player.GDEMINMDJAC READ FAILED: ...
```

Either the magazine is visible in that dump, or the read is broken and it says so. One run
distinguishes them.

### Note on method

Three separate attempts to find ammo have each failed for a *different* silent reason rather than
because the value was absent. Prefer instrumentation that proves it ran over instrumentation that
only reports differences.

---

## 18. Array reads verified working — the negative result is real

`net-20260823-142601`. The bind-time dump came back clean, with **no `READ FAILED` lines**, so the
array access works and the earlier "zero changes" was a genuine negative, not silent breakage:

```
player.GPBAJMJILMA len=2  [0, 0]
player.JNBMIDFBOHD len=2  [7.03125, 0]
player.JPDHFNADBKI len=2  [7.03125, 90]
player.GDEMINMDJAC len=4  [0, 0, 0, 0]      <-- all zero, never was ammo
player.DDKPBGMMNIA len=10 [0, 0, 0, 1, 0, 1, 0, 0, 0, 0]
player.PPOOANLEBNI len=40 [0, 0, ...]
activeEntry.COAAKMDBKJM len=3 [0, 0, 0]
Controll.static.DDOHELGGICN len=6 [(normal:…, distance:…) x6]   <-- frustum planes
```

`GDEMINMDJAC` is all zeros while playing, so the long-standing assumption that it is
"ammo per slot" (still referenced by the old `LogAmmoStatus` diagnostic) is **wrong**.

### Ammo is definitively not on

`KBBBHJDINCB`, `Controll` (static + instance), `CGJPBNDDPIN`, `FPNENMKEFBB` — all scalars, and the
first 12 elements of all eight arrays.

### Next search space

The HUD must read the number to draw it, and `PLH` is the weapon manager:

| Type | Singleton |
|---|---|
| `PLH` | `PLH.LPCJFAOOIKA` |
| `HUD` | `HUD.LPCJFAOOIKA` — HUD carries ~20 int fields (`0x150`–`0x1F8`) |
| `GUIInv` | `GUIInv.LPCJFAOOIKA` |

FieldWatch now binds statics **and** singleton instance for each, via a generic
`AddStaticsAndSingleton` helper, and logs explicitly when a type or singleton is missing.

---

## 19. Correction: `JLAALPNBABH` is the skin loader, not reload

Disassembling past the first ~110 instructions of `PLH.JLAALPNBABH` (VA `0x10AFF330`) shows what it
actually does:

```
VWGen2::CDANNBDNHFM(string, int)          resolve weapon prefab
PlayerPrefs::HasKey / GetString           saved skin selection
TEX::CALGHDNPMLO(string, bool)            load texture
Material::SetTexture(string, Texture)     apply skin
PLH::BMDDPNJEIEC(KBBBHJDINCB, CGJPBNDDPIN)
```

It is the **weapon model/skin loader**. It never touches ammo and is not the reload entry point.

**This invalidates §13.** The `JDIHHMABLAJ[slot].DBMOPKGMECL[1]` walk described there is the skin
loader's path to the weapon definition, not the reload's path to ammo. `activeEntry` was bound
along a route with no reason to hold a magazine counter — which is consistent with it recording
zero changes across every capture since.

The SDK alias is corrected from `Weapon.Reload` to `Weapon.LoadWeaponSkin`, with a note.

## 20. Ammo: change-detection exhausted, switching to value matching

`net-20260823-142940` bound eleven targets — `player`, `Controll` (static + instance), `weapon`,
`activeEntry`, and now `PLH.static`, `HUD.static`, `HUD`, `GUIInv.static`, `GUIInv`.
(`PLH.LPCJFAOOIKA` is null; `GUIInv.LPCJFAOOIKA` resolves to a `BIMFEOACIDM`.)

Scanned for both signatures of a magazine and found neither:

- **Steps down by exactly 1**: only boolean toggles (`GLGCAOADGMN`, `DJACNOGOCKD`, `APFNBGHAJMD`,
  `BFEOOOMMGLK`) — no counter.
- **Steps up by exactly 1** (shots-fired rather than remaining): nothing at all.

### Why change-detection keeps failing

It can only see a field that changes *while being sampled*, and with eleven targets the per-target
budget is ~10 reads/frame, so any given field is visited roughly every fifth frame. Worse, it can
never identify a field whose value we have never actually seen.

**New approach: value matching.** FieldWatch now dumps every non-zero scalar once at bind, the same
way arrays are dumped:

```
#   player nonzero: FDOJDJLIGLF=100 MOPBMENEGLN=2 ...
#   HUD nonzero: ...
```

If the HUD reads e.g. `24/90`, then whichever field equals 24 at that instant is the magazine —
no need to catch it mid-change. This is how the ammo field should have been hunted from the start.

---

## 21. Value snapshot: near miss, and the tool that will settle it

`net-20260823-143513`, taken with a sniper rifle at 7 rounds. The bind-time snapshot produced the
first full picture of every non-zero scalar across all eleven targets.

One tempting hit: `GUIInv.static` holds `INCDIECCFMC=7`, `LOILCIEIHHF=8`, `LALEHGDILEB=9` — both
reported ammo counts, sitting adjacent.

**It is a false positive.** `GUIInv.static` recorded **zero changes** across the entire capture, so
those are inventory-grid layout constants that happen to be 7/8/9. This is exactly the collision
risk of matching on small numbers, and the reason to demand an unusual value.

No other target held a 7 or a 9 anywhere.

### Why one snapshot is not enough

A single bind-time dump has nothing to be compared against. What identifies a counter is not its
value at one instant but that it **differs between two states whose ammo we know**.

### `F4` — snapshot on demand

`F4` now dumps the **complete** state of every target: all scalars including zeros, plus all
arrays, under a numbered header.

```
#### SNAPSHOT #1 ####
#S  player: FDOJDJLIGLF=100 MOPBMENEGLN=2 ...
#S  HUD: ...
```

Zeros are included deliberately: an empty magazine reads `0`, and excluding it would hide the field
at exactly the moment it is most identifiable.

Procedure: press `F4` at a known ammo count, fire a few rounds, press `F4` again at the new count.
Diffing the two snapshots against a known delta identifies the field by elimination rather than by
hoping to catch it mid-change.

### Keys

| Key | Effect |
|---|---|
| `F4` | full snapshot of every target, on demand |
| `F5` | include/mute per-frame churn fields |
| `F6` | field watcher on/off |
| `F7` | packet capture on/off |
| `F8` | numbered marker |

---

## 22. AMMO FOUND — and it is server-pushed, not client-side

Three `F4` snapshots at a known ammo sequence **8 → (fire 2) → 6 → (fire 1) → 5** finally located it,
by elimination rather than by catching a change.

No field matched `8, 6, 5`. But of 245 scalars, only 21 differed across the three snapshots, and one
of them was unmistakable:

```
HUD.static.GONEFAMEMOJ   [325, 315, 310]     -10 for two shots, -5 for one
```

Exactly **5 per round**, giving `GONEFAMEMOJ = 285 + 5 x ammo`. It is a HUD layout coordinate for
the ammo readout, not the counter itself — but it led straight to the counter.

### The chain

Scanning for writers of `HUD` static `0x178`:

```
HUD.GEGHOEFBKMO(int a, int b, int c)    VA 0x103BCB30
    a.ToString() -> HUD static 0x160    (ammo text)
    b.ToString() -> HUD static 0x164    (ammo text)
    ...          -> HUD static 0x178    (GONEFAMEMOJ, layout)
```

And its **only two callers**:

```
Client.AGMCDJGEGGB()     VA 0x10B475A0
Client.FPKEAECEOPE()     VA 0x10B4F...   <-- the inbound packet processor
```

The call site sits directly after `NET.AGIJMMKMPPB(buffer, len, 4)` and `NET.IFIEBMLBNIN()` — i.e.
it is **decoding an inbound packet** and feeding the values straight to the HUD.

### This inverts §3

§3 concluded "there is no ammo opcode, the server is never told, ammo is client-local simulation."
That was inferred from the **outgoing** opcode table only, and the inference was backwards.

**Ammo is server-authoritative.** The client never owns a magazine counter — it receives the numbers
and renders them. That is why nine objects, 245 scalars and eight arrays contained no ammo value:
there is nothing to find client-side. It also explains why infinite ammo has never worked, and it
is a genuine answer to the original question about what the server controls.

Reload remains client-side (no outgoing reload opcode, and the timers in §11 are real), but the
**ammo count that reload restores comes from the server.**

### Instrumentation

`NetProbe` now hooks `HUD.GEGHOEFBKMO` and logs on change:

```
AMMO a=<mag> b=<reserve> c=<third>
```

Which argument is magazine vs reserve still needs one run to confirm against the on-screen numbers.

---

## 23. RETRACTION of section 22 — that was the match clock, not ammo

**Section 22 is wrong. Ammo has NOT been found, and it is NOT known to be server-pushed.**

Hooking `HUD.GEGHOEFBKMO` directly, in a run of 3 shots ending at 7 rounds:

```
11153  a=30 b=45 c=155
16096  a=31 b=45 c=150
21059  a=31 b=45 c=145
26214  a=31 b=46 c=140
31061  a=31 b=46 c=135
```

- Calls arrive every **~5 s** (deltas 4943/4963/5155/4846 ms) — a HUD refresh, not per shot
- `c` counts **down** 155→135 — the **match clock in seconds**
- `a` and `b` count **up** (30→31, 45→46) — **team scores**
- Only 5 calls for 3 shots, and none aligned to a shot

So `GEGHOEFBKMO(scoreA, scoreB, clockSeconds)` is the **scoreboard and timer**.

### How the error happened

`GONEFAMEMOJ` read `[325, 315, 310]` across three snapshots, which I read as "−10 for two shots,
−5 for one shot = 5 per round". It was the match clock, and the snapshots were ~8.1 s and ~4.7 s
apart. The deltas tracked **elapsed time**, not shots fired.

This is the same failure as the 7/8/9 constants in §21 — a small-number coincidence accepted
without a control — and it was made one section after warning about it.

### Rule: snapshot diffs need a control

A field differing between two snapshots may be responding to **time**, not to the action. Before
attributing a delta to shooting:

1. Take two snapshots **with no shooting between them**, separated by a similar interval. Every
   field that moves is time-driven — clocks, accumulators, bob, score.
2. Only fields that move in the shooting pair but *not* in the idle pair are candidates.
3. Confirm the **rate**: a real magazine changes by exactly the number of shots, and does not
   change when idle.

The `F4` snapshot tool is right; the protocol around it was not.

### Retained

The hook is kept and renamed `OnScoreHud`, logging `SCORE a=<scoreA> b=<scoreB> clock=<seconds>`.
It is genuinely useful — just not for ammo.

### Ammo status: still unknown

Ruled out: all scalars and arrays on `KBBBHJDINCB`, `Controll` (static + instance), `CGJPBNDDPIN`,
`FPNENMKEFBB`, `PLH.static`, `HUD` (static + instance), `GUIInv` (static + instance) — 245 scalars,
8 arrays, 11 targets. `GEGHOEFBKMO` is not it either.
