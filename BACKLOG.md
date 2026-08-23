# Protocol / SDK Backlog

This is a research backlog for the network protocol and SDK. Items are ordered
from easiest/most independent to hardest. It focuses on parsing and validation,
not on building exploit features.

1. **Inbound `0x03` snapshot decoder** — format is already mostly understood:
   `u8 count, N * (u8 id, s16 x, s16 y, s16 z, s16 yaw, u8 state)` with `x/y/z` scaled
   by `1/64` and `yaw` as unsigned 16-bit (0-65535 = 0-360°). Add to NetProbe.

2. **Inbound `0x04` event decoder** — short event packet. Payload looks like
   `u8 target?, u8 value?, u16?`. Need to correlate with health changes and hits.

3. **Human-readable `rx` logging** — parse the `F5 <op> <len> <payload>` header on
   all received packets and print `rx op=0xNN <name> : <fields>` instead of raw hex.

4. **`0x06` shot packet fields** — both tx and rx have `0x06` with three shorts.
   Confirm mapping to damage/body part/weapon/seed by correlating with `0x04` hits.

5. **Map player IDs** — connect `KBBBHJDINCB` fields (`Id0`/`Id1`/`Id2`/`PlayerId`)
   to the `u8 id` in `0x03` snapshots and the `u8 targetId` in `0x04`.

6. **`0x0F` slot switch** — both tx and rx; confirm it is just `u8 slot` plus an
   optional extra byte.

7. **`0x07`, `0x0A`, `0x0B`, `0x0C`, `0x13`, `0x14`, `0x15` decoders** — smaller
   state/event packets. Need more captures with reload, crouch, jump, death, and
   equipment use.

8. **`0x0E` and `0x34` replication decoders** — larger/medium packets, likely
   weapon/attachment/loadout replication for other players.

9. **`0x01` join/spawn message** — 39-byte initial server packet; contains the
   player assignment and room state.

10. **`0x09` loadout / `0x08` weapondata full parser** — convert the existing
    text dump into a structured log showing `id`, `codename`, `displayName`, and
    stat mapping.

11. **Server validation tests** — controlled tests to see whether the server
    validates:
    - `0x04` LOS / range / wall
    - `0x06` fire rate
    - `0x2D` movement speed and wall collision
    - `0x0F` slot against the current loadout

12. ~~**SDK property parsing**~~ — **done.** `parse_properties` in
    `Tools/generate_sdk.py` emits a `Properties` block per class, and
    `sdk_aliases.json` now supports a `Properties` map (used by
    `Game.ReloadMinigameResult`).

## Done

- **Reload system fully mapped** — see `PROTOCOL.md` §11. Start/end timestamps,
  minigame marker position, and the result enum (`0` none / `1` perfect /
  `2` failed). Perfect reload works by subtracting from the completion stamp.
  Instant reload implemented on top of it; **not yet tested in game.**
- **Alias collision fixed** — `ECBCOHFLJCC` no longer carries both `TotalAmmo`
  and `ActiveWeaponIndex`; it is `Player.ActiveWeaponId`.
