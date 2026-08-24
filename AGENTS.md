# Blockpost Legacy Trainer - Reverse Engineering Notes

## Build & Deploy
- `dotnet build` builds and auto-deploys to `C:\Steam\steamapps\common\BLOCKPOST\BepInEx\plugins`
- Game exe: `C:\Steam\steamapps\common\BLOCKPOST\BLOCKPOST.exe`
- BepInEx log: `C:\Steam\steamapps\common\BLOCKPOST\BepInEx\LogOutput.log`
- Diagnostics: `C:\Steam\steamapps\common\BLOCKPOST\BepInEx\captures\diag-*.log`
- Config: `C:\Steam\steamapps\common\BLOCKPOST\BepInEx\plugins\BlockpostTrainer.cfg`
- SDK aliases: `Tools/sdk_aliases.json` → regenerate with `python Tools/generate_sdk.py`
- Dump file: `.tools/Il2CppDumper/dump.cs` (~240k lines)

## Key Classes (Obfuscated → Human)
| Obfuscated | Human | TypeDefIndex | Purpose |
|------------|-------|--------------|---------|
| Controll | Game | 377 | Main game controller (static singleton) |
| KBBBHJDINCB | Player | 214 | Player data (health, weapon, position) |
| CGJPBNDDPIN | WeaponItem | 458 | Weapon instance (visual/audio only, no ammo) |
| PLH | Weapon | 420 | Weapon system (fire, reload, skins) |
| Client | Network | 425 | Game server TCP client |
| MasterClient | MasterServer | 423 | Master server TCP client |
| NET | Net | - | Packet building primitives (F32, I32, U8, etc.) |
| DMHBMAAFCFJ | Hit | - | Hit data (targetId, bodyPart, point) |
| NAHLLMJMOED | WeaponData | - | Weapon definition (damage, fireRate, magSize) |
| FPNENMKEFBB | LoadoutEntry | - | Loadout slot entry |
| Movement | Movement | 2 | Movement physics (MoveGround, MoveAir, Accelerate) |
| HUD | HUD | 101 | HUD rendering |
| GUIInv | Inventory | - | Inventory UI |

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
1. **Infinite ammo** — Need to confirm which field is ammo (FGGKANNFBDH or PELNEJDOBKH)
2. **Speed hack** — Time.timeScale or modify BNHEPNNOAIK (movement speed)
3. **Fly hack** — Set Rigidbody.useGravity=false, add upward force
4. **No clip** — Disable colliders or CharacterController.detectCollisions
5. **Third person** — Move camera behind player
6. **Chams** — Material override on player models
7. **Triggerbot** — Auto-fire when crosshair on enemy (raycast check)

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
