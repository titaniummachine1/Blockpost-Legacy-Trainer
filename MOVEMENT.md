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

## Shelved — proven broken by the game's own code

| Feature | Why it cannot work as written |
|---|---|
| **Fly hack** | The player is driven by the game's custom Quake-style velocity sim (`Movement.MoveGround/MoveAir`); `rb.useGravity=false` is ignored and per-frame velocity writes fight the sim. Needs a Movement patch instead. |
| **No clip** | World collision is `VUtil.isValidBBox`-style custom voxel AABB — disabling Unity `Collider` components does nothing against voxel terrain. Needs a `VUtil` collision query patch. |

Both remain in the code (config still loads them) but have no UI toggle.

## Next run checklist (validates the fixes)

1. Toggle **auto-bhop** while running: should re-jump exactly on each landing, never mid-air.
2. Debug overlay: `VUtil` ground value should go true exactly when standing on any surface.
3. **Rapid fire** with interval 0.06: steady fast fire, no sound spam, game stays alive.
4. **Speed hack** 2x: movement faster, audio/animations untouched.
5. **Slide hack**: crouch engages while running on ground, releases in air.
