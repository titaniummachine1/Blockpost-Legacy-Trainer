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
        public EspBox(Rect bounds, Color color, int health)
        {
            Bounds = bounds;
            Color = color;
            Health = health;
        }

        public Rect Bounds { get; }
        public Color Color { get; }
        public int Health { get; }
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
        // Reset autoShootPending. If UpdateAimbotSafely sets it again (target found),
        // the postfix will send mouse_event to keep the button held.
        autoShootPending = false;
        LogControllerStartup();
        ApplyInstantReload();
        UpdateAimbotSafely();
        ApplyCheatFeatures();
        PrepareRapidFirePrefix();
    }

    private static void ControllerUpdatePostfix(Controll __instance)
    {
        // Auto-shoot: send LEFTUP+LEFTDOWN each frame to simulate rapid clicking.
        // Only do this if the player is alive and has a weapon — don't hold the
        // virtual mouse button while dead (causes firing at wall after respawn).
        var main = Controll.HGAODFPBGLB;
        var alive = main != null && main.FDOJDJLIGLF > 0 && main.JPGGPPLOOML != null;

        if (autoShootPending && alive)
        {
            mouse_event(MouseEventFLeftUp, 0, 0, 0, 0);
            mouse_event(MouseEventFLeftDown, 0, 0, 0, 0);
            pendingLeftMouseUp = true;
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
        LogRuntimeDiagnostics();
        LogInventoryDump();
        DumpAllPlayerWeapons();
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
        if (!infiniteHealth && !infiniteAmmo)
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
                // Clear potential "dead" / "down" flags so the game doesn't keep us in
                // the death state even after health is restored.
                main.CLOEJLAOIGI = false;
                main.CGHKKDBILGF = false;
            }

            // Infinite ammo is intentionally left as a no-op for now while we identify
            // the correct ammo fields. Writing the wrong offsets causes "NO WEAPON" state.
            // The values are logged below in LogRuntimeDiagnostics.
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

    private static void LogInventoryDump()
    {
        if (Input.GetKeyDown(KeyCode.F9))
        {
            DumpInventoryNow();
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
            return;
        }

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
        var color = player.MMMGPDBMOLM == mainPlayer.MMMGPDBMOLM ? Color.green : Color.red;
        box = new EspBox(new Rect(top.x - width / 2, Mathf.Min(topY, bottomY), width, height), color, player.FDOJDJLIGLF);
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
        if (!menuVisible && !espEnabled)
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
        infiniteAmmo = GUI.Toggle(new Rect(x, y, w, 24), infiniteAmmo, "Infinite ammo (log only — identifying correct fields)"); y += 26;
        instantReload = GUI.Toggle(new Rect(x, y, w, 24), instantReload, $"Instant reload (EXPERIMENTAL: only hides the minigame bar — {instantReloads})"); y += 26;
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

    private static void DrawEspBoxes()
    {
        if (!espEnabled)
        {
            return;
        }

        var previousColor = GUI.color;
        foreach (var box in espBoxes)
        {
            GUI.color = box.Color;
            GUI.Box(box.Bounds, string.Empty);
            if (showHealth)
            {
                GUI.Label(new Rect(box.Bounds.xMax + 4, box.Bounds.y, 80, 24), $"HP {box.Health}");
            }
        }

        GUI.color = previousColor;
    }
}
