#!/usr/bin/env python3
"""Add new class aliases to sdk_aliases.json."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ALIASES_FILE = ROOT / "Tools" / "sdk_aliases.json"

data = json.loads(ALIASES_FILE.read_text(encoding="utf-8"))

# New classes to add with human-readable names and key field mappings
new_classes = {
    "Movement": {
        "HumanClass": "Movement",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MouseLook": {
        "HumanClass": "MouseLook",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "Shooter": {
        "HumanClass": "Shooter",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "Spectator": {
        "HumanClass": "Spectator",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "Crosshair": {
        "HumanClass": "Crosshair",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "Radar": {
        "HumanClass": "Radar",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MasterClient": {
        "HumanClass": "MasterServer",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MasterClient.DHCBFAKOCAA": {
        "HumanClass": "MasterServerPacket",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "NEGGNDFJMAK": {
        "HumanClass": "DevClient",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "DropClient": {
        "HumanClass": "DropClient",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "HUD": {
        "HumanClass": "HUD",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "HUDMessage": {
        "HumanClass": "HUDMessage",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIDeathMessage": {
        "HumanClass": "DeathMessage",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "HUDBuild": {
        "HumanClass": "BuildHUD",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "HUDGameEnd": {
        "HumanClass": "GameEndHUD",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "HUDTab": {
        "HumanClass": "TabHUD",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "HUDNames": {
        "HumanClass": "NameTags",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "HUDKiller": {
        "HumanClass": "KillFeed",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "HUDIndicator": {
        "HumanClass": "HUDIndicator",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIMap": {
        "HumanClass": "MapMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIPlay": {
        "HumanClass": "PlayMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIM": {
        "HumanClass": "GUIManager",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIMMain": {
        "HumanClass": "MainMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIMPlay": {
        "HumanClass": "PlayMenuOld",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIAdmin": {
        "HumanClass": "AdminPanel",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIAdminMaplist": {
        "HumanClass": "AdminMapList",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIAdminUpload": {
        "HumanClass": "AdminUpload",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIAdminSettings": {
        "HumanClass": "AdminSettings",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIAdminPlayers": {
        "HumanClass": "AdminPlayers",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIChar": {
        "HumanClass": "CharacterMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUICharEditor": {
        "HumanClass": "CharacterEditor",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUISkinEditor": {
        "HumanClass": "SkinEditor",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIGameSet": {
        "HumanClass": "GameSettings",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIGameMenu": {
        "HumanClass": "GameMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIGameSquad": {
        "HumanClass": "SquadMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUICraft": {
        "HumanClass": "CraftMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUICase": {
        "HumanClass": "CaseMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIClan": {
        "HumanClass": "ClanMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIIcon": {
        "HumanClass": "IconMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIShop": {
        "HumanClass": "ShopMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIOptions": {
        "HumanClass": "OptionsMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIObj": {
        "HumanClass": "ObjectEditor",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIName": {
        "HumanClass": "NameInput",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIFX": {
        "HumanClass": "EffectsMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIProfile": {
        "HumanClass": "ProfileMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIRank": {
        "HumanClass": "RankMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIGold": {
        "HumanClass": "GoldMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUIBonus": {
        "HumanClass": "BonusMenu",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GUI3D": {
        "HumanClass": "GUI3D",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "VoxelPaletteGUI": {
        "HumanClass": "VoxelPaletteUI",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIChatMessage": {
        "HumanClass": "ChatMessage",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIHUD": {
        "HumanClass": "HUDManager",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIMPlay": {
        "HumanClass": "PlayUIManager",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIMMainmenu": {
        "HumanClass": "MainMenuUIManager",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIMInventory": {
        "HumanClass": "InventoryUIManager",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIMPlaymode": {
        "HumanClass": "PlaymodeUI",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIMShop": {
        "HumanClass": "ShopUIManager",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIMTasks": {
        "HumanClass": "TasksUIManager",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIMReward": {
        "HumanClass": "RewardUIManager",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIDrop": {
        "HumanClass": "DropUI",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIDropButton": {
        "HumanClass": "DropButton",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIDropButtonExit": {
        "HumanClass": "DropExitButton",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIElementBase": {
        "HumanClass": "UIElement",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIColors": {
        "HumanClass": "UIColors",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIPalette": {
        "HumanClass": "ColorPaletteUI",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIPaletteColorPreview": {
        "HumanClass": "ColorPreviewUI",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "Main": {
        "HumanClass": "Main",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MainManager": {
        "HumanClass": "MainManager",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MainConfig": {
        "HumanClass": "Config",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MainBack": {
        "HumanClass": "BackgroundManager",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UIManager": {
        "HumanClass": "UIManager",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "ParticleManager": {
        "HumanClass": "ParticleManager",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "SteamManager": {
        "HumanClass": "SteamManager",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MapLoader": {
        "HumanClass": "MapLoader",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MapGenerator": {
        "HumanClass": "MapGenerator",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MapAutoload": {
        "HumanClass": "MapAutoload",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MapEvent": {
        "HumanClass": "MapEvent",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MapPrefab": {
        "HumanClass": "MapPrefab",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MapCulling": {
        "HumanClass": "MapCulling",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "VoxelMap": {
        "HumanClass": "VoxelMap",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "VoxelBattleMap": {
        "HumanClass": "BattleMap",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "VoxelMapLight": {
        "HumanClass": "VoxelLighting",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "FXBloodSplat": {
        "HumanClass": "BloodSplat",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "FXTracer": {
        "HumanClass": "BulletTracer",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GeneralCameraShake": {
        "HumanClass": "CameraShake",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "OutlineSystem": {
        "HumanClass": "OutlineSystem",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MChar": {
        "HumanClass": "MultiplayerChar",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MCharAnimator": {
        "HumanClass": "MPCharAnimator",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "ControllTouch": {
        "HumanClass": "TouchControls",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "VInput": {
        "HumanClass": "InputSystem",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "InputHelper": {
        "HumanClass": "InputHelper",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "Following": {
        "HumanClass": "CameraFollow",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "FreeFlyCamera": {
        "HumanClass": "FreeFlyCamera",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "DemoRec": {
        "HumanClass": "DemoRecorder",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "Util": {
        "HumanClass": "Util",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "Util2": {
        "HumanClass": "Util2",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UtilHash": {
        "HumanClass": "HashUtil",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "UtilChar": {
        "HumanClass": "CharUtil",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "Log": {
        "HumanClass": "GameLog",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "DevDraw": {
        "HumanClass": "DebugDraw",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "dbgNet": {
        "HumanClass": "NetDebug",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "ConsoleBase": {
        "HumanClass": "DevConsole",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "Console": {
        "HumanClass": "Console",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "Lang": {
        "HumanClass": "Localization",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "LangWeapon": {
        "HumanClass": "WeaponLocalization",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    # Data structures
    "PJPKAJCOJLB": {
        "HumanClass": "WeaponDefinition",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "PJIMMBGGOBM": {
        "HumanClass": "WeaponVariant",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "ACEDGBLFHDK": {
        "HumanClass": "WeaponCollection",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "EEEBDHNOPDI": {
        "HumanClass": "InventoryEntry",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "OJGPKMCPJDB": {
        "HumanClass": "ShopItem",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "LFLEFDINMDA": {
        "HumanClass": "Achievement",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "HOONFDNBMIM": {
        "HumanClass": "GameModeConfig",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "HHMFAGJJOMH": {
        "HumanClass": "RoomConfig",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "MDADLLEFHKO": {
        "HumanClass": "IconData",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "AEKADIMKDIL": {
        "HumanClass": "ExtendedIconData",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "NMGFEEKOKDB": {
        "HumanClass": "ParticleEffect",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "IFALFNHBMFO": {
        "HumanClass": "MapMetadata",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "CFMGCCJAFCD": {
        "HumanClass": "MapPreview",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "LANMKMLNGOP": {
        "HumanClass": "ServerBrowserEntry",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "EICNFHFLMOF": {
        "HumanClass": "ChunkCoord",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "GOMBJHAKIFE": {
        "HumanClass": "BlockValidator",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "PBFLCAFNKMG": {
        "HumanClass": "CursorData",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "HitData": {
        "HumanClass": "HitDataMono",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "FileSender": {
        "HumanClass": "FileSender",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    # Enums
    "MouseLook.NLJBDGBDDLP": {
        "HumanClass": "MouseLookAxis",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "PBMAFIFKGEH": {
        "HumanClass": "TeamColor",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "FGICCBAAPGC": {
        "HumanClass": "GameMode",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "LIMCMHLKAPK": {
        "HumanClass": "MaterialType",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "PHMJFCEPJLH": {
        "HumanClass": "GraphicsTier",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "EDODLIKGBOC": {
        "HumanClass": "MotionBlurType",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "HECKHONLMLN": {
        "HumanClass": "ChunkVisibility",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "IHFCHDIAMHJ": {
        "HumanClass": "DataStructureType",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "DIKJFIAOHOI": {
        "HumanClass": "CoordinateSpace",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "AKNKNGOIGMJ": {
        "HumanClass": "PlatformMode",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "NDANMCKCENA": {
        "HumanClass": "AxisConstraint",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
    "JNPOJGEBDJJ": {
        "HumanClass": "TransformOp",
        "Fields": {},
        "Methods": {},
        "Notes": {}
    },
}

# Add new classes that don't already exist
added = 0
for class_name, mapping in new_classes.items():
    if class_name not in data:
        data[class_name] = mapping
        added += 1

ALIASES_FILE.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"Added {added} new class aliases. Total: {len(data)}")
