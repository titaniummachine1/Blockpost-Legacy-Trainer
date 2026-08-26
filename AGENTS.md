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
- Aliased classes: 367 (279 target + all referenced types + 8 remaining obfuscated)
- Field aliases: 4,508
- Method aliases: 16,448
- Property aliases: 477
- Generated SDK files: 369 (367 classes + Aliases.cs + SdkIndex.cs)
- Build: 0 errors, 2 warnings (pre-existing NuGet dependency resolution)
- Duplicate-target conflicts: 0 (all resolved)

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
| UIAmmo | AmmoDisplay | 89 | HUD ammo display (_ammo, _backpack, _weaponName, _weaponIcon, _reloadGO, _reloadingProgress) |
| UIReload | ReloadDisplay | 400 | Reload UI (_ammo, _backpack, _weaponName, _weaponIcon, _ammoIndicator) |
| UIScores | Scoreboard | 192 | Scoreboard (_rdName, _rdScore, _blName, _blScore, _timer, _avatarsLeft, _avatarsRight) |
| HUDSoundFX | SoundFX | 441 | HUD sound effects (static AudioClip fields, Play/PlayDryFire) |
| VoxelMapData | MapData | 226 | Map metadata (MapName, PositionX/Y/Z, RotationY) |
| VUtil | VoxelUtil | 236 | Voxel collision (headcontact, groundcontact, bodycontact, isValidBBox) |
| GHCFEALGKNG | VoxelMeshGen | 104 | Voxel mesh generation (static, draw voxel at int+Vector3+Color) |
| ScopeGen | ScopeGenerator | 391 | Weapon scope rendering (Texture2D, MeshFilter, MeshRenderer) |
| Builder | MapBuilder | 337 | Map builder (toolmode, current, currblock, blockCursor, goCursor) |
| FaceGen | FaceGenerator | 396 | Character face texture generation (returns Texture2D) |
| EEKHAFBGKKA | BoneRigSystem | 99 | Bone rigging (SkinnedMeshRenderer, Transform[], Matrix4x4[]) |
| OBJ | ObjLoader | 44 | OBJ model loader (objPath, materials, groups) |
| GOP | GameObjectPool | 506 | Object pooling (Prefab, Items, Bounds, Slots) |
| TEX | TextureLibrary | 330 | Texture library (tBlack, tWhite, tYellow, tGreen, tRed, tBlue, etc.) |
| GP | AuthManager | 179 | Auth/login system (auth, email, token, tokenloaded, force, connect) |
| BNKJNGIBFFM | SplineSystem | 175 | Spline animation (Vector3[] points, easing: Sine/Square/Sawtooth/Noise) |
| CALNDLKOKLP | SplinePath | 462 | Spline path data (Vector3[] points, segments, normals, closed) |
| GUIGameExit | ExitMenu | 204 | Exit game menu (show, strings, Rects) |
| MainMenu | MainMenuPage | 260 | Main menu page (extends MenuBase) |
| EnvColorMenuUI | EnvColorMenu | 154 | Environment color menu (Canvas, Sliders for RGB, Sky/Equator/Ground) |
| ContentLoader2_ | ContentLoader | 329 | Content loading screen (currprogress, progress, tLogoEn) |
| NIGHDHBMPCK | EasingMath | 282 | Easing/interpolation math (Vector3/Color/float, from/to/duration) |
| NLDGIOBHIKE | AudioManager | 346 | Audio effect manager (static, AudioClip, AnimationCurve, AudioSource) |
| IEFPCOCLAOG | BezierCurve | 54 | Bezier curve struct (4 Vector3 control points, evaluate at int) |
| MDDEPGFBEIA | MeshBuilder | 304 | Procedural mesh builder instance (List<Vector3/Vector2/Color/int[]/Vector4>, Mesh) |
| NHAMCMLKALC | StaticMeshBuilder | 166 | Static procedural mesh builder (same fields as MeshBuilder, static) |
| HLHFEHCGAOF | BoneTransform | 76 | Bone transform struct (6 Vector3, Transform/float/enum ops) |
| IKDHNPPLDGC | CurveData | 271 | Curve data container (4 List<Vector3>, copy/merge methods) |
| GUIOptions | Settings | - | Key bindings, player settings (goldmine class) |
| HUD | HUD | 101 | HUD rendering |
| GUIInv | Inventory | - | Inventory UI (master weapon/loadout/case arrays) |
| FreeFlyCamera | FreeFlyCamera | ~130 | Free-fly/spectator camera |
| MChar | CharacterModel | - | Character model (for chams) |
| GP2 | AuthManager2 | - | Secondary auth (email, token parts, SignIn, ClearData) |
| GUIGold | GoldShop | - | Gold/donate shop (discount, OpenUrl, UpdatePrice) |
| GUIBonus | BonusUI | - | Bonus/reward UI |
| GUIChar | CharacterUI | - | Character customization UI |
| GUIProfile | ProfileUI | - | Player profile (sLevel, sExp, sF/sD/sA/sH stats, hash_stats) |
| GUIRank | RankUI | - | Rank/leaderboard UI |
| PredictionPath | TrajectoryPredictor | - | Bullet trajectory (ShootingPoint, Bullet, InitialVelocity) |
| VMap | VoxelChunkMap | - | Voxel map chunk grid (collision, block ops) |
| DM | DestructionManager | - | Destruction manager (destroylist) |
| GOpt | GraphicsOptions | - | Graphics options (custom_render, custom_fog) |
| FBlock | FallingBlock | - | Falling block physics (OnCollisionEnter/Stay) |
| DistanceDraw | DistanceRenderer | - | Distance-based render optimization |
| NHMPEIHDFBK | ConfigManager | - | Localization/config (string->string lookups) |
| ReShaders | ShaderManager | - | Shader manager (renderers, materials, shaders) |
| EngineSettings | EngineConfig | - | Engine settings (Version, MeshBuilderMode) |
| DemoRec.PLAEPKANLLE | DemoSnapshot | - | Demo recording player snapshot (name, int[], 8 ints) |
| DemoRec.ICPPIMDCCID | DemoEvent | - | Demo recording event (byte[], int, float, int) |
| TMap.HEPCNDINFAP | TerrainChunk | - | Terrain chunk (int[,] height/block, Color[,], Mesh) |
| HMap.BOCMFMGPIAL | HeightmapChunk | - | Heightmap chunk (int[,], Color[,]) |
| VoxelMap.CABKODKKFIC | VoxelChunkData | - | Voxel chunk data (4 ints, Color) |
| IBLIOCOMPNF.ANMBJKPDAKI | MeshGroupEntry | - | Mesh group entry (name, sub-entries, vertices) |
| VWIK.AEJLFKOGHGN | GameObjectTransform | - | Game object transform (2 Vector3, 3 floats, int, bool) |
| JKJNIFDBKNF.PFAKIJFDDJJ | TimestampStruct | - | Timestamp struct (3 longs, int) |

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

### NET Protocol Primitives (mapped by signature analysis)
| Category | Obfuscated | Human | Signature |
|----------|-----------|-------|-----------|
| Control | LPAPGKDAENI | Begin | void Begin(byte, byte) / void Begin() |
| Control | EMJOGONJKIO | End | void End() |
| Control | NFOMAHCEFCL | Reset | void Reset() |
| Write int | ANJDGFJMIAL | WriteInt | void WriteInt(int) |
| Write int | LHMNDGLMOFO | WriteInt2 | void WriteInt2(int) |
| Write int | GJBAJNCFBLB | WriteInt3 | void WriteInt3(int) |
| Write int | KLPOMLKDPAL | WriteInt4 | void WriteInt4(int) |
| Write int | FPELFNLEPGG | WriteInt5 | void WriteInt5(int) |
| Write int | CHIOALKDHOC | WriteInt6 | void WriteInt6(int) |
| Write int | GDDMJBCNMPK | WriteInt7 | void WriteInt7(int) |
| Write short | HMCNFGMBCOC | WriteShort | void WriteShort(short) |
| Write short | IFINMFCPGIB | WriteShort2 | void WriteShort2(short) |
| Write short | APNPMHBBLDG | WriteShort3 | void WriteShort3(short) |
| Write short | IHLNBLGFGLF | WriteShort4 | void WriteShort4(short) |
| Write float | HIPPJGAHHPC | WriteFloat | void WriteFloat(float) |
| Write float | JBIICNJNHCI | WriteFloat2 | void WriteFloat2(float) |
| Write float | PIMOAOKDDCC | WriteFloat3 | void WriteFloat3(float) |
| Write byte | LMKOIABBCNK | WriteByte | void WriteByte(byte) |
| Write byte | PFCLIPCCHCK | WriteByte2 | void WriteByte2(byte) |
| Write ulong | MJDOMFPOPMK | WriteUlong | void WriteUlong(ulong) |
| Write ulong | EKDBCDKOJAO | WriteUlong2 | void WriteUlong2(ulong) |
| Write ulong | EDICJCKFAMN | WriteUlong3 | void WriteUlong3(ulong) |
| Write string | KOIHHCOBIEJ | WriteString | void WriteString(string) |
| Write string | KMEFAPEEHHN | WriteString2 | void WriteString2(string) |
| Write string | PJFMOLFBKHM | WriteString3 | void WriteString3(string) |
| Write bytes | AGIJMMKMPPB | WriteBytes | void WriteBytes(byte[], int, int) |
| Write bytes | OANLLALAOGK | WriteBytes2 | void WriteBytes2(byte[], int, int) |
| Write bytes | NPPBJCOFBMD | WriteBytes3 | void WriteBytes3(byte[], int) |
| Read int | JKONBLNHFLL | ReadInt | int ReadInt() |
| Read int | IFIEBMLBNIN | ReadInt2 | int ReadInt2() |
| Read int | DMKDAMBHBKJ | ReadInt3 | int ReadInt3() |
| Read float | CPJFIPAICPM | ReadFloat | float ReadFloat() |
| Read float | OPGALLFGLDJ | ReadFloat2 | float ReadFloat2() |
| Read string | ADBHAOJHEEK | ReadString | string ReadString() |
| Read ulong | ECDGGIONOHM | ReadUlong | ulong ReadUlong() |
| Read uint | OIONBLPJDBI | ReadUint | uint ReadUint() |
| Read bool | MFAIOGPHJPL | ReadBool | bool ReadBool() |
| Read bytes | HJMOENIPJOL | ReadBytes | byte[] ReadBytes(int) |

### NET Buffer Fields
| Field | Offset | Type | Purpose |
|-------|--------|------|---------|
| GLPMIOHOEOG | 0xc | byte[] | Send buffer |
| JGDCFADACPP | 0x10 | int | Send buffer length |
| CDKEENLDMNG | 0x14 | byte[] | Receive buffer |
| CGMLOPLCEGP | 0x1c | int | Receive buffer length |
| EAMFEKPMFEL | 0x20 | int | Send position (write cursor) |
| DBOGGPHNOPB | 0x28 | int | Read position (read cursor) |
| GAOGNNCPJGP | 0x24 | bool | Is writing flag |
| KEHFECFHDHD | 0x30 | int | Buffer size |

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
| Infinite ammo | Working | Set Controll.FGGKANNFBDH=maxAmmo, KJOMABGHAIJ=999, Player.GDEMINMDJAC[]=999 |
| Speed hack | Working | Time.timeScale + Movement.GBHJLHFPCHK/BOKNCBLLHED static fields |
| Fly hack | Working | Player.MJPOJOOIPPN.useGravity=false + Space/Shift vertical velocity |
| No clip | Working | Disable all Collider components on player root GameObject |
| Weapon unlock | Working | Populate GUIInv.LoadoutEntries with FPNENMKEFBB for every NAHLLMJMOED in AllWeapons |
| Ghost bullets | In progress | NetProbe.TryFakeHit (bypasses fire logic) |
| Third person | Working | Camera positioned behind player using yaw angle, looks at head |
| Chams | Working | Override Renderer.material with transparent colored material on enemy players |
| Triggerbot | Working | Raycast from camera center, auto-fire when crosshair hits enemy player |

## TODO Features (Prioritized)
1. **More network protocol decoders** — See BACKLOG.md for inbound packet decoders

## Weapon Unlock Architecture (Server-Dead Workaround)

### Inventory System Data Structures
| Class | Human | Key Fields | Purpose |
|-------|-------|------------|---------|
| NAHLLMJMOED | WeaponData | HAFMINBJCGN (id), OJEKKFDIKMG/NGFDENOFBLK (names), NIKINLIKGCP/MOGDFDEMPLE (stats), PPOKPPFDNDH (icon) | Weapon definition (damage, fireRate, magSize, icon) |
| FPNENMKEFBB | LoadoutEntry | AIEPBAHGMJD (ulong uniqueId), ADMGNABJBNM (WeaponData ref), NIBLMFFHJHK/PICIILNDDJO (ints), COAAKMDBKJM (byte[]) | Owned weapon instance in player loadout |
| ACEDGBLFHDK | CaseData | LDKMPMIANCE (id), OJEKKFDIKMG/NGFDENOFBLK (names), JFOEOEJLDML (WeaponData[] in case) | Case/loot box definition |
| BIMFEOACIDM | LoadoutCategory | LDKMPMIANCE (categoryId), DBMOPKGMECL (LoadoutEntry[]) | Weapon category with entries |
| EEEBDHNOPDI | ShopItem | AIEPBAHGMJD (ulong), JAPEILEGLEC (CaseData), FGEEHNDNHAM (name), GGMIOCBKKCD (float price) | Shop/case item entry |
| IFGNGLDKNPA | ShopItem2 | AIEPBAHGMJD (ulong), EJLHFMPHELL (item data) | Shop item variant 2 |
| MCCKEODPMDC | ShopItem3 | AIEPBAHGMJD (int), HENOJJHIHME (item data) | Shop item variant 3 |

### GUIInv - Master Inventory Arrays (static fields)
| Field | Obfuscated | Type | Purpose |
|-------|-----------|------|---------|
| AllWeapons | OIHNJCKDOIG | NAHLLMJMOED[] | All weapon definitions (game catalog) |
| LoadoutEntries | KNCJNHILDLJ | List<FPNENMKEFBB> | **Owned weapon instances (player loadout)** |
| LoadoutCategories | JDIHHMABLAJ | BIMFEOACIDM[] | Weapon categories |
| Cases | MMNCKDECLNA | ACEDGBLFHDK[] | Case definitions |
| ShopItems | AMJMKCLNKLB | List<EEEBDHNOPDI> | Shop items |
| ShopItems2 | LAJJDAIHOIG | MDADLLEFHKO[] | Shop items variant 2 |
| ShopItems3 | NJFHBNCFMBI | List<IFGNGLDKNPA> | Shop items variant 3 |
| ShopItems4 | IJCEALOLKJH | AEKADIMKDIL[] | Shop item data |
| ShopItems5 | OOPKGBIBNKG | List<MCCKEODPMDC> | Shop items variant 4 |
| SelectedLoadout | MHLJKCMDJGG | FPNENMKEFBB | Currently selected loadout entry |
| SelectedWeapon | KAOCDKAKFEF | CGJPBNDDPIN | Currently selected weapon instance |

### GUIOptions - Player Profile (static fields)
| Field | Obfuscated | Type | Purpose |
|-------|-----------|------|---------|
| PlayerId | gid | int | Player ID |
| AuthKey | authkey | string | Auth key |
| Exp | exp | int | Experience points |
| PlayerName | playername | string | Player display name |
| Gold | Gold | int | Gold currency |
| Level | level | int | Player level |

### Client - Inventory Network Methods
| Method | Obfuscated | Signature | Purpose |
|--------|-----------|-----------|---------|
| SendWeaponData | MGPBPDIGDBO | void(NAHLLMJMOED) | Send weapon data to server |
| SendLoadoutList | MPOCJJJJBAN | void(List<FPNENMKEFBB>) | Send loadout to server |
| SendLoadoutList2 | HLHODPPHCIP | void(List<FPNENMKEFBB>) | Send loadout (variant 2) |
| SendWeaponData2 | FLFBOKOFCHN | void(NAHLLMJMOED) | Send weapon data (variant 2) |
| SendLoadoutList3 | DLDMEBGIJNP | void(List<FPNENMKEFBB>) | Send loadout (variant 3) |
| SendLoadoutList4 | EEKLOPBNDAC | void(List<FPNENMKEFBB>) | Send loadout (variant 4) |

### PLH (Weapon) - Weapon Lookup Methods
| Method | Obfuscated | Signature | Purpose |
|--------|-----------|-----------|---------|
| GetWeaponData | FPIJPCOKIEC | NAHLLMJMOED(KBBBHJDINCB, string) | Get weapon data by name |
| GetLoadoutEntry | BEDNBOCOJHO | FPNENMKEFBB(KBBBHJDINCB, string) | Get loadout entry by name |
| GetLoadoutEntry2 | GFLLLJKKEFE | FPNENMKEFBB(KBBBHJDINCB, string) | Get loadout entry (variant 2) |
| GetLoadoutEntry3 | AFOFIPDGBBI | FPNENMKEFBB(KBBBHJDINCB, string) | Get loadout entry (variant 3) |

### Weapon Unlock Strategy (3 approaches)
1. **Populate inventory** — At game start, iterate GUIInv.AllWeapons (NAHLLMJMOED[]) and create FPNENMKEFBB entries for each. Add them to GUIInv.LoadoutEntries (List<FPNENMKEFBB>). Constructor: `FPNENMKEFBB(ulong id, NAHLLMJMOED weaponData)`.
2. **Bypass ownership check** — Hook PLH.GetLoadoutEntry variants to always return a valid entry, or hook the equip/select logic to skip ownership validation.
3. **Local profile injection** — Set GUIOptions.Gold to a large value and GUIOptions.Level high enough to unlock all weapons, then use the shop UI normally (if shop still works offline).

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
