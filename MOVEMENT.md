# Movement / fire feature audit (2026-09)

Why the movement and fire-rate features misbehaved, what was changed, and what is shelved.
Root causes were found by cross-reading `dump.cs` and the capture logs — not by guessing.

## Root cause 1 — the input bitfield is rebuilt every frame

`Controll.MNHBPCOOMLE` (input state bitfield) is rebuilt by `Controll.Update` from the real
`UnityEngine.Input` state at the start of every frame. Any bit we write (`jump |= 0x10`,
`duck |= 0x20`, `sprint |= 0x40`, clearing movement bits) is wiped before the game logic that
would consume it runs. **Every feature that "set a flag" on this bitfield was a silent no-op.**

Fix: all of these now send **real OS key events** (`keybd_event`) with edge-triggered
down/up latches, exactly like auto-shoot already did with the mouse:

| Feature | Old (broken) | New |
|---|---|---|
| Auto-bhop | write jump bit | `keybd_event` Space tap on the landing frame |
| Bunny hop | Space spam every frame | Space tap only on the landing frame while held |
| Edge jump | write jump bit | Space tap on ground frame while moving |
| Slide hack | write duck bit | hold real Ctrl while moving on ground |
| Auto-crouch idle | write duck bit | hold real Ctrl while idle on ground |
| Auto-sprint | write sprint bit | hold real Shift while moving forward |
| Auto-strafe | write plus/minus x bits | alternate real A/D presses every 0.3 s |
| Fake lag | clear movement bits | **shelved** (no way to freeze rebuilt input without eating the user's keys) |

## Root cause 2 — dishonest ground detection

All jump/stance features gated on `Controll.HLBAGIACGBI`. The game's real ground state comes
from its **custom voxel AABB collision** (`VUtil.groundcontact` / `headcontact` / `bodycontact`
statics). Gating on the wrong flag caused mid-air jumps and dead hops. All features now use
`VUtil.groundcontact`; the debug overlay prints **both** values so the next run confirms the
mapping (`Ground: VUtil=... Controll=...`).

## Root cause 3 — fire-rate features zeroed the cooldown

Capture evidence: the game sets `Controll.LCMOBPPHLLM` (fire cooldown) `0 -> 0.485 -> 0`
per shot. Zeroing it every frame = fire every frame: audio spam, spread chaos, absurd `0x06`
fire rate to the server.

- **No-spread** no longer touches `Controll.LCMOBPPHLLM` (it zeroed the *cooldown*, not spread —
  that alone turned no-spread into an unintended machine gun). It only clears
  `Player.FGFKPMPLNKO`.
- **Rapid fire / fast fire** now **clamp** the cooldown to a configurable floor
  (`Min interval` slider, default 0.06 s) and skip while reloading. The game's own fire logic
  stays in charge. Bonus: the "Rapid fire" toggle was previously wired to **nothing at all**.

## Root cause 4 — speed hack used `Time.timeScale`

That scales the whole game (audio, animations, interpolation). Speed hack now scales only
`Movement.GBHJLHFPCHK` / `BOKNCBLLHED`, caching the game's originals on enable and restoring
exactly on disable (the old code restored hardcoded 6/9 guesses). `Time scale hack` stays in
Misc as its own honest tool.

## Update — the velocity exploit (teleport done right)

**Live feedback killed the position-write theory**: writing `rb.position` (the old Goku TP)
yanks the main rigidbody out of its joint chain — the player is a **jointed physics ragdoll**,
so it spun off the map uncontrollably. Position is *not* a safe lever. Velocity is:

- The Movement sim **accelerates from the current velocity** (`Accelerate(vel, wishdir, ...)`),
  so a large velocity produces a fast dash that the ragdoll, camera and netcode all follow
  naturally. Recomputed every frame while active (`velocity = dir * clamp(dist*60, 25..400)`),
  zeroed on arrival — gravity between frames cannot derail it.
- **Goku TP** now dashes to 2m behind the enemy via this dash (all previous modes kept).
- **Click TP** (new mode 5): dash toward the crosshair's raycast point while the bound key is
  held. Aim at a wall and, with no clip on, the dash continues through it.
- **Fly** is back as velocity control: `rb.velocity = wishdir * flySpeed` every frame; hover
  (no input) zeroes velocity, which also cancels gravity for that frame.
- **No clip** no longer touches Unity colliders. It Harmony-prefixes the VUtil voxel validity
  queries (`isValidBBox`, `JKHDGCLHOOL`, `CHGEMKFHCPE`, `BKOJJOHKOGM`, `LBMCCDHCEKB`) to return
  "free" while enabled. Caveat: if the bool semantics turn out inverted, the player sticks in
  place — the toggle is instantly reversible and nothing is corrupted.

Nothing here is confirmed working until one live run: dash to an enemy, click-TP across the
map, fly around, and fly through a wall with no clip on.

## Shelved — proven broken by the game's own code

| Feature | Why the old version could not work |
|---|---|
| ~~Fly hack (gravity version)~~ | `rb.useGravity=false` is ignored — the sim owns the velocity. Re-implemented above as velocity control. |
| ~~No clip (collider version)~~ | World collision is custom voxel AABB — Unity colliders are irrelevant. Re-implemented above as a VUtil query patch. |

Fake lag remains shelved (input bitfield rebuild).

## Next run checklist (validates the fixes)

1. Toggle **auto-bhop** while running: should re-jump exactly on each landing, never mid-air.
2. Debug overlay: `VUtil` ground value should go true exactly when standing on any surface.
3. **Rapid fire** with interval 0.06: steady fast fire, no sound spam, game stays alive.
4. **Speed hack** 2x: movement faster, audio/animations untouched.
5. **Slide hack**: crouch engages while running on ground, releases in air.
