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
        "Mouse 4"
    };
    private static readonly string[] AimStyleLabels =
    {
        "Plain aim lock",
        "Silent aim"
    };
    private static Plugin? instance;
    private static bool menuVisible;
    private static Rect menuRect = new(20, 20, 520, 660);
    private static bool menuDragging;
    private static Vector2 menuDragOffset;
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
    private static readonly List<string> killFeedEntries = new();
    private static float lastKillFeedCheck;
    private static int lastKillCount = -1;
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
        if (!infiniteHealth && !infiniteAmmo && !bunnyHop && !fovChanger && !speedHack && !flyHack && !noClip && !thirdPerson && !fullbright && !antiFlash && !noSpread && !fastFire && !autoReload && !spinbot && !antiAimPitch && !autoStrafe && !edgeJump && !fakeLag)
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
                // Auto-reload when magazine is empty and we have reserve ammo
                var currentAmmo = Controll.FGGKANNFBDH;
                var maxAmmo = Controll.ILFOFIOFBAM;
                var reserve = Controll.KJOMABGHAIJ;
                if (currentAmmo == 0 && maxAmmo > 0 && reserve > 0 && !Controll.EKEAAHAKHIN)
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

            foreach (var line in File.ReadAllLines(ConfigPath))
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
                    case "targetFov": float.TryParse(val, out targetFov); break;
                    case "speedHack": speedHack = val == "1"; break;
                    case "speedMultiplier": float.TryParse(val, out speedMultiplier); break;
                    case "flyHack": flyHack = val == "1"; break;
                    case "noClip": noClip = val == "1"; break;
                    case "weaponUnlock": weaponUnlock = val == "1"; break;
                    case "thirdPerson": thirdPerson = val == "1"; break;
                    case "thirdPersonDistance": float.TryParse(val, out thirdPersonDistance); break;
                    case "chams": chams = val == "1"; break;
                    case "triggerbot": triggerbot = val == "1"; break;
                    case "triggerbotRange": float.TryParse(val, out triggerbotRange); break;
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
                    case "debugLogging": debugLogging = val == "1"; break;
                    case "heavyDiagnostics": heavyDiagnostics = val == "1"; break;
                    case "showRuntimeStatus": showRuntimeStatus = val == "1"; break;
                    case "menuX": menuRect.x = ParseFloat(val, menuRect.x); break;
                    case "menuY": menuRect.y = ParseFloat(val, menuRect.y); break;
                }
            }

            instance?.Log.LogInfo($"Config loaded from {ConfigPath}");
        }
        catch (Exception e)
        {
            instance?.Log.LogWarning($"Config load failed: {e.Message}");
        }
    }

    private static void SaveConfig()
    {
        try
        {
            var lines = new[]
            {
                $"espEnabled={(espEnabled ? 1 : 0)}",
                $"showHealth={(showHealth ? 1 : 0)}",
                $"showTeammates={(showTeammates ? 1 : 0)}",
                $"aimbotEnabled={(aimbotEnabled ? 1 : 0)}",
                $"aimActivationMode={aimActivationMode}",
                $"aimStyle={aimStyle}",
                $"aimbotFov={aimbotFov:0.###}",
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
                $"targetFov={targetFov:0.###}",
                $"speedHack={(speedHack ? 1 : 0)}",
                $"speedMultiplier={speedMultiplier:0.###}",
                $"flyHack={(flyHack ? 1 : 0)}",
                $"noClip={(noClip ? 1 : 0)}",
                $"weaponUnlock={(weaponUnlock ? 1 : 0)}",
                $"thirdPerson={(thirdPerson ? 1 : 0)}",
                $"thirdPersonDistance={thirdPersonDistance:0.###}",
                $"chams={(chams ? 1 : 0)}",
                $"triggerbot={(triggerbot ? 1 : 0)}",
                $"triggerbotRange={triggerbotRange:0.###}",
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
                $"debugLogging={(debugLogging ? 1 : 0)}",
                $"heavyDiagnostics={(heavyDiagnostics ? 1 : 0)}",
                $"showRuntimeStatus={(showRuntimeStatus ? 1 : 0)}",
                $"menuX={menuRect.x:0.###}",
                $"menuY={menuRect.y:0.###}"
            };

            File.WriteAllLines(ConfigPath, lines);
        }
        catch (Exception e)
        {
            instance?.Log.LogWarning($"Config save failed: {e.Message}");
        }
    }

    private static int ParseInt(string s, int fallback) => int.TryParse(s, out var v) ? v : fallback;
    private static float ParseFloat(string s, float fallback) => float.TryParse(s, out var v) ? v : fallback;

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
            if (!IsVisibleTarget(player, mainPlayer, false, false) || !TryGetHeadPosition(player, out var headPosition))
            {
                continue;
            }

            var direction = headPosition - camera.transform.position;
            var angle = Vector3.Angle(camera.transform.forward, direction);
            if (angle <= bestAngle && (!requireLos || HasLineOfSight(camera, player, headPosition)))
            {
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

        // Aiming at the raw head bone (neck pivot) misses when the target looks up/down
        // because the visible head is offset from that pivot. Use the head collider or
        // renderer bounds center to aim at the actual head hitbox.
        var collider = head.GetComponentInChildren<Collider>();
        if (collider != null)
        {
            position = collider.bounds.center;
            return true;
        }

        var renderer = head.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            position = renderer.bounds.center;
            return true;
        }

        position = head.transform.position;
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
        // Draggable title bar.
        var headerRect = new Rect(menuRect.x, menuRect.y, menuRect.width, 24);
        GUI.Box(menuRect, "Blockpost Legacy Trainer");
        GUI.Label(new Rect(menuRect.x + 8, menuRect.y + 2, menuRect.width - 16, 20), "Blockpost Legacy Trainer (drag)");

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

        var x = menuRect.x + 20;
        var y = menuRect.y + 30;
        var w = menuRect.width - 40;

        GUI.Label(new Rect(x, y, w, 24), "Offline bot-game feature port"); y += 30;
        espEnabled = GUI.Toggle(new Rect(x, y, w, 24), espEnabled, "ESP boxes"); y += 26;
        if (espEnabled)
        {
            showHealth = GUI.Toggle(new Rect(x + 20, y, w - 20, 24), showHealth, "Show health"); y += 26;
            showTeammates = GUI.Toggle(new Rect(x + 20, y, w - 20, 24), showTeammates, "Show teammates"); y += 26;
        }

        aimbotEnabled = GUI.Toggle(new Rect(x, y, w, 24), aimbotEnabled, "Aimbot"); y += 26;
        if (aimbotEnabled)
        {
            GUI.Label(new Rect(x, y, w, 24), "Aimbot activation:"); y += 24;
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

            GUI.Label(new Rect(x, y, w, 24), $"Aimbot FOV: {aimbotFov:0} degrees"); y += 24;
            aimbotFov = GUI.HorizontalSlider(new Rect(x, y, w, 24), aimbotFov, MinimumAimbotFov, MaximumAimbotFov); y += 28;
            autoShoot = GUI.Toggle(new Rect(x, y, w, 24), autoShoot, "Auto shoot (Win32 input)"); y += 26;
            if (autoShoot)
            {
                rapidFire = GUI.Toggle(new Rect(x + 20, y, w - 20, 24), rapidFire, "Rapid fire (1 shot/tick)"); y += 26;
            }

            ghostBullets = GUI.Toggle(new Rect(x + 20, y, w - 20, 24), ghostBullets, "Ghost bullets (hit through walls)"); y += 26;

            if (aimStyle == 0 && !ghostBullets)
            {
                serverTrustTest = GUI.Toggle(new Rect(x + 20, y, w - 20, 24), serverTrustTest, "Server trust test — fake hit packets"); y += 26;
            }
        }

        noRecoil = GUI.Toggle(new Rect(x, y, w, 24), noRecoil, "No recoil"); y += 26;
        infiniteHealth = GUI.Toggle(new Rect(x, y, w, 24), infiniteHealth, "Infinite health"); y += 26;
        infiniteAmmo = GUI.Toggle(new Rect(x, y, w, 24), infiniteAmmo, "Infinite ammo"); y += 26;
        instantReload = GUI.Toggle(new Rect(x, y, w, 24), instantReload, $"Instant reload ({instantReloads})"); y += 26;
        bunnyHop = GUI.Toggle(new Rect(x, y, w, 24), bunnyHop, "Bunny hop"); y += 26;
        fovChanger = GUI.Toggle(new Rect(x, y, w, 24), fovChanger, "FOV changer"); y += 26;
        if (fovChanger)
        {
            GUI.Label(new Rect(x, y, w, 20), $"FOV: {targetFov:0}");
            targetFov = GUI.HorizontalSlider(new Rect(x + 60, y + 4, w - 60, 20), targetFov, 60f, 120f);
            y += 26;
        }
        customCrosshair = GUI.Toggle(new Rect(x, y, w, 24), customCrosshair, "Custom crosshair"); y += 26;
        speedHack = GUI.Toggle(new Rect(x, y, w, 24), speedHack, "Speed hack"); y += 26;
        if (speedHack)
        {
            GUI.Label(new Rect(x, y, w, 20), $"Speed: {speedMultiplier:0.0}x");
            speedMultiplier = GUI.HorizontalSlider(new Rect(x + 60, y + 4, w - 60, 20), speedMultiplier, 0.5f, 5f);
            y += 26;
        }
        flyHack = GUI.Toggle(new Rect(x, y, w, 24), flyHack, "Fly hack (Space=up, Shift=down)"); y += 26;
        noClip = GUI.Toggle(new Rect(x, y, w, 24), noClip, "No clip"); y += 26;
        weaponUnlock = GUI.Toggle(new Rect(x, y, w, 24), weaponUnlock, $"Unlock all weapons ({(weaponUnlockApplied ? weaponUnlockCount.ToString() : "off")})"); y += 26;
        thirdPerson = GUI.Toggle(new Rect(x, y, w, 24), thirdPerson, "Third person camera"); y += 26;
        if (thirdPerson)
        {
            GUI.Label(new Rect(x, y, w, 20), $"Distance: {thirdPersonDistance:0.0}");
            thirdPersonDistance = GUI.HorizontalSlider(new Rect(x + 80, y + 4, w - 80, 20), thirdPersonDistance, 2f, 10f);
            y += 26;
        }
        chams = GUI.Toggle(new Rect(x, y, w, 24), chams, "Chams (see players through walls)"); y += 26;
        triggerbot = GUI.Toggle(new Rect(x, y, w, 24), triggerbot, "Triggerbot (auto-fire on crosshair)"); y += 26;
        if (triggerbot)
        {
            GUI.Label(new Rect(x, y, w, 20), $"Range: {triggerbotRange:0}m");
            triggerbotRange = GUI.HorizontalSlider(new Rect(x + 80, y + 4, w - 80, 20), triggerbotRange, 50f, 500f);
            y += 26;
        }
        fullbright = GUI.Toggle(new Rect(x, y, w, 24), fullbright, "Fullbright (no fog, max light)"); y += 26;
        antiFlash = GUI.Toggle(new Rect(x, y, w, 24), antiFlash, "Anti-flashbang (block screen flash)"); y += 26;
        wallhack = GUI.Toggle(new Rect(x, y, w, 24), wallhack, "Wallhack (tracer lines + distance)"); y += 26;
        noSpread = GUI.Toggle(new Rect(x, y, w, 24), noSpread, "No spread (zero recoil accumulator)"); y += 26;
        fastFire = GUI.Toggle(new Rect(x, y, w, 24), fastFire, "Fast fire rate (zero fire timer)"); y += 26;
        autoReload = GUI.Toggle(new Rect(x, y, w, 24), autoReload, "Auto-reload (reload when empty)"); y += 26;
        nameEsp = GUI.Toggle(new Rect(x, y, w, 24), nameEsp, "Name ESP (show player names)"); y += 26;
        spinbot = GUI.Toggle(new Rect(x, y, w, 24), spinbot, "Spinbot (anti-aim yaw spin)"); y += 26;
        skeletonEsp = GUI.Toggle(new Rect(x, y, w, 24), skeletonEsp, "Skeleton ESP (bone tracing)"); y += 26;
        radarHack = GUI.Toggle(new Rect(x, y, w, 24), radarHack, "Radar hack (mini-map all players)"); y += 26;
        antiAimPitch = GUI.Toggle(new Rect(x, y, w, 24), antiAimPitch, "Anti-aim pitch (fake look up/down)"); y += 26;
        autoStrafe = GUI.Toggle(new Rect(x, y, w, 24), autoStrafe, "Auto-strafe (dodge pattern)"); y += 26;
        killFeed = GUI.Toggle(new Rect(x, y, w, 24), killFeed, "Kill feed (log kills on screen)"); y += 26;
        edgeJump = GUI.Toggle(new Rect(x, y, w, 24), edgeJump, "Edge jump (auto-jump at ledges)"); y += 26;
        fakeLag = GUI.Toggle(new Rect(x, y, w, 24), fakeLag, "Fake lag (delay position updates)"); y += 26;
        spectatorWarning = GUI.Toggle(new Rect(x, y, w, 24), spectatorWarning, "Spectator warning (alert when watched)"); y += 26;
        debugLogging = GUI.Toggle(new Rect(x, y, w, 24), debugLogging, $"Verbose diagnostics (summary only, 1/{DiagnosticInterval:0}s)"); y += 26;
        if (debugLogging)
        {
            heavyDiagnostics = GUI.Toggle(new Rect(x + 20, y, w - 20, 24), heavyDiagnostics, "+ full field sweep (COSTLY: budgeted, still slows the game)"); y += 26;
        }
        showRuntimeStatus = GUI.Toggle(new Rect(x, y, w, 24), showRuntimeStatus, "Show runtime status"); y += 26;
        if (showRuntimeStatus)
        {
            GUI.Label(new Rect(x, y, w, 24), $"Update: {(controllerRunning ? "running" : "waiting")} | Boxes: {espBoxes.Count} | {featureStatus}"); y += 24;
            GUI.Label(new Rect(x, y, w, 24), $"Aimbot: {aimStatus}"); y += 24;
        }

        // Resize the menu to fit the content.
        menuRect.height = y - menuRect.y + 10;

        // Auto-save config whenever any menu control was interacted with.
        if (GUI.changed)
        {
            SaveConfig();
        }
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
        if (autoReload) features.Add("AutoReload");
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
        if (ghostBullets) features.Add("GhostBullets");

        if (features.Count == 0) return;

        var prevColor = GUI.color;
        GUI.color = new Color(0f, 1f, 0f, 0.7f);
        var y = 5f;
        GUI.Label(new Rect(5, y, 300, 20), $"Blockpost Trainer [{features.Count} active]");
        y += 20;
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
