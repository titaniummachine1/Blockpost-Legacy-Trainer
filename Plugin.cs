using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BlockpostTrainer.Sdk;
using Raw = BlockpostTrainer.Sdk.Raw;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BlockpostTrainer;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("blockpost.exe")]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "local.blockpost.legacytrainer";
    public const string PluginName = "Blockpost Legacy Trainer";
    public const string PluginVersion = "0.7.1";

    private static readonly string ConfigPath = Path.Combine(Paths.BepInExRootPath, "plugins", "BlockpostTrainer.cfg");

    private const float AimbotStrength = 14f;
    private const float MinimumAimbotFov = 1f;
    private const float MaximumAimbotFov = 180f;
    private const float DiagnosticInterval = 5f;
    // Hard ceiling on IL2CPP property reads per diagnostic tick. Each read is a
    // runtime_invoke through the interop layer, not a cheap managed reflection call,
    // so this -- not the file I/O -- is what decides whether a frame survives.
    private const int DiagnosticReadBudget = 150;
    private const int DiagnosticPlayerWindow = 3;
    private static readonly string[] AimActivationLabels =
    {
        "Left mouse (fire)",
        "Right mouse",
        "Left Alt",
        "Mouse 4",
        "Always on"
    };
    private static readonly string[] AimStyleLabels =
    {
        "Plain aim lock",
        "Silent aim"
    };
    private static Plugin? instance;
    private static bool menuVisible;
    private static Rect menuRect = new(20, 20, 560, 700);
    private static bool menuDragging;
    private static Vector2 menuDragOffset;
    private static int menuTab;
    private static readonly string[] MenuTabLabels = { "Combat", "ESP/Visual", "Movement", "Weapons", "Misc", "Config" };
    private static Vector2 menuScroll = Vector2.zero;
    private static bool bootstrapLogged;
    private static bool bindingsLogged;
    private static bool featureFailureLogged;
    private static bool controllerRunning;
    private static bool showRuntimeStatus = true;
    private static bool debugLogging;
    private static float nextDiagnosticTime;
    private static float nextPlayerWeaponDumpTime;
    private static int aimActivationMode = 1;
    private static int aimStyle;
    private static float aimbotFov = 30f;
    private static string featureStatus = "inactive";
    private static string aimStatus = "not active";
    private static int lastAimTargetIndex = -1;
    private static Vector3 lastAimTargetPosition;
    private static Camera? activeCamera;
    private static float nextCameraSearchTime;
    private static float nextPlayerFieldDumpTime;
    private static int diagnosticReadsLeft;
    private static int diagnosticPlayerCursor;
    private static bool heavyDiagnostics;
    private static PropertyInfo[]? playerPrimitiveProps;
    private static int recoilCalls;
    private static int recoilSuppressed;
    private static bool guiFailureLogged;
    private static bool inventoryDumped;
    private static bool pendingLeftMouseUp;
    private static bool forceShotThisFrame;
    private static bool rapidFireFailureLogged;
    // Set by silent aim prefix when auto-shoot should fire. The postfix sends mouse_event
    // LEFTDOWN which arrives next frame, making Input.GetMouseButton(0) return true.
    private static bool autoShootPending;
    // When true, Input.GetMouseButton(0) returns true regardless of actual mouse state.
    // Set by silent aim / auto-shoot prefix so Controll.Update fires through its own logic.
    private static bool autoShootThisFrame;
    private static bool espEnabled = true;
    private static bool showHealth = true;
    private static bool showTeammates;
    private static bool aimbotEnabled = true;
    private static bool noRecoil;
    private static bool autoShoot = true;
    private static bool serverTrustTest;
    private static bool rapidFire;
    private static bool ghostBullets;
    private static bool infiniteHealth;
    private static bool infiniteAmmo;
    private static bool instantReload;
    private static bool instantReloadFailureLogged;
    private static bool bunnyHop;
    private static bool customCrosshair;
    private static bool fovChanger;
    private static float targetFov = 90f;
    private static bool speedHack;
    private static float speedMultiplier = 2f;
    private static bool flyHack;
    private static bool noClip;
    private static bool gokuTp;
    private static Vector3 gokuReturnPos;
    private static bool gokuHasReturnPos;
    private static float gokuTpCooldown;
    private static bool weaponUnlock;
    private static bool weaponUnlockApplied;
    private static int weaponUnlockCount;
    private static bool weaponUnlockFailureLogged;
    private static bool thirdPerson;
    private static float thirdPersonDistance = 4f;
    private static float thirdPersonHeight = 2f;
    private static bool chams;
    private static bool triggerbot;
    private static float triggerbotRange = 200f;
    private static bool fullbright;
    private static bool antiFlash;
    private static float flashBlockUntil;
    private static bool wallhack;
    private static bool noSpread;
    private static bool fastFire;
    private static bool autoReload;
    private static bool nameEsp;
    private static bool spinbot;
    private static bool skeletonEsp;
    private static bool radarHack;
    private static bool antiAimPitch;
    private static bool autoStrafe;
    private static bool killFeed;
    private static bool edgeJump;
    private static bool fakeLag;
    private static bool spectatorWarning;
    private static bool damageIndicator;
    private static bool hitMarker;
    private static bool autoPickup;
    private static bool xpGoldHack;
    private static bool healthBarEsp;
    private static bool snaplines;
    private static bool threatIndicator;
    private static bool nameChanger;
    private static string customName = "Player";
    private static bool scoreboardHack;
    private static bool autoBhop;
    private static bool pingSpoof;
    private static int fakePing = 150;
    private static bool footstepEsp;
    private static bool preFire;
    private static readonly List<Vector3> recentFootsteps = new();
    private static float lastFootstepScan;
    private static bool backtrack;
    private static readonly Dictionary<int, Queue<(float time, Vector3 pos)>> enemyPositionHistory = new();
    private const float BacktrackDuration = 2f;
    private static bool adminUnlock;
    private static bool boxHeadEsp;
    private static bool slideHack;
    private static bool grenadeTrajectory;
    private static bool noFallDamage;
    private static bool crosshairCustom;
    private static float crosshairR = 0f, crosshairG = 1f, crosshairB = 0f;
    private static int crosshairSize = 10;
    private static int crosshairThickness = 2;
    private static bool killSound;
    private static bool aimbotSmoothing;
    private static float aimbotSmoothFactor = 0.5f;
    private static int lastKillCountForSound = -1;
    private static bool fastWeaponSwitch;
    private static bool configPreset1;
    private static bool configPreset2;
    private static bool configPreset3;
    private static bool antiAimJitter;
    private static float sessionStartTime = -1f;
    private static bool debugOverlay;
    private static bool autoAccept;
    private static bool distanceEsp;
    private static bool weaponIdEsp;
    private static bool chatSpammer;
    private static float lastChatSpam;
    private static string spamMessage = "GG";
    private static bool autoVoteYes;
    private static bool autoRevive;
    private static bool noSkybox;
    private static bool wireframePlayers;
    private static bool zoomHack;
    private static float zoomFov = 30f;
    private static bool noMuzzleFlash;
    private static bool nightVision;
    private static bool noSmoke;
    private static bool autoSprint;
    private static bool noRain;
    private static bool thirdPersonShoulder;
    private static float thirdPersonShoulderX = 1.5f;
    private static bool aimbotPrediction;
    private static readonly Dictionary<int, Vector3> lastEnemyVelocities = new();
    private static bool autoCrouchIdle;
    private static int lastKillStreakCheck = -1;
    private static int currentKillStreak;
    private static float lastKillTime;
    private static bool panicMode;
    private static bool fovFilterEsp;
    private static bool hitLog;
    private static int lastHitLogCount = -1;
    private static bool autoWeaponSwap;
    private static bool screenCleaner;
    private static bool noShadows;
    private static bool playerList;
    private static bool noGrass;
    private static bool crosshairHitIndicator;
    private static bool timeScaleHack;
    private static float customTimeScale = 1f;
    private static bool noFog;
    private static bool aimEnemiesOnly = true;
    private static bool boneScan;
    private static bool radarMiniMap;
    private static int autoReloadThreshold = 5;
    private static int aimBone = 0; // 0=head, 1=chest, 2=pelvis
    private static readonly string[] AimBoneLabels = { "Head", "Chest", "Pelvis" };
    private static float fpsUpdateTimer;
    private static int fpsFrameCount;
    private static float currentFps;
    private static readonly List<string> killFeedEntries = new();
    private static float lastKillFeedCheck;
    private static int lastKillCount = -1;
    private static int lastHealthForDamage = -1;
    private static float damageIndicatorTime;
    private static int lastDamageAmount;
    private static float hitMarkerTime;
    private static int lastHitCount = -1;
    private static int instantReloads;
    private static float nextAutoShootTime;

    // Silent aim state: in the prefix we save the player's real aim angles AND camera
    // rotation, redirect the angles to the target. Controll.Update runs (fires the shot
    // at the target, applies mouse delta to angles, updates camera rotation). In the
    // postfix we restore the saved angles + mouse delta AND restore the camera rotation
    // to match — so the player never sees the snap and mouse movement is preserved.
    private static bool silentAimRedirected;
    private static float savedYaw;
    private static float savedPitch;
    private static float targetYaw;
    private static float targetPitch;
    private static Quaternion savedCameraRot;
    private static float nextGhostBulletTime;
    private static readonly List<EspBox> espBoxes = new();

    private readonly struct EspBox
    {
        public EspBox(Rect bounds, Color color, int health, Vector2 screenPos, float dist, bool isEnemy, string name)
        {
            Bounds = bounds;
            Color = color;
            Health = health;
            ScreenPos = screenPos;
            Distance = dist;
            IsEnemy = isEnemy;
            Name = name;
        }

        public Rect Bounds { get; }
        public Color Color { get; }
        public int Health { get; }
        public Vector2 ScreenPos { get; }
        public float Distance { get; }
        public bool IsEnemy { get; }
        public string Name { get; }
    }

    public override void Load()
    {
        instance = this;
        Log.LogInfo($"{PluginName} {PluginVersion} loaded through BepInEx IL2CPP.");

        var controllerType = AccessTools.TypeByName("Controll");
        if (controllerType == null)
        {
            Log.LogWarning("Could not resolve Controll. Generated interop assemblies may be unavailable.");
            return;
        }

        var updateMethod = AccessTools.Method(controllerType, "Update");
        var onGuiMethod = AccessTools.Method(controllerType, "OnGUI");
        var recoilMethod = AccessTools.Method(controllerType, nameof(Raw.Controll.Methods.ILIDJBFOFJA));
        if (updateMethod == null || onGuiMethod == null)
        {
            Log.LogWarning("Could not resolve the Controll.Update or Controll.OnGUI hook.");
            return;
        }

        var harmony = new Harmony(PluginGuid);
        harmony.Patch(updateMethod, prefix: new HarmonyMethod(typeof(Plugin), nameof(ControllerUpdatePrefix)), postfix: new HarmonyMethod(typeof(Plugin), nameof(ControllerUpdatePostfix)));
        harmony.Patch(onGuiMethod, postfix: new HarmonyMethod(typeof(Plugin), nameof(ControllerOnGUIPostfix)));

        var guiInvType = AccessTools.TypeByName("GUIInv");
        if (guiInvType != null)
        {
            var guiOnGuiMethod = AccessTools.Method(guiInvType, "OnGUI");
            if (guiOnGuiMethod != null)
            {
                harmony.Patch(guiOnGuiMethod, postfix: new HarmonyMethod(typeof(Plugin), nameof(GUIInvOnGUIPostfix)));
                Log.LogInfo("Patched GUIInv.OnGUI for inventory menu support.");
            }
        }

        if (recoilMethod != null)
        {
            harmony.Patch(recoilMethod, prefix: new HarmonyMethod(typeof(Plugin), nameof(SetRecoilPrefix)));
        }
        else
        {
            Log.LogWarning("Could not resolve the current SetRecoil signature; no-recoil will remain unavailable.");
        }

        // Patch Input.GetMouseButton AND Input.GetMouseButtonDown so auto-shoot can trick
        // Controll.Update into firing through its own logic. The game reads input at the
        // start of Update — we don't know if it uses GetMouseButton (held) or
        // GetMouseButtonDown (edge-triggered), so patch both.
        var getMouseButton = AccessTools.Method(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetMouseButton));
        var getMouseButtonDown = AccessTools.Method(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetMouseButtonDown));
        var getMouseButtonUp = AccessTools.Method(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetMouseButtonUp));
        if (getMouseButton != null)
        {
            harmony.Patch(getMouseButton, prefix: new HarmonyMethod(typeof(Plugin), nameof(GetMouseButtonPrefix)));
            Log.LogInfo($"Patched Input.GetMouseButton for auto-shoot: {getMouseButton.GetParameters().Length} params");
        }
        if (getMouseButtonDown != null)
        {
            harmony.Patch(getMouseButtonDown, prefix: new HarmonyMethod(typeof(Plugin), nameof(GetMouseButtonPrefix)));
            Log.LogInfo($"Patched Input.GetMouseButtonDown for auto-shoot: {getMouseButtonDown.GetParameters().Length} params");
        }
        if (getMouseButtonUp != null)
        {
            harmony.Patch(getMouseButtonUp, prefix: new HarmonyMethod(typeof(Plugin), nameof(GetMouseButtonUpPrefix)));
            Log.LogInfo($"Patched Input.GetMouseButtonUp for auto-shoot: {getMouseButtonUp.GetParameters().Length} params");
        }
        if (getMouseButton == null)
        {
            Log.LogWarning("Could not patch Input.GetMouseButton; auto-shoot will not work.");
        }

        Log.LogInfo($"Patched Controll.Update, Controll.OnGUI, and recoil hook: {recoilMethod != null}. Auto-shoot uses direct PLH.CDEGJOBLOFO when rapid fire is active.");

        NetProbe.Install(harmony, Log);
        AsyncLog.Start(Log);
        FieldProbe.Initialize(Log);
        LoadConfig();
    }

    public override bool Unload()
    {
        SaveConfig();
        // Flush and stop the writer threads explicitly. Leaving them to be killed at process exit
        // is what left the game hanging on close.
        NetProbe.Shutdown();
        AsyncLog.Shutdown();
        return base.Unload();
    }

    private static void ControllerUpdatePrefix()
    {
        controllerRunning = true;
        forceShotThisFrame = false;
        autoShootThisFrame = false;
        autoShootPending = false;
        LogControllerStartup();
        ApplyInstantReload();
        UpdateAimbotSafely();
        ApplyCheatFeatures();
        UpdateKillFeed();
        UpdateDamageIndicator();
        UpdateHitMarker();
        UpdateAutoPickup();
        if (xpGoldHack) ApplyXpGoldHack();
        if (nameChanger) ApplyNameChanger();
        if (scoreboardHack) ApplyScoreboardHack();
        if (autoBhop) ApplyAutoBhop();
        UpdateFootstepEsp();
        if (preFire) ApplyPreFire();
        UpdateBacktrack();
        if (adminUnlock) ApplyAdminUnlock();
        if (slideHack) ApplySlideHack();
        if (noFallDamage) ApplyNoFallDamage();
        UpdateKillSound();
        if (fastWeaponSwitch) ApplyFastWeaponSwitch();
        if (antiAimJitter) ApplyAntiAimJitter();
        if (autoAccept) ApplyAutoAccept();
        if (chatSpammer) ApplyChatSpammer();
        if (autoVoteYes) ApplyAutoVoteYes();
        if (autoRevive) ApplyAutoRevive();
        ApplyNoSkybox();
        ApplyWireframePlayers();
        if (zoomHack) ApplyZoomHack();
        if (noMuzzleFlash) ApplyNoMuzzleFlash();
        if (nightVision) ApplyNightVision();
        if (noSmoke) ApplyNoSmoke();
        if (autoSprint) ApplyAutoSprint();
        if (noRain) ApplyNoRain();
        if (thirdPersonShoulder) ApplyThirdPersonShoulder();
        if (autoCrouchIdle) ApplyAutoCrouchIdle();
        UpdateKillStreak();
        UpdateHitLog();
        if (autoWeaponSwap) ApplyAutoWeaponSwap();
        if (screenCleaner) ApplyScreenCleaner();
        if (noShadows) ApplyNoShadows();
        if (noGrass) ApplyNoGrass();
        if (timeScaleHack) Time.timeScale = customTimeScale;
        else if (!timeScaleHack && Time.timeScale != 1f) Time.timeScale = 1f;
        if (noFog) RenderSettings.fog = false;
        PrepareRapidFirePrefix();

        // If we're not shooting this frame but the button is still held from
        // last frame's postfix, release it NOW before Controll.Update runs.
        // Otherwise the game sees left mouse stuck down and breaks.
        if (!autoShootPending && pendingLeftMouseUp)
        {
            mouse_event(MouseEventFLeftUp, 0, 0, 0, 0);
            pendingLeftMouseUp = false;
        }
    }

    private static void ControllerUpdatePostfix(Controll __instance)
    {
        // Auto-shoot: send a fresh click (LEFTUP then LEFTDOWN) every frame while
        // aiming at a target. The game uses GetMouseButtonDown (edge-triggered),
        // not GetMouseButton (held), so holding the button doesn't fire repeatedly.
        // We need a new press event each frame.
        if (autoShootPending)
        {
            var main = Controll.HGAODFPBGLB;
            var alive = main != null && main.FDOJDJLIGLF > 0 && main.JPGGPPLOOML != null;
            if (alive)
            {
                mouse_event(MouseEventFLeftUp, 0, 0, 0, 0);
                mouse_event(MouseEventFLeftDown, 0, 0, 0, 0);
                pendingLeftMouseUp = true;
            }
        }
        else if (pendingLeftMouseUp)
        {
            mouse_event(MouseEventFLeftUp, 0, 0, 0, 0);
            pendingLeftMouseUp = false;
        }

        ForceRapidFireShot();
        RestoreSilentAim();

        NetProbe.Tick();
        FieldWatch.Tick(__instance);
        GlobalScan.Tick();
        ToggleMenuIfRequested();
        ApplyCheatFeatures();
        if (chams)
        {
            ApplyChams();
        }

        if (triggerbot && !menuVisible)
        {
            ApplyTriggerbot();
        }

        LogRuntimeDiagnostics();
        LogInventoryDump();
        DumpAllPlayerWeapons();
        FieldProbe.Tick();
    }

    private static void ReleaseLeftMouseIfNeeded()
    {
        if (pendingLeftMouseUp)
        {
            mouse_event(MouseEventFLeftUp, 0, 0, 0, 0);
            pendingLeftMouseUp = false;
        }
    }

    /// <summary>
    /// Harmony prefix for Input.GetMouseButton(int) and Input.GetMouseButtonDown(int).
    /// When autoShootThisFrame is true, forces button 0 to return true so
    /// Controll.Update fires through its own logic (raycast + network hit packet)
    /// while aim angles are redirected to the target.
    /// </summary>
    private static bool GetMouseButtonPrefix(int button, ref bool __result)
    {
        if (autoShootThisFrame && button == 0)
        {
            __result = true;
            return false; // skip original method
        }
        return true; // run original method
    }

    /// <summary>
    /// Harmony prefix for Input.GetMouseButtonUp(int). When autoShootThisFrame is true,
    /// forces button 0 up to return false so the game doesn't think the player released.
    /// </summary>
    private static bool GetMouseButtonUpPrefix(int button, ref bool __result)
    {
        if (autoShootThisFrame && button == 0)
        {
            __result = false;
            return false; // skip original method
        }
        return true; // run original method
    }

    /// <summary>
    /// Finish any in-progress reload immediately.
    ///
    /// Reload is pure client-side simulation -- there is no reload or ammo opcode in the outgoing
    /// protocol, so the server is never told and there is nothing to stay in sync with. The timer
    /// is a pair of adjacent Controll statics: FBINCNDDPAO is Time.time at reload start and
    /// ILGHFLMKMCO is the completion stamp, normally start + 2.0s. A successful perfect-reload
    /// minigame works by *subtracting* from ILGHFLMKMCO, so pulling it back to the start time is
    /// the same mechanism taken to its limit rather than a new code path.
    /// </summary>
    /// <summary>
    /// Bunny hop: when the player holds space, send space every frame.
    /// The game's own jump logic checks if the player is grounded — it won't
    /// jump in mid-air. We don't need to detect grounding ourselves.
    /// </summary>
    private static void ApplyBunnyHop(KBBBHJDINCB main)
    {
        try
        {
            if (!Input.GetKey(KeyCode.Space))
            {
                return;
            }

            keybd_event(VkSpace, 0, KeyEventFKeyDown, 0);
            keybd_event(VkSpace, 0, KeyEventFKeyUp, 0);
        }
        catch { }
    }

    /// <summary>
    /// Fly hack: disable gravity, move up/down with Space/Shift.
    /// No clip: disable all colliders on the player.
    /// </summary>
    private static void ApplyFlyNoClip(KBBBHJDINCB main)
    {
        try
        {
            var rb = main.MJPOJOOIPPN;
            if (rb == null)
            {
                return;
            }

            if (flyHack)
            {
                rb.useGravity = false;
                var vel = rb.velocity;
                vel.y = 0f;
                if (Input.GetKey(KeyCode.Space))
                {
                    vel.y = 5f;
                }
                else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.C))
                {
                    vel.y = -5f;
                }
                rb.velocity = vel;
            }
            else
            {
                rb.useGravity = true;
            }

            if (noClip)
            {
                // Disable all colliders on the player's root GameObject.
                var root = main.LANBONKMIME;
                if (root != null)
                {
                    var colliders = root.GetComponentsInChildren<Collider>(true);
                    if (colliders != null)
                    {
                        foreach (var c in colliders)
                        {
                            if (c != null && c.enabled)
                            {
                                c.enabled = false;
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Goku TP: teleport behind the closest valid enemy for a kill.
    /// When no valid enemy exists, teleport back to the stored return position.
    /// Cooldown prevents rapid oscillation.
    /// </summary>
    private static void ApplyGokuTp(KBBBHJDINCB main)
    {
        try
        {
            if (gokuTpCooldown > 0f)
            {
                gokuTpCooldown -= Time.unscaledDeltaTime;
                return;
            }

            var players = PLH.BAKLNPIEHMI;
            if (players == null) return;

            var myPos = main.OOMJGHCFODI;
            var myTeam = main.MMMGPDBMOLM;

            // Find closest valid enemy (alive, not spawn-protected, different team)
            KBBBHJDINCB? bestTarget = null;
            var bestDist = float.MaxValue;
            var bestPos = Vector3.zero;

            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null || player == main) continue;
                if (player.FDOJDJLIGLF <= 0) continue;           // dead
                if (player.LBKINNIDKEC) continue;                 // spawn protected
                if (player.MMMGPDBMOLM == myTeam) continue;       // same team
                if (player._LCEIAGLFFJN_k__BackingField) continue; // invalid flag

                var enemyPos = player.OOMJGHCFODI;
                var dist = Vector3.Distance(myPos, enemyPos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestTarget = player;
                    bestPos = enemyPos;
                }
            }

            if (bestTarget != null)
            {
                // Save return position if we don't have one yet
                if (!gokuHasReturnPos)
                {
                    gokuReturnPos = myPos;
                    gokuHasReturnPos = true;
                }

                // Teleport behind the enemy (2m behind, same height)
                var enemyForward = Vector3.forward;
                try
                {
                    // Use enemy's rigidbody velocity direction as "forward" fallback
                    var enemyRb = bestTarget.MJPOJOOIPPN;
                    if (enemyRb != null && enemyRb.velocity.sqrMagnitude > 0.1f)
                    {
                        enemyForward = enemyRb.velocity.normalized;
                        enemyForward.y = 0f;
                        if (enemyForward.sqrMagnitude < 0.01f) enemyForward = Vector3.forward;
                    }
                }
                catch { }

                // Position 2m behind the enemy
                var tpPos = bestPos - enemyForward * 2f;
                tpPos.y = bestPos.y; // same height

                // Teleport via rigidbody position
                var rb = main.MJPOJOOIPPN;
                if (rb != null)
                {
                    rb.position = tpPos;
                    rb.velocity = Vector3.zero;
                }

                // Also set the player position field directly
                main.OOMJGHCFODI = tpPos;

                // Set yaw to face the enemy
                var dir = bestPos - tpPos;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                {
                    var yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                    Controll.NAKNALFCOIF = yaw;
                }

                gokuTpCooldown = 0.15f; // 150ms cooldown between teleports
            }
            else
            {
                // No valid enemy — teleport back to return position
                if (gokuHasReturnPos)
                {
                    var rb = main.MJPOJOOIPPN;
                    if (rb != null)
                    {
                        rb.position = gokuReturnPos;
                        rb.velocity = Vector3.zero;
                    }
                    main.OOMJGHCFODI = gokuReturnPos;
                    gokuHasReturnPos = false;
                    gokuTpCooldown = 0.3f;
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Moves the camera behind and above the player for a third-person view.
    /// Uses the player's position and yaw angle (Controll.NAKNALFCOIF) to compute
    /// the camera position, then makes the camera look at the player.
    /// </summary>
    private static void ApplyThirdPerson(KBBBHJDINCB main)
    {
        try
        {
            var camera = Controll.CDFACGAFFFH;
            if (camera == null)
            {
                return;
            }

            var playerPos = main.OOMJGHCFODI;
            var yaw = Controll.NAKNALFCOIF * Mathf.Deg2Rad;

            // Position camera behind player based on yaw, plus height offset.
            var offset = new Vector3(
                -Mathf.Sin(yaw) * thirdPersonDistance,
                thirdPersonHeight,
                -Mathf.Cos(yaw) * thirdPersonDistance);

            camera.transform.position = playerPos + offset;

            // Look at a point slightly above the player (head level).
            var lookTarget = playerPos + Vector3.up * 1.5f;
            camera.transform.LookAt(lookTarget);
        }
        catch { }
    }

    private static bool fullbrightApplied;
    private static float originalAmbientIntensity = -1f;
    private static float originalFogDensity = -1f;
    private static bool originalFogEnabled;

    /// <summary>
    /// Fullbright / night vision: disable fog, max out ambient light intensity,
    /// and boost RenderSettings so all maps are fully visible.
    /// </summary>
    private static void ApplyFullbright()
    {
        try
        {
            // Save original values once
            if (!fullbrightApplied)
            {
                originalFogEnabled = RenderSettings.fog;
                originalFogDensity = RenderSettings.fogDensity;
                fullbrightApplied = true;
            }

            // Disable fog
            RenderSettings.fog = false;

            // Boost ambient light
            RenderSettings.ambientLight = Color.white;
            RenderSettings.ambientIntensity = 2f;

            // Boost all lights in the scene
            var lights = UnityEngine.Object.FindObjectsOfType<Light>();
            if (lights != null)
            {
                foreach (var light in lights)
                {
                    if (light != null && light.intensity < 2f)
                    {
                        light.intensity = 2f;
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Restore original lighting settings when fullbright is turned off.
    /// </summary>
    private static void RestoreFullbright()
    {
        if (!fullbrightApplied) return;
        try
        {
            RenderSettings.fog = originalFogEnabled;
            if (originalFogDensity >= 0f)
                RenderSettings.fogDensity = originalFogDensity;
            RenderSettings.ambientIntensity = 1f;
            fullbrightApplied = false;
        }
        catch { }
    }

    /// <summary>
    /// Anti-flashbang: detect when a flash effect is active and immediately
    /// cancel it by zeroing the flash overlay. Uses reflection to find and
    /// disable full-screen UI overlays with high alpha.
    /// </summary>
    private static void ApplyAntiFlash(KBBBHJDINCB main)
    {
        try
        {
            // Find all GameObjects with "flash" or "Flash" in their name
            // and disable them. The flash effect is typically a full-screen
            // overlay that fades out.
            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            if (allObjects == null) return;

            foreach (var go in allObjects)
            {
                if (go == null || !go.activeInHierarchy) continue;
                var name = go.name;
                if (name == null) continue;
                if (name.IndexOf("flash", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Flash", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("blind", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    go.SetActive(false);
                }
            }

            // Also check for a flash timer field on the player or Controll
            // by looking for fields that changed recently
            // (the FieldProbe can help identify these)
        }
        catch { }
    }

    private static Material? _chamsMaterial;
    private static readonly Color ChamsEnemyColor = new(1f, 0f, 0f, 0.5f);
    private static readonly Color ChamsTeamColor = new(0f, 0.5f, 1f, 0.5f);

    /// <summary>
    /// Overrides all Renderer materials on enemy player models with a transparent
    /// colored material so they are visible through walls. Runs each frame on all
    /// players in the match.
    /// </summary>
    private static void ApplyChams()
    {
        try
        {
            if (_chamsMaterial == null)
            {
                // Create a simple transparent material for chams.
                _chamsMaterial = new Material(Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default"));
                _chamsMaterial.SetFloat("_Mode", 3); // Transparent mode
                _chamsMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _chamsMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _chamsMaterial.SetInt("_ZWrite", 0);
                _chamsMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off); // Render both sides
            }

            var players = PLH.BAKLNPIEHMI;
            if (players == null)
            {
                return;
            }

            var mainPlayer = Controll.HGAODFPBGLB;
            var mainTeam = mainPlayer?.MMMGPDBMOLM ?? -1;

            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null || player.FDOJDJLIGLF <= 0)
                {
                    continue;
                }

                if (player == mainPlayer)
                {
                    continue;
                }

                var root = player.LANBONKMIME;
                if (root == null)
                {
                    continue;
                }

                var isEnemy = mainTeam < 0 || player.MMMGPDBMOLM != mainTeam;
                _chamsMaterial.color = isEnemy ? ChamsEnemyColor : ChamsTeamColor;

                var renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers == null)
                {
                    continue;
                }

                foreach (var r in renderers)
                {
                    if (r == null)
                    {
                        continue;
                    }

                    // Only override if not already overridden (check material color).
                    if (r.material == null || r.material.color != _chamsMaterial.color)
                    {
                        r.material = _chamsMaterial;
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Auto-fires when the crosshair is over an enemy player. Uses a raycast from
    /// the camera center; if it hits an enemy player's collider, triggers fire input.
    /// </summary>
    private static void ApplyTriggerbot()
    {
        try
        {
            var camera = Controll.CDFACGAFFFH;
            if (camera == null)
            {
                return;
            }

            var mainPlayer = Controll.HGAODFPBGLB;
            if (mainPlayer == null || mainPlayer.FDOJDJLIGLF <= 0)
            {
                return;
            }

            // Raycast from camera center.
            var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out var hit, triggerbotRange))
            {
                return;
            }

            // Check if the hit object belongs to an enemy player.
            var hitTransform = hit.transform;
            if (hitTransform == null)
            {
                return;
            }

            // Walk up the hierarchy to find a player root.
            var players = PLH.BAKLNPIEHMI;
            if (players == null)
            {
                return;
            }

            var mainTeam = mainPlayer.MMMGPDBMOLM;
            KBBBHJDINCB? targetPlayer = null;
            for (var i = 0; i < players.Length; i++)
            {
                var p = players[i];
                if (p == null || p == mainPlayer || p.FDOJDJLIGLF <= 0)
                {
                    continue;
                }

                var root = p.LANBONKMIME;
                if (root == null)
                {
                    continue;
                }

                // Check if the hit transform is a child of this player's root.
                var t = hitTransform;
                while (t != null)
                {
                    if (t.gameObject == root)
                    {
                        // Check team: only fire at enemies.
                        if (mainTeam < 0 || p.MMMGPDBMOLM != mainTeam)
                        {
                            targetPlayer = p;
                        }

                        break;
                    }

                    t = t.parent;
                }

                if (targetPlayer != null)
                {
                    break;
                }
            }

            if (targetPlayer != null)
            {
                // Trigger fire by setting the fire input flag.
                Controll.EPEEFBDJAHO = 1f;
                forceShotThisFrame = true;
            }
        }
        catch { }
    }

    private static void ApplyInstantReload()
    {
        if (!instantReload)
        {
            return;
        }

        try
        {
            if (!Controll.DJACNOGOCKD)
            {
                return;
            }

            if (Controll.ILGHFLMKMCO > Controll.FBINCNDDPAO)
            {
                Controll.ILGHFLMKMCO = Controll.FBINCNDDPAO;
                instantReloads++;
            }
        }
        catch (Exception exception)
        {
            if (!instantReloadFailureLogged)
            {
                instance?.Log.LogWarning($"[Instant reload] apply failed: {exception}");
                instantReloadFailureLogged = true;
            }
        }
    }

    private static void ApplyCheatFeatures()
    {
        if (!infiniteHealth && !infiniteAmmo && !bunnyHop && !fovChanger && !speedHack && !flyHack && !noClip && !thirdPerson && !fullbright && !antiFlash && !noSpread && !fastFire && !autoReload && !spinbot && !antiAimPitch && !autoStrafe && !edgeJump && !fakeLag && !gokuTp)
        {
            return;
        }

        try
        {
            var main = Controll.HGAODFPBGLB;
            if (main == null)
            {
                return;
            }

            if (infiniteHealth)
            {
                main.FDOJDJLIGLF = 1000;
                main.EFHBKMHCMOH = 1000;
                main.INGHEHAALBJ = 1000;
                main.CLOEJLAOIGI = false;
                main.CGHKKDBILGF = false;
            }

            if (infiniteAmmo)
            {
                // Controll.FGGKANNFBDH (0xC0) = ammo in magazine
                // Controll.ILFOFIOFBAM (0xC8) = max ammo (-1 = no weapon equipped)
                // Controll.KJOMABGHAIJ (0xCC) = reserve ammo
                // Only refill if a weapon is actually equipped (maxAmmo != -1).
                var maxAmmo = Controll.ILFOFIOFBAM;
                if (maxAmmo > 0)
                {
                    Controll.FGGKANNFBDH = maxAmmo;
                    Controll.KJOMABGHAIJ = 999;
                }

                // Also refill the player's per-slot ammo array (GDEMINMDJAC at 0xA8).
                var slotAmmo = main.GDEMINMDJAC;
                if (slotAmmo != null && slotAmmo.Length > 0)
                {
                    for (var i = 0; i < slotAmmo.Length; i++)
                    {
                        if (slotAmmo[i] < 999)
                        {
                            slotAmmo[i] = 999;
                        }
                    }
                }
            }

            if (bunnyHop && Application.isFocused && main.FDOJDJLIGLF > 0)
            {
                ApplyBunnyHop(main);
            }

            if (fovChanger)
            {
                var camera = Controll.CDFACGAFFFH;
                if (camera != null && Mathf.Abs(camera.fieldOfView - targetFov) > 0.1f)
                {
                    camera.fieldOfView = targetFov;
                }
            }

            if (speedHack)
            {
                Time.timeScale = speedMultiplier;
                // Also boost Movement static speed constants for more natural movement.
                // GBHJLHFPCHK = move speed, BOKNCBLLHED = sprint speed.
                try
                {
                    Movement.GBHJLHFPCHK = 8f * speedMultiplier;
                    Movement.BOKNCBLLHED = 12f * speedMultiplier;
                }
                catch { /* static field access may vary by interop version */ }
            }
            else if (Time.timeScale != 1f)
            {
                Time.timeScale = 1f;
                try
                {
                    Movement.GBHJLHFPCHK = 6f;
                    Movement.BOKNCBLLHED = 9f;
                }
                catch { }
            }

            if (flyHack || noClip)
            {
                ApplyFlyNoClip(main);
            }

            if (gokuTp)
            {
                ApplyGokuTp(main);
            }

            if (thirdPerson)
            {
                ApplyThirdPerson(main);
            }

            if (fullbright)
            {
                ApplyFullbright();
            }
            else if (fullbrightApplied)
            {
                RestoreFullbright();
            }

            if (antiFlash)
            {
                ApplyAntiFlash(main);
            }

            if (noSpread)
            {
                // FGFKPMPLNKO = spread/recoil accumulator on Player
                main.FGFKPMPLNKO = 0f;
                // Also zero the Controll-side fire timer/spread
                Controll.LCMOBPPHLLM = 0f;
            }

            if (fastFire)
            {
                // Zero the fire timer so the "has enough time passed" check
                // always passes, allowing firing every frame.
                Controll.LCMOBPPHLLM = 0f;
                main.LCMOBPPHLLM = 0f;
            }

            if (autoReload)
            {
                // Auto-reload when magazine is at/below threshold and we have reserve ammo
                var currentAmmo = Controll.FGGKANNFBDH;
                var maxAmmo = Controll.ILFOFIOFBAM;
                var reserve = Controll.KJOMABGHAIJ;
                if (currentAmmo <= autoReloadThreshold && maxAmmo > 0 && reserve > 0 && !Controll.EKEAAHAKHIN)
                {
                    // Trigger reload by setting the reload flag
                    Controll.EKEAAHAKHIN = true;
                    Controll.DJACNOGOCKD = true;
                    // Set reload timer to start
                    Controll.FBINCNDDPAO = Time.time;
                }
            }

            if (spinbot)
            {
                // Constantly spin yaw to make hitboxes harder to hit
                Controll.NAKNALFCOIF = (Controll.NAKNALFCOIF + 30f) % 360f;
            }

            if (antiAimPitch)
            {
                // Set pitch to extreme up/down to make head hitbox harder to hit
                // Alternate between looking straight up and down each frame
                var pitchTarget = ((int)Time.time % 2 == 0) ? -89f : 89f;
                Controll.IGLCENGMMMJ = pitchTarget;
            }

            if (autoStrafe)
            {
                // Auto-strafe: alternate left/right movement to dodge incoming fire
                // Toggle the movement input flags every 0.3 seconds
                var strafePhase = (int)(Time.time / 0.3f) % 2;
                if (strafePhase == 0)
                {
                    // Move right (plus_x=1, clear minus_x=2)
                    Controll.MNHBPCOOMLE = (Controll.MNHBPCOOMLE & ~0x2u) | 0x1u;
                }
                else
                {
                    // Move left (minus_x=2, clear plus_x=1)
                    Controll.MNHBPCOOMLE = (Controll.MNHBPCOOMLE & ~0x1u) | 0x2u;
                }
            }

            if (edgeJump)
            {
                // Edge jump: auto-jump when grounded and moving forward
                // Detects "edge" by checking if grounded + moving + not already jumping
                if (Controll.HLBAGIACGBI && !Controll.GCHFDAPNBNB)
                {
                    var input = Controll.MNHBPCOOMLE;
                    var isMoving = (input & 0x4u) != 0 || (input & 0x8u) != 0; // forward or backward
                    if (isMoving)
                    {
                        // Set jump flag
                        Controll.MNHBPCOOMLE |= 0x10u; // jump=16
                        Controll.GCHFDAPNBNB = true;
                    }
                }
            }

            if (fakeLag)
            {
                // Fake lag: delay position updates by holding position static
                // every other 100ms window, making hit prediction harder
                var lagPhase = (int)(Time.time * 10f) % 3;
                if (lagPhase == 0)
                {
                    // During lag phase, don't update position (freeze)
                    // This makes server-side prediction harder for opponents
                    // We achieve this by zeroing the movement input briefly
                    Controll.MNHBPCOOMLE &= ~(0x1u | 0x2u | 0x4u | 0x8u);
                }
            }
        }
        catch (Exception exception)
        {
            if (!featureFailureLogged)
            {
                instance?.Log.LogWarning($"[Cheat features] apply failed: {exception}");
                featureFailureLogged = true;
            }
        }
    }

    private static void PrepareRapidFirePrefix()
    {
        if (!forceShotThisFrame)
        {
            return;
        }

        try
        {
            var main = Controll.HGAODFPBGLB;
            if (main != null && main.JPGGPPLOOML != null)
            {
                main.FGFKPMPLNKO = 1000f;
            }
        }
        catch
        {
            // Layout or null state: native Controll.Update will simply be allowed to fire normally.
        }
    }

    private static void ForceRapidFireShot()
    {
        if (!forceShotThisFrame)
        {
            return;
        }

        try
        {
            var main = Controll.HGAODFPBGLB;
            if (main == null || main.JPGGPPLOOML == null)
            {
                forceShotThisFrame = false;
                return;
            }

            // Reset the fire timer so PLH.CDEGJOBLOFO's internal "has enough time passed"
            // check passes every tick.
            Controll.LCMOBPPHLLM = 0f;
            main.FGFKPMPLNKO = -1000f;
            PLH.CDEGJOBLOFO(main, 0f, false, false);
            main.FGFKPMPLNKO = 1000f;
            AsyncLog.Write($"[RapidFire] fired: weapon={main.JPGGPPLOOML?.OCDNCKANJPB}, health={main.FDOJDJLIGLF}");
        }
        catch (Exception exception)
        {
            if (!rapidFireFailureLogged)
            {
                instance?.Log.LogWarning($"[Rapid fire] direct PLH.CDEGJOBLOFO failed: {exception}");
                rapidFireFailureLogged = true;
            }
        }
    }

    /// <summary>
    /// Send the hit report to the server. The game normally does this from
    /// Controll.Update after processing the hit list, but since we fire from
    /// the prefix we need to do it ourselves.
    /// </summary>
    private static void SendHitReport(KBBBHJDINCB main)
    {
        try
        {
            var hitList = Controll.GOMFKJNNJAP;
            if (hitList == null || hitList.Count == 0)
            {
                return; // no hits to report
            }

            var client = NetProbe.GetClient();
            if (client == null)
            {
                return;
            }

            // Call Client.AHLDAPJEJNC(Vector3, uint, List<DMHBMAAFCFJ>) via reflection.
            // The IL2CPP interop generates a managed wrapper we can invoke.
            var clientType = AccessTools.TypeByName("Client");
            if (clientType == null)
            {
                return;
            }

            var method = clientType.GetMethod("AHLDAPJEJNC", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                AsyncLog.Write("[HitReport] AHLDAPJEJNC method not found");
                return;
            }

            var origin = main.OOMJGHCFODI;
            var seq = (uint)Controll.GAMBHJPMDON;
            method.Invoke(client, new object[] { origin, seq, hitList });
            AsyncLog.Write($"[HitReport] sent: hits={hitList.Count}, seq={seq}");
        }
        catch (Exception e)
        {
            AsyncLog.Write($"[HitReport] failed: {e.Message}");
        }
    }

    private static void LogControllerStartup()
    {
        if (bootstrapLogged)
        {
            return;
        }

        instance?.Log.LogInfo("Blockpost controller update is running.");
        bootstrapLogged = true;
    }

    // Saved cursor state before the menu opened, so we can restore it on close
    // instead of forcing Locked (which breaks the game's own main menu / lobby).
    private static CursorLockMode savedLockState;
    private static bool savedCursorVisible;

    private static void ToggleMenuIfRequested()
    {
        // Panic mode: press End to instantly disable all features
        if (Input.GetKeyDown(KeyCode.End))
        {
            panicMode = !panicMode;
            if (panicMode)
            {
                ResetAllFeatures();
                instance?.Log.LogInfo("[Panic] All features disabled (panic mode ON)");
            }
            else
            {
                instance?.Log.LogInfo("[Panic] Panic mode OFF — reload config");
                LoadConfig();
            }
            return;
        }

        if (!Input.GetKeyDown(KeyCode.Home))
        {
            return;
        }

        menuVisible = !menuVisible;
        if (menuVisible)
        {
            // Save the game's current cursor state so we can restore it exactly on close.
            savedLockState = Cursor.lockState;
            savedCursorVisible = Cursor.visible;
            // Free the mouse cursor so the user can interact with the menu.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            // Restore whatever the game had before we opened the menu — could be
            // Locked (in-match), None (main menu/lobby), or Confined.
            Cursor.lockState = savedLockState;
            Cursor.visible = savedCursorVisible;
        }
        instance?.Log.LogInfo($"Trainer menu {(menuVisible ? "opened" : "closed")}.");
    }

    // ---- config persistence ----

    private static void LoadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return;
            }

            ApplyConfigLines(File.ReadAllLines(ConfigPath));
        }
        catch (Exception ex)
        {
            instance?.Log.LogError($"[Config] Failed to load config: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply an array of config lines to the feature fields.
    /// </summary>
    private static void ApplyConfigLines(string[] lines)
    {
        foreach (var line in lines)
        {
            var idx = line.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();

            switch (key)
            {
                    case "espEnabled": espEnabled = val == "1"; break;
                    case "showHealth": showHealth = val == "1"; break;
                    case "showTeammates": showTeammates = val == "1"; break;
                    case "aimbotEnabled": aimbotEnabled = val == "1"; break;
                    case "aimActivationMode": aimActivationMode = ParseInt(val, aimActivationMode); break;
                    case "aimStyle": aimStyle = ParseInt(val, aimStyle); break;
                    case "aimbotFov": aimbotFov = ParseFloat(val, aimbotFov); break;
                    case "autoShoot": autoShoot = val == "1"; break;
                    case "rapidFire": rapidFire = val == "1"; break;
                    case "ghostBullets": ghostBullets = val == "1"; break;
                    case "serverTrustTest": serverTrustTest = val == "1"; break;
                    case "noRecoil": noRecoil = val == "1"; break;
                    case "infiniteHealth": infiniteHealth = val == "1"; break;
                    case "infiniteAmmo": infiniteAmmo = val == "1"; break;
                    case "instantReload": instantReload = val == "1"; break;
                    case "bunnyHop": bunnyHop = val == "1"; break;
                    case "customCrosshair": customCrosshair = val == "1"; break;
                    case "fovChanger": fovChanger = val == "1"; break;
                    case "targetFov": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out targetFov); break;
                    case "speedHack": speedHack = val == "1"; break;
                    case "speedMultiplier": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out speedMultiplier); break;
                    case "flyHack": flyHack = val == "1"; break;
                    case "gokuTp": gokuTp = val == "1"; break;
                    case "noClip": noClip = val == "1"; break;
                    case "weaponUnlock": weaponUnlock = val == "1"; break;
                    case "thirdPerson": thirdPerson = val == "1"; break;
                    case "thirdPersonDistance": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out thirdPersonDistance); break;
                    case "chams": chams = val == "1"; break;
                    case "triggerbot": triggerbot = val == "1"; break;
                    case "triggerbotRange": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out triggerbotRange); break;
                    case "fullbright": fullbright = val == "1"; break;
                    case "antiFlash": antiFlash = val == "1"; break;
                    case "wallhack": wallhack = val == "1"; break;
                    case "noSpread": noSpread = val == "1"; break;
                    case "fastFire": fastFire = val == "1"; break;
                    case "autoReload": autoReload = val == "1"; break;
                    case "nameEsp": nameEsp = val == "1"; break;
                    case "spinbot": spinbot = val == "1"; break;
                    case "skeletonEsp": skeletonEsp = val == "1"; break;
                    case "radarHack": radarHack = val == "1"; break;
                    case "antiAimPitch": antiAimPitch = val == "1"; break;
                    case "autoStrafe": autoStrafe = val == "1"; break;
                    case "killFeed": killFeed = val == "1"; break;
                    case "edgeJump": edgeJump = val == "1"; break;
                    case "fakeLag": fakeLag = val == "1"; break;
                    case "spectatorWarning": spectatorWarning = val == "1"; break;
                    case "damageIndicator": damageIndicator = val == "1"; break;
                    case "hitMarker": hitMarker = val == "1"; break;
                    case "autoPickup": autoPickup = val == "1"; break;
                    case "aimBone": aimBone = ParseInt(val, 0); break;
                    case "xpGoldHack": xpGoldHack = val == "1"; break;
                    case "healthBarEsp": healthBarEsp = val == "1"; break;
                    case "snaplines": snaplines = val == "1"; break;
                    case "threatIndicator": threatIndicator = val == "1"; break;
                    case "nameChanger": nameChanger = val == "1"; break;
                    case "customName": customName = val; break;
                    case "scoreboardHack": scoreboardHack = val == "1"; break;
                    case "autoBhop": autoBhop = val == "1"; break;
                    case "pingSpoof": pingSpoof = val == "1"; break;
                    case "fakePing": fakePing = ParseInt(val, 150); break;
                    case "footstepEsp": footstepEsp = val == "1"; break;
                    case "preFire": preFire = val == "1"; break;
                    case "backtrack": backtrack = val == "1"; break;
                    case "adminUnlock": adminUnlock = val == "1"; break;
                    case "boxHeadEsp": boxHeadEsp = val == "1"; break;
                    case "slideHack": slideHack = val == "1"; break;
                    case "grenadeTrajectory": grenadeTrajectory = val == "1"; break;
                    case "noFallDamage": noFallDamage = val == "1"; break;
                    case "crosshairCustom": crosshairCustom = val == "1"; break;
                    case "crosshairR": crosshairR = float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cr) ? cr : 0f; break;
                    case "crosshairG": crosshairG = float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cg) ? cg : 1f; break;
                    case "crosshairB": crosshairB = float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cb) ? cb : 0f; break;
                    case "crosshairSize": crosshairSize = ParseInt(val, 10); break;
                    case "crosshairThickness": crosshairThickness = ParseInt(val, 2); break;
                    case "killSound": killSound = val == "1"; break;
                    case "aimbotSmoothing": aimbotSmoothing = val == "1"; break;
                    case "aimbotSmoothFactor": aimbotSmoothFactor = float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var asf) ? asf : 0.5f; break;
                    case "fastWeaponSwitch": fastWeaponSwitch = val == "1"; break;
                    case "antiAimJitter": antiAimJitter = val == "1"; break;
                    case "debugOverlay": debugOverlay = val == "1"; break;
                    case "autoAccept": autoAccept = val == "1"; break;
                    case "distanceEsp": distanceEsp = val == "1"; break;
                    case "weaponIdEsp": weaponIdEsp = val == "1"; break;
                    case "chatSpammer": chatSpammer = val == "1"; break;
                    case "spamMessage": spamMessage = val; break;
                    case "autoVoteYes": autoVoteYes = val == "1"; break;
                    case "autoRevive": autoRevive = val == "1"; break;
                    case "noSkybox": noSkybox = val == "1"; break;
                    case "wireframePlayers": wireframePlayers = val == "1"; break;
                    case "zoomHack": zoomHack = val == "1"; break;
                    case "zoomFov": zoomFov = float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var zf) ? zf : 30f; break;
                    case "noMuzzleFlash": noMuzzleFlash = val == "1"; break;
                    case "nightVision": nightVision = val == "1"; break;
                    case "noSmoke": noSmoke = val == "1"; break;
                    case "autoSprint": autoSprint = val == "1"; break;
                    case "noRain": noRain = val == "1"; break;
                    case "thirdPersonShoulder": thirdPersonShoulder = val == "1"; break;
                    case "thirdPersonShoulderX": thirdPersonShoulderX = float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tpsx) ? tpsx : 1.5f; break;
                    case "aimbotPrediction": aimbotPrediction = val == "1"; break;
                    case "autoCrouchIdle": autoCrouchIdle = val == "1"; break;
                    case "panicMode": panicMode = val == "1"; break;
                    case "fovFilterEsp": fovFilterEsp = val == "1"; break;
                    case "hitLog": hitLog = val == "1"; break;
                    case "autoWeaponSwap": autoWeaponSwap = val == "1"; break;
                    case "screenCleaner": screenCleaner = val == "1"; break;
                    case "noShadows": noShadows = val == "1"; break;
                    case "playerList": playerList = val == "1"; break;
                    case "noGrass": noGrass = val == "1"; break;
                    case "crosshairHitIndicator": crosshairHitIndicator = val == "1"; break;
                    case "timeScaleHack": timeScaleHack = val == "1"; break;
                    case "customTimeScale": customTimeScale = float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cts) ? cts : 1f; break;
                    case "noFog": noFog = val == "1"; break;
                    case "aimEnemiesOnly": aimEnemiesOnly = val == "1"; break;
                    case "boneScan": boneScan = val == "1"; break;
                    case "radarMiniMap": radarMiniMap = val == "1"; break;
                    case "autoReloadThreshold": autoReloadThreshold = int.TryParse(val, out var art) ? art : 5; break;
                    case "debugLogging": debugLogging = val == "1"; break;
                    case "heavyDiagnostics": heavyDiagnostics = val == "1"; break;
                    case "showRuntimeStatus": showRuntimeStatus = val == "1"; break;
                    case "menuX": menuRect.x = ParseFloat(val, menuRect.x); break;
                    case "menuY": menuRect.y = ParseFloat(val, menuRect.y); break;
                }
            }
        }

    private static void SaveConfig()
    {
        try
        {
            var lines = BuildConfigLines();
            File.WriteAllLines(ConfigPath, lines);
        }
        catch (Exception e)
        {
            instance?.Log.LogWarning($"Config save failed: {e.Message}");
        }
    }

    /// <summary>
    /// Build the array of config lines from current feature states.
    /// </summary>
    private static string[] BuildConfigLines()
    {
        return new[]
        {
                $"espEnabled={(espEnabled ? 1 : 0)}",
                $"showHealth={(showHealth ? 1 : 0)}",
                $"showTeammates={(showTeammates ? 1 : 0)}",
                $"aimbotEnabled={(aimbotEnabled ? 1 : 0)}",
                $"aimActivationMode={aimActivationMode}",
                $"aimStyle={aimStyle}",
                $"aimbotFov={aimbotFov.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}",
                $"autoShoot={(autoShoot ? 1 : 0)}",
                $"rapidFire={(rapidFire ? 1 : 0)}",
                $"ghostBullets={(ghostBullets ? 1 : 0)}",
                $"serverTrustTest={(serverTrustTest ? 1 : 0)}",
                $"noRecoil={(noRecoil ? 1 : 0)}",
                $"infiniteHealth={(infiniteHealth ? 1 : 0)}",
                $"infiniteAmmo={(infiniteAmmo ? 1 : 0)}",
                $"instantReload={(instantReload ? 1 : 0)}",
                $"bunnyHop={(bunnyHop ? 1 : 0)}",
                $"customCrosshair={(customCrosshair ? 1 : 0)}",
                $"fovChanger={(fovChanger ? 1 : 0)}",
                $"targetFov={targetFov.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}",
                $"speedHack={(speedHack ? 1 : 0)}",
                $"speedMultiplier={speedMultiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}",
                $"flyHack={(flyHack ? 1 : 0)}",
                $"gokuTp={(gokuTp ? 1 : 0)}",
                $"noClip={(noClip ? 1 : 0)}",
                $"weaponUnlock={(weaponUnlock ? 1 : 0)}",
                $"thirdPerson={(thirdPerson ? 1 : 0)}",
                $"thirdPersonDistance={thirdPersonDistance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}",
                $"chams={(chams ? 1 : 0)}",
                $"triggerbot={(triggerbot ? 1 : 0)}",
                $"triggerbotRange={triggerbotRange.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}",
                $"fullbright={(fullbright ? 1 : 0)}",
                $"antiFlash={(antiFlash ? 1 : 0)}",
                $"wallhack={(wallhack ? 1 : 0)}",
                $"noSpread={(noSpread ? 1 : 0)}",
                $"fastFire={(fastFire ? 1 : 0)}",
                $"autoReload={(autoReload ? 1 : 0)}",
                $"nameEsp={(nameEsp ? 1 : 0)}",
                $"spinbot={(spinbot ? 1 : 0)}",
                $"skeletonEsp={(skeletonEsp ? 1 : 0)}",
                $"radarHack={(radarHack ? 1 : 0)}",
                $"antiAimPitch={(antiAimPitch ? 1 : 0)}",
                $"autoStrafe={(autoStrafe ? 1 : 0)}",
                $"killFeed={(killFeed ? 1 : 0)}",
                $"edgeJump={(edgeJump ? 1 : 0)}",
                $"fakeLag={(fakeLag ? 1 : 0)}",
                $"spectatorWarning={(spectatorWarning ? 1 : 0)}",
                $"damageIndicator={(damageIndicator ? 1 : 0)}",
                $"hitMarker={(hitMarker ? 1 : 0)}",
                $"autoPickup={(autoPickup ? 1 : 0)}",
                $"aimBone={aimBone}",
                $"xpGoldHack={(xpGoldHack ? 1 : 0)}",
                $"healthBarEsp={(healthBarEsp ? 1 : 0)}",
                $"snaplines={(snaplines ? 1 : 0)}",
                $"threatIndicator={(threatIndicator ? 1 : 0)}",
                $"nameChanger={(nameChanger ? 1 : 0)}",
                $"customName={customName}",
                $"scoreboardHack={(scoreboardHack ? 1 : 0)}",
                $"autoBhop={(autoBhop ? 1 : 0)}",
                $"pingSpoof={(pingSpoof ? 1 : 0)}",
                $"fakePing={fakePing}",
                $"footstepEsp={(footstepEsp ? 1 : 0)}",
                $"preFire={(preFire ? 1 : 0)}",
                $"backtrack={(backtrack ? 1 : 0)}",
                $"adminUnlock={(adminUnlock ? 1 : 0)}",
                $"boxHeadEsp={(boxHeadEsp ? 1 : 0)}",
                $"slideHack={(slideHack ? 1 : 0)}",
                $"grenadeTrajectory={(grenadeTrajectory ? 1 : 0)}",
                $"noFallDamage={(noFallDamage ? 1 : 0)}",
                $"crosshairCustom={(crosshairCustom ? 1 : 0)}",
                $"crosshairR={crosshairR.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"crosshairG={crosshairG.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"crosshairB={crosshairB.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"crosshairSize={crosshairSize}",
                $"crosshairThickness={crosshairThickness}",
                $"killSound={(killSound ? 1 : 0)}",
                $"aimbotSmoothing={(aimbotSmoothing ? 1 : 0)}",
                $"aimbotSmoothFactor={aimbotSmoothFactor.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"fastWeaponSwitch={(fastWeaponSwitch ? 1 : 0)}",
                $"antiAimJitter={(antiAimJitter ? 1 : 0)}",
                $"debugOverlay={(debugOverlay ? 1 : 0)}",
                $"autoAccept={(autoAccept ? 1 : 0)}",
                $"distanceEsp={(distanceEsp ? 1 : 0)}",
                $"weaponIdEsp={(weaponIdEsp ? 1 : 0)}",
                $"chatSpammer={(chatSpammer ? 1 : 0)}",
                $"spamMessage={spamMessage}",
                $"autoVoteYes={(autoVoteYes ? 1 : 0)}",
                $"autoRevive={(autoRevive ? 1 : 0)}",
                $"noSkybox={(noSkybox ? 1 : 0)}",
                $"wireframePlayers={(wireframePlayers ? 1 : 0)}",
                $"zoomHack={(zoomHack ? 1 : 0)}",
                $"zoomFov={zoomFov.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"noMuzzleFlash={(noMuzzleFlash ? 1 : 0)}",
                $"nightVision={(nightVision ? 1 : 0)}",
                $"noSmoke={(noSmoke ? 1 : 0)}",
                $"autoSprint={(autoSprint ? 1 : 0)}",
                $"noRain={(noRain ? 1 : 0)}",
                $"thirdPersonShoulder={(thirdPersonShoulder ? 1 : 0)}",
                $"thirdPersonShoulderX={thirdPersonShoulderX.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"aimbotPrediction={(aimbotPrediction ? 1 : 0)}",
                $"autoCrouchIdle={(autoCrouchIdle ? 1 : 0)}",
                $"panicMode={(panicMode ? 1 : 0)}",
                $"fovFilterEsp={(fovFilterEsp ? 1 : 0)}",
                $"hitLog={(hitLog ? 1 : 0)}",
                $"autoWeaponSwap={(autoWeaponSwap ? 1 : 0)}",
                $"screenCleaner={(screenCleaner ? 1 : 0)}",
                $"noShadows={(noShadows ? 1 : 0)}",
                $"playerList={(playerList ? 1 : 0)}",
                $"noGrass={(noGrass ? 1 : 0)}",
                $"crosshairHitIndicator={(crosshairHitIndicator ? 1 : 0)}",
                $"timeScaleHack={(timeScaleHack ? 1 : 0)}",
                $"customTimeScale={customTimeScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"noFog={(noFog ? 1 : 0)}",
                $"aimEnemiesOnly={(aimEnemiesOnly ? 1 : 0)}",
                $"boneScan={(boneScan ? 1 : 0)}",
                $"radarMiniMap={(radarMiniMap ? 1 : 0)}",
                $"autoReloadThreshold={autoReloadThreshold}",
                $"debugLogging={(debugLogging ? 1 : 0)}",
                $"heavyDiagnostics={(heavyDiagnostics ? 1 : 0)}",
                $"showRuntimeStatus={(showRuntimeStatus ? 1 : 0)}",
                $"menuX={menuRect.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}",
                $"menuY={menuRect.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}"
        };
    }

    private static int ParseInt(string s, int fallback) => int.TryParse(s, out var v) ? v : fallback;

    /// <summary>
    /// Save current configuration to a named preset file.
    /// </summary>
    private static void SavePreset(string name)
    {
        try
        {
            var presetPath = Path.Combine(Path.GetDirectoryName(ConfigPath) ?? ".", $"preset_{name}.cfg");
            var lines = BuildConfigLines();
            File.WriteAllLines(presetPath, lines);
            instance?.Log.LogInfo($"[Config] Saved preset '{name}' to {presetPath}");
        }
        catch (Exception ex)
        {
            instance?.Log.LogError($"[Config] Failed to save preset '{name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Load a named preset file and apply its settings.
    /// </summary>
    private static void LoadPreset(string name)
    {
        try
        {
            var presetPath = Path.Combine(Path.GetDirectoryName(ConfigPath) ?? ".", $"preset_{name}.cfg");
            if (!File.Exists(presetPath))
            {
                instance?.Log.LogWarning($"[Config] Preset '{name}' not found at {presetPath}");
                return;
            }
            var lines = File.ReadAllLines(presetPath);
            ApplyConfigLines(lines);
            SaveConfig();
            instance?.Log.LogInfo($"[Config] Loaded preset '{name}' from {presetPath}");
        }
        catch (Exception ex)
        {
            instance?.Log.LogError($"[Config] Failed to load preset '{name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Auto-crouch when idle: crouch when the player is not moving to reduce
    /// hitbox size. Automatically stands up when movement input is detected.
    /// </summary>
    private static void ApplyAutoCrouchIdle()
    {
        try
        {
            var input = Controll.MNHBPCOOMLE;
            var isMoving = (input & 0x4u) != 0 || (input & 0x1u) != 0 || (input & 0x2u) != 0 || (input & 0x8u) != 0;
            if (!isMoving && Controll.HLBAGIACGBI)
            {
                Controll.MNHBPCOOMLE |= 0x20u; // duck
                Controll.NJPDKJKJMCG = true;
            }
        }
        catch { }
    }

    /// <summary>
    /// No grass: disable grass and foliage GameObjects for better ground visibility.
    /// </summary>
    private static void ApplyNoGrass()
    {
        try
        {
            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            if (allObjects == null) return;
            foreach (var go in allObjects)
            {
                if (go == null) continue;
                var name = go.name;
                if (string.IsNullOrEmpty(name)) continue;
                var lower = name.ToLower();
                if (lower.Contains("grass") || lower.Contains("bush") || lower.Contains("foliage") || lower.Contains("plant"))
                {
                    go.SetActive(false);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Draw radar mini-map: shows enemy positions relative to player in a circular radar.
    /// Displayed in top-right corner. Scale: 1m = 1px, max range 100m.
    /// </summary>
    private static void DrawRadarMiniMap()
    {
        try
        {
            var mainPlayer = Controll.HGAODFPBGLB;
            if (mainPlayer == null) return;
            var players = PLH.BAKLNPIEHMI;
            if (players == null) return;

            var radarSize = 150f;
            var radarX = Screen.width - radarSize - 10;
            var radarY = 10;
            var radarCenter = new Vector2(radarX + radarSize / 2, radarY + radarSize / 2);
            var radarRange = 100f; // meters
            var radarScale = (radarSize / 2) / radarRange;

            // Draw radar background
            var prevColor = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(radarX, radarY, radarSize, radarSize), Texture2D.whiteTexture);
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            // Border
            GUI.DrawTexture(new Rect(radarX, radarY, radarSize, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(radarX, radarY + radarSize - 1, radarSize, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(radarX, radarY, 1, radarSize), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(radarX + radarSize - 1, radarY, 1, radarSize), Texture2D.whiteTexture);

            // Draw player at center
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(radarCenter.x - 2, radarCenter.y - 2, 4, 4), Texture2D.whiteTexture);

            // Draw enemies on radar
            var myPos = mainPlayer.OOMJGHCFODI;
            var myYaw = Controll.NAKNALFCOIF * Mathf.Deg2Rad;
            var cosYaw = Mathf.Cos(myYaw);
            var sinYaw = Mathf.Sin(myYaw);

            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null || player.FDOJDJLIGLF <= 0) continue;
                if (player == mainPlayer) continue;

                var delta = player.OOMJGHCFODI - myPos;
                var dist = delta.magnitude;
                if (dist > radarRange) continue;

                // Rotate relative to player yaw
                var relX = delta.x * cosYaw + delta.z * sinYaw;
                var relZ = -delta.x * sinYaw + delta.z * cosYaw;

                var radarPx = radarCenter.x + relX * radarScale;
                var radarPy = radarCenter.y - relZ * radarScale;

                var isEnemy = player.MMMGPDBMOLM != mainPlayer.MMMGPDBMOLM;
                GUI.color = isEnemy ? new Color(1f, 0.2f, 0.2f, 0.9f) : new Color(0.2f, 1f, 0.2f, 0.9f);
                GUI.DrawTexture(new Rect(radarPx - 2, radarPy - 2, 4, 4), Texture2D.whiteTexture);
            }

            GUI.color = prevColor;
        }
        catch { }
    }

    /// <summary>
    /// Draw crosshair hit indicator: changes crosshair color when aiming at an enemy.
    /// Uses a raycast from camera center to check if crosshair is on an enemy player.
    /// </summary>
    private static void DrawCrosshairHitIndicator()
    {
        var camera = ResolveCamera();
        if (camera == null) return;
        var mainPlayer = Controll.HGAODFPBGLB;
        if (mainPlayer == null) return;

        var hitEnemy = false;
        var ray = new Ray(camera.transform.position, camera.transform.forward);
        if (Physics.Raycast(ray, out var hit, 200f))
        {
            // Check if hit object is a player
            var hitTransform = hit.transform;
            while (hitTransform != null)
            {
                var players = PLH.BAKLNPIEHMI;
                if (players != null)
                {
                    for (var i = 0; i < players.Length; i++)
                    {
                        var player = players[i];
                        if (player == null || player.ACEHIBLPHCA == null) continue;
                        if (hitTransform == player.ACEHIBLPHCA.transform ||
                            hitTransform.IsChildOf(player.ACEHIBLPHCA.transform))
                        {
                            if (player.MMMGPDBMOLM != mainPlayer.MMMGPDBMOLM)
                            {
                                hitEnemy = true;
                            }
                            break;
                        }
                    }
                }
                if (hitEnemy) break;
                hitTransform = hitTransform.parent;
            }
        }

        var cx = Screen.width / 2f;
        var cy = Screen.height / 2f;
        var prevColor = GUI.color;
        GUI.color = hitEnemy ? new Color(1f, 0f, 0f, 0.9f) : new Color(1f, 1f, 1f, 0.5f);
        // Draw a small dot in center
        GUI.DrawTexture(new Rect(cx - 2, cy - 2, 4, 4), Texture2D.whiteTexture);
        GUI.color = prevColor;
    }

    /// <summary>
    /// No shadows: disable all shadow-casting lights and set shadow distance to 0.
    /// Improves performance and visibility.
    /// </summary>
    private static void ApplyNoShadows()
    {
        try
        {
            var lights = UnityEngine.Object.FindObjectsOfType<Light>();
            if (lights != null)
            {
                foreach (var light in lights)
                {
                    if (light != null) light.shadows = LightShadows.None;
                }
            }
            // Disable shadow distance on quality settings
            QualitySettings.shadowDistance = 0f;
            QualitySettings.shadowCascades = 0;
        }
        catch { }
    }

    /// <summary>
    /// Draw player list: shows all players with their names, HP, team, and distance.
    /// Displayed in the top-left corner below the watermark.
    /// </summary>
    private static void DrawPlayerList()
    {
        try
        {
            var players = PLH.BAKLNPIEHMI;
            var mainPlayer = Controll.HGAODFPBGLB;
            if (players == null || mainPlayer == null) return;

            var x = 5f;
            var y = 100f;
            var prevColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(x, y, 200, 20), "--- Player List ---");
            y += 20;

            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null) continue;
                var isEnemy = player.MMMGPDBMOLM != mainPlayer.MMMGPDBMOLM;
                var hp = player.FDOJDJLIGLF;
                var name = player.NHHBNNBDDIA ?? "Unknown";
                var dist = Vector3.Distance(mainPlayer.OOMJGHCFODI, player.OOMJGHCFODI);

                GUI.color = isEnemy ? new Color(1f, 0.3f, 0.3f, 0.7f) : new Color(0.3f, 1f, 0.3f, 0.7f);
                GUI.Label(new Rect(x, y, 200, 20), $"{name} HP:{hp} {dist:F0}m {(isEnemy ? "ENEMY" : "ALLY")}");
                y += 20;
                if (y > Screen.height - 100) break; // Don't overflow
            }

            GUI.color = prevColor;
        }
        catch { }
    }

    /// <summary>
    /// Auto weapon swap: swap to the next weapon when current ammo is 0.
    /// Uses keyPrevWeapon/keyNextWeapon key codes from GUIOptions.
    /// </summary>
    private static void ApplyAutoWeaponSwap()
    {
        try
        {
            // If current weapon has no ammo, swap to next
            if (Controll.FGGKANNFBDH <= 0 && Controll.ILFOFIOFBAM > 0)
            {
                // Simulate weapon switch by setting the slot directly
                var currentSlot = Controll.HGAODFPBGLB?.MOPBMENEGLN ?? 0;
                var nextSlot = (currentSlot + 1) % 4; // 4 weapon slots
                if (Controll.HGAODFPBGLB != null)
                {
                    Controll.HGAODFPBGLB.MOPBMENEGLN = nextSlot;
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Screen cleaner: disable non-essential UI GameObjects to reduce clutter.
    /// Disables objects with "banner", "ad", "promo", "notification" in name.
    /// </summary>
    private static void ApplyScreenCleaner()
    {
        try
        {
            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            if (allObjects == null) return;
            foreach (var go in allObjects)
            {
                if (go == null) continue;
                var name = go.name;
                if (string.IsNullOrEmpty(name)) continue;
                var lower = name.ToLower();
                if (lower.Contains("banner") || lower.Contains("promo") || lower.Contains("ad_") || lower.Contains("notification"))
                {
                    go.SetActive(false);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Hit log: monitor hit counter and log each hit to a file with timestamp.
    /// </summary>
    private static void UpdateHitLog()
    {
        if (!hitLog) return;
        try
        {
            var currentHits = Controll.GAMBHJPMDON;
            if (lastHitLogCount < 0)
            {
                lastHitLogCount = currentHits;
                return;
            }
            if (currentHits > lastHitLogCount)
            {
                var hitCount = currentHits - lastHitLogCount;
                var logPath = Path.Combine(Paths.BepInExRootPath, "plugins", "hitlog.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] Hit registered (count={hitCount}, total={currentHits}){Environment.NewLine}");
            }
            lastHitLogCount = currentHits;
        }
        catch { }
    }

    /// <summary>
    /// Track kill streaks: monitor kill count and display current streak.
    /// Streak resets if no kill within 10 seconds.
    /// </summary>
    private static void UpdateKillStreak()
    {
        try
        {
            var currentKills = Controll.DEBGAILDKPC;
            if (lastKillStreakCheck < 0)
            {
                lastKillStreakCheck = currentKills;
                return;
            }
            if (currentKills > lastKillStreakCheck)
            {
                // New kill
                if (Time.time - lastKillTime > 10f)
                {
                    currentKillStreak = 1;
                }
                else
                {
                    currentKillStreak++;
                }
                lastKillTime = Time.time;
            }
            lastKillStreakCheck = currentKills;
        }
        catch { }
    }

    /// <summary>
    /// No rain: disable rain and weather particle GameObjects.
    /// </summary>
    private static void ApplyNoRain()
    {
        try
        {
            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            if (allObjects == null) return;
            foreach (var go in allObjects)
            {
                if (go == null) continue;
                var name = go.name;
                if (string.IsNullOrEmpty(name)) continue;
                var lower = name.ToLower();
                if (lower.Contains("rain") || lower.Contains("weather") || lower.Contains("snow"))
                {
                    go.SetActive(false);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Third person shoulder: offset the camera to the right of the player
    /// for an over-the-shoulder view. Uses camera.transform.position offset.
    /// </summary>
    private static void ApplyThirdPersonShoulder()
    {
        try
        {
            var camera = ResolveCamera();
            var main = Controll.HGAODFPBGLB;
            if (camera == null || main == null) return;
            // Offset camera to the right and slightly up
            var right = camera.transform.right * thirdPersonShoulderX;
            var up = camera.transform.up * 0.5f;
            camera.transform.position = main.OOMJGHCFODI + right + up + camera.transform.forward * -2f;
        }
        catch { }
    }

    /// <summary>
    /// Auto-sprint: always set the sprint flag (0x40) in movement input
    /// when the player is moving forward.
    /// </summary>
    private static void ApplyAutoSprint()
    {
        try
        {
            var input = Controll.MNHBPCOOMLE;
            // If moving forward, set sprint flag
            if ((input & 0x4u) != 0)
            {
                Controll.MNHBPCOOMLE |= 0x40u; // sprint=64
            }
        }
        catch { }
    }

    /// <summary>
    /// Night vision: boost ambient light intensity and set a green tint
    /// on the camera for night vision effect. Also disables fog.
    /// </summary>
    private static void ApplyNightVision()
    {
        try
        {
            RenderSettings.fog = false;
            RenderSettings.ambientLight = new Color(0.1f, 0.3f, 0.1f, 1f);
            var lights = UnityEngine.Object.FindObjectsOfType<Light>();
            if (lights != null)
            {
                foreach (var light in lights)
                {
                    if (light != null && light.type != LightType.Directional)
                    {
                        light.intensity = 3f;
                        light.color = new Color(0.5f, 1f, 0.5f);
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// No smoke: disable smoke grenade GameObjects by searching for
    /// objects with "smoke" or "particle" in name and deactivating them.
    /// </summary>
    private static void ApplyNoSmoke()
    {
        try
        {
            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            if (allObjects == null) return;
            foreach (var go in allObjects)
            {
                if (go == null) continue;
                var name = go.name;
                if (string.IsNullOrEmpty(name)) continue;
                var lower = name.ToLower();
                if (lower.Contains("smoke") || lower.Contains("gas") || lower.Contains("fog_"))
                {
                    go.SetActive(false);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Zoom hack: set camera FOV to a lower value for zoom effect.
    /// Configurable FOV (default 30, lower = more zoom).
    /// </summary>
    private static void ApplyZoomHack()
    {
        try
        {
            var cam = ResolveCamera();
            if (cam != null) cam.fieldOfView = zoomFov;
        }
        catch { }
    }

    /// <summary>
    /// No muzzle flash: disable muzzle flash GameObjects and light effects.
    /// Searches for GameObjects with "muzzle", "flash", "flashlight" in name.
    /// </summary>
    private static void ApplyNoMuzzleFlash()
    {
        try
        {
            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            if (allObjects == null) return;
            foreach (var go in allObjects)
            {
                if (go == null) continue;
                var name = go.name;
                if (string.IsNullOrEmpty(name)) continue;
                var lower = name.ToLower();
                if (lower.Contains("muzzle") || lower.Contains("flashlight"))
                {
                    go.SetActive(false);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// No skybox: disable the skybox to improve visibility of enemies
    /// against the sky. Sets RenderSettings.skybox to null and background
    /// to a solid color.
    /// </summary>
    private static void ApplyNoSkybox()
    {
        if (!noSkybox)
        {
            // Restore skybox if needed (only do this once when toggled off)
            return;
        }
        try
        {
            RenderSettings.skybox = null;
            Camera.main?.backgroundColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        }
        catch { }
    }

    /// <summary>
    /// Wireframe players: set all enemy player renderers to wireframe mode
    /// by changing their material to a wireframe shader.
    /// </summary>
    private static void ApplyWireframePlayers()
    {
        if (!wireframePlayers) return;
        try
        {
            var players = PLH.BAKLNPIEHMI;
            var mainPlayer = Controll.HGAODFPBGLB;
            if (players == null || mainPlayer == null) return;

            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (!IsVisibleTarget(player, mainPlayer, true, true)) continue;
                var head = player.ACEHIBLPHCA;
                if (head == null) continue;

                var renderers = head.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    if (r == null || r.material == null) continue;
                    // Set wireframe mode by using a solid color shader with no texture
                    r.material.shader = Shader.Find("Hidden/Internal-Colored");
                    r.material.SetColor("_Color", new Color(0f, 1f, 0f, 0.3f));
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Auto-revive: when the player is dead, automatically trigger respawn.
    /// Monitors the death flag and sends respawn input.
    /// </summary>
    private static void ApplyAutoRevive()
    {
        try
        {
            var main = Controll.HGAODFPBGLB;
            if (main == null) return;
            // Check if dead (CLOEJLAOIGI is the death/respawn flag)
            if (main.CLOEJLAOIGI)
            {
                // Try to trigger respawn by looking for respawn button
                var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
                if (allObjects == null) return;
                foreach (var go in allObjects)
                {
                    if (go == null) continue;
                    var name = go.name;
                    if (string.IsNullOrEmpty(name)) continue;
                    var lower = name.ToLower();
                    if (lower.Contains("respawn") || lower.Contains("revive"))
                    {
                        go.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
                        return;
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Chat spammer: send a message to chat every 3 seconds.
    /// Uses the Client.SendChat method if available, otherwise tries
    /// to find and trigger the chat input field.
    /// </summary>
    private static void ApplyChatSpammer()
    {
        if (Time.time - lastChatSpam < 3f) return;
        lastChatSpam = Time.time;
        try
        {
            // Try to send chat via Client method
            var clientType = AccessTools.TypeByName("Client");
            if (clientType == null) return;
            // Look for a SendChat or Chat method
            var chatMethod = clientType.GetMethod("SendChat")
                ?? clientType.GetMethod("Chat")
                ?? clientType.GetMethod("Send");
            if (chatMethod != null)
            {
                chatMethod.Invoke(null, new object[] { spamMessage });
            }
        }
        catch { }
    }

    /// <summary>
    /// Auto vote yes: automatically vote yes on votekick/vote sessions.
    /// Searches for GameObjects with "vote", "yes", "f1" in name.
    /// </summary>
    private static void ApplyAutoVoteYes()
    {
        try
        {
            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            if (allObjects == null) return;
            foreach (var go in allObjects)
            {
                if (go == null) continue;
                var name = go.name;
                if (string.IsNullOrEmpty(name)) continue;
                var lower = name.ToLower();
                if (lower.Contains("voteyes") || lower.Contains("vote_yes") || lower.Contains("f1"))
                {
                    go.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Auto-accept: automatically accept match invites and ready up.
    /// Looks for GameObjects with "accept", "ready", "invite" in name and clicks them.
    /// </summary>
    private static void ApplyAutoAccept()
    {
        try
        {
            // Look for accept/ready buttons by searching for GameObjects
            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            if (allObjects == null) return;
            foreach (var go in allObjects)
            {
                if (go == null) continue;
                var name = go.name;
                if (string.IsNullOrEmpty(name)) continue;
                var lower = name.ToLower();
                if (lower.Contains("accept") || lower.Contains("ready") || lower.Contains("invite"))
                {
                    // Try to click the button by sending it a message
                    go.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Draw debug overlay: shows player count, health, ammo, position, and
    /// other diagnostic info in the top-right corner.
    /// </summary>
    private static void DrawDebugOverlay()
    {
        try
        {
            var main = Controll.HGAODFPBGLB;
            var players = PLH.BAKLNPIEHMI;
            var x = Screen.width - 250;
            var y = 5f;
            var prevColor = GUI.color;
            GUI.color = new Color(0f, 1f, 0f, 0.8f);

            GUI.Label(new Rect(x, y, 240, 20), $"--- Debug Overlay ---");
            y += 20;
            GUI.Label(new Rect(x, y, 240, 20), $"Players: {(players != null ? players.Length : 0)}");
            y += 20;

            if (main != null)
            {
                GUI.Label(new Rect(x, y, 240, 20), $"HP: {main.FDOJDJLIGLF}/{main.EFHBKMHCMOH} Armor: {main.INGHEHAALBJ}");
                y += 20;
                var pos = main.OOMJGHCFODI;
                GUI.Label(new Rect(x, y, 240, 20), $"Pos: {pos.x:F1}, {pos.y:F1}, {pos.z:F1}");
                y += 20;
                GUI.Label(new Rect(x, y, 240, 20), $"Ammo: {Controll.FGGKANNFBDH}/{Controll.ILFOFIOFBAM} Reserve: {Controll.KJOMABGHAIJ}");
                y += 20;
                GUI.Label(new Rect(x, y, 240, 20), $"Kills: {Controll.DEBGAILDKPC} Deaths: {Controll.GKNJELHPMDE}");
                y += 20;
                GUI.Label(new Rect(x, y, 240, 20), $"Team: {Controll.POFKNJGAKPK} ID: {Controll.OGDPMIBJLDH}");
                y += 20;
                GUI.Label(new Rect(x, y, 240, 20), $"Grounded: {Controll.HLBAGIACGBI} Sprint: {Controll.PBICPLCFAGG}");
                y += 20;
                GUI.Label(new Rect(x, y, 240, 20), $"Crouch: {Controll.NJPDKJKJMCG} Jump: {Controll.GCHFDAPNBNB}");
                y += 20;
                GUI.Label(new Rect(x, y, 240, 20), $"Input: 0x{Controll.MNHBPCOOMLE:X8}");
                y += 20;
            }

            GUI.color = prevColor;
        }
        catch { }
    }

    /// <summary>
    /// Anti-aim jitter: randomly jitter the yaw angle by small amounts each frame
    /// to make it harder for enemy aimbots to track the player.
    /// </summary>
    private static void ApplyAntiAimJitter()
    {
        try
        {
            // Only jitter when not aiming (don't interfere with own aim)
            if (Controll.BFEOOOMMGLK) return;
            // Add random jitter between -15 and +15 degrees
            var jitter = UnityEngine.Random.Range(-15f, 15f);
            Controll.NAKNALFCOIF += jitter;
        }
        catch { }
    }

    /// <summary>
    /// Fast weapon switch: zero the weapon switch timer to allow instant switching.
    /// The game likely has a switch delay field that we can zero.
    /// </summary>
    private static void ApplyFastWeaponSwitch()
    {
        try
        {
            // Zero the reload/equip timer fields to allow instant weapon switching
            var main = Controll.HGAODFPBGLB;
            if (main == null) return;
            // Zero the fire timer to allow immediate fire after switch
            main.LCMOBPPHLLM = 0f;
            Controll.LCMOBPPHLLM = 0f;
        }
        catch { }
    }

    /// <summary>
    /// Kill sound: monitor kill count and play a beep sound when getting a kill.
    /// Uses Console.Beep as a simple audio cue.
    /// </summary>
    private static void UpdateKillSound()
    {
        if (!killSound) return;
        try
        {
            var currentKills = Controll.DEBGAILDKPC;
            if (lastKillCountForSound < 0)
            {
                lastKillCountForSound = currentKills;
                return;
            }
            if (currentKills > lastKillCountForSound)
            {
                // Play a short beep using System.Console.Beep (non-blocking)
                System.Console.Beep(800, 100);
            }
            lastKillCountForSound = currentKills;
        }
        catch { }
    }

    /// <summary>
    /// No fall damage: clamp the player's vertical velocity when falling to
    /// prevent fall damage. Also restores health immediately if it drops.
    /// </summary>
    private static void ApplyNoFallDamage()
    {
        try
        {
            var main = Controll.HGAODFPBGLB;
            if (main == null) return;
            // If health dropped below max but we're not in combat (no recent damage indicator),
            // restore to max. This effectively prevents fall damage.
            var hp = main.FDOJDJLIGLF;
            var maxHp = main.EFHBKMHCMOH;
            if (hp > 0 && hp < maxHp)
            {
                // Only restore if we're not taking bullet damage (check damage indicator timer)
                if (damageIndicatorTime <= 0 || Time.time - damageIndicatorTime > 0.5f)
                {
                    main.FDOJDJLIGLF = maxHp;
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Draw a custom crosshair with configurable color, size, and thickness.
    /// Replaces the default crosshair with a more visible one.
    /// </summary>
    private static void DrawCustomCrosshairV2()
    {
        if (!crosshairCustom) return;
        var cx = Screen.width / 2f;
        var cy = Screen.height / 2f;
        var size = crosshairSize;
        var thick = crosshairThickness;
        var color = new Color(crosshairR, crosshairG, crosshairB, 0.9f);

        var prevColor = GUI.color;
        GUI.color = color;

        // Center dot
        GUI.DrawTexture(new Rect(cx - 1, cy - 1, 2, 2), Texture2D.whiteTexture);

        // Four lines (top, bottom, left, right) with gap in center
        var gap = 3;
        // Top
        GUI.DrawTexture(new Rect(cx - thick / 2f, cy - gap - size, thick, size), Texture2D.whiteTexture);
        // Bottom
        GUI.DrawTexture(new Rect(cx - thick / 2f, cy + gap, thick, size), Texture2D.whiteTexture);
        // Left
        GUI.DrawTexture(new Rect(cx - gap - size, cy - thick / 2f, size, thick), Texture2D.whiteTexture);
        // Right
        GUI.DrawTexture(new Rect(cx + gap, cy - thick / 2f, size, thick), Texture2D.whiteTexture);

        GUI.color = prevColor;
    }

    /// <summary>
    /// Slide hack: force crouch while moving to enable sliding without cooldown.
    /// Sets the crouch flag (0x20) continuously while moving forward.
    /// </summary>
    private static void ApplySlideHack()
    {
        try
        {
            var input = Controll.MNHBPCOOMLE;
            var isMoving = (input & 0x4u) != 0 || (input & 0x1u) != 0 || (input & 0x2u) != 0;
            if (isMoving && Controll.HLBAGIACGBI)
            {
                // Set crouch flag while moving on ground
                Controll.MNHBPCOOMLE |= 0x20u; // duck=32
                Controll.NJPDKJKJMCG = true;
            }
        }
        catch { }
    }

    /// <summary>
    /// Draw grenade trajectory prediction: simulates a parabolic arc from
    /// the player's view direction and draws it as a series of dots.
    /// </summary>
    private static void DrawGrenadeTrajectory()
    {
        var camera = ResolveCamera();
        if (camera == null) return;
        var mainPlayer = Controll.HGAODFPBGLB;
        if (mainPlayer == null) return;

        var startPos = camera.transform.position;
        var direction = camera.transform.forward;
        var velocity = direction * 20f; // Initial throw velocity
        var gravity = new Vector3(0, -9.81f, 0);

        var prevColor = GUI.color;
        GUI.color = new Color(0f, 1f, 1f, 0.5f);

        var pos = startPos;
        for (var i = 0; i < 60; i++)
        {
            var screenPos = camera.WorldToScreenPoint(pos);
            if (screenPos.z <= 0) break;

            var x = screenPos.x;
            var y = Screen.height - screenPos.y;
            GUI.DrawTexture(new Rect(x - 1, y - 1, 2, 2), Texture2D.whiteTexture);

            // Simulate physics
            velocity += gravity * 0.1f;
            pos += velocity * 0.1f;

            // Stop if we hit ground level (approximate)
            if (pos.y < mainPlayer.OOMJGHCFODI.y - 2f) break;
        }

        GUI.color = prevColor;
    }

    /// <summary>
    /// Admin unlock: set GUIAdmin.show=true to force open the admin panel.
    /// Also sets admin-related flags on GUIOptions if available.
    /// </summary>
    private static void ApplyAdminUnlock()
    {
        try
        {
            var guiAdminType = AccessTools.TypeByName("GUIAdmin");
            if (guiAdminType == null) return;
            var showField = guiAdminType.GetField("show");
            if (showField != null)
            {
                showField.SetValue(null, true);
            }
        }
        catch { }
    }

    /// <summary>
    /// Draw box-head ESP: a more detailed ESP that draws a box around the
    /// player body and a separate smaller box around the head, with a line
    /// connecting them. Shows health, name, and distance.
    /// </summary>
    private static void DrawBoxHeadEsp()
    {
        if (!espEnabled) return;
        var camera = ResolveCamera();
        if (camera == null) return;
        var players = PLH.BAKLNPIEHMI;
        var mainPlayer = Controll.HGAODFPBGLB;
        if (players == null || mainPlayer == null) return;

        var prevColor = GUI.color;

        for (var i = 0; i < players.Length; i++)
        {
            var player = players[i];
            if (!IsVisibleTarget(player, mainPlayer, true, true)) continue;
            var head = player.ACEHIBLPHCA;
            if (head == null || head.transform == null) continue;

            var headPos = head.transform.position;
            var headScreen = camera.WorldToScreenPoint(headPos);
            if (headScreen.z <= 0) continue;

            var bodyScreen = camera.WorldToScreenPoint(headPos - Vector3.up * 1.5f);
            if (bodyScreen.z <= 0) continue;

            var isEnemy = player.MMMGPDBMOLM != mainPlayer.MMMGPDBMOLM;
            var color = isEnemy ? Color.red : Color.green;
            GUI.color = new Color(color.r, color.g, color.b, 0.7f);

            // Head box (small)
            var headY = Screen.height - headScreen.y;
            var headSize = 8f;
            DrawBoxOutline(new Rect(headScreen.x - headSize, headY - headSize, headSize * 2, headSize * 2));

            // Body box (larger)
            var bodyY = Screen.height - bodyScreen.y;
            var bodyHeight = Mathf.Abs(headY - bodyY);
            var bodyWidth = bodyHeight * 0.6f;
            DrawBoxOutline(new Rect(headScreen.x - bodyWidth / 2, bodyY, bodyWidth, bodyHeight));

            // Line connecting head to body
            DrawLine2D(new Vector2(headScreen.x, headY), new Vector2(headScreen.x, bodyY));

            // Info text
            if (isEnemy)
            {
                var dist = Vector3.Distance(mainPlayer.OOMJGHCFODI, headPos);
                GUI.Label(new Rect(headScreen.x + headSize + 2, headY - headSize, 100, 20),
                    $"{player.NHHBNNBDDIA} {dist:F0}m");
            }
        }

        GUI.color = prevColor;
    }

    /// <summary>
    /// Track enemy position history for backtrack feature.
    /// Stores last 2 seconds of each enemy's position, allowing the aimbot
    /// to target past positions for easier hit registration.
    /// </summary>
    private static void UpdateBacktrack()
    {
        if (!backtrack) return;
        try
        {
            var players = PLH.BAKLNPIEHMI;
            var mainPlayer = Controll.HGAODFPBGLB;
            if (players == null || mainPlayer == null) return;

            var now = Time.time;

            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (!IsVisibleTarget(player, mainPlayer, false, true)) continue;

                // Use player index as ID (stable within a match)
                if (!enemyPositionHistory.ContainsKey(i))
                {
                    enemyPositionHistory[i] = new Queue<(float, Vector3)>();
                }

                var queue = enemyPositionHistory[i];
                queue.Enqueue((now, player.OOMJGHCFODI));

                // Remove entries older than BacktrackDuration
                while (queue.Count > 0 && now - queue.Peek().Item1 > BacktrackDuration)
                {
                    queue.Dequeue();
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Get the backtrack position for a given player ID.
    /// Returns the position from ~200ms ago, or current position if no history.
    /// </summary>
    private static Vector3 GetBacktrackPosition(int playerId, float delay = 0.2f)
    {
        if (!enemyPositionHistory.TryGetValue(playerId, out var queue)) return Vector3.zero;
        var targetTime = Time.time - delay;
        var result = Vector3.zero;
        foreach (var (time, pos) in queue)
        {
            if (time <= targetTime)
            {
                result = pos;
            }
            else
            {
                break;
            }
        }
        return result == Vector3.zero ? queue.Peek().Item2 : result;
    }

    /// <summary>
    /// Draw backtrack positions as blue dots on screen.
    /// </summary>
    private static void DrawBacktrack()
    {
        if (!backtrack || enemyPositionHistory.Count == 0) return;
        var camera = ResolveCamera();
        if (camera == null) return;

        var prevColor = GUI.color;
        GUI.color = new Color(0.3f, 0.5f, 1f, 0.5f);

        foreach (var kvp in enemyPositionHistory)
        {
            var queue = kvp.Value;
            if (queue.Count < 2) continue;

            // Draw the oldest position in the history
            var oldest = queue.Peek();
            var screenPos = camera.WorldToScreenPoint(oldest.Item2);
            if (screenPos.z <= 0) continue;

            var x = screenPos.x;
            var y = Screen.height - screenPos.y;
            GUI.DrawTexture(new Rect(x - 3, y - 3, 6, 6), Texture2D.whiteTexture);
        }

        GUI.color = prevColor;
    }

    /// <summary>
    /// Track enemy positions for footstep ESP. Records recent enemy positions
    /// every 0.5 seconds and draws them as fading dots on the minimap/radar.
    /// </summary>
    private static void UpdateFootstepEsp()
    {
        if (!footstepEsp) return;
        if (Time.time - lastFootstepScan < 0.5f) return;
        lastFootstepScan = Time.time;

        try
        {
            var players = PLH.BAKLNPIEHMI;
            var mainPlayer = Controll.HGAODFPBGLB;
            if (players == null || mainPlayer == null) return;

            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (!IsVisibleTarget(player, mainPlayer, false, true)) continue;
                // Record enemy position as a "footstep"
                recentFootsteps.Add(player.OOMJGHCFODI);
            }

            // Keep only last 30 footsteps (15 seconds at 0.5s interval)
            while (recentFootsteps.Count > 30)
            {
                recentFootsteps.RemoveAt(0);
            }
        }
        catch { }
    }

    /// <summary>
    /// Pre-fire: automatically fire when an enemy is within a very close range
    /// and about to peek around a corner. Uses proximity detection.
    /// </summary>
    private static void ApplyPreFire()
    {
        try
        {
            var players = PLH.BAKLNPIEHMI;
            var mainPlayer = Controll.HGAODFPBGLB;
            if (players == null || mainPlayer == null) return;
            var camera = ResolveCamera();
            if (camera == null) return;

            var myPos = mainPlayer.OOMJGHCFODI;

            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (!IsVisibleTarget(player, mainPlayer, false, false)) continue;

                var enemyPos = player.OOMJGHCFODI;
                var dist = Vector3.Distance(myPos, enemyPos);

                // Pre-fire when enemy is very close (< 5m) and we have LOS
                if (dist < 5f && HasLineOfSight(camera, player, enemyPos))
                {
                    // Set fire input flag
                    Controll.EPEEFBDJAHO = 1f;
                    return;
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Draw footsteps on screen as fading dots.
    /// </summary>
    private static void DrawFootstepEsp()
    {
        if (!footstepEsp || recentFootsteps.Count == 0) return;
        var camera = ResolveCamera();
        if (camera == null) return;

        var prevColor = GUI.color;
        var myPos = Controll.HGAODFPBGLB?.OOMJGHCFODI ?? Vector3.zero;

        for (var i = 0; i < recentFootsteps.Count; i++)
        {
            var pos = recentFootsteps[i];
            var screenPos = camera.WorldToScreenPoint(pos);
            if (screenPos.z <= 0) continue;

            // Fade older footsteps
            var age = (float)(recentFootsteps.Count - i) / recentFootsteps.Count;
            var alpha = (1f - age) * 0.5f;
            GUI.color = new Color(1f, 0.5f, 0f, alpha);
            var x = screenPos.x;
            var y = Screen.height - screenPos.y;
            GUI.DrawTexture(new Rect(x - 2, y - 2, 4, 4), Texture2D.whiteTexture);
        }

        GUI.color = prevColor;
    }

    /// <summary>
    /// Auto-bhop: perfectly timed jump when landing for maximum speed.
    /// Detects the exact frame the player touches ground and immediately jumps.
    /// </summary>
    private static void ApplyAutoBhop()
    {
        try
        {
            // If grounded and moving forward, auto-jump
            if (Controll.HLBAGIACGBI && !Controll.GCHFDAPNBNB)
            {
                var input = Controll.MNHBPCOOMLE;
                var isMoving = (input & 0x4u) != 0 || (input & 0x1u) != 0 || (input & 0x2u) != 0;
                if (isMoving)
                {
                    Controll.MNHBPCOOMLE |= 0x10u; // Set jump flag
                    Controll.GCHFDAPNBNB = true;
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Apply name changer: set GUIOptions.playername to custom name.
    /// </summary>
    private static void ApplyNameChanger()
    {
        try
        {
            var guiOptionsType = AccessTools.TypeByName("GUIOptions");
            if (guiOptionsType == null) return;
            var nameField = guiOptionsType.GetField("playername");
            if (nameField != null)
            {
                nameField.SetValue(null, customName);
            }
        }
        catch { }
    }

    /// <summary>
    /// Apply scoreboard hack: modify UIScores to show custom team scores.
    /// Sets both team scores to 999 to always show winning team.
    /// </summary>
    private static void ApplyScoreboardHack()
    {
        try
        {
            // Access UIScores via reflection
            var uiScoresType = AccessTools.TypeByName("UIScores");
            if (uiScoresType == null) return;

            // UIScores has a static instance field IOEOEOEJJOH at offset 0x0
            var instanceField = uiScoresType.GetField("IOEOEOEJJOH");
            if (instanceField == null) return;

            var instance = instanceField.GetValue(null);
            if (instance == null) return;

            // Set score fields
            var rdScoreField = uiScoresType.GetField("GNFNBOHPJKG");
            var blScoreField = uiScoresType.GetField("OIIKKGPPPLO");
            if (rdScoreField != null) rdScoreField.SetValue(instance, 999);
            if (blScoreField != null) blScoreField.SetValue(instance, 0);
        }
        catch { }
    }

    /// <summary>
    /// Apply XP/Gold hack: set GUIOptions.exp and GUIOptions.Gold to high values.
    /// These are static fields on the GUIOptions class.
    /// </summary>
    private static void ApplyXpGoldHack()
    {
        try
        {
            // Access static fields via reflection since GUIOptions may not be
            // directly accessible as an interop type
            var guiOptionsType = AccessTools.TypeByName("GUIOptions");
            if (guiOptionsType == null) return;

            var expField = guiOptionsType.GetField("exp");
            var goldField = guiOptionsType.GetField("Gold");
            var levelField = guiOptionsType.GetField("level");

            if (expField != null) expField.SetValue(null, 999999);
            if (goldField != null) goldField.SetValue(null, 999999);
            if (levelField != null) levelField.SetValue(null, 100);
        }
        catch { }
    }

    /// <summary>
    /// Reset all cheat features to disabled state.
    /// </summary>
    private static void ResetAllFeatures()
    {
        espEnabled = false;
        aimbotEnabled = false;
        autoShoot = false;
        noRecoil = false;
        infiniteHealth = false;
        infiniteAmmo = false;
        instantReload = false;
        rapidFire = false;
        bunnyHop = false;
        fovChanger = false;
        customCrosshair = false;
        speedHack = false;
        flyHack = false;
        gokuTp = false;
        gokuHasReturnPos = false;
        noClip = false;
        weaponUnlock = false;
        thirdPerson = false;
        chams = false;
        triggerbot = false;
        fullbright = false;
        antiFlash = false;
        wallhack = false;
        noSpread = false;
        fastFire = false;
        autoReload = false;
        nameEsp = false;
        spinbot = false;
        skeletonEsp = false;
        radarHack = false;
        antiAimPitch = false;
        autoStrafe = false;
        killFeed = false;
        edgeJump = false;
        fakeLag = false;
        spectatorWarning = false;
        damageIndicator = false;
        hitMarker = false;
        autoPickup = false;
        xpGoldHack = false;
        healthBarEsp = false;
        snaplines = false;
        threatIndicator = false;
        nameChanger = false;
        scoreboardHack = false;
        autoBhop = false;
        pingSpoof = false;
        footstepEsp = false;
        preFire = false;
        backtrack = false;
        adminUnlock = false;
        boxHeadEsp = false;
        slideHack = false;
        grenadeTrajectory = false;
        noFallDamage = false;
        crosshairCustom = false;
        killSound = false;
        aimbotSmoothing = false;
        fastWeaponSwitch = false;
        antiAimJitter = false;
        debugOverlay = false;
        autoAccept = false;
        distanceEsp = false;
        weaponIdEsp = false;
        chatSpammer = false;
        autoVoteYes = false;
        autoRevive = false;
        noSkybox = false;
        wireframePlayers = false;
        zoomHack = false;
        noMuzzleFlash = false;
        nightVision = false;
        noSmoke = false;
        autoSprint = false;
        noRain = false;
        thirdPersonShoulder = false;
        aimbotPrediction = false;
        autoCrouchIdle = false;
        fovFilterEsp = false;
        hitLog = false;
        autoWeaponSwap = false;
        screenCleaner = false;
        noShadows = false;
        playerList = false;
        noGrass = false;
        crosshairHitIndicator = false;
        timeScaleHack = false;
        noFog = false;
        aimEnemiesOnly = true;
        boneScan = false;
        radarMiniMap = false;
        autoReload = false;
        ghostBullets = false;
        showHealth = false;
        SaveConfig();
        instance?.Log.LogInfo("[Config] All features reset.");
    }
    private static float ParseFloat(string s, float fallback) => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static bool IsUsableCamera(Camera? camera)
    {
        return camera != null && camera.enabled && camera.gameObject.activeInHierarchy;
    }

    private static bool IsGameplayCamera(Camera? camera)
    {
        return camera != null && string.Equals(camera.name, "Camera", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludedCamera(Camera? camera)
    {
        return camera != null
            && (camera.name.IndexOf("Radar", StringComparison.OrdinalIgnoreCase) >= 0
                || camera.name.IndexOf("GUI", StringComparison.OrdinalIgnoreCase) >= 0
                || camera.name.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static Camera? ResolveCamera()
    {
        var controllerCamera = Controll.CDFACGAFFFH;
        if (IsUsableCamera(controllerCamera))
        {
            activeCamera = controllerCamera;
            return activeCamera;
        }

        if (IsUsableCamera(activeCamera) && IsGameplayCamera(activeCamera))
        {
            return activeCamera;
        }

        if (Time.unscaledTime < nextCameraSearchTime && IsUsableCamera(activeCamera) && !IsExcludedCamera(activeCamera))
        {
            return activeCamera;
        }

        nextCameraSearchTime = Time.unscaledTime + DiagnosticInterval;
        Camera? fallback = null;
        var mainCamera = Camera.main;
        if (IsUsableCamera(mainCamera) && !IsExcludedCamera(mainCamera))
        {
            fallback = mainCamera;
        }

        foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>())
        {
            if (!IsUsableCamera(camera))
            {
                continue;
            }

            if (debugLogging)
            {
                AsyncLog.Write($"[Diagnostics][Camera] name={camera.name}, enabled={camera.enabled}, active={camera.gameObject.activeInHierarchy}, depth={camera.depth}, pos={camera.transform.position}");
            }

            if (IsGameplayCamera(camera))
            {
                activeCamera = camera;
                return activeCamera;
            }

            if (!IsExcludedCamera(camera) && (fallback == null || camera.depth > fallback.depth))
            {
                fallback = camera;
            }
        }

        activeCamera = fallback;
        return activeCamera;
    }

    private static void DumpInventoryNow()
    {
        try
        {
            var all = GUIInv.OIHNJCKDOIG;
            if (all == null)
            {
                NetProbe.Note("weapon-dump: GUIInv.AllWeapons is null");
                instance?.Log.LogInfo("[WeaponDump] GUIInv.AllWeapons is null");
                return;
            }

            NetProbe.Note($"weapon-dump: AllWeapons count={all.Length}");
            instance?.Log.LogInfo($"[WeaponDump] AllWeapons count={all.Length}");

            for (var i = 0; i < all.Length; i++)
            {
                var w = all[i];
                if (w == null)
                {
                    continue;
                }

                var line = $"[WeaponDump] id={w.HAFMINBJCGN}, codename={w.OJEKKFDIKMG}, name={w.NGFDENOFBLK}";
                instance?.Log.LogInfo(line);
                NetProbe.Note($"weapon-dump: {line}");
            }

            var loadout = GUIInv.KNCJNHILDLJ;
            var loadoutCount = loadout?.Count ?? -1;
            NetProbe.Note($"weapon-dump: Loadout count={loadoutCount}");
            instance?.Log.LogInfo($"[WeaponDump] Loadout count={loadoutCount}");

            if (loadout != null)
            {
                for (var i = 0; i < loadout.Count; i++)
                {
                    var e = loadout[i];
                    if (e?.ADMGNABJBNM == null)
                    {
                        continue;
                    }

                    var line = $"[WeaponDump] loadout[{i}] uid={e.AIEPBAHGMJD}, weaponId={e.ADMGNABJBNM.HAFMINBJCGN}, codename={e.ADMGNABJBNM.OJEKKFDIKMG}, amount={e.NIBLMFFHJHK}";
                    instance?.Log.LogInfo(line);
                    NetProbe.Note($"weapon-dump: {line}");
                }
            }

            var cases = GUIInv.MMNCKDECLNA;
            var caseCount = cases?.Length ?? -1;
            NetProbe.Note($"weapon-dump: Cases count={caseCount}");
            instance?.Log.LogInfo($"[WeaponDump] Cases count={caseCount}");

            if (cases != null)
            {
                for (var i = 0; i < cases.Length; i++)
                {
                    var c = cases[i];
                    if (c == null)
                    {
                        continue;
                    }

                    var caseLine = $"[WeaponDump] case[{i}] id={c.LDKMPMIANCE}, codename={c.OJEKKFDIKMG}, name={c.NGFDENOFBLK}";
                    instance?.Log.LogInfo(caseLine);
                    NetProbe.Note($"weapon-dump: {caseLine}");

                    if (c.JFOEOEJLDML == null)
                    {
                        continue;
                    }

                    for (var j = 0; j < c.JFOEOEJLDML.Length; j++)
                    {
                        var w = c.JFOEOEJLDML[j];
                        if (w == null)
                        {
                            continue;
                        }

                        var line = $"[WeaponDump] case[{i}] weapon[{j}] id={w.HAFMINBJCGN}, codename={w.OJEKKFDIKMG}, name={w.NGFDENOFBLK}";
                        instance?.Log.LogInfo(line);
                        NetProbe.Note($"weapon-dump: {line}");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            if (!guiFailureLogged)
            {
                instance?.Log.LogWarning($"[WeaponDump] failed: {exception}");
                guiFailureLogged = true;
            }
        }
    }

    /// <summary>
    /// Populates GUIInv.LoadoutEntries (KNCJNHILDLJ) with a FPNENMKEFBB entry for every
    /// weapon in GUIInv.AllWeapons (OIHNJCKDOIG). This gives the player access to every
    /// weapon definition in the game client-side, bypassing the dead server's ownership
    /// validation. Only runs once per inventory-open session to avoid duplicate entries.
    /// </summary>
    private static void ApplyWeaponUnlock()
    {
        if (!weaponUnlock || weaponUnlockApplied)
        {
            return;
        }

        try
        {
            var allWeapons = GUIInv.OIHNJCKDOIG;
            if (allWeapons == null || allWeapons.Length == 0)
            {
                instance?.Log.LogInfo("[WeaponUnlock] AllWeapons is null/empty - inventory not loaded yet.");
                return;
            }

            var loadout = GUIInv.KNCJNHILDLJ;
            if (loadout == null)
            {
                instance?.Log.LogInfo("[WeaponUnlock] LoadoutEntries list is null - inventory not loaded yet.");
                return;
            }

            // Build a set of weapon IDs already in the loadout to avoid duplicates.
            var existing = new HashSet<ulong>();
            for (var i = 0; i < loadout.Count; i++)
            {
                var e = loadout[i];
                if (e?.ADMGNABJBNM != null)
                {
                    existing.Add((ulong)e.ADMGNABJBNM.HAFMINBJCGN);
                }
            }

            var added = 0;
            for (var i = 0; i < allWeapons.Length; i++)
            {
                var w = allWeapons[i];
                if (w == null)
                {
                    continue;
                }

                var wid = (ulong)w.HAFMINBJCGN;
                if (existing.Contains(wid))
                {
                    continue;
                }

                // Construct a new loadout entry: FPNENMKEFBB(ulong uniqueId, NAHLLMJMOED weaponData)
                // Use the weapon ID as the unique ID so entries are stable across re-runs.
                var entry = new FPNENMKEFBB(wid, w);
                loadout.Add(entry);
                existing.Add(wid);
                added++;
            }

            weaponUnlockApplied = true;
            weaponUnlockCount = added;
            instance?.Log.LogInfo($"[WeaponUnlock] Added {added} weapons to loadout. Total loadout entries: {loadout.Count}");
            NetProbe.Note($"weapon-unlock: added {added} weapons, total loadout={loadout.Count}");
        }
        catch (Exception exception)
        {
            if (!weaponUnlockFailureLogged)
            {
                instance?.Log.LogWarning($"[WeaponUnlock] failed: {exception}");
                weaponUnlockFailureLogged = true;
            }
        }
    }

    private static void LogInventoryDump()
    {
        if (Input.GetKeyDown(KeyCode.F9))
        {
            DumpInventoryNow();
        }

        // F10: Toggle continuous field probe (logs field changes every 2s)
        if (Input.GetKeyDown(KeyCode.F10))
        {
            FieldProbe.Toggle();
        }

        // F11: One-shot field probe snapshot
        if (Input.GetKeyDown(KeyCode.F11))
        {
            FieldProbe.ProbeAll();
        }

        // F6: Dump task-related GameObjects and UIMTasks state
        if (Input.GetKeyDown(KeyCode.F6))
        {
            DumpTaskState();
        }
    }

    /// <summary>
    /// Dump all task-related GameObjects and UIMTasks state to the log.
    /// Searches for GameObjects with "task", "mission", "daily", "quest",
    /// "challenge", "objective" in their name, and probes UIMTasks.cs.
    /// </summary>
    private static void DumpTaskState()
    {
        try
        {
            instance?.Log.LogInfo("[TaskProbe] === Searching for task-related GameObjects ===");

            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            if (allObjects != null)
            {
                var found = 0;
                foreach (var go in allObjects)
                {
                    if (go == null) continue;
                    var name = go.name;
                    if (string.IsNullOrEmpty(name)) continue;
                    var lower = name.ToLower();
                    if (lower.Contains("task") || lower.Contains("mission") || lower.Contains("daily") ||
                        lower.Contains("quest") || lower.Contains("challenge") || lower.Contains("objective") ||
                        lower.Contains("contract") || lower.Contains("goal"))
                    {
                        var path = go.name;
                        var parent = go.transform.parent;
                        while (parent != null)
                        {
                            path = parent.name + "/" + path;
                            parent = parent.parent;
                        }
                        var active = go.activeSelf ? "ACTIVE" : "INACTIVE";
                        instance?.Log.LogInfo($"[TaskProbe] GO: {path} [{active}]");

                        // Dump full hierarchy recursively
                        DumpTransformTree(go.transform, 1);
                        found++;
                    }
                }
                instance?.Log.LogInfo($"[TaskProbe] Found {found} task-related GameObjects (total {allObjects.Length} scanned)");
            }

            // Also dump all children of CanvasMenu to find task UI elements
            var canvasMenu = GameObject.Find("CanvasMenu");
            if (canvasMenu != null)
            {
                instance?.Log.LogInfo("[TaskProbe] === CanvasMenu children ===");
                DumpTransformTree(canvasMenu.transform, 0);
            }
        }
        catch (Exception e)
        {
            instance?.Log.LogError($"[TaskProbe] Error: {e.Message}");
        }
    }

    /// <summary>
    /// Recursively dump a Transform tree to the log with indentation.
    /// </summary>
    private static void DumpTransformTree(Transform t, int depth)
    {
        if (t == null) return;
        var indent = new string(' ', depth * 2);
        var active = t.gameObject.activeSelf ? "A" : "I";
        var info = $"{indent}{t.name} [{active}]";
        // Add component info
        var comps = t.GetComponents<Component>();
        if (comps != null)
        {
            var compNames = new System.Text.StringBuilder();
            foreach (var c in comps)
            {
                if (c != null) compNames.Append(c.GetType().Name).Append(" ");
            }
            if (compNames.Length > 0) info += $" <{compNames.ToString().Trim()}>";
        }
        instance?.Log.LogInfo($"[TaskProbe] {info}");

        for (var i = 0; i < t.childCount; i++)
        {
            DumpTransformTree(t.GetChild(i), depth + 1);
        }
    }

    private static void ScanAllWeaponIds()
    {
        try
        {
            const int MaxId = 1024;
            NetProbe.Note($"weapon-scan: scanning ids 0..{MaxId - 1}");
            instance?.Log.LogInfo($"[WeaponScan] scanning ids 0..{MaxId - 1}");

            var found = 0;
            for (var i = 0; i < MaxId; i++)
            {
                NAHLLMJMOED? w;
                try
                {
                    w = GUIInv.NJDNGJNPHNE(i);
                }
                catch
                {
                    continue;
                }

                if (w == null || string.IsNullOrEmpty(w.OJEKKFDIKMG))
                {
                    continue;
                }

                var line = $"[WeaponScan] id={w.HAFMINBJCGN}, codename={w.OJEKKFDIKMG}, name={w.NGFDENOFBLK}";
                instance?.Log.LogInfo(line);
                NetProbe.Note($"weapon-scan: {line}");
                found++;
            }

            NetProbe.Note($"weapon-scan: found {found} weapons");
            instance?.Log.LogInfo($"[WeaponScan] found {found} weapons");
        }
        catch (Exception exception)
        {
            if (!guiFailureLogged)
            {
                instance?.Log.LogWarning($"[WeaponScan] failed: {exception}");
                guiFailureLogged = true;
            }
        }
    }

    private static void GUIInvOnGUIPostfix()
    {
        // Only draw these when the inventory is actually open; OnGUI runs even when the UI is hidden.
        if (!GUIInv.CBFLNECJIFF)
        {
            inventoryDumped = false;
            weaponUnlockApplied = false; // reset so re-opening inventory re-applies
            return;
        }

        // Apply weapon unlock as early as possible when inventory opens.
        ApplyWeaponUnlock();

        // Draw dump/scan buttons on the inventory screen so they are available even if F9 does not register.
        if (GUI.Button(new Rect(Screen.width - 170, 10, 160, 32), "DUMP WEAPONS"))
        {
            DumpInventoryNow();
        }

        if (GUI.Button(new Rect(Screen.width - 170, 46, 160, 32), "SCAN ALL IDS"))
        {
            ScanAllWeaponIds();
        }

        // Auto-dump once each time the inventory opens. This is free; it only runs in the menu.
        if (!inventoryDumped)
        {
            inventoryDumped = true;
            DumpInventoryNow();
        }

        // OnGUI is called multiple times per frame (Layout, Repaint, ...).
        // Only act during the Repaint pass so F7/F9 are not double-triggered.
        if (Event.current is not { type: EventType.Repaint })
        {
            return;
        }

        NetProbe.Tick();
        LogInventoryDump();
    }

    private static void DumpAllPlayerWeapons()
    {
        if (Time.unscaledTime < nextPlayerWeaponDumpTime)
        {
            return;
        }

        nextPlayerWeaponDumpTime = Time.unscaledTime + 5f;

        try
        {
            var players = PLH.BAKLNPIEHMI;
            if (players == null)
            {
                return;
            }

            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player?.KPNAADPGNCP == null)
                {
                    continue;
                }

                foreach (var slot in player.KPNAADPGNCP)
                {
                    var weapon = slot?.ADMGNABJBNM;
                    var id = weapon?.HAFMINBJCGN ?? slot?.NIKINLIKGCP ?? -1;
                    if (id < 0)
                    {
                        continue;
                    }

                    var codename = weapon?.OJEKKFDIKMG;
                    var name = weapon?.NGFDENOFBLK;
                    NetProbe.DiscoverWeapon(id, codename ?? "unknown", name ?? $"Unknown {id}");
                }
            }
        }
        catch
        {
            // Discovery is best-effort; do not lag the game if it fails.
        }
    }

    /// <summary>
    /// Consume one unit of the per-tick IL2CPP read budget. Returns false once the tick's budget
    /// is spent, so a sweep stops mid-way and resumes on the next tick instead of blocking the
    /// frame until it finishes.
    /// </summary>
    private static bool DiagTake()
    {
        if (diagnosticReadsLeft <= 0)
        {
            return false;
        }

        diagnosticReadsLeft--;
        return true;
    }

    private static void LogRuntimeDiagnostics()
    {
        if (!debugLogging || Time.unscaledTime < nextDiagnosticTime)
        {
            return;
        }

        nextDiagnosticTime = Time.unscaledTime + DiagnosticInterval;
        diagnosticReadsLeft = DiagnosticReadBudget;
        try
        {
            var players = PLH.BAKLNPIEHMI;
            var mainPlayer = Controll.HGAODFPBGLB;
            // Deliberately the cached camera, not ResolveCamera(): that calls
            // FindObjectsOfType<Camera>(), a full scene scan, and running one per tick was part of
            // what turned this routine into a multi-second stall.
            var camera = activeCamera;
            AsyncLog.Write($"[Diagnostics] menu={menuVisible}, esp={espEnabled}, aimbot={aimbotEnabled}, aimMode={AimActivationLabels[aimActivationMode]}, aimStyle={AimStyleLabels[aimStyle]}, fovDegrees={aimbotFov:0}, noRecoil={noRecoil}, infiniteHealth={infiniteHealth}, infiniteAmmo={infiniteAmmo}, rapidFire={rapidFire}, leftMouse={Input.GetMouseButton(0)}, rightMouse={Input.GetMouseButton(1)}, players={(players == null ? "null" : players.Length.ToString())}, mainPlayer={(mainPlayer == null ? "null" : "present")}, camera={(camera == null ? "null" : camera.name)}, featureStatus={featureStatus}, aimStatus={aimStatus}, aimTargetIndex={lastAimTargetIndex}, aimTargetPos={lastAimTargetPosition}, recoilCalls={recoilCalls}, recoilSuppressed={recoilSuppressed}.");
            LogAmmoStatus(mainPlayer);
            if (heavyDiagnostics)
            {
                LogControllCandidates();
                LogPlayers(players, mainPlayer, camera);
            }
        }
        catch (Exception exception)
        {
            AsyncLog.Write($"[Diagnostics] Runtime inspection failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    // Re-querying reflection every second was the other half of the verbose-mode cost, on top of
    // the synchronous logging. The set never changes, so resolve it once.
    private static PropertyInfo[]? controllStatics;

    private static void LogControllCandidates()
    {
        controllStatics ??= typeof(Controll).GetProperties(BindingFlags.Public | BindingFlags.Static);

        foreach (var property in controllStatics)
        {
            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (!DiagTake())
            {
                return;
            }

            try
            {
                var value = property.GetValue(null);
                if (value != null && IsDiagnosticType(property.PropertyType))
                {
                    AsyncLog.Write($"[Diagnostics][Controll] {property.Name}={FormatDiagnosticValue(value)}");
                }
            }
            catch (Exception exception)
            {
                AsyncLog.Write($"[Diagnostics][Controll] {property.Name} failed: {exception.GetType().Name}");
            }
        }
    }

    private static bool IsDiagnosticType(Type type)
    {
        return type == typeof(float)
            || type == typeof(int)
            || type == typeof(bool)
            || type == typeof(Vector3)
            || type == typeof(Camera)
            || type == typeof(Transform)
            || type == typeof(KBBBHJDINCB);
    }

    private static string FormatDiagnosticValue(object value)
    {
        return value switch
        {
            Camera camera => $"Camera(name={camera.name},pos={camera.transform?.position.ToString() ?? "null"})",
            Transform transform => $"Transform(name={transform.name},pos={transform.position})",
            Vector3 vector => vector.ToString(),
            _ => value.ToString() ?? "<null>"
        };
    }

    private static void LogPlayers(
        Il2CppReferenceArray<KBBBHJDINCB>? players,
        KBBBHJDINCB? mainPlayer,
        Camera? camera)
    {
        if (players == null)
        {
            return;
        }

        var dumpFields = Time.unscaledTime >= nextPlayerFieldDumpTime;
        if (dumpFields)
        {
            nextPlayerFieldDumpTime = Time.unscaledTime + DiagnosticInterval * 3;
        }

        // Scanning all 40 players every tick was the single biggest cost. Walk a small rotating
        // window instead: the whole roster still gets covered, just spread over several ticks.
        var count = players.Length;
        if (count == 0)
        {
            return;
        }

        for (var offset = 0; offset < DiagnosticPlayerWindow && offset < count; offset++)
        {
            var index = (diagnosticPlayerCursor + offset) % count;
            if (!DiagTake())
            {
                break;
            }

            LogPlayer(index, players[index], mainPlayer, camera, dumpFields && offset == 0);
        }

        diagnosticPlayerCursor = (diagnosticPlayerCursor + DiagnosticPlayerWindow) % count;
    }

    private static void LogAmmoStatus(KBBBHJDINCB? mainPlayer)
    {
        if (mainPlayer == null)
        {
            return;
        }

        try
        {
            var weapon = mainPlayer.JPGGPPLOOML;
            var weaponId = weapon == null ? -1 : weapon.OCDNCKANJPB;
            var gd = mainPlayer.GDEMINMDJAC;
            var slot = mainPlayer.MOPBMENEGLN;
            var slotAmmo = gd == null || slot < 0 || slot >= gd.Length ? -1 : gd[slot];
            instance?.Log.LogInfo($"[Ammo] {nameof(Raw.KBBBHJDINCB.Offsets.MOPBMENEGLN)}={slot}, {nameof(Raw.KBBBHJDINCB.Offsets.ECBCOHFLJCC)}={mainPlayer.ECBCOHFLJCC}, {nameof(Raw.KBBBHJDINCB.Offsets.GDEMINMDJAC)}.Length={(gd == null ? -1 : gd.Length)}, {nameof(Raw.KBBBHJDINCB.Offsets.GDEMINMDJAC)}[{slot}]={slotAmmo}, weaponId={weaponId}, weaponNull={weapon == null}");
            instance?.Log.LogInfo($"[Health] HP={mainPlayer.FDOJDJLIGLF}, MaxHP={mainPlayer.EFHBKMHCMOH}, Armor={mainPlayer.INGHEHAALBJ}, CLOEJLAOIGI={mainPlayer.CLOEJLAOIGI}, CGHKKDBILGF={mainPlayer.CGHKKDBILGF}, LBKINNIDKEC={mainPlayer.LBKINNIDKEC}");
            // Log Controll static fields for reverse engineering.
            instance?.Log.LogInfo($"[Controll] FGGKANNFBDH={Controll.FGGKANNFBDH}, ILFOFIOFBAM={Controll.ILFOFIOFBAM}, KJOMABGHAIJ={Controll.KJOMABGHAIJ}, KEPGFOEOHPD={Controll.KEPGFOEOHPD}, HLBAGIACGBI={Controll.HLBAGIACGBI}, PBICPLCFAGG={Controll.PBICPLCFAGG}, NJPDKJKJMCG={Controll.NJPDKJKJMCG}, GCHFDAPNBNB={Controll.GCHFDAPNBNB}, BFEOOOMMGLK={Controll.BFEOOOMMGLK}, EKEAAHAKHIN={Controll.EKEAAHAKHIN}, DJACNOGOCKD={Controll.DJACNOGOCKD}, MJHNOEIFBEO={Controll.MJHNOEIFBEO}, HCOLPFEEENG={Controll.HCOLPFEEENG}, GLGCAOADGMN={Controll.GLGCAOADGMN}, CFACCGMPPOE={Controll.CFACCGMPPOE}, NKFBOBMMGCL={Controll.NKFBOBMMGCL}, DEBGAILDKPC={Controll.DEBGAILDKPC}, GKNJELHPMDE={Controll.GKNJELHPMDE}, POFKNJGAKPK={Controll.POFKNJGAKPK}, OGDPMIBJLDH={Controll.OGDPMIBJLDH}, MNHBPCOOMLE={Controll.MNHBPCOOMLE}");
            // Log player ammo candidates from the Player class.
            instance?.Log.LogInfo($"[PlayerAmmo] PELNEJDOBKH={mainPlayer.PELNEJDOBKH}, DECAKELAHPI={mainPlayer.DECAKELAHPI}, GEDMGLAMGMD={mainPlayer.GEDMGLAMGMD}, MHCOJFIAGLP={mainPlayer.MHCOJFIAGLP}, JHGGICCFNFJ={mainPlayer.JHGGICCFNFJ}, CNHNFDDJMJO={mainPlayer.CNHNFDDJMJO}, EGCCBDKJGAB={mainPlayer.EGCCBDKJGAB}, EEHMHJBNAFP={mainPlayer.EEHMHJBNAFP}, NGCMFJECPIO={mainPlayer.NGCMFJECPIO}, BCLPAILBBFP={mainPlayer.BCLPAILBBFP}");
        }
        catch
        {
            // Ammo logging is diagnostic only.
        }
    }

    private static void LogPlayer(int index, KBBBHJDINCB? player, KBBBHJDINCB? mainPlayer, Camera? camera, bool dumpFields)
    {
        if (player == null)
        {
            AsyncLog.Write($"[Diagnostics][Player] index={index}, value=null");
            return;
        }

        try
        {
            var head = player.ACEHIBLPHCA;
            var headTransform = head?.transform;
            var headPosition = headTransform?.position ?? Vector3.zero;
            var screenPosition = camera == null || headTransform == null
                ? Vector3.zero
                : camera.WorldToScreenPoint(headPosition);
            var isMain = player._LCEIAGLFFJN_k__BackingField;
            var sameTeam = mainPlayer != null && player.MMMGPDBMOLM == mainPlayer.MMMGPDBMOLM;
            AsyncLog.Write($"[Diagnostics][Player] index={index}, main={isMain}, team={player.MMMGPDBMOLM}, health={player.FDOJDJLIGLF}, sameTeam={sameTeam}, head={(head == null ? "null" : head.name)}, headPos={headPosition}, screenPos={screenPosition}");
            LogPlayerObjectCandidates(player, camera);
            if (dumpFields || isMain)
            {
                LogPlayerPrimitiveFields(player);
            }
        }
        catch (Exception exception)
        {
            AsyncLog.Write($"[Diagnostics][Player] index={index} failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void LogPlayerPrimitiveFields(KBBBHJDINCB player)
    {
        // This was re-enumerated per player per tick; the set never changes.
        playerPrimitiveProps ??= typeof(KBBBHJDINCB).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in playerPrimitiveProps)
        {
            if (property.GetIndexParameters().Length != 0 || !IsPlayerPrimitiveType(property.PropertyType))
            {
                continue;
            }

            if (!DiagTake())
            {
                return;
            }

            try
            {
                var value = property.GetValue(player);
                AsyncLog.Write($"[Diagnostics][PlayerField] property={property.Name}, type={property.PropertyType.Name}, value={(value == null ? "null" : FormatDiagnosticValue(value))}");
            }
            catch (Exception exception)
            {
                AsyncLog.Write($"[Diagnostics][PlayerField] property={property.Name} failed: {exception.GetType().Name}");
            }
        }
    }

    private static bool IsPlayerPrimitiveType(Type type)
    {
        return type == typeof(bool)
            || type == typeof(int)
            || type == typeof(float)
            || type == typeof(string)
            || type == typeof(Vector3);
    }

    private static void LogPlayerObjectCandidates(KBBBHJDINCB player, Camera? camera)
    {
        var candidates = new (string Name, GameObject? Value)[]
        {
            (nameof(Raw.KBBBHJDINCB.Offsets.LANBONKMIME), player.LANBONKMIME),
            (nameof(Raw.KBBBHJDINCB.Offsets.ACEHIBLPHCA), player.ACEHIBLPHCA),
            (nameof(Raw.KBBBHJDINCB.Offsets.JEFLHCHAABB), player.JEFLHCHAABB),
            (nameof(Raw.KBBBHJDINCB.Offsets.NMJKANFIDFM), player.NMJKANFIDFM),
            (nameof(Raw.KBBBHJDINCB.Offsets.JEKGMDMKFAG), player.JEKGMDMKFAG),
            (nameof(Raw.KBBBHJDINCB.Offsets.PLCCFFJNFPG), player.PLCCFFJNFPG),
            (nameof(Raw.KBBBHJDINCB.Offsets.ACFAMOFOOLB), player.ACFAMOFOOLB),
            (nameof(Raw.KBBBHJDINCB.Offsets.LNJODHNBFMN), player.LNJODHNBFMN),
            (nameof(Raw.KBBBHJDINCB.Offsets.OIDEDEHDLGA), player.OIDEDEHDLGA),
            (nameof(Raw.KBBBHJDINCB.Offsets.DEPIOGBOPIG), player.DEPIOGBOPIG),
            (nameof(Raw.KBBBHJDINCB.Offsets.HKDLHNJEKIO), player.HKDLHNJEKIO),
            (nameof(Raw.KBBBHJDINCB.Offsets.NCDOKAKJEJF), player.NCDOKAKJEJF)
        };

        foreach (var candidate in candidates)
        {
            if (candidate.Value == null)
            {
                continue;
            }

            var transform = candidate.Value.transform;
            var position = transform?.position ?? Vector3.zero;
            var screenPosition = camera == null || transform == null
                ? Vector3.zero
                : camera.WorldToScreenPoint(position);
            AsyncLog.Write($"[Diagnostics][Object] field={candidate.Name}, name={candidate.Value.name}, pos={position}, screenPos={screenPosition}");
        }
    }

    private static void UpdateAimbotSafely()
    {
        if (!aimbotEnabled)
        {
            aimStatus = "not active";
            return;
        }

        try
        {
            // Don't run the aimbot while the menu is open — the cursor is free for menu
            // interaction and we don't want auto-shoot firing into the wall.
            if (menuVisible)
            {
                aimStatus = "menu open";
                return;
            }

            if (!IsAimbotActivationPressed())
            {
                aimStatus = $"waiting for {AimActivationLabels[aimActivationMode]}";
                return;
            }

            var players = PLH.BAKLNPIEHMI;
            var mainPlayer = Controll.HGAODFPBGLB;
            var camera = ResolveCamera();
            if (players == null || mainPlayer == null || camera == null)
            {
                aimStatus = "waiting for game objects";
                return;
            }

            LogResolvedBindings(players, mainPlayer, camera);
            UpdateAimbot(players, mainPlayer, camera);
        }
        catch (Exception exception)
        {
            if (!featureFailureLogged)
            {
                AsyncLog.Write($"[Diagnostics] Aimbot update failed: {exception}");
                featureFailureLogged = true;
            }
        }
    }

    private static void UpdateEspSafely()
    {
        if (!espEnabled)
        {
            espBoxes.Clear();
            return;
        }

        try
        {
            var players = PLH.BAKLNPIEHMI;
            var mainPlayer = Controll.HGAODFPBGLB;
            var camera = ResolveCamera();
            if (players == null || mainPlayer == null || camera == null)
            {
                espBoxes.Clear();
                return;
            }

            UpdateEsp(players, mainPlayer, camera);
        }
        catch (Exception exception)
        {
            espBoxes.Clear();
            if (!featureFailureLogged)
            {
                AsyncLog.Write($"[Diagnostics] ESP update failed: {exception}");
                featureFailureLogged = true;
            }
        }
    }

    private static bool IsAimbotActivationPressed()
    {
        return aimActivationMode switch
        {
            0 => Input.GetMouseButton(0),
            1 => Input.GetMouseButton(1),
            2 => Input.GetKey(KeyCode.LeftAlt),
            3 => Input.GetKey(KeyCode.Mouse3),
            4 => true, // Always on
            _ => false
        };
    }

    private static void LogResolvedBindings(
        Il2CppReferenceArray<KBBBHJDINCB> players,
        KBBBHJDINCB mainPlayer,
        Camera camera)
    {
        if (bindingsLogged)
        {
            return;
        }

        instance?.Log.LogInfo($"Resolved player bindings: count={players.Length}, camera={camera.name}, mainPlayer={mainPlayer._LCEIAGLFFJN_k__BackingField}.");
        bindingsLogged = true;
    }

    private static void UpdateEsp(
        Il2CppReferenceArray<KBBBHJDINCB> players,
        KBBBHJDINCB mainPlayer,
        Camera camera)
    {
        espBoxes.Clear();
        for (var index = 0; index < players.Length; index++)
        {
            var player = players[index];
            if (!IsVisibleTarget(player, mainPlayer, showTeammates, true))
            {
                continue;
            }

            if (TryCreateEspBox(player, mainPlayer, camera, out var box))
            {
                // FOV filter: only show enemies within the aimbot FOV
                if (fovFilterEsp && box.IsEnemy)
                {
                    var screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
                    var screenPos = new Vector2(box.ScreenPos.x, box.ScreenPos.y);
                    var distFromCenter = Vector2.Distance(screenCenter, screenPos);
                    var screenFovRadius = aimbotFov * Screen.height / 90f;
                    if (distFromCenter > screenFovRadius) continue;
                }
                espBoxes.Add(box);
            }
        }

        featureStatus = $"players={players.Length}, boxes={espBoxes.Count}";
    }

    private static bool IsVisibleTarget(KBBBHJDINCB? player, KBBBHJDINCB mainPlayer, bool includeTeammates, bool ignoreSpawnProtection)
    {
        return player != null
            && !player._LCEIAGLFFJN_k__BackingField
            && player.FDOJDJLIGLF > 0
            && (ignoreSpawnProtection || !player.LBKINNIDKEC)
            && (includeTeammates || player.MMMGPDBMOLM != mainPlayer.MMMGPDBMOLM);
    }

    private static bool TryCreateEspBox(
        KBBBHJDINCB player,
        KBBBHJDINCB mainPlayer,
        Camera camera,
        out EspBox box)
    {
        box = default;
        var head = player.ACEHIBLPHCA;
        if (head == null || head.transform == null)
        {
            return false;
        }

        var position = head.transform.position;
        var top = camera.WorldToScreenPoint(position + Vector3.up);
        var bottom = camera.WorldToScreenPoint(position - Vector3.up);
        if (top.z <= 0 || bottom.z <= 0)
        {
            return false;
        }

        var screenHeight = Screen.height;
        var topY = screenHeight - top.y;
        var bottomY = screenHeight - bottom.y;
        var height = Mathf.Abs(bottomY - topY);
        if (height < 4)
        {
            return false;
        }

        var width = height * 0.45f;
        var isEnemy = player.MMMGPDBMOLM != mainPlayer.MMMGPDBMOLM;
        var color = isEnemy ? Color.red : Color.green;
        var dist = Vector3.Distance(mainPlayer.OOMJGHCFODI, position);
        var screenPos = new Vector2(top.x, screenHeight - top.y);
        var playerName = player.NHHBNNBDDIA ?? "?";
        box = new EspBox(new Rect(top.x - width / 2, Mathf.Min(topY, bottomY), width, height), color, player.FDOJDJLIGLF, screenPos, dist, isEnemy, playerName);
        return true;
    }

    private static void UpdateAimbot(
        Il2CppReferenceArray<KBBBHJDINCB> players,
        KBBBHJDINCB mainPlayer,
        Camera camera)
    {
        var bestAngle = aimbotFov;
        var bestPosition = Vector3.zero;
        var foundTarget = false;
        lastAimTargetIndex = -1;
        lastAimTargetPosition = Vector3.zero;

        // Ghost bullets: ignore line of sight — the server trusts client-authored hits,
        // so we can kill targets through walls by sending fake 0x04/0x06 packets.
        var requireLos = !ghostBullets;

        for (var index = 0; index < players.Length; index++)
        {
            var player = players[index];
            if (!IsVisibleTarget(player, mainPlayer, !aimEnemiesOnly, false) || !TryGetHeadPosition(player, out var headPosition))
            {
                continue;
            }

            var direction = headPosition - camera.transform.position;
            var angle = Vector3.Angle(camera.transform.forward, direction);
            if (angle <= bestAngle && (!requireLos || HasLineOfSight(camera, player, headPosition)))
            {
                // Aimbot prediction: lead moving targets
                if (aimbotPrediction)
                {
                    var currentPos = player.OOMJGHCFODI;
                    if (lastEnemyVelocities.TryGetValue(index, out var lastPos))
                    {
                        // Estimate velocity from position delta (approximate frame time)
                        var velocity = (currentPos - lastPos) / Time.deltaTime;
                        // Predict where target will be in ~0.1s (bullet travel time approx)
                        headPosition += velocity * 0.1f;
                    }
                    lastEnemyVelocities[index] = currentPos;
                }

                bestAngle = angle;
                bestPosition = headPosition;
                lastAimTargetIndex = index;
                lastAimTargetPosition = headPosition;
                foundTarget = true;
            }
        }

        if (!foundTarget)
        {
            aimStatus = "no visible target in radius";
            return;
        }

        var target = players[lastAimTargetIndex];

        // Ghost bullets: send fake hit packets through walls. This is independent of aim
        // style — it works with plain aim, silent aim, or even no aim style at all.
        if (ghostBullets)
        {
            TryGhostBullet(target, bestPosition, camera);
        }

        // Silent aim: redirect the shot to the target without moving the camera.
        // 1. Raycast from camera to target to confirm the shot can reach (not through walls,
        //    unless ghost bullets is on)
        // 2. Save the player's real aim angles, redirect to target
        // 3. Controll.Update fires the shot at the target
        // 4. Postfix restores the real angles + mouse delta (so the camera doesn't lock)
        if (aimStyle == 1)
        {
            // Raycast check — confirm we can actually hit the target. Skip if ghost bullets
            // is on (that's the whole point of ghost bullets — shoot through walls).
            if (!ghostBullets && !HasLineOfSight(camera, target, bestPosition))
            {
                aimStatus = $"silent: no line of sight to target={lastAimTargetIndex}";
                return;
            }

            // Save real angles and redirect to target. This also updates the camera
            // rotation so the fire raycast goes toward the target.
            SaveAndRedirectAim(camera, bestPosition);

            // Auto-shoot: set autoShootPending. The postfix will send mouse_event(LEFTDOWN)
            // which arrives NEXT frame. Next frame, Unity polls input → GetMouseButton(0)=true
            // → Controll.Update fires through its own full code path (raycast + hit packet)
            // at the redirected angles (silent aim prefix runs again next frame).
            if (Application.isFocused && mainPlayer.JPGGPPLOOML != null)
            {
                autoShootPending = true;
            }
            aimStatus = $"silent target={lastAimTargetIndex}, angle={bestAngle:0.0} degrees";
            return;
        }

        var targetDirection = bestPosition - camera.transform.position;
        var targetRotation = Quaternion.LookRotation(targetDirection);
        ApplyAimRotation(camera, targetRotation);
        TryAutoShoot(target, bestPosition, camera);
        aimStatus = $"target={lastAimTargetIndex}, angle={bestAngle:0.0} degrees";
    }

    /// <summary>
    /// Save the player's real aim angles, then redirect to the target. Controll.Update
    /// (running after this prefix) will fire at the target. RestoreSilentAim() in the
    /// postfix restores the real angles plus whatever mouse delta Update applied, so
    /// the player's mouse movement is preserved and the camera never locks.
    /// </summary>
    private static void SaveAndRedirectAim(Camera camera, Vector3 targetPosition)
    {
        savedYaw = Controll.NAKNALFCOIF;
        savedPitch = Controll.IGLCENGMMMJ;
        savedCameraRot = camera.transform.rotation;
        silentAimRedirected = true;

        var targetDirection = targetPosition - camera.transform.position;
        var targetRotation = Quaternion.LookRotation(targetDirection);
        var targetAngles = targetRotation.eulerAngles;
        targetYaw = targetAngles.y;
        targetPitch = Mathf.Clamp(NormalizeAngle(targetAngles.x), -89f, 89f);

        // Apply aimbot smoothing if enabled: interpolate toward target
        if (aimbotSmoothing && aimbotSmoothFactor > 0f && aimbotSmoothFactor < 1f)
        {
            var smooth = aimbotSmoothFactor;
            targetYaw = Mathf.LerpAngle(Controll.NAKNALFCOIF, targetYaw, smooth);
            targetPitch = Mathf.Lerp(Controll.IGLCENGMMMJ, targetPitch, smooth);
        }

        Controll.NAKNALFCOIF = targetYaw;
        Controll.IGLCENGMMMJ = targetPitch;

        // Update the camera rotation NOW so PLH.CDEGJOBLOFO's raycast goes toward
        // the target. Without this, the camera still points the old way (Controll.Update
        // hasn't run yet to sync camera from angles) and the shot misses.
        camera.transform.rotation = Quaternion.Euler(targetPitch, targetYaw, 0f);
    }

    /// <summary>
    /// Restore the real aim angles and camera rotation after Controll.Update has fired.
    /// Preserves the mouse delta: the difference between the angles now and the target
    /// angles is the mouse movement, which we add back to the saved real angles.
    /// Also restores the camera rotation so the player never sees the snap.
    /// </summary>
    private static void RestoreSilentAim()
    {
        if (!silentAimRedirected)
        {
            return;
        }

        // Mouse delta = how far the angles moved from the target during Update.
        var deltaYaw = Controll.NAKNALFCOIF - targetYaw;
        var deltaPitch = Controll.IGLCENGMMMJ - targetPitch;

        // Restore real angles + mouse delta.
        var restoredYaw = savedYaw + deltaYaw;
        var restoredPitch = Mathf.Clamp(savedPitch + deltaPitch, -89f, 89f);
        Controll.NAKNALFCOIF = restoredYaw;
        Controll.IGLCENGMMMJ = restoredPitch;

        // Restore camera rotation to match the real angles, undoing the snap that
        // Controll.Update applied when it read the redirected angles.
        var camera = Controll.CDFACGAFFFH;
        if (camera != null && camera.transform != null)
        {
            camera.transform.rotation = Quaternion.Euler(restoredPitch, restoredYaw, 0f);
        }

        silentAimRedirected = false;
    }

    /// <summary>
    /// Ghost bullets: send fake 0x04 hit report + 0x06 damage triple for the target,
    /// ignoring line of sight. The server trusts client-authored hits, so the target
    /// dies even through walls. Fires at a controlled rate to avoid flooding the server.
    /// </summary>
    private static void TryGhostBullet(KBBBHJDINCB target, Vector3 targetPosition, Camera camera)
    {
        if (Time.unscaledTime < nextGhostBulletTime)
        {
            return;
        }

        if (!Application.isFocused)
        {
            return;
        }

        // Only fire ghost bullets when the player is actually holding the fire key,
        // unless auto-shoot is on.
        if (!autoShoot && !Input.GetMouseButton(0))
        {
            return;
        }

        var origin = camera.transform.position;
        if (NetProbe.TryFakeHit(target, origin, targetPosition, 100))
        {
            nextGhostBulletTime = Time.unscaledTime + 0.12f;
            aimStatus = $"{aimStatus} | ghost bullet";
        }
    }

    private static void ApplyAimRotation(Camera camera, Quaternion targetRotation)
    {
        // Disassembly of Controll.UpdateInput shows these two static floats are the
        // mouse-driven aim angles: NAKNALFCOIF (yaw / Mouse X) and IGLCENGMMMJ
        // (pitch / Mouse Y). Setting them here, before Controll.Update consumes them,
        // makes both the camera and server-side aim track the target and prevents the
        // snap-back that occurs when only the camera transform is overwritten.
        var targetAngles = targetRotation.eulerAngles;
        var pitch = NormalizeAngle(targetAngles.x);
        pitch = Mathf.Clamp(pitch, -89f, 89f);

        Controll.NAKNALFCOIF = targetAngles.y;
        Controll.IGLCENGMMMJ = pitch;
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > 180f)
        {
            angle -= 360f;
        }
        while (angle < -180f)
        {
            angle += 360f;
        }
        return angle;
    }

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    private const uint KeyEventFKeyDown = 0x00;
    private const uint KeyEventFKeyUp = 0x02;
    private const byte VkSpace = 0x20;

    private const uint MouseEventFLeftDown = 0x02;
    private const uint MouseEventFLeftUp = 0x04;

    private static void TryAutoShoot(KBBBHJDINCB? target, Vector3 targetPosition, Camera camera)
    {
        if (!autoShoot)
        {
            return;
        }

        if (!Application.isFocused)
        {
            return;
        }

        // Experimental server-trust test: skip the actual shot and tell the server we hit the
        // aimbot target. If the server accepts client-authored hits, the target dies anyway.
        if (serverTrustTest && target != null)
        {
            if (Time.unscaledTime < nextAutoShootTime)
            {
                return;
            }
            var origin = camera.transform.position;
            if (NetProbe.TryFakeHit(target, origin, targetPosition, 1000))
            {
                nextAutoShootTime = Time.unscaledTime + 0.12f;
                aimStatus = $"{aimStatus} | fake-hit sent";
            }
            return;
        }

        // Auto-shoot: set autoShootPending. Postfix sends mouse_event(LEFTDOWN) which
        // arrives next frame → GetMouseButton(0)=true → game fires at aimed target.
        var main = Controll.HGAODFPBGLB;
        if (main == null || main.JPGGPPLOOML == null)
        {
            return;
        }

        autoShootPending = true;
        aimStatus = $"{aimStatus} | auto-shoot";
    }

    private static bool TryGetHeadPosition(KBBBHJDINCB player, out Vector3 position)
    {
        position = Vector3.zero;
        var head = player.ACEHIBLPHCA;
        if (head == null || head.transform == null)
        {
            return false;
        }

        // Aim bone selector: 0=head, 1=chest, 2=pelvis
        // For chest/pelvis, offset from head position
        var basePosition = head.transform.position;

        // Try to get more accurate position from collider/renderer
        var collider = head.GetComponentInChildren<Collider>();
        if (collider != null)
        {
            basePosition = collider.bounds.center;
        }
        else
        {
            var renderer = head.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                basePosition = renderer.bounds.center;
            }
        }

        // Apply bone offset
        switch (aimBone)
        {
            case 0: // Head - use position as-is
                position = basePosition;
                break;
            case 1: // Chest - offset down by ~0.5m
                position = basePosition - Vector3.up * 0.5f;
                break;
            case 2: // Pelvis - offset down by ~1.0m
                position = basePosition - Vector3.up * 1.0f;
                break;
            default:
                position = basePosition;
                break;
        }

        // Bone scan: if enabled, try alternate bones when primary is blocked
        if (boneScan)
        {
            var camera = ResolveCamera();
            if (camera != null)
            {
                var bones = new[] { basePosition, basePosition - Vector3.up * 0.5f, basePosition - Vector3.up * 1.0f, basePosition - Vector3.up * 1.5f };
                foreach (var bonePos in bones)
                {
                    var dir = bonePos - camera.transform.position;
                    if (Physics.Raycast(camera.transform.position, dir.normalized, out _, dir.magnitude))
                    {
                        // Hit something - this bone is blocked, try next
                        continue;
                    }
                    // This bone has LOS - use it
                    position = bonePos;
                    break;
                }
            }
        }

        return true;
    }

    private static bool TryGetPlayerRoot(KBBBHJDINCB player, out GameObject root)
    {
        root = player.LANBONKMIME;
        return root != null;
    }

    private static bool HasLineOfSight(Camera camera, KBBBHJDINCB player, Vector3 target)
    {
        var head = player.ACEHIBLPHCA;
        var direction = target - camera.transform.position;
        var distance = direction.magnitude;
        if (head == null || head.transform == null || distance <= 0)
        {
            return false;
        }

        if (!Physics.Raycast(camera.transform.position, direction.normalized, out var hit, distance))
        {
            return true;
        }

        var hitTransform = hit.collider?.transform;
        return hitTransform != null
            && (hitTransform == head.transform
                || hitTransform.IsChildOf(head.transform)
                || head.transform.IsChildOf(hitTransform));
    }

    private static bool SetRecoilPrefix(ref float __1)
    {
        recoilCalls++;
        if (noRecoil)
        {
            recoilSuppressed++;
            // Zero the recoil force instead of skipping the whole method.
            // Skipping can drop network state because the method may also update server-side weapon data.
            __1 = 0f;
        }

        return true;
    }

    private static void ControllerOnGUIPostfix()
    {
        if (!menuVisible && !espEnabled && !customCrosshair)
        {
            return;
        }

        try
        {
            if (espEnabled)
            {
                UpdateEspSafely();
                DrawEspBoxes();
            }

            if (aimbotEnabled && !menuVisible)
            {
                DrawAimbotFovCircle();
            }

            if (customCrosshair && !menuVisible)
            {
                DrawCustomCrosshair();
            }

            // Feature watermark: show active features in top-left corner
            if (!menuVisible)
            {
                DrawFeatureWatermark();
            }

            // Skeleton ESP: draw bone connections on player models
            if (skeletonEsp && !menuVisible)
            {
                DrawSkeletonEsp();
            }

            // Radar hack: draw mini-radar showing all player positions
            if (radarHack && !menuVisible)
            {
                DrawRadarHack();
            }

            // Kill feed: show recent kills on screen
            if (killFeed && !menuVisible)
            {
                DrawKillFeed();
            }

            // Spectator warning: alert when being spectated
            if (spectatorWarning && !menuVisible)
            {
                DrawSpectatorWarning();
            }

            // Damage indicator: red vignette when taking damage
            if (damageIndicator && !menuVisible)
            {
                DrawDamageIndicator();
            }

            // Hit marker: X at crosshair when hitting enemy
            if (hitMarker && !menuVisible)
            {
                DrawHitMarker();
            }

            // Health bar ESP: draw health bars above players
            if (healthBarEsp && !menuVisible)
            {
                DrawHealthBarEsp();
            }

            // Snaplines: lines from bottom of screen to enemies
            if (snaplines && !menuVisible)
            {
                DrawSnaplines();
            }

            // Threat indicator: arrow pointing to closest enemy
            if (threatIndicator && !menuVisible)
            {
                DrawThreatIndicator();
            }

            // Ping spoof: display fake ping in bottom-right corner
            if (pingSpoof && !menuVisible)
            {
                var prevColor = GUI.color;
                GUI.color = new Color(1f, 1f, 0f, 0.8f);
                GUI.Label(new Rect(Screen.width - 100, Screen.height - 30, 90, 24), $"Ping: {fakePing}ms");
                GUI.color = prevColor;
            }

            // Footstep ESP: show recent enemy positions as fading dots
            if (footstepEsp && !menuVisible)
            {
                DrawFootstepEsp();
            }

            // Backtrack: show past enemy positions as blue dots
            if (backtrack && !menuVisible)
            {
                DrawBacktrack();
            }

            // Box-head ESP: detailed box around head + body
            if (boxHeadEsp && !menuVisible)
            {
                DrawBoxHeadEsp();
            }

            // Grenade trajectory: show predicted throw arc
            if (grenadeTrajectory && !menuVisible)
            {
                DrawGrenadeTrajectory();
            }

            // Custom crosshair v2: configurable color/size/thickness
            if (crosshairCustom && !menuVisible)
            {
                DrawCustomCrosshairV2();
            }

            // Debug overlay: diagnostic info in top-right corner
            if (debugOverlay && !menuVisible)
            {
                DrawDebugOverlay();
            }

            // Player list: show all players with info
            if (playerList && !menuVisible)
            {
                DrawPlayerList();
            }

            // Crosshair hit indicator: red dot when aiming at enemy
            if (crosshairHitIndicator && !menuVisible)
            {
                DrawCrosshairHitIndicator();
            }

            // Radar mini-map: show enemy positions
            if (radarMiniMap && !menuVisible)
            {
                DrawRadarMiniMap();
            }

            // Distance ESP: show distance below enemy boxes
            if (distanceEsp && !menuVisible)
            {
                DrawDistanceEsp();
            }

            // Weapon ID ESP: show enemy weapon IDs
            if (weaponIdEsp && !menuVisible)
            {
                DrawWeaponIdEsp();
            }

            if (menuVisible)
            {
                DrawTrainerMenu();
            }
        }
        catch (Exception exception)
        {
            if (!guiFailureLogged)
            {
                AsyncLog.Write($"[Diagnostics] Trainer GUI draw failed: {exception}");
                guiFailureLogged = true;
            }
        }
    }

    private static void DrawCustomCrosshair()
    {
        var cx = Screen.width / 2;
        var cy = Screen.height / 2;
        var color = new Color(0f, 1f, 0f, 0.8f);
        var thickness = 2;
        var gap = 6;
        var length = 8;

        // Draw 4 lines (top, bottom, left, right) + center dot
        DrawLine(cx, cy - gap - length, cx, cy - gap, thickness, color); // top
        DrawLine(cx, cy + gap, cx, cy + gap + length, thickness, color); // bottom
        DrawLine(cx - gap - length, cy, cx - gap, cy, thickness, color); // left
        DrawLine(cx + gap, cy, cx + gap + length, cy, thickness, color); // right
        DrawLine(cx - 1, cy, cx + 1, cy, 2, color); // center dot h
        DrawLine(cx, cy - 1, cx, cy + 1, 2, color); // center dot v
    }

    private static void DrawLine(int x1, int y1, int x2, int y2, int thickness, Color color)
    {
        var minX = Math.Min(x1, x2);
        var minY = Math.Min(y1, y2);
        var maxX = Math.Max(x1, x2);
        var maxY = Math.Max(y1, y2);
        var w = Math.Max(maxX - minX, thickness);
        var h = Math.Max(maxY - minY, thickness);
        var tex = Texture2D.whiteTexture;
        var prevColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(minX, minY, w, h), tex);
        GUI.color = prevColor;
    }

    private static void DrawTrainerMenu()
    {
        // Clamp menu position to screen bounds so it's always visible.
        menuRect.x = Mathf.Clamp(menuRect.x, 0, Mathf.Max(0, Screen.width - menuRect.width));
        menuRect.y = Mathf.Clamp(menuRect.y, 0, Mathf.Max(0, Screen.height - 100));

        // Fixed-size window with header + tab bar + scrollable content.
        GUI.Box(menuRect, "Blockpost Legacy Trainer");
        var headerRect = new Rect(menuRect.x, menuRect.y, menuRect.width, 24);
        GUI.Label(new Rect(menuRect.x + 8, menuRect.y + 2, menuRect.width - 16, 20), "Blockpost Legacy Trainer (drag)");

        // Dragging
        var mousePos = Event.current.mousePosition;
        if (Event.current.type == EventType.MouseDown && headerRect.Contains(mousePos))
        {
            menuDragging = true;
            menuDragOffset = mousePos - new Vector2(menuRect.x, menuRect.y);
        }
        else if (Event.current.type == EventType.MouseUp)
        {
            menuDragging = false;
        }
        else if (Event.current.type == EventType.MouseDrag && menuDragging)
        {
            menuRect.x = mousePos.x - menuDragOffset.x;
            menuRect.y = mousePos.y - menuDragOffset.y;
        }

        // Tab bar
        var tabY = menuRect.y + 28;
        var tabW = menuRect.width / MenuTabLabels.Length;
        for (var i = 0; i < MenuTabLabels.Length; i++)
        {
            var tabRect = new Rect(menuRect.x + i * tabW, tabY, tabW, 24);
            var prev = GUI.color;
            if (i == menuTab) GUI.color = new Color(0.6f, 0.8f, 1f, 1f);
            if (GUI.Button(tabRect, MenuTabLabels[i]))
            {
                menuTab = i;
                menuScroll = Vector2.zero;
            }
            GUI.color = prev;
        }

        // Scrollable content area below the tab bar
        var contentX = menuRect.x + 10;
        var contentY = tabY + 28;
        var contentW = menuRect.width - 20;
        var contentH = menuRect.height - (contentY - menuRect.y) - 10;
        var viewRect = new Rect(contentX, contentY, contentW, contentH);

        // Estimate inner height per tab (will be set by each tab method)
        var innerH = EstimateTabHeight(menuTab);
        var innerRect = new Rect(0, 0, contentW - 20, innerH);

        menuScroll = GUI.BeginScrollView(viewRect, menuScroll, innerRect);
        var x = 4f;
        var y = 4f;
        var w = innerRect.width - 8;

        switch (menuTab)
        {
            case 0: DrawCombatTab(x, ref y, w); break;
            case 1: DrawEspTab(x, ref y, w); break;
            case 2: DrawMovementTab(x, ref y, w); break;
            case 3: DrawWeaponsTab(x, ref y, w); break;
            case 4: DrawMiscTab(x, ref y, w); break;
            case 5: DrawConfigTab(x, ref y, w); break;
        }

        GUI.EndScrollView();

        // Auto-save config whenever any menu control was interacted with.
        if (GUI.changed)
        {
            SaveConfig();
        }
    }

    /// <summary>
    /// Rough height estimate for each tab so the scroll view knows the content size.
    /// Over-estimating is fine; under-estimating clips content.
    /// </summary>
    private static float EstimateTabHeight(int tab)
    {
        return tab switch
        {
            0 => 1100f,  // Combat
            1 => 1400f,  // ESP/Visual
            2 => 700f,   // Movement
            3 => 700f,   // Weapons
            4 => 600f,   // Misc
            5 => 400f,   // Config
            _ => 400f,
        };
    }

    // ---- Tab: Combat ----
    private static void DrawCombatTab(float x, ref float y, float w)
    {
        GUI.Label(new Rect(x, y, w, 24), "--- Aimbot ---"); y += 26;
        aimbotEnabled = GUI.Toggle(new Rect(x, y, w, 24), aimbotEnabled, "Aimbot"); y += 26;
        if (aimbotEnabled)
        {
            if (GUI.Button(new Rect(x, y, w, 28), $"Aim key: {AimActivationLabels[aimActivationMode]}"))
            {
                aimActivationMode = (aimActivationMode + 1) % AimActivationLabels.Length;
            }
            y += 32;
            if (GUI.Button(new Rect(x, y, w, 28), $"Aim style: {AimStyleLabels[aimStyle]}"))
            {
                aimStyle = (aimStyle + 1) % AimStyleLabels.Length;
            }
            y += 32;
            GUI.Label(new Rect(x, y, w, 24), $"Aimbot FOV: {aimbotFov:0} deg"); y += 24;
            aimbotFov = GUI.HorizontalSlider(new Rect(x, y, w, 24), aimbotFov, MinimumAimbotFov, MaximumAimbotFov); y += 28;
            autoShoot = GUI.Toggle(new Rect(x, y, w, 24), autoShoot, "Auto shoot (Win32 input)"); y += 26;
            if (autoShoot)
            {
                rapidFire = GUI.Toggle(new Rect(x + 20, y, w - 20, 24), rapidFire, "Rapid fire (1 shot/tick)"); y += 26;
            }
            ghostBullets = GUI.Toggle(new Rect(x + 20, y, w - 20, 24), ghostBullets, "Ghost bullets (through walls)"); y += 26;
            if (aimStyle == 0 && !ghostBullets)
            {
                serverTrustTest = GUI.Toggle(new Rect(x + 20, y, w - 20, 24), serverTrustTest, "Server trust test"); y += 26;
            }
            aimbotSmoothing = GUI.Toggle(new Rect(x, y, w, 24), aimbotSmoothing, "Aimbot smoothing"); y += 26;
            if (aimbotSmoothing)
            {
                GUI.Label(new Rect(x, y, 60, 24), "Smooth:");
                aimbotSmoothFactor = GUI.HorizontalSlider(new Rect(x + 60, y, 120, 24), aimbotSmoothFactor, 0.05f, 1f);
                y += 26;
            }
            aimbotPrediction = GUI.Toggle(new Rect(x, y, w, 24), aimbotPrediction, "Aimbot prediction (lead targets)"); y += 26;
            aimEnemiesOnly = GUI.Toggle(new Rect(x, y, w, 24), aimEnemiesOnly, "Aim enemies only (ignore allies)"); y += 26;
            boneScan = GUI.Toggle(new Rect(x, y, w, 24), boneScan, "Bone scan (try alt bones if blocked)"); y += 26;
            GUI.Label(new Rect(x, y, 80, 24), "Aim bone:"); y += 26;
            for (var i = 0; i < AimBoneLabels.Length; i++)
            {
                if (GUI.Toggle(new Rect(x + i * 90, y, 90, 24), aimBone == i, AimBoneLabels[i]))
                {
                    aimBone = i;
                }
            }
            y += 26;
        }

        GUI.Label(new Rect(x, y, w, 24), "--- Trigger/Fire ---"); y += 26;
        triggerbot = GUI.Toggle(new Rect(x, y, w, 24), triggerbot, "Triggerbot (auto-fire on crosshair)"); y += 26;
        if (triggerbot)
        {
            GUI.Label(new Rect(x, y, w, 20), $"Range: {triggerbotRange:0}m");
            triggerbotRange = GUI.HorizontalSlider(new Rect(x + 80, y + 4, w - 80, 20), triggerbotRange, 50f, 500f);
            y += 26;
        }
        preFire = GUI.Toggle(new Rect(x, y, w, 24), preFire, "Pre-fire (auto-fire at close range)"); y += 26;
        noRecoil = GUI.Toggle(new Rect(x, y, w, 24), noRecoil, "No recoil"); y += 26;
        noSpread = GUI.Toggle(new Rect(x, y, w, 24), noSpread, "No spread (zero recoil accumulator)"); y += 26;
        fastFire = GUI.Toggle(new Rect(x, y, w, 24), fastFire, "Fast fire rate (zero fire timer)"); y += 26;

        GUI.Label(new Rect(x, y, w, 24), "--- Anti-Aim ---"); y += 26;
        spinbot = GUI.Toggle(new Rect(x, y, w, 24), spinbot, "Spinbot (anti-aim yaw spin)"); y += 26;
        antiAimPitch = GUI.Toggle(new Rect(x, y, w, 24), antiAimPitch, "Anti-aim pitch (fake look up/down)"); y += 26;
        antiAimJitter = GUI.Toggle(new Rect(x, y, w, 24), antiAimJitter, "Anti-aim jitter (random yaw)"); y += 26;
        autoStrafe = GUI.Toggle(new Rect(x, y, w, 24), autoStrafe, "Auto-strafe (dodge pattern)"); y += 26;
        fakeLag = GUI.Toggle(new Rect(x, y, w, 24), fakeLag, "Fake lag (delay position updates)"); y += 26;

        GUI.Label(new Rect(x, y, w, 24), "--- Hit/Kill Feedback ---"); y += 26;
        hitMarker = GUI.Toggle(new Rect(x, y, w, 24), hitMarker, "Hit marker (X at crosshair on hit)"); y += 26;
        damageIndicator = GUI.Toggle(new Rect(x, y, w, 24), damageIndicator, "Damage indicator (red vignette)"); y += 26;
        killFeed = GUI.Toggle(new Rect(x, y, w, 24), killFeed, "Kill feed (log kills on screen)"); y += 26;
        killSound = GUI.Toggle(new Rect(x, y, w, 24), killSound, "Kill sound (beep on kill)"); y += 26;
        hitLog = GUI.Toggle(new Rect(x, y, w, 24), hitLog, "Hit log (log hits to file)"); y += 26;
    }

    // ---- Tab: ESP / Visual ----
    private static void DrawEspTab(float x, ref float y, float w)
    {
        GUI.Label(new Rect(x, y, w, 24), "--- ESP Boxes ---"); y += 26;
        espEnabled = GUI.Toggle(new Rect(x, y, w, 24), espEnabled, "ESP boxes"); y += 26;
        if (espEnabled)
        {
            showHealth = GUI.Toggle(new Rect(x + 20, y, w - 20, 24), showHealth, "Show health"); y += 26;
            showTeammates = GUI.Toggle(new Rect(x + 20, y, w - 20, 24), showTeammates, "Show teammates"); y += 26;
            fovFilterEsp = GUI.Toggle(new Rect(x + 20, y, w - 20, 24), fovFilterEsp, "FOV filter ESP (only in FOV)"); y += 26;
        }

        GUI.Label(new Rect(x, y, w, 24), "--- ESP Types ---"); y += 26;
        nameEsp = GUI.Toggle(new Rect(x, y, w, 24), nameEsp, "Name ESP (show player names)"); y += 26;
        healthBarEsp = GUI.Toggle(new Rect(x, y, w, 24), healthBarEsp, "Health bar ESP (bars above players)"); y += 26;
        boxHeadEsp = GUI.Toggle(new Rect(x, y, w, 24), boxHeadEsp, "Box-head ESP (detailed boxes)"); y += 26;
        distanceEsp = GUI.Toggle(new Rect(x, y, w, 24), distanceEsp, "Distance ESP (show meters)"); y += 26;
        weaponIdEsp = GUI.Toggle(new Rect(x, y, w, 24), weaponIdEsp, "Weapon ID ESP (show enemy weapon)"); y += 26;
        skeletonEsp = GUI.Toggle(new Rect(x, y, w, 24), skeletonEsp, "Skeleton ESP (bone tracing)"); y += 26;
        footstepEsp = GUI.Toggle(new Rect(x, y, w, 24), footstepEsp, "Footstep ESP (recent enemy positions)"); y += 26;
        backtrack = GUI.Toggle(new Rect(x, y, w, 24), backtrack, "Backtrack (past enemy positions)"); y += 26;
        snaplines = GUI.Toggle(new Rect(x, y, w, 24), snaplines, "Snaplines (lines to enemies)"); y += 26;
        threatIndicator = GUI.Toggle(new Rect(x, y, w, 24), threatIndicator, "Threat indicator (arrow to closest)"); y += 26;

        GUI.Label(new Rect(x, y, w, 24), "--- Radar ---"); y += 26;
        radarHack = GUI.Toggle(new Rect(x, y, w, 24), radarHack, "Radar hack (mini-map all players)"); y += 26;
        radarMiniMap = GUI.Toggle(new Rect(x, y, w, 24), radarMiniMap, "Radar mini-map (enemy positions)"); y += 26;
        playerList = GUI.Toggle(new Rect(x, y, w, 24), playerList, "Player list (show all players)"); y += 26;

        GUI.Label(new Rect(x, y, w, 24), "--- Player Rendering ---"); y += 26;
        chams = GUI.Toggle(new Rect(x, y, w, 24), chams, "Chams (see players through walls)"); y += 26;
        wallhack = GUI.Toggle(new Rect(x, y, w, 24), wallhack, "Wallhack (tracer lines + distance)"); y += 26;
        wireframePlayers = GUI.Toggle(new Rect(x, y, w, 24), wireframePlayers, "Wireframe players (green outline)"); y += 26;

        GUI.Label(new Rect(x, y, w, 24), "--- World Visual ---"); y += 26;
        fullbright = GUI.Toggle(new Rect(x, y, w, 24), fullbright, "Fullbright (no fog, max light)"); y += 26;
        nightVision = GUI.Toggle(new Rect(x, y, w, 24), nightVision, "Night vision (green tint + boost light)"); y += 26;
        noFog = GUI.Toggle(new Rect(x, y, w, 24), noFog, "No fog (disable fog rendering)"); y += 26;
        noSkybox = GUI.Toggle(new Rect(x, y, w, 24), noSkybox, "No skybox (better sky visibility)"); y += 26;
        noShadows = GUI.Toggle(new Rect(x, y, w, 24), noShadows, "No shadows (disable all shadows)"); y += 26;
        noSmoke = GUI.Toggle(new Rect(x, y, w, 24), noSmoke, "No smoke (disable smoke GOs)"); y += 26;
        noRain = GUI.Toggle(new Rect(x, y, w, 24), noRain, "No rain (disable weather GOs)"); y += 26;
        noGrass = GUI.Toggle(new Rect(x, y, w, 24), noGrass, "No grass (disable foliage)"); y += 26;
        noMuzzleFlash = GUI.Toggle(new Rect(x, y, w, 24), noMuzzleFlash, "No muzzle flash (disable flash GOs)"); y += 26;
        antiFlash = GUI.Toggle(new Rect(x, y, w, 24), antiFlash, "Anti-flashbang (block screen flash)"); y += 26;
        grenadeTrajectory = GUI.Toggle(new Rect(x, y, w, 24), grenadeTrajectory, "Grenade trajectory (throw arc)"); y += 26;
        screenCleaner = GUI.Toggle(new Rect(x, y, w, 24), screenCleaner, "Screen cleaner (remove ads/banners)"); y += 26;

        GUI.Label(new Rect(x, y, w, 24), "--- Crosshair/Camera ---"); y += 26;
        customCrosshair = GUI.Toggle(new Rect(x, y, w, 24), customCrosshair, "Custom crosshair (v1)"); y += 26;
        crosshairCustom = GUI.Toggle(new Rect(x, y, w, 24), crosshairCustom, "Custom crosshair v2 (color/size)"); y += 26;
        if (crosshairCustom)
        {
            GUI.Label(new Rect(x, y, 40, 24), "Size:");
            var szStr = GUI.TextField(new Rect(x + 40, y, 40, 24), crosshairSize.ToString(), 3);
            if (int.TryParse(szStr, out var sz)) crosshairSize = Mathf.Clamp(sz, 2, 50);
            GUI.Label(new Rect(x + 90, y, 50, 24), "Thick:");
            var thStr = GUI.TextField(new Rect(x + 140, y, 40, 24), crosshairThickness.ToString(), 2);
            if (int.TryParse(thStr, out var th)) crosshairThickness = Mathf.Clamp(th, 1, 10);
            y += 26;
            GUI.Label(new Rect(x, y, 30, 24), "R:");
            crosshairR = GUI.HorizontalSlider(new Rect(x + 30, y, 60, 24), crosshairR, 0f, 1f);
            GUI.Label(new Rect(x + 100, y, 30, 24), "G:");
            crosshairG = GUI.HorizontalSlider(new Rect(x + 130, y, 60, 24), crosshairG, 0f, 1f);
            GUI.Label(new Rect(x + 200, y, 30, 24), "B:");
            crosshairB = GUI.HorizontalSlider(new Rect(x + 230, y, 60, 24), crosshairB, 0f, 1f);
            y += 26;
        }
        crosshairHitIndicator = GUI.Toggle(new Rect(x, y, w, 24), crosshairHitIndicator, "Crosshair hit indicator (red on enemy)"); y += 26;
        zoomHack = GUI.Toggle(new Rect(x, y, w, 24), zoomHack, "Zoom hack (lower FOV)"); y += 26;
        if (zoomHack)
        {
            GUI.Label(new Rect(x, y, 40, 24), "FOV:");
            zoomFov = GUI.HorizontalSlider(new Rect(x + 40, y, 120, 24), zoomFov, 10f, 90f);
            y += 26;
        }
        fovChanger = GUI.Toggle(new Rect(x, y, w, 24), fovChanger, "FOV changer (camera FOV)"); y += 26;
        if (fovChanger)
        {
            GUI.Label(new Rect(x, y, w, 20), $"FOV: {targetFov:0}");
            targetFov = GUI.HorizontalSlider(new Rect(x + 60, y + 4, w - 60, 20), targetFov, 60f, 120f);
            y += 26;
        }
        thirdPerson = GUI.Toggle(new Rect(x, y, w, 24), thirdPerson, "Third person camera"); y += 26;
        if (thirdPerson)
        {
            GUI.Label(new Rect(x, y, w, 20), $"Distance: {thirdPersonDistance:0.0}");
            thirdPersonDistance = GUI.HorizontalSlider(new Rect(x + 80, y + 4, w - 80, 20), thirdPersonDistance, 2f, 10f);
            y += 26;
        }
        thirdPersonShoulder = GUI.Toggle(new Rect(x, y, w, 24), thirdPersonShoulder, "Third person shoulder (OTS cam)"); y += 26;
        if (thirdPersonShoulder)
        {
            GUI.Label(new Rect(x, y, 40, 24), "Offset:");
            thirdPersonShoulderX = GUI.HorizontalSlider(new Rect(x + 40, y, 120, 24), thirdPersonShoulderX, 0.5f, 4f);
            y += 26;
        }
        spectatorWarning = GUI.Toggle(new Rect(x, y, w, 24), spectatorWarning, "Spectator warning (alert when watched)"); y += 26;
    }

    // ---- Tab: Movement ----
    private static void DrawMovementTab(float x, ref float y, float w)
    {
        GUI.Label(new Rect(x, y, w, 24), "--- Jump/Hop ---"); y += 26;
        bunnyHop = GUI.Toggle(new Rect(x, y, w, 24), bunnyHop, "Bunny hop"); y += 26;
        autoBhop = GUI.Toggle(new Rect(x, y, w, 24), autoBhop, "Auto-bhop (perfect jump timing)"); y += 26;
        edgeJump = GUI.Toggle(new Rect(x, y, w, 24), edgeJump, "Edge jump (auto-jump at ledges)"); y += 26;

        GUI.Label(new Rect(x, y, w, 24), "--- Speed/Fly ---"); y += 26;
        speedHack = GUI.Toggle(new Rect(x, y, w, 24), speedHack, "Speed hack"); y += 26;
        if (speedHack)
        {
            GUI.Label(new Rect(x, y, w, 20), $"Speed: {speedMultiplier:0.0}x");
            speedMultiplier = GUI.HorizontalSlider(new Rect(x + 60, y + 4, w - 60, 20), speedMultiplier, 0.5f, 5f);
            y += 26;
        }
        flyHack = GUI.Toggle(new Rect(x, y, w, 24), flyHack, "Fly hack (Space=up, Shift=down)"); y += 26;
        noClip = GUI.Toggle(new Rect(x, y, w, 24), noClip, "No clip"); y += 26;
        autoSprint = GUI.Toggle(new Rect(x, y, w, 24), autoSprint, "Auto-sprint (always sprint when moving)"); y += 26;

        GUI.Label(new Rect(x, y, w, 24), "--- Goku TP ---"); y += 26;
        gokuTp = GUI.Toggle(new Rect(x, y, w, 24), gokuTp, "Goku TP (teleport behind enemy)"); y += 26;
        if (gokuTp)
        {
            GUI.Label(new Rect(x + 20, y, w - 20, 24), "TP behind closest enemy, return when none"); y += 24;
        }

        GUI.Label(new Rect(x, y, w, 24), "--- Stance/Slide ---"); y += 26;
        slideHack = GUI.Toggle(new Rect(x, y, w, 24), slideHack, "Slide hack (auto-crouch while moving)"); y += 26;
        autoCrouchIdle = GUI.Toggle(new Rect(x, y, w, 24), autoCrouchIdle, "Auto-crouch when idle (smaller hitbox)"); y += 26;
        noFallDamage = GUI.Toggle(new Rect(x, y, w, 24), noFallDamage, "No fall damage (restore HP)"); y += 26;

        GUI.Label(new Rect(x, y, w, 24), "--- Time ---"); y += 26;
        timeScaleHack = GUI.Toggle(new Rect(x, y, w, 24), timeScaleHack, "Time scale hack (slow-mo/speed)"); y += 26;
        if (timeScaleHack)
        {
            GUI.Label(new Rect(x, y, 40, 24), "Scale:");
            customTimeScale = GUI.HorizontalSlider(new Rect(x + 40, y, 120, 24), customTimeScale, 0.1f, 3f);
            y += 26;
        }
    }

    // ---- Tab: Weapons / Player ----
    private static void DrawWeaponsTab(float x, ref float y, float w)
    {
        GUI.Label(new Rect(x, y, w, 24), "--- Health/Ammo ---"); y += 26;
        infiniteHealth = GUI.Toggle(new Rect(x, y, w, 24), infiniteHealth, "Infinite health"); y += 26;
        infiniteAmmo = GUI.Toggle(new Rect(x, y, w, 24), infiniteAmmo, "Infinite ammo"); y += 26;

        GUI.Label(new Rect(x, y, w, 24), "--- Reload ---"); y += 26;
        instantReload = GUI.Toggle(new Rect(x, y, w, 24), instantReload, $"Instant reload ({instantReloads})"); y += 26;
        autoReload = GUI.Toggle(new Rect(x, y, w, 24), autoReload, "Auto-reload (reload when low)"); y += 26;
        if (autoReload)
        {
            GUI.Label(new Rect(x, y, 60, 24), "Threshold:");
            autoReloadThreshold = (int)GUI.HorizontalSlider(new Rect(x + 60, y, 100, 24), autoReloadThreshold, 1, 30);
            y += 26;
        }

        GUI.Label(new Rect(x, y, w, 24), "--- Weapons ---"); y += 26;
        weaponUnlock = GUI.Toggle(new Rect(x, y, w, 24), weaponUnlock, $"Unlock all weapons ({(weaponUnlockApplied ? weaponUnlockCount.ToString() : "off")})"); y += 26;
        fastWeaponSwitch = GUI.Toggle(new Rect(x, y, w, 24), fastWeaponSwitch, "Fast weapon switch (zero timers)"); y += 26;
        autoWeaponSwap = GUI.Toggle(new Rect(x, y, w, 24), autoWeaponSwap, "Auto weapon swap (swap when empty)"); y += 26;
        autoPickup = GUI.Toggle(new Rect(x, y, w, 24), autoPickup, "Auto-pickup (grab nearby items)"); y += 26;

        GUI.Label(new Rect(x, y, w, 24), "--- Player/Stats ---"); y += 26;
        xpGoldHack = GUI.Toggle(new Rect(x, y, w, 24), xpGoldHack, "XP/Gold hack (set exp=999k, gold=999k)"); y += 26;
        nameChanger = GUI.Toggle(new Rect(x, y, w, 24), nameChanger, "Name changer"); y += 26;
        if (nameChanger)
        {
            GUI.Label(new Rect(x, y, 60, 24), "Name:");
            customName = GUI.TextField(new Rect(x + 60, y, w - 60, 24), customName, 20);
            y += 26;
        }
        scoreboardHack = GUI.Toggle(new Rect(x, y, w, 24), scoreboardHack, "Scoreboard hack (team scores=999)"); y += 26;
        pingSpoof = GUI.Toggle(new Rect(x, y, w, 24), pingSpoof, "Ping spoof (display fake ping)"); y += 26;
        if (pingSpoof)
        {
            GUI.Label(new Rect(x, y, 60, 24), "Ping:");
            var pingStr = GUI.TextField(new Rect(x + 60, y, 80, 24), fakePing.ToString(), 5);
            if (int.TryParse(pingStr, out var parsed)) fakePing = parsed;
            y += 26;
        }
        adminUnlock = GUI.Toggle(new Rect(x, y, w, 24), adminUnlock, "Admin panel unlock"); y += 26;
    }

    // ---- Tab: Misc ----
    private static void DrawMiscTab(float x, ref float y, float w)
    {
        GUI.Label(new Rect(x, y, w, 24), "--- Auto Actions ---"); y += 26;
        autoAccept = GUI.Toggle(new Rect(x, y, w, 24), autoAccept, "Auto-accept (auto ready up)"); y += 26;
        autoRevive = GUI.Toggle(new Rect(x, y, w, 24), autoRevive, "Auto-revive (auto respawn when dead)"); y += 26;
        autoVoteYes = GUI.Toggle(new Rect(x, y, w, 24), autoVoteYes, "Auto vote yes (votekick auto-yes)"); y += 26;
        chatSpammer = GUI.Toggle(new Rect(x, y, w, 24), chatSpammer, "Chat spammer (send message every 3s)"); y += 26;
        if (chatSpammer)
        {
            GUI.Label(new Rect(x, y, 50, 24), "Msg:");
            spamMessage = GUI.TextField(new Rect(x + 50, y, w - 50, 24), spamMessage, 30);
            y += 26;
        }

        GUI.Label(new Rect(x, y, w, 24), "--- Debug ---"); y += 26;
        debugOverlay = GUI.Toggle(new Rect(x, y, w, 24), debugOverlay, "Debug overlay (diagnostic info)"); y += 26;
        debugLogging = GUI.Toggle(new Rect(x, y, w, 24), debugLogging, $"Verbose diagnostics (1/{DiagnosticInterval:0}s)"); y += 26;
        if (debugLogging)
        {
            heavyDiagnostics = GUI.Toggle(new Rect(x + 20, y, w - 20, 24), heavyDiagnostics, "+ full field sweep (COSTLY)"); y += 26;
        }
        showRuntimeStatus = GUI.Toggle(new Rect(x, y, w, 24), showRuntimeStatus, "Show runtime status"); y += 26;
        if (showRuntimeStatus)
        {
            GUI.Label(new Rect(x, y, w, 24), $"Update: {(controllerRunning ? "running" : "waiting")} | Boxes: {espBoxes.Count} | {featureStatus}"); y += 24;
            GUI.Label(new Rect(x, y, w, 24), $"Aimbot: {aimStatus}"); y += 24;
        }

        GUI.Label(new Rect(x, y, w, 24), "--- Panic ---"); y += 26;
        GUI.Label(new Rect(x, y, w, 24), "Press [End] to disable all features instantly"); y += 26;
        GUI.Label(new Rect(x, y, w, 24), "Press [Home] to toggle this menu"); y += 26;
    }

    // ---- Tab: Config ----
    private static void DrawConfigTab(float x, ref float y, float w)
    {
        GUI.Label(new Rect(x, y, w, 24), "--- Config ---"); y += 26;
        if (GUI.Button(new Rect(x, y, 80, 24), "Save"))
        {
            SaveConfig();
        }
        if (GUI.Button(new Rect(x + 90, y, 80, 24), "Reset All"))
        {
            ResetAllFeatures();
        }
        y += 30;

        GUI.Label(new Rect(x, y, w, 24), "--- Presets ---"); y += 26;
        for (var i = 1; i <= 3; i++)
        {
            var pname = i.ToString();
            if (GUI.Button(new Rect(x, y, 50, 24), $"P{i}S"))
            {
                SavePreset(pname);
            }
            if (GUI.Button(new Rect(x + 60, y, 50, 24), $"P{i}L"))
            {
                LoadPreset(pname);
            }
            y += 28;
        }

        GUI.Label(new Rect(x, y, w, 24), "--- Menu Position ---"); y += 26;
        GUI.Label(new Rect(x, y, w, 24), $"Position: ({menuRect.x:0}, {menuRect.y:0})"); y += 24;
        if (GUI.Button(new Rect(x, y, 100, 24), "Reset Pos"))
        {
            menuRect.x = 20;
            menuRect.y = 20;
        }
        y += 28;
    }

    /// <summary>
    /// Draw distance ESP: shows distance to each enemy below their ESP box.
    /// </summary>
    private static void DrawDistanceEsp()
    {
        if (!espEnabled) return;
        var mainPlayer = Controll.HGAODFPBGLB;
        if (mainPlayer == null) return;
        var prevColor = GUI.color;
        foreach (var box in espBoxes)
        {
            if (!box.IsEnemy) continue;
            GUI.color = new Color(box.Color.r, box.Color.g, box.Color.b, 0.8f);
            GUI.Label(new Rect(box.Bounds.x, box.Bounds.y + box.Bounds.height + 2, 80, 20), $"{box.Distance:F0}m");
        }
        GUI.color = prevColor;
    }

    /// <summary>
    /// Draw weapon ID ESP: shows the weapon ID of each enemy next to their ESP box.
    /// </summary>
    private static void DrawWeaponIdEsp()
    {
        if (!espEnabled) return;
        var players = PLH.BAKLNPIEHMI;
        var mainPlayer = Controll.HGAODFPBGLB;
        if (players == null || mainPlayer == null) return;
        var camera = ResolveCamera();
        if (camera == null) return;

        var prevColor = GUI.color;
        for (var i = 0; i < players.Length; i++)
        {
            var player = players[i];
            if (!IsVisibleTarget(player, mainPlayer, true, true)) continue;

            var head = player.ACEHIBLPHCA;
            if (head == null) continue;
            var screenPos = camera.WorldToScreenPoint(head.transform.position);
            if (screenPos.z <= 0) continue;

            var weaponId = player.ECBCOHFLJCC;
            var x = screenPos.x + 30;
            var y = Screen.height - screenPos.y;
            GUI.color = new Color(1f, 0.7f, 0f, 0.8f);
            GUI.Label(new Rect(x, y, 80, 20), $"W:{weaponId}");
        }
        GUI.color = prevColor;
    }

    /// <summary>
    /// Draw health bars above each player's ESP box.
    /// Green bar for full health, red for low, with exact HP number.
    /// </summary>
    private static void DrawHealthBarEsp()
    {
        if (!espEnabled) return;
        var prevColor = GUI.color;
        foreach (var box in espBoxes)
        {
            var hp = box.Health;
            if (hp <= 0) continue;
            var hpRatio = Mathf.Clamp01(hp / 100f);
            var barWidth = box.Bounds.width;
            var barX = box.Bounds.x;
            var barY = box.Bounds.y - 6;
            // Background
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(barX - 1, barY - 1, barWidth + 2, 5), Texture2D.whiteTexture);
            // Health fill
            GUI.color = new Color(1f - hpRatio, hpRatio, 0f, 0.9f);
            GUI.DrawTexture(new Rect(barX, barY, barWidth * hpRatio, 3), Texture2D.whiteTexture);
        }
        GUI.color = prevColor;
    }

    /// <summary>
    /// Draw snaplines from bottom-center of screen to each enemy.
    /// </summary>
    private static void DrawSnaplines()
    {
        if (!espEnabled) return;
        var prevColor = GUI.color;
        var bottomCenter = new Vector2(Screen.width / 2f, Screen.height);
        foreach (var box in espBoxes)
        {
            if (!box.IsEnemy) continue;
            GUI.color = new Color(box.Color.r, box.Color.g, box.Color.b, 0.3f);
            DrawLine2D(bottomCenter, box.ScreenPos);
        }
        GUI.color = prevColor;
    }

    /// <summary>
    /// Draw a threat indicator arrow pointing toward the closest enemy.
    /// Shows direction and distance when enemy is off-screen.
    /// </summary>
    private static void DrawThreatIndicator()
    {
        try
        {
            var players = PLH.BAKLNPIEHMI;
            var mainPlayer = Controll.HGAODFPBGLB;
            if (players == null || mainPlayer == null) return;
            var camera = ResolveCamera();
            if (camera == null) return;

            var myPos = mainPlayer.OOMJGHCFODI;
            var closestDist = float.MaxValue;
            var closestPos = Vector3.zero;
            var found = false;

            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (!IsVisibleTarget(player, mainPlayer, false, true)) continue;
                var pos = player.OOMJGHCFODI;
                var dist = Vector3.Distance(myPos, pos);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestPos = pos;
                    found = true;
                }
            }

            if (!found) return;

            // Check if enemy is on-screen
            var screenPos = camera.WorldToScreenPoint(closestPos);
            var onScreen = screenPos.z > 0 && screenPos.x > 0 && screenPos.x < Screen.width &&
                           Screen.height - screenPos.y > 0 && Screen.height - screenPos.y < Screen.height;

            if (onScreen) return; // Enemy visible, no indicator needed

            // Draw arrow at screen edge pointing toward enemy
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var dir = new Vector2(screenPos.x - center.x, (Screen.height - screenPos.y) - center.y).normalized;
            var arrowPos = center + dir * Mathf.Min(Screen.width, Screen.height) * 0.4f;
            arrowPos.x = Mathf.Clamp(arrowPos.x, 30, Screen.width - 30);
            arrowPos.y = Mathf.Clamp(arrowPos.y, 30, Screen.height - 30);

            var prevColor = GUI.color;
            GUI.color = new Color(1f, 0.3f, 0.3f, 0.8f);
            // Draw arrow (triangle approximation)
            var angle = Mathf.Atan2(dir.y, dir.x);
            var arrowSize = 12f;
            for (var i = 0; i < 3; i++)
            {
                var a = angle + (i - 1) * 0.5f;
                var p = arrowPos + new Vector2(Mathf.Cos(a) * arrowSize, Mathf.Sin(a) * arrowSize);
                DrawLine2D(arrowPos, p);
            }
            // Distance text
            GUI.Label(new Rect(arrowPos.x - 30, arrowPos.y + 15, 60, 20), $"{closestDist:F0}m");
            GUI.color = prevColor;
        }
        catch { }
    }

    /// <summary>
    /// Monitor health changes and trigger damage indicator when health drops.
    /// </summary>
    private static void UpdateDamageIndicator()
    {
        if (!damageIndicator) return;
        try
        {
            var main = Controll.HGAODFPBGLB;
            if (main == null) return;
            var currentHp = main.FDOJDJLIGLF;
            if (lastHealthForDamage < 0)
            {
                lastHealthForDamage = currentHp;
                return;
            }
            if (currentHp < lastHealthForDamage)
            {
                lastDamageAmount = lastHealthForDamage - currentHp;
                damageIndicatorTime = Time.time;
            }
            lastHealthForDamage = currentHp;
        }
        catch { }
    }

    /// <summary>
    /// Monitor hit count changes and trigger hit marker when we hit someone.
    /// </summary>
    private static void UpdateHitMarker()
    {
        if (!hitMarker) return;
        try
        {
            // Use the hit sequence counter (GAMBHJPMDON) to detect hits
            var currentHits = Controll.GAMBHJPMDON;
            if (lastHitCount < 0)
            {
                lastHitCount = currentHits;
                return;
            }
            if (currentHits > lastHitCount)
            {
                hitMarkerTime = Time.time;
            }
            lastHitCount = currentHits;
        }
        catch { }
    }

    /// <summary>
    /// Draw damage indicator: red flash + damage amount when taking damage.
    /// </summary>
    private static void DrawDamageIndicator()
    {
        if (damageIndicatorTime <= 0) return;
        var elapsed = Time.time - damageIndicatorTime;
        if (elapsed > 2f) return;

        var alpha = 1f - (elapsed / 2f);
        var prevColor = GUI.color;
        GUI.color = new Color(1f, 0f, 0f, alpha * 0.5f);
        // Red vignette border
        var thickness = 40f;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0, Screen.height - thickness, Screen.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0, 0, thickness, Screen.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(Screen.width - thickness, 0, thickness, Screen.height), Texture2D.whiteTexture);

        // Damage amount text
        GUI.color = new Color(1f, 0.5f, 0.5f, alpha);
        GUI.Label(new Rect(Screen.width / 2f - 50, Screen.height / 2f + 100, 100, 30), $"-{lastDamageAmount} HP");
        GUI.color = prevColor;
    }

    /// <summary>
    /// Draw hit marker: X shape at crosshair when hitting an enemy.
    /// </summary>
    private static void DrawHitMarker()
    {
        if (hitMarkerTime <= 0) return;
        var elapsed = Time.time - hitMarkerTime;
        if (elapsed > 0.5f) return;

        var alpha = 1f - (elapsed / 0.5f);
        var prevColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        var cx = Screen.width / 2f;
        var cy = Screen.height / 2f;
        var size = 8f;
        // Draw X shape (4 lines from center)
        DrawLine2D(new Vector2(cx - size, cy - size), new Vector2(cx - 2, cy - 2));
        DrawLine2D(new Vector2(cx + 2, cy - 2), new Vector2(cx + size, cy - size));
        DrawLine2D(new Vector2(cx - size, cy + size), new Vector2(cx - 2, cy + 2));
        DrawLine2D(new Vector2(cx + 2, cy + 2), new Vector2(cx + size, cy + size));
        GUI.color = prevColor;
    }

    /// <summary>
    /// Auto-pickup: find nearby pickup GameObjects and move toward them.
    /// </summary>
    private static void UpdateAutoPickup()
    {
        if (!autoPickup) return;
        try
        {
            var main = Controll.HGAODFPBGLB;
            if (main == null) return;
            var myPos = main.OOMJGHCFODI;

            // Search for GameObjects with "pickup", "item", "drop", "loot" in name
            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            if (allObjects == null) return;

            GameObject? closest = null;
            var closestDist = 10f; // Max pickup range

            foreach (var go in allObjects)
            {
                if (go == null) continue;
                var name = go.name;
                if (string.IsNullOrEmpty(name)) continue;
                if (!name.ToLower().Contains("pickup") &&
                    !name.ToLower().Contains("drop") &&
                    !name.ToLower().Contains("loot") &&
                    !name.ToLower().Contains("item"))
                    continue;

                var dist = Vector3.Distance(myPos, go.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = go;
                }
            }

            if (closest != null)
            {
                // Move toward the pickup by setting movement flags
                var direction = (closest.transform.position - myPos).normalized;
                // Set forward/backward based on z direction
                if (direction.z > 0.1f)
                    Controll.MNHBPCOOMLE |= 0x4u; // forward
                else if (direction.z < -0.1f)
                    Controll.MNHBPCOOMLE |= 0x8u; // backward
                if (direction.x > 0.1f)
                    Controll.MNHBPCOOMLE |= 0x1u; // right
                else if (direction.x < -0.1f)
                    Controll.MNHBPCOOMLE |= 0x2u; // left
            }
        }
        catch { }
    }

    /// <summary>
    /// Draw a warning if the local player is being spectated.
    /// Searches for a GameObject named "Spectator" and checks if it's active.
    /// </summary>
    private static void DrawSpectatorWarning()
    {
        try
        {
            // Search for Spectator GameObject in scene
            var spectator = GameObject.Find("Spectator");
            if (spectator == null) return;

            if (!spectator.activeInHierarchy) return;

            // Draw warning in center-top of screen
            var prevColor = GUI.color;
            GUI.color = new Color(1f, 0.2f, 0.2f, 0.9f);
            var warning = "! YOU ARE BEING SPECTATED !";
            var rect = new Rect(Screen.width / 2f - 150f, 50f, 300f, 30f);
            GUI.Label(rect, warning);
            GUI.color = prevColor;
        }
        catch { }
    }

    /// <summary>
    /// Monitor kill count changes and log kill feed entries.
    /// Checks Controll.DEBGAILDKPC (kill count) for increments.
    /// </summary>
    private static void UpdateKillFeed()
    {
        if (!killFeed) return;

        try
        {
            var currentKills = Controll.DEBGAILDKPC;
            if (lastKillCount < 0)
            {
                lastKillCount = currentKills;
                return;
            }

            if (currentKills > lastKillCount)
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var entry = $"[{timestamp}] KILL #{currentKills}";
                killFeedEntries.Add(entry);
                instance?.Log.LogInfo($"[KillFeed] {entry}");
                lastKillCount = currentKills;
            }
            else if (currentKills < lastKillCount)
            {
                // Reset (new match)
                lastKillCount = currentKills;
            }

            // Also check death count
            var currentDeaths = Controll.GKNJELHPMDE;
            // Keep only last 10 entries
            while (killFeedEntries.Count > 10)
            {
                killFeedEntries.RemoveAt(0);
            }
        }
        catch { }
    }

    /// <summary>
    /// Draw the kill feed in the top-right corner below the radar.
    /// Shows recent kills with timestamps.
    /// </summary>
    private static void DrawKillFeed()
    {
        if (killFeedEntries.Count == 0) return;

        var prevColor = GUI.color;
        var startX = Screen.width - 250f;
        var startY = 170f; // Below radar

        GUI.color = new Color(1f, 0.3f, 0.3f, 0.8f);
        for (var i = 0; i < killFeedEntries.Count; i++)
        {
            GUI.Label(new Rect(startX, startY + i * 20, 240, 20), killFeedEntries[i]);
        }

        GUI.color = prevColor;
    }

    /// <summary>
    /// Draw skeleton ESP by tracing bone transforms from each player's model.
    /// Connects head, body, arms, and legs using child transform hierarchy.
    /// </summary>
    private static void DrawSkeletonEsp()
    {
        try
        {
            var players = PLH.BAKLNPIEHMI;
            var mainPlayer = Controll.HGAODFPBGLB;
            var camera = ResolveCamera();
            if (players == null || mainPlayer == null || camera == null) return;

            var prevColor = GUI.color;

            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (!IsVisibleTarget(player, mainPlayer, false, true)) continue;

                var head = player.ACEHIBLPHCA;
                if (head == null) continue;

                var rootTransform = head.transform;
                if (rootTransform == null) continue;

                var isEnemy = player.MMMGPDBMOLM != mainPlayer.MMMGPDBMOLM;
                var color = isEnemy ? Color.red : Color.green;
                GUI.color = new Color(color.r, color.g, color.b, 0.8f);

                // Get head position in screen space
                var headPos = camera.WorldToScreenPoint(rootTransform.position);
                if (headPos.z <= 0) continue;

                // Draw connections from head to all child transforms (bones)
                DrawTransformHierarchy(camera, rootTransform, headPos);
            }

            GUI.color = prevColor;
        }
        catch { }
    }

    /// <summary>
    /// Recursively draw lines between a transform and all its children.
    /// </summary>
    private static void DrawTransformHierarchy(Camera camera, Transform parent, Vector3 parentScreen)
    {
        var childCount = parent.childCount;
        for (var i = 0; i < childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child == null) continue;

            var childWorld = child.position;
            var childScreen = camera.WorldToScreenPoint(childWorld);
            if (childScreen.z <= 0) continue;

            // Convert to GUI coordinates (Y flipped)
            var pStart = new Vector2(parentScreen.x, Screen.height - parentScreen.y);
            var pEnd = new Vector2(childScreen.x, Screen.height - childScreen.y);

            // Draw line from parent to child
            DrawLine2D(pStart, pEnd);

            // Recurse into children
            DrawTransformHierarchy(camera, child, childScreen);
        }
    }

    /// <summary>
    /// Draw a 2D line between two points using Texture2D.whiteTexture.
    /// </summary>
    private static void DrawLine2D(Vector2 start, Vector2 end)
    {
        var dx = end.x - start.x;
        var dy = end.y - start.y;
        var len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < 1f) return;

        var steps = Mathf.Max(2, (int)(len / 3f));
        for (var i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            var px = start.x + dx * t;
            var py = start.y + dy * t;
            GUI.DrawTexture(new Rect(px - 1, py - 1, 2, 2), Texture2D.whiteTexture);
        }
    }

    /// <summary>
    /// Draw a mini-radar in the top-right corner showing all player positions
    /// relative to the local player. Enemies are red dots, teammates are green.
    /// </summary>
    private static void DrawRadarHack()
    {
        try
        {
            var players = PLH.BAKLNPIEHMI;
            var mainPlayer = Controll.HGAODFPBGLB;
            if (players == null || mainPlayer == null) return;

            var myPos = mainPlayer.OOMJGHCFODI;
            var myYaw = Controll.NAKNALFCOIF * Mathf.Deg2Rad;

            // Radar dimensions
            var radarSize = 150f;
            var radarX = Screen.width - radarSize - 10f;
            var radarY = 10f;
            var radarRange = 100f; // meters shown on radar

            // Draw radar background
            var prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(radarX, radarY, radarSize, radarSize), Texture2D.whiteTexture);

            // Draw border
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            DrawBoxOutline(new Rect(radarX, radarY, radarSize, radarSize));

            // Draw center (player position)
            GUI.color = new Color(1f, 1f, 0f, 0.8f);
            GUI.DrawTexture(new Rect(radarX + radarSize / 2 - 2, radarY + radarSize / 2 - 2, 4, 4), Texture2D.whiteTexture);

            // Draw player direction line
            var dirX = radarX + radarSize / 2 + Mathf.Sin(myYaw) * 10f;
            var dirY = radarY + radarSize / 2 - Mathf.Cos(myYaw) * 10f;
            GUI.color = new Color(1f, 1f, 0f, 0.5f);
            DrawLine2D(new Vector2(radarX + radarSize / 2, radarY + radarSize / 2), new Vector2(dirX, dirY));

            // Draw other players
            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (!IsVisibleTarget(player, mainPlayer, true, true)) continue;

                var pos = player.OOMJGHCFODI;
                var dx = pos.x - myPos.x;
                var dz = pos.z - myPos.z;

                // Rotate by -yaw so forward is up on radar
                var cosYaw = Mathf.Cos(-myYaw);
                var sinYaw = Mathf.Sin(-myYaw);
                var rx = dx * cosYaw - dz * sinYaw;
                var rz = dx * sinYaw + dz * cosYaw;

                // Scale to radar size
                var scale = radarSize / 2f / radarRange;
                var px = radarX + radarSize / 2 + rx * scale;
                var py = radarY + radarSize / 2 - rz * scale;

                // Clamp to radar bounds
                px = Mathf.Clamp(px, radarX + 2, radarX + radarSize - 2);
                py = Mathf.Clamp(py, radarY + 2, radarY + radarSize - 2);

                var isEnemy = player.MMMGPDBMOLM != mainPlayer.MMMGPDBMOLM;
                GUI.color = isEnemy ? Color.red : Color.green;
                GUI.DrawTexture(new Rect(px - 2, py - 2, 4, 4), Texture2D.whiteTexture);
            }

            GUI.color = prevColor;
        }
        catch { }
    }

    /// <summary>
    /// Draw a simple box outline (4 lines).
    /// </summary>
    private static void DrawBoxOutline(Rect rect)
    {
        DrawLine2D(new Vector2(rect.x, rect.y), new Vector2(rect.xMax, rect.y));
        DrawLine2D(new Vector2(rect.xMax, rect.y), new Vector2(rect.xMax, rect.yMax));
        DrawLine2D(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.x, rect.yMax));
        DrawLine2D(new Vector2(rect.x, rect.yMax), new Vector2(rect.x, rect.y));
    }

    /// <summary>
    /// Draw a watermark in the top-left corner showing all active features.
    /// </summary>
    private static void DrawFeatureWatermark()
    {
        // Update FPS counter
        fpsFrameCount++;
        if (Time.time - fpsUpdateTimer >= 1f)
        {
            currentFps = fpsFrameCount / (Time.time - fpsUpdateTimer);
            fpsUpdateTimer = Time.time;
            fpsFrameCount = 0;
        }

        var features = new List<string>();
        if (espEnabled) features.Add("ESP");
        if (aimbotEnabled) features.Add("Aimbot");
        if (autoShoot) features.Add("AutoShoot");
        if (noRecoil) features.Add("NoRecoil");
        if (infiniteHealth) features.Add("InfHealth");
        if (infiniteAmmo) features.Add("InfAmmo");
        if (instantReload) features.Add("InstReload");
        if (rapidFire) features.Add("RapidFire");
        if (bunnyHop) features.Add("BunnyHop");
        if (fovChanger) features.Add($"FOV:{targetFov:0}");
        if (customCrosshair) features.Add("Crosshair");
        if (speedHack) features.Add($"Speed:{speedMultiplier:0.0}x");
        if (flyHack) features.Add("Fly");
        if (gokuTp) features.Add("GokuTP");
        if (noClip) features.Add("NoClip");
        if (weaponUnlock) features.Add("WeaponUnlock");
        if (thirdPerson) features.Add("3rdPerson");
        if (chams) features.Add("Chams");
        if (triggerbot) features.Add("Triggerbot");
        if (fullbright) features.Add("Fullbright");
        if (antiFlash) features.Add("AntiFlash");
        if (wallhack) features.Add("Wallhack");
        if (noSpread) features.Add("NoSpread");
        if (fastFire) features.Add("FastFire");
        if (autoReload) features.Add($"AutoReload:{autoReloadThreshold}");
        if (nameEsp) features.Add("NameESP");
        if (spinbot) features.Add("Spinbot");
        if (skeletonEsp) features.Add("SkeletonESP");
        if (radarHack) features.Add("RadarHack");
        if (antiAimPitch) features.Add("AntiAimPitch");
        if (autoStrafe) features.Add("AutoStrafe");
        if (killFeed) features.Add("KillFeed");
        if (edgeJump) features.Add("EdgeJump");
        if (fakeLag) features.Add("FakeLag");
        if (spectatorWarning) features.Add("SpectatorWarn");
        if (damageIndicator) features.Add("DmgIndicator");
        if (hitMarker) features.Add("HitMarker");
        if (autoPickup) features.Add("AutoPickup");
        if (xpGoldHack) features.Add("XP/GoldHack");
        if (healthBarEsp) features.Add("HealthBar");
        if (snaplines) features.Add("Snaplines");
        if (threatIndicator) features.Add("ThreatInd");
        if (nameChanger) features.Add($"Name:{customName}");
        if (scoreboardHack) features.Add("ScoreHack");
        if (autoBhop) features.Add("AutoBhop");
        if (pingSpoof) features.Add($"Ping:{fakePing}ms");
        if (footstepEsp) features.Add("FootstepESP");
        if (preFire) features.Add("PreFire");
        if (backtrack) features.Add("Backtrack");
        if (adminUnlock) features.Add("AdminUnlock");
        if (boxHeadEsp) features.Add("BoxHeadESP");
        if (slideHack) features.Add("SlideHack");
        if (grenadeTrajectory) features.Add("GrenadeTraj");
        if (noFallDamage) features.Add("NoFallDmg");
        if (crosshairCustom) features.Add("CrosshairV2");
        if (killSound) features.Add("KillSound");
        if (aimbotSmoothing) features.Add($"AimSmooth:{aimbotSmoothFactor:F2}");
        if (fastWeaponSwitch) features.Add("FastSwitch");
        if (antiAimJitter) features.Add("AntiAimJitter");
        if (debugOverlay) features.Add("DebugOverlay");
        if (autoAccept) features.Add("AutoAccept");
        if (distanceEsp) features.Add("DistESP");
        if (weaponIdEsp) features.Add("WeaponIdESP");
        if (chatSpammer) features.Add("ChatSpam");
        if (autoVoteYes) features.Add("AutoVoteYes");
        if (autoRevive) features.Add("AutoRevive");
        if (noSkybox) features.Add("NoSkybox");
        if (wireframePlayers) features.Add("Wireframe");
        if (zoomHack) features.Add($"Zoom:{zoomFov:F0}");
        if (noMuzzleFlash) features.Add("NoMuzzleFlash");
        if (nightVision) features.Add("NightVision");
        if (noSmoke) features.Add("NoSmoke");
        if (autoSprint) features.Add("AutoSprint");
        if (noRain) features.Add("NoRain");
        if (thirdPersonShoulder) features.Add("OTSCam");
        if (aimbotPrediction) features.Add("AimPredict");
        if (autoCrouchIdle) features.Add("AutoCrouch");
        if (panicMode) features.Add("PANIC!");
        if (fovFilterEsp) features.Add("FOVFilter");
        if (hitLog) features.Add("HitLog");
        if (autoWeaponSwap) features.Add("AutoSwap");
        if (screenCleaner) features.Add("ScreenClean");
        if (noShadows) features.Add("NoShadows");
        if (playerList) features.Add("PlayerList");
        if (noGrass) features.Add("NoGrass");
        if (crosshairHitIndicator) features.Add("HitIndicator");
        if (timeScaleHack) features.Add($"TimeScale:{customTimeScale:F1}x");
        if (noFog) features.Add("NoFog");
        if (!aimEnemiesOnly) features.Add("AimAll");
        if (boneScan) features.Add("BoneScan");
        if (radarMiniMap) features.Add("Radar");
        if (ghostBullets) features.Add("GhostBullets");

        if (features.Count == 0) return;

        var prevColor = GUI.color;
        GUI.color = new Color(0f, 1f, 0f, 0.7f);
        var y = 5f;
        GUI.Label(new Rect(5, y, 300, 20), $"Blockpost Trainer [{features.Count} active] FPS:{currentFps:0} {DateTime.Now:HH:mm:ss}");
        y += 20;
        if (sessionStartTime < 0) sessionStartTime = Time.time;
        var sessionTime = Time.time - sessionStartTime;
        var ts = TimeSpan.FromSeconds(sessionTime);
        GUI.Label(new Rect(5, y, 300, 20), $"Session: {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}");
        y += 20;
        if (currentKillStreak > 0 && Time.time - lastKillTime < 10f)
        {
            GUI.Label(new Rect(5, y, 300, 20), $"Killstreak: {currentKillStreak}x");
            y += 20;
        }
        GUI.color = new Color(1f, 1f, 0f, 0.6f);
        // Show features in rows of 5
        for (var i = 0; i < features.Count; i++)
        {
            var row = i / 5;
            var col = i % 5;
            GUI.Label(new Rect(5 + col * 120, y + row * 18, 120, 18), features[i]);
        }
        GUI.color = prevColor;
    }

    /// <summary>
    /// Draw a circle on screen showing the aimbot's FOV targeting range.
    /// The circle radius is proportional to the aimbotFov setting.
    /// </summary>
    private static void DrawAimbotFovCircle()
    {
        var centerX = Screen.width / 2f;
        var centerY = Screen.height / 2f;
        // Convert FOV angle to screen pixels (approximate: 1 degree ~ 8 pixels at 90 FOV)
        var radius = aimbotFov * 8f;
        if (radius < 5f) return;

        var prevColor = GUI.color;
        GUI.color = new Color(1f, 0.5f, 0f, 0.4f); // Orange, semi-transparent

        // Draw circle as series of small dots
        var steps = 64;
        for (var i = 0; i < steps; i++)
        {
            var angle = (float)i / steps * Mathf.PI * 2f;
            var px = centerX + Mathf.Cos(angle) * radius;
            var py = centerY + Mathf.Sin(angle) * radius;
            GUI.DrawTexture(new Rect(px - 1, py - 1, 2, 2), Texture2D.whiteTexture);
        }

        GUI.color = prevColor;
    }

    private static void DrawEspBoxes()
    {
        if (!espEnabled)
        {
            return;
        }

        var previousColor = GUI.color;
        var screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);

        foreach (var box in espBoxes)
        {
            GUI.color = box.Color;
            GUI.Box(box.Bounds, string.Empty);
            if (showHealth)
            {
                GUI.Label(new Rect(box.Bounds.xMax + 4, box.Bounds.y, 80, 24), $"HP {box.Health}");
            }

            // Wallhack: draw tracer line from screen center to enemy + distance
            if (wallhack && box.IsEnemy)
            {
                DrawTracerLine(screenCenter, box.ScreenPos, box.Color);
                GUI.Label(new Rect(box.Bounds.xMax + 4, box.Bounds.y + 24, 80, 20), $"{box.Distance:F0}m");
            }

            // Name ESP: show player name above the box
            if (nameEsp)
            {
                var nameRect = new Rect(box.Bounds.x - 20, box.Bounds.y - 20, box.Bounds.width + 40, 20);
                GUI.Label(nameRect, box.Name);
            }
        }

        GUI.color = previousColor;
    }

    /// <summary>
    /// Draw a line from start to end using GL immediate mode (works through walls).
    /// </summary>
    private static void DrawTracerLine(Vector2 start, Vector2 end, Color color)
    {
        // Use GL.LINES to draw through walls (GUI lines are occluded by geometry)
        // GL.Begin(GL.LINES) requires a material, so we use a simple fallback:
        // Draw a thin rectangle as a line approximation using GUI.DrawTexture
        var dx = end.x - start.x;
        var dy = end.y - start.y;
        var len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < 1f) return;

        // Draw multiple small boxes along the line to simulate a line
        var steps = Mathf.Max(2, (int)(len / 4f));
        var prevColor = GUI.color;
        GUI.color = new Color(color.r, color.g, color.b, 0.6f);
        for (var i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            var px = start.x + dx * t;
            var py = start.y + dy * t;
            GUI.DrawTexture(new Rect(px - 1, py - 1, 2, 2), Texture2D.whiteTexture);
        }
        GUI.color = prevColor;
    }
}
