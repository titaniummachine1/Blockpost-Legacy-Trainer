# Inventory / Loadout / Economy — architecture

How the inventory actually works, what is server-side, and what is still untested. Complements
`PROTOCOL.md` (transport) and `Sdk/README.md` (aliases). Research date: 2026-09, dump of
BLOCKPOST build on Unity 2021.3.42f1, Steam appid **706990**.

## TL;DR

| Question | Answer | Confidence |
|---|---|---|
| Where does the inventory live? | **Server-side**, per account, on `underdogs.ru` backend | high |
| How does it reach the client? | Over the **game TCP protocol** (op `0x08` weapondata / `0x09` loadout), not HTTP, not Steam Cloud | high |
| Steam Cloud used for inventory? | No. Steam is used for **auth ticket** + micro-transactions + market links only | high |
| Are game settings local? | Yes — PlayerPrefs / `GUIOptions` statics (name, keybinds, gold/exp display), `GUIOptions.AuthKey` cached locally | medium |
| Can we force-equip a weapon we don't own? | Client-side yes (`GameAccess.ForceLoadoutEntry`), server keeps its own copy — slot switch may be rejected | client part proven, server part **untested** |
| Crates/cases | Contents are client-known (`WeaponCollection.Weapons`); **opening is a server transaction** | high |
| Missions | `UIMTasks` is empty UI stubs — mission logic lives elsewhere (likely server-pushed). **Not solved** | — |
| Achievements | Via Steamworks (SteamManager); progress likely server-pushed. **Not solved** | — |
| XP / Gold / Level | `GUIOptions.Exp/Gold/Level` writes are **display-only**; real values come from the server | proven (0x06 carries damage; profile sync untested) |

## Auth chain

1. Steam: `SteamManager` gets a **session ticket** (`GetAuthSessionTicket`, `SteamManager.ticket`
   static) — this identifies the Steam account.
2. `GP` (AuthManager): `auth`, `email`, `token`, `tokenloaded`, `force`, `connect` statics.
   `GP2` (AuthManager2) holds the token split in two parts (`token_part0/1`) with
   `SendAuth / SignIn / ClearData / Chop` methods — the non-Steam (VK/web) login path.
3. Token authenticates against the backend (`https://files.underdogs.ru/bp/u` is the only game
   file host in the string tables; the shop/market links — Steam market appid 706990, VK order
   URL — are external storefronts).

## Inventory model in the client

`GUIInv` (Inventory) statics — populated once by the server, then read by every UI:

| Alias | Type | Meaning |
|---|---|---|
| `AllWeapons` | `WeaponData[]` | full weapon catalog (definitions, not ownership) |
| `LoadoutEntries` | `List<LoadoutEntry>` | **owned** items — this is the inventory |
| `LoadoutCategories` | `BIMFEOACIDM[]` | UI category grouping |
| `Cases` | `WeaponCollection[]` | crates; `Weapons` field = possible contents |
| `ShopItems`/`ShopItems2..5` | shop variants | storefront entries |
| `SelectedLoadout` / `SelectedLoadoutAlt` | `LoadoutEntry` | current selection (equip) |
| `SelectedWeapon` | `WeaponInstance` | previewed weapon |
| `Progress0..4` | `int[]` | candidate mission/achievement progress (unverified) |

`LoadoutEntry` (`FPNENMKEFBB`) = one owned item: `UniqueId` (server instance id, ulong),
`WeaponData` ref, `SkinData` (byte[] — **skin changer path**), `Slot` (byte), three
`InstanceStat*` ints, and 10 `StatGetter` methods (computed stats).

## Network surface

Outgoing (client → server):

| Alias | Obfuscated | Purpose |
|---|---|---|
| `Network.SendWeaponData` | `Client.MGPBPDIGDBO` / `FLFBOKOFCHN` | push weapon data (`0x08`) |
| `Network.SendLoadoutList` | `Client.MPOCJJJJBAN` / `HLHODPPHCIP` / `DLDMEBGIJNP` / `EEKLOPBNDAC` | push loadout (`0x09`), 4 variants |

Inbound: inventory is filled during menu load from server packets (`0x08`/`0x09` family; the
**real inbound path is still unfixed** — see `HANDOFF.md` Next #2).

## What can be forced (and what will bite back)

1. **Equip / slot switching** — client-declared (`Player.Slot` write + op `0x0F`).
   `GameAccess.ForceSlot(slot)` works in-match today.
2. **Inject unowned weapon** — `GameAccess.ForceLoadoutEntry("ak47")` creates a
   `LoadoutEntry` with a fake `UniqueId` and selects it. Works until the next server
   inventory sync. **Untested**: whether the server accepts `0x0F` for a slot it does not
   believe you own (BACKLOG item 11 — the moment this is tested, we know if free-loadout is
   real or client-skin-only).
3. **Skin changer** — `LoadoutEntry.SkinData` bytes pushed via `SendWeaponData` is the
   designed path; whether the server validates ownership of the skin id is untested.
4. **Purchases / crate opening / missions / achievements** — server transactions. The client
   can *display* anything (GUIOptions writes) but cannot mint items; treat all "reward" hacks
   as display-only unless a server acceptance is proven in a capture.

## Test plan (needs one capture session)

1. Join a match, `ForceLoadoutEntry` on an unowned weapon, press the slot key — watch the
   outbound `0x0F` and any server correction (needs HANDOFF #2 fixed first).
2. Rewrite `SkinData` on an owned entry, call `SendWeaponData`, observe other clients
   (second account or demo recording).
3. Complete a mission while `Progress0..4` are being watched by `FieldWatch` to confirm
   whether progress arrays are client-mirrored or server-only.
