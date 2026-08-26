# Handoff — current state

**Read [`PROTOCOL.md`](PROTOCOL.md) first.** It holds the durable knowledge: transport, opcode
table, packet layouts, field identifications, and open questions. This file is just the "where were
we" note.

## Build

```bash
dotnet build "BlockpostTrainer.csproj"
```

Auto-deploys to `C:\Steam\steamapps\common\BLOCKPOST\BepInEx\plugins`. Override with
`-p:GamePluginDirectory=...`, or disable with `-p:AutoDeploy=false`.

## What is in the plugin

| File | Role |
|---|---|
| `Plugin.cs` | trainer features + IMGUI menu; patches `Controll.Update` / `OnGUI` |
| `NetProbe.cs` | packet wire tap — hooks the `NET` writers and `Client` flush/receive |
| `FieldWatch.cs` | per-frame numeric field differ over the player entity and `Controll` |

Probe keys (only while spawned in a match, since they are read from the `Controll.Update` hook):
`F5` mute/unmute churn fields · `F6` field watcher · `F7` packet capture · `F8` marker.
Logs: `BepInEx/captures/net-<timestamp>.log`, one per run.

## Settled

- Transport is raw TCP with a hand-rolled binary framing; `Client.LPCJFAOOIKA` is the room client.
  `websocket-sharp.dll` is an unused WebGL leftover.
- Full outgoing opcode table extracted (75 opcodes, 100 callsites) — see `PROTOCOL.md` §3.
- **Hit registration is client-authoritative.** `op 0x04` declares target id, body part and hit
  point with no proof of any kind. `op 0x06` appears to carry the damage number too.
- **Movement is client-declared** (`op 0x2D`), with no client-side validation before send.
- **No ammo, reload, or health opcode exists.** The server is never told about any of them, so
  ammo and the perfect-reload minigame are pure client-side simulation. Instant reload does not
  need to be synchronised with anything — find the timer and zero it.
- Socket confirmed live during capture (`TcpClient.Connected == True`), so the captured outgoing
  packets were genuinely transmitted.

## In progress

Locating the **reload timer and magazine counter**. Confirmed *not* on `KBBBHJDINCB` — everything
moving there is presentation state (bob, sway, distance accumulator; see `PROTOCOL.md` §5). The
field watcher now also covers `Controll` statics and the live `Controll` instance; next run should
expose them.

## Known-bad, do not repeat

- Writing `999` to `ECBCOHFLJCC` / `GDEMINMDJAC` corrupts the weapon into "NO WEAPON".
- Calling `PLH.CDEGJOBLOFO` directly only plays local FX — it does not fire a real shot. Drive the
  game's own fire path instead.
- `UnityEngine.Object.FindObjectOfType<Controll>()` throws "Method unstripping failed" under
  Il2CppInterop. Take the instance from Harmony's `__instance` instead.
- Never log to a fixed filename with `FileMode.Create`; it destroyed a capture on restart.

## Next

1. Reload timer / magazine counter on `Controll`, then instant reload.
2. Find the **real inbound packet path** — two captures showed zero inbound traffic despite a
   connected socket, so `Client.FPIDGCHIEMJ` is not the live read path. Until this is fixed we
   cannot see whether the server ever corrects or rejects anything.
3. Rebuild rapid fire on top of the game's own fire path once the fire cooldown field is known.

## SDK state (latest)

- `Tools/build_sdk.py` now regenerates the entire SDK in one command and passes `verify_sdk.py`.
- `Client.ProcessPacket` is correctly aliased to `FPIDGCHIEMJ`; `Client.Flush` is `HKOFHOANEJD`.
- SDK now covers 278 aliased classes (up from 252), 3,971 field aliases, 12,570 method aliases, 411
  property aliases, with `SdkIndex.cs` providing `ByOriginalName`, `ByHumanName`, and
  `ByTypeDefIndex` lookups.
- Build is clean: 0 errors, 5 warnings (pre-existing NuGet + KeyCode `Equals` hiding + duplicate
  `using`).
