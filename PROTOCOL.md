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
