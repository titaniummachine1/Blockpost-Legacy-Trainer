# Blockpost Legacy Trainer - Reverse Engineering Notes

## Build & Deploy
- `dotnet build` builds and auto-deploys to `C:\Steam\steamapps\common\BLOCKPOST\BepInEx\plugins`
- Game exe: `C:\Steam\steamapps\common\BLOCKPOST\BLOCKPOST.exe`
- BepInEx log: `C:\Steam\steamapps\common\BLOCKPOST\BepInEx\LogOutput.log`
- Diagnostics: `C:\Steam\steamapps\common\BLOCKPOST\BepInEx\captures\diag-*.log`
- Config: `C:\Steam\steamapps\common\BLOCKPOST\BepInEx\plugins\BlockpostTrainer.cfg`
- Full SDK regeneration: `python Tools/build_sdk.py`
- SDK aliases: `Tools/sdk_aliases.json` → regenerate with `python Tools/build_sdk.py` or `python Tools/generate_sdk.py`
- Verify SDK: `python Tools/verify_sdk.py`
- Search classes: `python Tools/sdk_search.py ClassName`
- Dump file: `.tools/Il2CppDumper/dump.cs` (~240k lines)

## RE Tooling (Tools/)
- `dump_analyzer.py` — Parse dump.cs into `analysis/type_database.json` (5360 types, 43010 methods, 27516 fields)
- `build_sdk.py` — Full pipeline: dump_analyzer → add_aliases → auto_alias → generate_sdk → dotnet build
- `generate_sdk.py` — Generate C# SDK from dump + aliases
- `add_aliases.py` — Merge high-confidence curated overrides into sdk_aliases.json
- `auto_alias.py` — Auto-generate aliases for all 202 TARGET_CLASSES + every referenced type
- `prune_aliases.py` — Remove stale alias targets that don't resolve in the generated SDK
- `verify_sdk.py` — Validate generated SDK resolves every alias and check for duplicate human names
- `sdk_search.py ClassName` — Search the type database and alias map
- `inspect_class.py ClassName` — Inspect a specific class in detail
- `deep_analysis.py` — Find cheat target candidates (ammo, network, speed, fly, noclip, ESP, chams, triggerbot)
- `extract_strings.py` — Extract string literals (URLs, PlayerPrefs keys, field descriptions)
- `map_net.py` — Map NET protocol primitives by signature analysis
- `find_readable.py` — Find classes with readable (non-obfuscated) field names

## RE Documentation
- `PROTOCOL.md` — Network protocol, opcodes, transport layer (40KB, detailed)
- `AMMO_ANALYSIS.md` — Deep ammo field analysis with implementation strategies
- `CLASS_MAP.md` — Comprehensive class map (all important classes, fields, methods)
- `BACKLOG.md` — Feature backlog and TODO
- `HANDOFF.md` — Session handoff notes

## SDK Statistics
- Aliased classes: 278 (202 target + all referenced types)
- Field aliases: 3,971
- Method aliases: 12,570
- Property aliases: 411
- Generated SDK files: 280 (278 classes + Aliases.cs + SdkIndex.cs)
- Build: 0 errors, 5 warnings (2 pre-existing NuGet, 2 KeyCode `Equals` member hiding, 1 duplicate using)

## Key Classes (Obfuscated → Human)
| Obfuscated | Human | TypeDefIndex | Purpose |
|------------|-------|--------------|---------|
| Controll | Game | 377 | Main game controller (static singleton) |
| KBBBHJDINCB | Player | 214 | Player data (health, weapon, position) |
| CGJPBNDDPIN | WeaponItem | 458 | Weapon instance (visual/audio only, no ammo) |
| PLH | Weapon | 420 | Weapon system (fire, reload, skins) |
| Client | Network | 425 | Game server TCP client |
| MasterClient | MasterServer | 423 | Master server TCP client |
| NET | Net | 348 | Packet building primitives (F32, I32, U8, etc.) |
| DMHBMAAFCFJ | Hit | 293 | Hit data (targetId, bodyPart, point) |
| NAHLLMJMOED | WeaponData | ~290 | Weapon definition (damage, fireRate, magSize) |
| FPNENMKEFBB | LoadoutEntry | ~75 | Loadout slot entry |
| Movement | Movement | 2 | Movement physics (MoveGround, MoveAir, Accelerate) |
| MouseLook | MouseLook | ~45 | Camera look controller (sensitivity, clamps) |
| UIAmmo | AmmoDisplay | - | HUD ammo display (_ammo, _backpack Text fields) |
| GUIOptions | Settings | - | Key bindings, player settings (goldmine class) |
| HUD | HUD | 101 | HUD rendering |
| GUIInv | Inventory | - | Inventory UI |
| FreeFlyCamera | FreeFlyCamera | ~130 | Free-fly/spectator camera |
| MChar | CharacterModel | - | Character model (for chams) |

## Controll Class - Key Fields
| Field | Offset | Type | Purpose |
|-------|--------|------|---------|
| LPCJFAOOIKA | 0x0 | Controll | Singleton instance |
| HGAODFPBGLB | 0x140 | KBBBHJDINCB | Main player |
| CDFACGAFFFH | 0x88 | Camera | Main camera |
| NAKNALFCOIF | 0x3C | float | Yaw angle (silent aim) |
| IGLCENGMMMJ | 0x40 | float | Pitch angle (silent aim) |
| EPEEFBDJAHO | 0x44 | float | Fire input flag (1=firing, 0=not) |
| LCMOBPPHLLM | 0x20 | float | Fire timer / spread |
| GOMFKJNNJAP | 0x1F0 | List<Hit> | Hit list for network |
| GAMBHJPMDON | 0x1EC | int | Hit sequence counter |
| KEPGFOEOHPD | 0xB0 | bool | Can fire |
| HLBAGIACGBI | 0xB1 | bool | Is grounded |
| PBICPLCFAGG | 0xB2 | bool | Is sprinting |
| NJPDKJKJMCG | 0xB3 | bool | Is crouching |
| GCHFDAPNBNB | 0xB4 | bool | Is jumping |
| BFEOOOMMGLK | 0xB5 | bool | Is aiming (ADS) |
| EKEAAHAKHIN | 0xB6 | bool | Is reloading (flag) |
| DJACNOGOCKD | 0xB7 | bool | Is reloading (timer) |
| FGGKANNFBDH | 0xC0 | int | **Ammo in magazine (candidate)** |
| ILFOFIOFBAM | 0xC8 | int | **Max ammo (candidate, -1 = no weapon)** |
| KJOMABGHAIJ | 0xCC | int | **Reserve ammo (candidate)** |
| CFACCGMPPOE | 0xF0 | int | Current health (Controll-side) |
| NKFBOBMMGCL | 0xF4 | int | Max health (Controll-side) |
| FBINCNDDPAO | 0x1A8 | float | Reload start time |
| ILGHFLMKMCO | 0x1AC | float | Reload end time (set = start for instant reload) |
| DEBGAILDKPC | 0x138 | int | Kill count |
| GKNJELHPMDE | 0x13C | int | Death count |
| POFKNJGAKPK | 0x218 | int | Team ID |
| OGDPMIBJLDH | 0x21C | int | Player ID |
| MNHBPCOOMLE | 0x220 | uint | Input state bitfield |

## Player Class (KBBBHJDINCB) - Key Fields
| Field | Offset | Type | Purpose |
|-------|--------|------|---------|
| FDOJDJLIGLF | 0x38 | int | Health |
| EFHBKMHCMOH | 0x3C | int | Max health |
| INGHEHAALBJ | 0x40 | int | Armor |
| OOMJGHCFODI | 0x44 | Vector3 | Position |
| FGFKPMPLNKO | 0x84 | float | Spread / recoil accumulator |
| LCMOBPPHLLM | 0x178 | float | Fire timer |
| MOPBMENEGLN | 0xA0 | int | Current weapon slot |
| GDEMINMDJAC | 0xA8 | int[] | Ammo per slot (candidate) |
| ECBCOHFLJCC | 0xAC | int | Active weapon ID (NOT ammo) |
| JPGGPPLOOML | prop | CGJPBNDDPIN | Active weapon instance |
| MJPOJOOIPPN | 0x114 | Rigidbody | Player rigidbody (for bunnyhop) |
| CLOEJLAOIGI | 0x1EC | bool | Is dead / movement flag |
| CGHKKDBILGF | 0x1EF | bool | Is down / movement flag |
| LBKINNIDKEC | 0x1A8 | bool | Spawn protected |
| PELNEJDOBKH | 0xCC | int | **Ammo candidate (from demo recording)** |
| GEDMGLAMGMD | 0x180 | int | **Ammo candidate (from weapon data)** |
| MHCOJFIAGLP | 0x184 | int | **Ammo candidate (paired with above)** |

## Movement Flags Enum (Controll.NJPOPGGFJIH)
| Flag | Value | Meaning |
|------|-------|---------|
| plus_x | 1 | Move right |
| minus_x | 2 | Move left |
| plus_z | 4 | Move forward |
| minus_z | 8 | Move backward |
| jump | 16 | Jump input |
| duck | 32 | Crouch input |
| speed | 64 | Sprint input |
| aim | 128 | Aim/ADS input |

## Movement Class - Static Speed Fields
| Field | Offset | Type | Purpose |
|-------|--------|------|---------|
| GBHJLHFPCHK | 0x0 | float | Move speed constant (speed hack target) |
| BOKNCBLLHED | 0x4 | float | Sprint speed constant (speed hack target) |
| MBBPFKGLEHN | 0x8 | float | Ground accel constant |
| PBIMHJCFMMK | 0xC | float | Air accel constant |
| OFKFIJICIIP | 0x10 | float | Friction constant |

Key methods: Accelerate(vel, wishDir, accel, maxSpeed), MoveGround(vel, wishDir), MoveAir(vel, wishDir)

## MouseLook Class - Key Fields
| Field | Offset | Type | Purpose |
|-------|--------|------|---------|
| active | 0xC | bool | Look active flag |
| sensitivityX | 0x14 | float | X sensitivity (aimbot smoothing) |
| sensitivityY | 0x18 | float | Y sensitivity (aimbot smoothing) |
| minimumX | 0x1C | float | Min X angle |
| maximumX | 0x20 | float | Max X angle |
| minimumY | 0x24 | float | Min Y angle |
| maximumY | 0x28 | float | Max Y angle |
| EPDMPLDKCDK | 0x48 | Quaternion | Rotation (silent aim target) |

## UIAmmo Class - Display Fields
| Field | Offset | Type | Purpose |
|-------|--------|------|---------|
| _ammo | 0x10 | Text | Magazine ammo display |
| _backpack | 0x14 | Text | Reserve ammo display |
| _reloadGO | 0x28 | GameObject | Reload indicator |
| _reloadingProgress | 0x2C | Image | Reload progress bar |

Key method: PFBJDHPMIJP(int ammo, int reserve) — Update both ammo displays

## Network Protocol
- TCP-based, custom binary protocol
- NET class provides primitives: Begin, F32, I32, U8, I16, End, string read/write
- Client.AHLDAPJEJNC(Vector3 origin, uint seq, List<Hit> hits) = send hit report
- Client.HKOFHOANEJD() = flush/send packet
- Client.FPIDGCHIEMJ(byte[], int) = process received packet
- No explicit opcodes in dump — hardcoded in compiled methods
- Server validates player IDs, positions, hit data, weapon data

## Implemented Features
| Feature | Status | How |
|---------|--------|-----|
| ESP (boxes) | Working | OnGUI WorldToScreenPoint |
| Aimbot (plain) | Working | Camera rotation to target |
| Silent aim | Working | Redirect yaw/pitch + camera, restore in postfix |
| Auto-shoot | Working | mouse_event LEFTUP+LEFTDOWN in postfix |
| No recoil | Working | Harmony patch ILIDJBFOFJA, zero recoil force |
| Infinite health | Working | Set FDOJDJLIGLF=1000, clear death flags |
| Instant reload | Working | Set ILGHFLMKMCO=FBINCNDDPAO in prefix |
| Bunnyhop | Working | keybd_event space while held |
| FOV changer | Working | Camera.fieldOfView = targetFov |
| Custom crosshair | Working | OnGUI line drawing |
| Rapid fire | Working | FGFKPMPLNKO manipulation |
| Ghost bullets | In progress | NetProbe.TryFakeHit (bypasses fire logic) |

## TODO Features (Prioritized)
1. **Infinite ammo** — Controll.FGGKANNFBDH (0xC0) = ammo in mag, Player.GDEMINMDJAC (0xA8) = ammo per slot array. See AMMO_ANALYSIS.md for 3 implementation strategies.
2. **Speed hack** — Modify Movement.GBHJLHFPCHK (move speed) and Movement.BOKNCBLLHED (sprint speed) static fields
3. **Fly hack** — Hook Movement.MoveGround/MoveAir to add vertical velocity, or set Player.MJPOJOOIPPN.useGravity=false
4. **No clip** — Hook Movement.MoveGround to ignore collision checks, or disable player colliders
5. **Third person** — Move camera behind player (FreeFlyCamera has settings for this)
6. **Chams** — Material override on player models (MChar/MCharAnimator classes)
7. **Triggerbot** — Auto-fire when crosshair on enemy (raycast check + PLH.CDEGJOBLOFO call)

## Auto-shoot Architecture
- Prefix: Reset autoShootPending=false → run aimbot → if target found, set autoShootPending=true
- Prefix: If !autoShootPending && pendingLeftMouseUp → send LEFTUP (release stuck button)
- Postfix: If autoShootPending && alive → send LEFTUP+LEFTDOWN (fresh click for next frame)
- Postfix: If !autoShootPending && pendingLeftMouseUp → send LEFTUP
- Game uses GetMouseButtonDown (edge-triggered), not GetMouseButton (held)
- mouse_event goes to Windows queue, Unity polls it next frame

## Silent Aim Architecture
- Prefix: Save real yaw/pitch → calculate target angles → set yaw/pitch + camera rotation
- Controll.Update runs with redirected angles → fires at target
- Postfix: Restore real yaw/pitch + mouse delta (camera doesn't lock)
- Auto-shoot sends virtual click → game fires at redirected angles
