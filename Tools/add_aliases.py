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
