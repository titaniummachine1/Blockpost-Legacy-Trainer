#!/usr/bin/env python3
"""Add or update curated class aliases in sdk_aliases.json.

Usage:
    python Tools/add_aliases.py

This script merges high-confidence, manually verified aliases into
Tools/sdk_aliases.json. Existing entries are preserved unless a known
override is provided here. Run this *before* Tools/auto_alias.py so
auto-generated aliases do not clobber verified mappings.
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ALIASES_FILE = ROOT / "Tools" / "sdk_aliases.json"

# Curated aliases. These override any auto-generated or stale entries.
# Only add mappings that are explicitly verified from the dump or AGENTS.md.
KNOWN_OVERRIDES = {
    "Client": {
        "HumanClass": "Network",
        "Methods": {
            "SendHitReport": "AHLDAPJEJNC",
            "ProcessPacket": "FPIDGCHIEMJ",
            "Flush": "HKOFHOANEJD",
        },
        "Notes": {
            "SendHitReport": "internal void AHLDAPJEJNC(Vector3 origin, uint seq, List<Hit> hits) - send hit report",
            "ProcessPacket": "internal void FPIDGCHIEMJ(byte[] receiveBuffer, int receiveLength) - process received packet",
            "Flush": "internal void HKOFHOANEJD() - flush/send packet",
        },
    },
    "NET": {
        "HumanClass": "Net",
    },
    "MasterClient": {
        "HumanClass": "MasterServer",
    },
    "Controll.NJPOPGGFJIH": {
        "HumanClass": "MovementFlags",
    },
    "MouseLook.NLJBDGBDDLP": {
        "HumanClass": "MouseLookAxis",
    },
    # NEGGNDFJMAK is not the same class as DevClient; keep human names unique.
    "NEGGNDFJMAK": {
        "HumanClass": "NEGGNDFJMAK",
    },
    # --- SDK expansion: curated aliases for newly added game-critical classes ---
    # HUD elements (readable field names verified from dump.cs)
    "UIAmmo": {
        "HumanClass": "AmmoDisplay",
        "Fields": {
            "Instance": "IOEOEOEJJOH",
            "PanelRT": "_panelRT",
            "AmmoText": "_ammo",
            "BackpackText": "_backpack",
            "WeaponNameText": "_weaponName",
            "WeaponIcon": "_weaponIcon",
            "WeaponIconFitter": "_weaponIconFitter",
            "AmmoIndicator": "_ammoIndicator",
            "ReloadGO": "_reloadGO",
            "ReloadingProgress": "_reloadingProgress",
            "MiniGameMarkerRT": "_miniGameMarkerRT",
            "ReloadMessage": "_reloadMessage",
            "GrenadeIcon": "_greanadeIcon",
            "AnimTimer": "IGLGDOAAIDN",
            "ReloadMessages": "KFAGDNDGLEG",
        },
        "Methods": {
            "PlayDryFire": "MFIBONNEMIK",
        },
    },
    "UIReload": {
        "HumanClass": "ReloadDisplay",
        "Fields": {
            "Instance": "IOEOEOEJJOH",
            "PanelRT": "_panelRT",
            "AmmoText": "_ammo",
            "BackpackText": "_backpack",
            "WeaponNameText": "_weaponName",
            "WeaponIcon": "_weaponIcon",
            "WeaponIconFitter": "_weaponIconFitter",
            "AmmoIndicator": "_ammoIndicator",
            "AnimTimer": "IGLGDOAAIDN",
        },
    },
    "UIScores": {
        "HumanClass": "Scoreboard",
        "Fields": {
            "Instance": "IOEOEOEJJOH",
            "RedName": "_rdName",
            "RedScore": "_rdScore",
            "BlueName": "_blName",
            "BlueScore": "_blScore",
            "Timer": "_timer",
            "AvatarsLeft": "_avatarsLeft",
            "AvatarsRight": "_avatarsRight",
            "ScoreA": "OIIKKGPPPLO",
            "ScoreB": "GNFNBOHPJKG",
            "TimerFloat": "AJBPJHMEOID",
        },
        "Methods": {
            "UpdateAvatars": "UpdateAvatars",
            "Remove": "Remove",
        },
    },
    "HUDSoundFX": {
        "HumanClass": "SoundFX",
        "Fields": {
            "Instance": "IOEOEOEJJOH",
            "Clip1": "PGCPNPCJEBA",
            "Clip2": "NPIMJBMFKCL",
            "Clip3": "BECDMHOIJOP",
            "Clip4": "DAGFCKLCKBI",
            "Clip5": "PNPCLKHGGGG",
            "Clip6": "EPMFBKEPDEG",
        },
        "Methods": {
            "Play": "Play",
            "PlayDryFire": "PlayDryFire",
            "Create": "Create",
            "Remove": "Remove",
        },
    },
    # Map / voxel
    "VoxelMapData": {
        "HumanClass": "MapData",
        "Properties": {
            "MapName": "MapName",
            "PositionX": "PositionX",
            "PositionY": "PositionY",
            "PositionZ": "PositionZ",
            "RotationY": "RotationY",
        },
    },
    "VUtil": {
        "HumanClass": "VoxelUtil",
        "Fields": {
            "HeadContact": "headcontact",
            "GroundContact": "groundcontact",
            "BodyContact": "bodycontact",
            "Points": "p",
            "PointCount": "ODAMFLEMGDD",
            "GridData": "GAHCHFJJJLF",
            "GridSize": "JEELMBIFFOP",
        },
        "Methods": {
            "IsValidBBox": "isValidBBox",
        },
    },
    "GHCFEALGKNG": {
        "HumanClass": "VoxelMeshGen",
    },
    "ScopeGen": {
        "HumanClass": "ScopeGenerator",
        "Fields": {
            "ScopeTexture": "CLKFLEKDIDG",
            "LensTexture": "LJEGHHIIDOB",
            "Width": "EKNABJAJEOA",
            "Height": "JEAFPDJIGBL",
            "Depth": "IKOOKEJOCJM",
            "ScopeColor": "NPHGPNPOANC",
            "ReticleColor": "JLJCCDJDKDB",
            "MeshFilter": "GHLKMAIOBAL",
            "MeshRenderer": "CMCACFDLCCM",
            "ScopeGO": "EHGNIHEPEAP",
        },
    },
    "ScopeGen.LFADLAIEEFI": {
        "HumanClass": "ScopeLayer",
        "Fields": {
            "X": "BCJADOIPFHE",
            "Y": "IAIHGIDAJEK",
            "Width": "EEJJPNIANHP",
            "Height": "NPMNMKLOPAM",
            "TexA": "DBMOPKGMECL",
            "TexB": "EEGPOLDOEOE",
        },
    },
    "ScopeGen.DNCPIKNIIJD": {
        "HumanClass": "ScopeConfig",
        "Fields": {
            "NameA": "DODMKOFIFPM",
            "NameB": "NGFDENOFBLK",
            "ParamA": "HMEKNEPIOPB",
            "ParamB": "KMOMLNOPJJK",
            "ParamC": "OBADHLBANCB",
        },
    },
    "Builder": {
        "HumanClass": "MapBuilder",
        "Fields": {
            "Instance": "cs",
            "ToolMode": "toolmode",
            "Current": "current",
            "CurrentBlock": "currblock",
            "BlockCursor": "blockCursor",
            "CursorGO": "goCursor",
            "CursorGO2": "goCursor2",
        },
    },
    # Character / rendering
    "FaceGen": {
        "HumanClass": "FaceGenerator",
    },
    "EEKHAFBGKKA": {
        "HumanClass": "BoneRigSystem",
        "Fields": {
            "SkinnedRenderer": "EENDPLLKLKJ",
            "BoneCount": "HIACIJPMNAD",
            "Bones": "NJLPAOIKKIA",
            "BoneMatrices": "LLAEGAJLJHO",
        },
    },
    # Object / texture loading
    "OBJ": {
        "HumanClass": "ObjLoader",
        "Fields": {
            "Instance": "cs",
            "ObjPath": "objPath",
        },
    },
    "OBJ.GKMPINHGANH": {
        "HumanClass": "ObjMaterial",
        "Fields": {
            "Name": "OJEKKFDIKMG",
            "AmbientColor": "PCCLEIPKBAE",
            "DiffuseColor": "MNKCNPHGKHE",
            "SpecularColor": "LJIPCCDEMEI",
            "Shininess": "JCPPFDFEFNP",
            "Alpha": "DAEEBKDMMKD",
            "Illumination": "NAICOIEGKKK",
            "TextureFile": "FLCNDJBKABP",
            "BumpFile": "BDANBJJNJFP",
            "Texture": "KDGBGGMIBMF",
        },
    },
    "OBJ.GBBHABJJFCJ": {
        "HumanClass": "ObjGroup",
        "Fields": {
            "GroupName": "HKIPAHCACHJ",
            "MaterialName": "NGGGDIGFGDD",
            "StartIndex": "JFKCDOHELJI",
            "EndIndex": "DMHICPPHDJN",
        },
    },
    "GOP": {
        "HumanClass": "GameObjectPool",
        "Fields": {
            "Active": "KCFCLCEPEPA",
            "Prefab": "OKKCCNPEFPD",
            "Items": "IEHDAJLNGNP",
            "Count": "CCIPDJICHNA",
            "Bounds": "MDNLOEJFDGP",
            "Center": "LEELLBLBKCB",
            "Slots": "PKMGJOFOPEN",
        },
    },
    "TEX": {
        "HumanClass": "TextureLibrary",
        "Fields": {
            "Textures": "BKLBLPENGME",
            "Default": "MLMIIDBJGCD",
            "Black": "tBlack",
            "White": "tWhite",
            "Yellow": "tYellow",
            "Green": "tGreen",
            "VK": "tVK",
            "Red": "tRed",
            "Gray": "tGray",
            "Blue": "tBlue",
            "BlackAlpha": "tBlackAlpha",
            "WhiteAlpha": "tWhiteAlpha",
            "DarkGray": "tDarkGray",
            "LightGray": "tLightGray",
            "Orange": "tOrange",
        },
        "Methods": {
            "Init": "Init",
        },
    },
    # Auth / login
    "GP": {
        "HumanClass": "AuthManager",
        "Fields": {
            "Instance": "cs",
            "Authenticated": "auth",
            "Email": "email",
            "Token": "token",
            "TokenLoaded": "tokenloaded",
            "Force": "force",
            "Connect": "connect",
        },
    },
    # Spline / animation
    "BNKJNGIBFFM": {
        "HumanClass": "SplineSystem",
        "Fields": {
            "EasingType": "PBGMGGGBNAI",
            "PointsA": "LIIJFJKEGIF",
            "PointsB": "FGKOMOFKHDK",
            "PointCount": "DFCNAGGMKKP",
            "Duration": "LMFAGEJMEAP",
            "Speed": "BLNGHNPAMBN",
            "Enabled": "EIKKPPFEOAL",
            "Target": "IHJMBOIAFFH",
        },
    },
    "BNKJNGIBFFM.BFCFFJEBEFE": {
        "HumanClass": "EasingType",
    },
    "CALNDLKOKLP": {
        "HumanClass": "SplinePath",
        "Fields": {
            "Points": "KNILCGGMDOE",
            "Segments": "CABJLLNELLP",
            "Normals": "LHBFHHKDKDD",
            "Closed": "IEFKGALOEJD",
            "Resolution": "LCANLDDJLFB",
        },
    },
    # Menu / UI
    "GUIGameExit": {
        "HumanClass": "ExitMenu",
        "Fields": {
            "Instance": "cs",
            "Show": "show",
        },
    },
    "MainMenu": {
        "HumanClass": "MainMenuPage",
    },
    "EnvColorMenuUI": {
        "HumanClass": "EnvColorMenu",
        "Fields": {
            "Instance": "IKAAKJKNBCC",
            "Visible": "CGHKKDBILGF",
            "Canvas": "DHOHKBKONEN",
            "Panel": "OPJHJFELHHM",
            "ColorImages": "CDMOBKPJFBH",
            "Labels": "JCANOHILJLD",
            "ColorPanel": "PABIHIHKEFL",
            "SliderR": "JDLCLGBPBPI",
            "SliderG": "IKFMKMDGHNO",
            "SliderB": "KJPDJOGABNP",
            "TextR": "FCPNDEOIDME",
            "TextG": "ECHLHNEGFGB",
            "TextB": "PCEFFBHDKMM",
            "Preview": "HPOKAEMOJEA",
            "CurrentMode": "EAECCOHLCBM",
        },
    },
    "EnvColorMenuUI.PFEFIEFHKIN": {
        "HumanClass": "EnvColorMode",
    },
    "ContentLoader2_": {
        "HumanClass": "ContentLoader",
        "Fields": {
            "CurrentProgress": "currprogress",
            "Progress": "progress",
            "LogoEn": "tLogoEn",
        },
        "Methods": {
            "Load": "Load",
        },
    },
    # --- SDK expansion: game-logic classes found by type-reference analysis ---
    "NIGHDHBMPCK": {
        "HumanClass": "EasingMath",
    },
    "NLDGIOBHIKE": {
        "HumanClass": "AudioManager",
        "Fields": {
            "StaticFloat": "CMCLMFGCCLA",
            "StaticInt": "CFEGCIHNEFD",
            "FloatArray": "JBGODJLBJAO",
            "StaticInt2": "KOBGIPEGALM",
            "FloatArray2": "JLJLEHPGHAE",
        },
    },
    "IEFPCOCLAOG": {
        "HumanClass": "BezierCurve",
        "Fields": {
            "Point0": "ILJIKLBMLHD",
            "Point1": "OKBGGBHIGKO",
            "Point2": "FMHDDOHEHHP",
            "Point3": "FKCJAMHCGJM",
        },
        "Properties": {
            "Normal": "HPJPLEAAKKC",
        },
    },
    "MDDEPGFBEIA": {
        "HumanClass": "MeshBuilder",
        "Fields": {
            "Vertices": "KBJCMOOGDMI",
            "UVs": "KCHPJDAEAMJ",
            "Normals": "CABJLLNELLP",
            "Colors": "MOHFPKCMOLE",
            "Triangles": "KEEHIJCEFHE",
            "Tangents": "GIFGBFHBLJJ",
            "VertexCount": "CLOPJEMMHNI",
            "Mesh": "KAGKBGONJIH",
        },
    },
    "NHAMCMLKALC": {
        "HumanClass": "StaticMeshBuilder",
        "Fields": {
            "Vertices": "KBJCMOOGDMI",
            "UVs": "KCHPJDAEAMJ",
            "Normals": "CABJLLNELLP",
            "Colors": "MOHFPKCMOLE",
            "Triangles": "KEEHIJCEFHE",
            "Tangents": "GIFGBFHBLJJ",
            "VertexCount": "CLOPJEMMHNI",
            "Mesh": "KAGKBGONJIH",
            "Bounds": "OCBAONFDAGI",
        },
    },
    "HLHFEHCGAOF": {
        "HumanClass": "BoneTransform",
        "Fields": {
            "Pos0": "KCDDEAJOGKI",
            "Pos1": "GEIDPNNOOPK",
            "Pos2": "BDAEOJKIAMK",
            "Pos3": "JBLKAOBOECG",
            "Pos4": "MHLEJGLICPE",
            "Pos5": "NNLGNHONGMN",
        },
    },
    "IKDHNPPLDGC": {
        "HumanClass": "CurveData",
        "Fields": {
            "PointsA": "BCJADOIPFHE",
            "PointsB": "IAIHGIDAJEK",
            "PointsC": "LICHKMCHMMF",
            "PointsD": "FADIMOOJPPI",
        },
    },
    # --- SDK expansion: inventory / shop / loadout / auth / profile ---
    "GP2": {
        "HumanClass": "AuthManager2",
        "Fields": {
            "Instance": "cs",
            "Email": "email",
            "TokenPart0": "token_part0",
            "TokenPart1": "token_part1",
        },
        "Methods": {
            "SendAuth": "SendAuth",
            "Auth": "Auth",
            "SignIn": "SignIn",
            "SignInNoSilent": "SignInNoSilent",
            "ClearData": "ClearData",
            "Chop": "Chop",
        },
    },
    "GUIGold": {
        "HumanClass": "GoldShop",
        "Fields": {
            "Show": "show",
            "Instance": "cs",
            "DrawOrderList": "draworderlist",
            "Tex1": "BIAEBFGDOOJ",
            "Tex2": "PFOGOBJNICE",
            "Tex3": "ONDOMLODILA",
            "Items": "BHOHPAKFOEI",
            "Title": "JODKCJJBGLN",
            "Discount": "discount",
            "TimerFloat": "DDOCIKCJKJA",
        },
        "Methods": {
            "OpenUrl": "OpenUrl",
            "UpdatePrice": "UpdatePrice",
            "UpdateTextures": "UpdateTextures",
            "SetActive": "SetActive",
            "Awake": "Awake",
        },
    },
    "GUIBonus": {
        "HumanClass": "BonusUI",
        "Fields": {
            "Instance": "cs",
            "Show": "show",
            "Items": "FJLOPOIFKCN",
            "Int1": "DEPHNHJNOEF",
            "Int2": "ACHJNCHLGDN",
            "Int3": "PBCODLDLHKL",
            "ShowNext": "shownext",
        },
        "Methods": {
            "Awake": "Awake",
            "LoadEnd": "LoadEnd",
            "LoadLang": "LoadLang",
            "OnResize": "OnResize",
        },
    },
    "GUIChar": {
        "HumanClass": "CharacterUI",
        "Fields": {
            "Show": "show",
            "Tex1": "BMCPLPJIMGH",
            "ModelGO": "MFNFGODECFM",
            "Int1": "HIDMDLLHEKO",
            "Int2": "PFGNALDDNDB",
            "TimerFloat": "NKCIOJGAABI",
        },
        "Methods": {
            "Update": "Update",
            "OnGUI": "OnGUI",
        },
    },
    "GUIProfile": {
        "HumanClass": "ProfileUI",
        "Fields": {
            "Show": "show",
            "Tex1": "BIAEBFGDOOJ",
            "Tex2": "HDMOMKHANAO",
            "LevelStr": "sLevel",
            "ExpStr": "sExp",
            "LevelProgressStr": "sLevelProgress",
            "LevelProgressFloat": "fLevelProgress",
            "ExpProgressStr": "sExpProgress",
            "KillsStr": "sF",
            "DeathsStr": "sD",
            "AssistsStr": "sA",
            "HeadshotsStr": "sH",
            "FinalDeathStr": "sFD",
            "HashStats": "hash_stats",
            "LevelTextStr": "sLeveltext",
        },
        "Methods": {
            "OnGUI": "OnGUI",
            "SetActive": "SetActive",
            "LoadEnd": "LoadEnd",
            "LoadLang": "LoadLang",
        },
    },
    "GUIRank": {
        "HumanClass": "RankUI",
        "Fields": {
            "Show": "CBFLNECJIFF",
            "Tex1": "BIAEBFGDOOJ",
            "Tex2": "HDMOMKHANAO",
            "Tex3": "CGOGLNCAHFP",
            "Tex4": "JEJJNPILBAE",
            "Tex5": "PFOGOBJNICE",
            "Entries": "HDPPMGMECHD",
            "Entries2": "OGHIGNGKKNG",
            "Int1": "AHHHAFEGJKB",
            "Str1": "GEKDOAHHCPO",
            "Int2": "INABADNCINL",
            "Float1": "HHPOIFPPMFC",
            "Str2": "LEKIPKNCCHC",
            "Str3": "ONKLHONPCHE",
            "Str4": "AEDGCKMKKJG",
            "RankEntries": "HLKMCMLCDCJ",
            "CurrentRank": "JIIMJCFDCEI",
        },
    },
    "PredictionPath": {
        "HumanClass": "TrajectoryPredictor",
        "Fields": {
            "Cube": "Cube",
            "Points": "MINIGPIOAOI",
            "ShootingPoint": "ShootingPoint",
            "EffectGO": "BAAGJMOOJDM",
            "Bullet": "Bullet",
            "Offset": "EIFLGFOOIFD",
            "FrequencyMultiplier": "FrequencyMultiplier",
            "Amount": "Ammount",
            "InitialVelocity": "InitialVelocity",
        },
    },
    "VMap": {
        "HumanClass": "VoxelChunkMap",
        "Fields": {
            "ChunkGrid": "chunk",
            "RootGO": "EOBMOEKEHLC",
        },
    },
    "DM": {
        "HumanClass": "DestructionManager",
        "Fields": {
            "DestroyList": "destroylist",
        },
    },
    "GOpt": {
        "HumanClass": "GraphicsOptions",
        "Fields": {
            "CustomRender": "custom_render",
            "CustomFog": "custom_fog",
            "CustomFogStart": "custom_fogstart",
            "CustomFogEnd": "custom_fogend",
        },
    },
    "MSC": {
        "HumanClass": "MeshSceneCache",
        "Fields": {
            "FullMesh": "cfull",
            "MeshData": "cmesh",
        },
    },
    "FBlock": {
        "HumanClass": "FallingBlock",
        "Methods": {
            "OnCollisionEnter": "OnCollisionEnter",
            "OnCollisionStay": "OnCollisionStay",
        },
    },
    "DistanceDraw": {
        "HumanClass": "DistanceRenderer",
        "Fields": {
            "IntArray": "HKPMJHCGMDE",
            "Int1": "KNELGJDCJKB",
            "Int2": "CAHOBOPACCF",
            "CameraTransform": "IJLPLONACIA",
            "Int3": "AIHGALBLKEE",
            "Int4": "KMDGOMBCGPE",
            "Str1": "GNDKDPAMAGI",
            "Str2": "JBNKAGCMJGP",
        },
        "Methods": {
            "Update": "Update",
            "Awake": "Awake",
        },
    },
    "NHMPEIHDFBK": {
        "HumanClass": "ConfigManager",
        "Fields": {
            "Int1": "PIJHEDNJIFG",
            "Int2": "DCGEHGIDMFF",
            "Int3": "FPNHCOFPDKK",
            "Float1": "EBMLPPEHJHM",
            "Bool1": "NNBODLNCPJL",
            "Bool2": "IKLJJBNPMJL",
        },
    },
    "ReShaders": {
        "HumanClass": "ShaderManager",
        "Fields": {
            "Renderers": "renderers",
            "Materials": "materials",
            "Shaders": "shaders",
        },
    },
    "EngineSettings": {
        "HumanClass": "EngineConfig",
        "Fields": {
            "Version": "Version",
            "MainEngineMode": "MainEngineMode",
            "ControlSnapToBlock": "ControlSnapToBlock",
            "MeshBuilderMode": "MeshBuilderMode",
            "BuildOptimizedWeaponMesh": "BuildOptimizedWeaponMesh",
            "BuildWeaponWithVerticesOffset": "BuildWeaponWithVerticesOffset",
            "BuildWeaponVerticesOffset": "BuildWeaponVerticesOffset",
        },
    },
    # Client inventory/loadout network methods (verified from dump)
    "Client": {
        "HumanClass": "Network",
        "Methods": {
            "SendHitReport": "AHLDAPJEJNC",
            "ProcessPacket": "FPIDGCHIEMJ",
            "Flush": "HKOFHOANEJD",
            "SendWeaponData": "MGPBPDIGDBO",
            "SendLoadoutList": "MPOCJJJJBAN",
            "SendLoadoutList2": "HLHODPPHCIP",
            "SendWeaponData2": "FLFBOKOFCHN",
            "SendLoadoutList3": "DLDMEBGIJNP",
            "SendLoadoutList4": "EEKLOPBNDAC",
        },
        "Notes": {
            "SendWeaponData": "void MGPBPDIGDBO(NAHLLMJMOED) - send weapon data to server",
            "SendLoadoutList": "void MPOCJJJJBAN(List<FPNENMKEFBB>) - send loadout entries to server",
            "SendLoadoutList2": "void HLHODPPHCIP(List<FPNENMKEFBB>) - send loadout entries (variant 2)",
            "SendWeaponData2": "void FLFBOKOFCHN(NAHLLMJMOED) - send weapon data (variant 2)",
            "SendLoadoutList3": "void DLDMEBGIJNP(List<FPNENMKEFBB>) - send loadout entries (variant 3)",
            "SendLoadoutList4": "void EEKLOPBNDAC(List<FPNENMKEFBB>) - send loadout entries (variant 4)",
        },
    },
    # PLH (Weapon) weapon lookup methods (verified from dump)
    "PLH": {
        "HumanClass": "Weapon",
        "Methods": {
            "Fire": "CDEGJOBLOFO",
            "FireAlt": "MFHJFPPOHLC",
            "GetWeaponData": "FPIJPCOKIEC",
            "GetWeaponData2": "OIOKLCCFNBM",
            "GetLoadoutEntry": "BEDNBOCOJHO",
            "GetLoadoutEntry2": "GFLLLJKKEFE",
            "GetLoadoutEntry3": "AFOFIPDGBBI",
            "GetWeaponData3": "EJBFDBKKECM",
        },
        "Notes": {
            "GetWeaponData": "NAHLLMJMOED FPIJPCOKIEC(KBBBHJDINCB player, string weaponName) - get weapon data by name",
            "GetLoadoutEntry": "FPNENMKEFBB BEDNBOCOJHO(KBBBHJDINCB player, string weaponName) - get loadout entry by name",
        },
    },
    # GUIInv - master inventory arrays (verified from dump)
    "GUIInv": {
        "HumanClass": "Inventory",
        "Fields": {
            "Show": "CBFLNECJIFF",
            "Bool1": "HIMLDGKPFPO",
            "Bool2": "DGIJIEDOAEG",
            "AllWeapons": "OIHNJCKDOIG",
            "LoadoutEntries": "KNCJNHILDLJ",
            "LoadoutCategories": "JDIHHMABLAJ",
            "Cases": "MMNCKDECLNA",
            "ShopItems": "AMJMKCLNKLB",
            "ShopItems2": "LAJJDAIHOIG",
            "ShopItems3": "NJFHBNCFMBI",
            "ShopItems4": "IJCEALOLKJH",
            "ShopItems5": "OOPKGBIBNKG",
            "ModelGO": "MFNFGODECFM",
            "Camera": "KKDJBDOBOJH",
            "PlayerModel": "GKGPFPNLIBE",
            "SelectedLoadout": "MHLJKCMDJGG",
            "SelectedCategory": "LPCJFAOOIKA",
            "SelectedWeapon": "KAOCDKAKFEF",
            "Bool3": "HKFEGAMNGEB",
            "SelectedSkin": "IKKIINGIICF",
            "Int1": "MPBHJGCJBDE",
            "Float1": "JMACIGCBFBD",
            "CurrentLoadoutEntry": "PJMELMGMNDO",
        },
        "Notes": {
            "AllWeapons": "NAHLLMJMOED[] OIHNJCKDOIG - all weapon definitions",
            "LoadoutEntries": "List<FPNENMKEFBB> KNCJNHILDLJ - owned weapon instances (loadout)",
            "LoadoutCategories": "BIMFEOACIDM[] JDIHHMABLAJ - weapon categories",
            "Cases": "ACEDGBLFHDK[] MMNCKDECLNA - case definitions",
        },
    },
    # GUIOptions - player profile/gold/level (verified from dump)
    "GUIOptions": {
        "HumanClass": "Settings",
        "Fields": {
            "Show": "show",
            "PlayerId": "gid",
            "AuthKey": "authkey",
            "Exp": "exp",
            "PlayerName": "playername",
            "GoldStr": "sGold",
            "Gold": "Gold",
            "NameCount": "NameCount",
            "NameKey": "namekey",
            "Level": "level",
            "GidStr": "sGid",
            "GlobalPresetLoad": "globalpresetload",
            "DistanceView": "distanceview",
            "GlobalPreset": "globalpreset",
            "HealthPosition": "health_position",
            "Resolution": "res",
            "DirectLight": "directlight",
            "AA": "aa",
            "Flare": "flare",
            "Motion": "motion",
            "Particles": "particles",
            "SSAO": "ssao",
            "GameVolume": "gamevolume",
            "MusicVolume": "musicvolume",
            "MobileRes": "mobileres",
            "MobileFPS": "mobilefps",
            "MobileGreedy": "mobilegreedy",
            "MobileFOV": "mobilefov",
            "MobileSecondAttack": "mobilesecondattack",
            "ShowInGame": "showingame",
            "KeyForward": "keyForward",
            "KeyBackward": "keyBackward",
            "KeyStrafeLeft": "keyStrafeLeft",
            "KeyStrafeRight": "keyStrafeRight",
            "KeyCrouch": "keyCrouch",
            "KeyPrimary": "keyPrimary",
            "KeySecondary": "keySecondary",
            "KeyShovel": "keyShovel",
            "KeyBlock": "keyBlock",
            "KeyPrevWeapon": "keyPrevWeapon",
            "KeySpecial": "keySpecial",
            "KeyChat": "keyChat",
            "KeyClanChat": "keyClanChat",
            "KeyTeamChat": "keyTeamChat",
            "KeyChangeSet": "keyChangeSet",
            "KeyChangeTeam": "keyChangeTeam",
        },
        "Methods": {
            "LoadConfigTouch": "LoadConfigTouch",
            "OnGUI": "OnGUI",
            "LoadEnd": "LoadEnd",
        },
    },
    # GP - auth manager (verified from dump)
    "GP": {
        "HumanClass": "AuthManager",
        "Fields": {
            "Instance": "cs",
            "Authenticated": "auth",
            "Email": "email",
            "Token": "token",
            "TokenLoaded": "tokenloaded",
            "Force": "force",
            "Connect": "connect",
        },
        "Methods": {
            "Awake": "Awake",
            "LoadEndFirst": "LoadEndFirst",
        },
    },
}

# Optional user-editable file for extra curated aliases.
EXTRA_ALIASES_FILE = ROOT / "Tools" / "known_aliases.json"


def deep_update(base: dict, override: dict) -> dict:
    """Recursively update `base` with `override`. Lists are replaced."""
    for key, value in override.items():
        if isinstance(value, dict) and key in base and isinstance(base[key], dict):
            base[key] = deep_update(base[key], value)
        else:
            base[key] = value
    return base


def _remove_duplicate_targets(existing: dict, override: dict, key: str) -> None:
    """Remove entries from existing[key] that point to the same target as override[key].

    This prevents duplicate aliases for the same field/method/property, which would
    cause the generator to silently skip one of them.
    """
    if key not in override or key not in existing:
        return
    new_targets = set(override[key].values())
    # Remove existing entries whose target is being overridden.
    to_remove = [h for h, o in existing[key].items() if o in new_targets and h not in override[key]]
    for h in to_remove:
        del existing[key][h]


def main() -> int:
    data = json.loads(ALIASES_FILE.read_text(encoding="utf-8"))

    extra = {}
    if EXTRA_ALIASES_FILE.exists():
        extra = json.loads(EXTRA_ALIASES_FILE.read_text(encoding="utf-8"))

    all_overrides = deep_update(dict(KNOWN_OVERRIDES), extra)

    updated = 0
    added = 0
    for class_name, mapping in all_overrides.items():
        is_new = class_name not in data
        if is_new:
            data[class_name] = {}
            added += 1
        else:
            updated += 1
        # Remove existing aliases that point to the same targets as the curated
        # overrides, so the curated name wins and no duplicates remain.
        for key in ("Fields", "Methods", "Properties"):
            _remove_duplicate_targets(data[class_name], mapping, key)
        data[class_name] = deep_update(data[class_name], mapping)
        print(f"  {'+' if is_new else '='} {class_name}")

    ALIASES_FILE.write_text(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print(f"Done. Added {added}, updated {updated}. Total: {len(data)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
