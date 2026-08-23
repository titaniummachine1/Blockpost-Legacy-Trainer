using System;
using System.Collections.Generic;
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

    private const float AimbotStrength = 14f;
    private const float MinimumAimbotFov = 1f;
    private const float MaximumAimbotFov = 180f;
    private const float DiagnosticInterval = 1f;
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
        "Silent aim (pending)"
    };
    private static Plugin? instance;
    private static bool menuVisible;
    private static bool bootstrapLogged;
    private static bool bindingsLogged;
    private static bool featureFailureLogged;
    private static bool controllerRunning;
    private static bool showRuntimeStatus = true;
    private static bool debugLogging;
    private static float nextDiagnosticTime;
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
    private static int recoilCalls;
    private static int recoilSuppressed;
    private static bool guiFailureLogged;
    private static bool pendingLeftMouseUp;
    private static bool forceShotThisFrame;
    private static bool rapidFireFailureLogged;
    private static bool espEnabled = true;
    private static bool showHealth = true;
    private static bool showTeammates;
    private static bool aimbotEnabled = true;
    private static bool noRecoil;
    private static bool autoShoot = true;
    private static bool rapidFire;
    private static bool infiniteHealth;
    private static bool infiniteAmmo;
    private static float nextAutoShootTime;
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
            var guiUpdateMethod = AccessTools.Method(guiInvType, "Update");
            if (guiUpdateMethod != null)
            {
                harmony.Patch(guiUpdateMethod, postfix: new HarmonyMethod(typeof(Plugin), nameof(GUIInvUpdatePostfix)));
                Log.LogInfo("Patched GUIInv.Update for inventory menu support.");
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

        Log.LogInfo($"Patched Controll.Update, Controll.OnGUI, and recoil hook: {recoilMethod != null}. Auto-shoot uses direct PLH.CDEGJOBLOFO when rapid fire is active.");

        NetProbe.Install(harmony, Log);
    }

    private static void ControllerUpdatePrefix()
    {
        controllerRunning = true;
        forceShotThisFrame = false;
        LogControllerStartup();
        UpdateAimbotSafely();
        ApplyCheatFeatures();
        PrepareRapidFirePrefix();
    }

    private static void ControllerUpdatePostfix(Controll __instance)
    {
        NetProbe.Tick();
        FieldWatch.Tick(__instance);
        ReleaseLeftMouseIfNeeded();
        ForceRapidFireShot();
        ToggleMenuIfRequested();
        ApplyCheatFeatures();
        LogRuntimeDiagnostics();
        LogInventoryDump();
    }

    private static void ReleaseLeftMouseIfNeeded()
    {
        if (pendingLeftMouseUp)
        {
            mouse_event(MouseEventFLeftUp, 0, 0, 0, 0);
            pendingLeftMouseUp = false;
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
                return;
            }

            main.FGFKPMPLNKO = -1000f;
            var fireParam = Mathf.Max(Controll.LCMOBPPHLLM, 1f);
            PLH.CDEGJOBLOFO(main, fireParam, false, false);
            main.FGFKPMPLNKO = 1000f;
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

    private static void LogControllerStartup()
    {
        if (bootstrapLogged)
        {
            return;
        }

        instance?.Log.LogInfo("Blockpost controller update is running.");
        bootstrapLogged = true;
    }

    private static void ToggleMenuIfRequested()
    {
        if (!Input.GetKeyDown(KeyCode.Home))
        {
            return;
        }

        menuVisible = !menuVisible;
        instance?.Log.LogInfo($"Trainer menu {(menuVisible ? "opened" : "closed")}.");
    }

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
                instance?.Log.LogInfo($"[Diagnostics][Camera] name={camera.name}, enabled={camera.enabled}, active={camera.gameObject.activeInHierarchy}, depth={camera.depth}, pos={camera.transform.position}");
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

    private static void LogInventoryDump()
    {
        if (!Input.GetKeyDown(KeyCode.F9))
        {
            return;
        }

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

    private static void GUIInvUpdatePostfix()
    {
        NetProbe.Tick();
        LogInventoryDump();
    }

    private static void LogRuntimeDiagnostics()
    {
        if (!debugLogging || Time.unscaledTime < nextDiagnosticTime)
        {
            return;
        }

        nextDiagnosticTime = Time.unscaledTime + DiagnosticInterval;
        try
        {
            var players = PLH.BAKLNPIEHMI;
            var mainPlayer = Controll.HGAODFPBGLB;
            var camera = ResolveCamera();
            instance?.Log.LogInfo($"[Diagnostics] menu={menuVisible}, esp={espEnabled}, aimbot={aimbotEnabled}, aimMode={AimActivationLabels[aimActivationMode]}, aimStyle={AimStyleLabels[aimStyle]}, fovDegrees={aimbotFov:0}, noRecoil={noRecoil}, infiniteHealth={infiniteHealth}, infiniteAmmo={infiniteAmmo}, rapidFire={rapidFire}, leftMouse={Input.GetMouseButton(0)}, rightMouse={Input.GetMouseButton(1)}, players={(players == null ? "null" : players.Length.ToString())}, mainPlayer={(mainPlayer == null ? "null" : "present")}, camera={(camera == null ? "null" : camera.name)}, featureStatus={featureStatus}, aimStatus={aimStatus}, aimTargetIndex={lastAimTargetIndex}, aimTargetPos={lastAimTargetPosition}, recoilCalls={recoilCalls}, recoilSuppressed={recoilSuppressed}.");
            LogControllCandidates();
            LogPlayers(players, mainPlayer, camera);
            LogAmmoStatus(mainPlayer);
        }
        catch (Exception exception)
        {
            instance?.Log.LogWarning($"[Diagnostics] Runtime inspection failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void LogControllCandidates()
    {
        foreach (var property in typeof(Controll).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            try
            {
                var value = property.GetValue(null);
                if (value != null && IsDiagnosticType(property.PropertyType))
                {
                    instance?.Log.LogInfo($"[Diagnostics][Controll] {property.Name}={FormatDiagnosticValue(value)}");
                }
            }
            catch (Exception exception)
            {
                instance?.Log.LogWarning($"[Diagnostics][Controll] {property.Name} failed: {exception.GetType().Name}");
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

        for (var index = 0; index < players.Length; index++)
        {
            LogPlayer(index, players[index], mainPlayer, camera, dumpFields && index < 5);
        }
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
            instance?.Log.LogInfo($"[Diagnostics][Player] index={index}, value=null");
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
            instance?.Log.LogInfo($"[Diagnostics][Player] index={index}, main={isMain}, team={player.MMMGPDBMOLM}, health={player.FDOJDJLIGLF}, sameTeam={sameTeam}, head={(head == null ? "null" : head.name)}, headPos={headPosition}, screenPos={screenPosition}");
            LogPlayerObjectCandidates(player, camera);
            if (dumpFields || isMain)
            {
                LogPlayerPrimitiveFields(player);
            }
        }
        catch (Exception exception)
        {
            instance?.Log.LogWarning($"[Diagnostics][Player] index={index} failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void LogPlayerPrimitiveFields(KBBBHJDINCB player)
    {
        foreach (var property in typeof(KBBBHJDINCB).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0 || !IsPlayerPrimitiveType(property.PropertyType))
            {
                continue;
            }

            try
            {
                var value = property.GetValue(player);
                instance?.Log.LogInfo($"[Diagnostics][PlayerField] property={property.Name}, type={property.PropertyType.Name}, value={(value == null ? "null" : FormatDiagnosticValue(value))}");
            }
            catch (Exception exception)
            {
                instance?.Log.LogWarning($"[Diagnostics][PlayerField] property={property.Name} failed: {exception.GetType().Name}");
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
            instance?.Log.LogInfo($"[Diagnostics][Object] field={candidate.Name}, name={candidate.Value.name}, pos={position}, screenPos={screenPosition}");
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
                instance?.Log.LogWarning($"[Diagnostics] Aimbot update failed: {exception}");
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
                instance?.Log.LogWarning($"[Diagnostics] ESP update failed: {exception}");
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
            if (!IsVisibleTarget(player, mainPlayer, showTeammates))
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

    private static bool IsVisibleTarget(KBBBHJDINCB? player, KBBBHJDINCB mainPlayer, bool includeTeammates)
    {
        return player != null
            && !player._LCEIAGLFFJN_k__BackingField
            && player.FDOJDJLIGLF > 0
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

        for (var index = 0; index < players.Length; index++)
        {
            var player = players[index];
            if (!IsVisibleTarget(player, mainPlayer, false) || !TryGetHeadPosition(player, out var headPosition))
            {
                continue;
            }

            var direction = headPosition - camera.transform.position;
            var angle = Vector3.Angle(camera.transform.forward, direction);
            if (angle <= bestAngle && HasLineOfSight(camera, player, headPosition))
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

        var targetDirection = bestPosition - camera.transform.position;
        var targetRotation = Quaternion.LookRotation(targetDirection);
        ApplyAimRotation(camera, targetRotation);
        TryAutoShoot();
        aimStatus = $"target={lastAimTargetIndex}, angle={bestAngle:0.0} degrees";
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

    private static void TryAutoShoot()
    {
        if (!autoShoot || Time.unscaledTime < nextAutoShootTime)
        {
            return;
        }

        if (!Application.isFocused)
        {
            return;
        }

        if (rapidFire)
        {
            forceShotThisFrame = true;
            nextAutoShootTime = Time.unscaledTime;
            aimStatus = $"{aimStatus} | rapid-fire queued";
            return;
        }

        // Only inject a click when the user is not already holding fire.
        if (Input.GetMouseButton(0))
        {
            return;
        }

        // Simulate a left mouse click so the game's own input handling fires the weapon.
        mouse_event(MouseEventFLeftDown, 0, 0, 0, 0);
        mouse_event(MouseEventFLeftUp, 0, 0, 0, 0);
        nextAutoShootTime = Time.unscaledTime + 0.12f;
        aimStatus = $"{aimStatus} | auto-shoot triggered";
    }

    private static bool TryGetHeadPosition(KBBBHJDINCB player, out Vector3 position)
    {
        position = Vector3.zero;
        var head = player.ACEHIBLPHCA;
        if (head == null || head.transform == null)
        {
            return false;
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
                instance?.Log.LogWarning($"[Diagnostics] Trainer GUI draw failed: {exception}");
                guiFailureLogged = true;
            }
        }
    }

    private static void DrawTrainerMenu()
    {
        GUI.Box(new Rect(20, 20, 500, 600), "Blockpost Legacy Trainer");
        GUI.Label(new Rect(40, 54, 460, 24), "Offline bot-game feature port");
        espEnabled = GUI.Toggle(new Rect(40, 84, 460, 24), espEnabled, "ESP boxes");
        if (espEnabled)
        {
            showHealth = GUI.Toggle(new Rect(60, 108, 440, 24), showHealth, "Show health");
            showTeammates = GUI.Toggle(new Rect(60, 132, 440, 24), showTeammates, "Show teammates");
        }

        aimbotEnabled = GUI.Toggle(new Rect(40, 162, 460, 24), aimbotEnabled, "Aimbot");
        if (aimbotEnabled)
        {
            GUI.Label(new Rect(40, 186, 460, 24), "Aimbot activation:");
            if (GUI.Button(new Rect(40, 210, 460, 32), $"Aim key: {AimActivationLabels[aimActivationMode]}"))
            {
                aimActivationMode = (aimActivationMode + 1) % AimActivationLabels.Length;
                instance?.Log.LogInfo($"Aimbot activation changed to {AimActivationLabels[aimActivationMode]}.");
            }

            if (GUI.Button(new Rect(40, 246, 460, 32), $"Aim style: {AimStyleLabels[aimStyle]}"))
            {
                aimStyle = (aimStyle + 1) % AimStyleLabels.Length;
                instance?.Log.LogInfo($"Aimbot style changed to {AimStyleLabels[aimStyle]}.");
            }

            GUI.Label(new Rect(40, 284, 460, 24), $"Aimbot FOV: {aimbotFov:0} degrees");
            aimbotFov = GUI.HorizontalSlider(new Rect(40, 308, 460, 24), aimbotFov, MinimumAimbotFov, MaximumAimbotFov);
            autoShoot = GUI.Toggle(new Rect(40, 340, 460, 24), autoShoot, "Auto shoot (Win32 input)");
            if (autoShoot)
            {
                rapidFire = GUI.Toggle(new Rect(60, 364, 440, 24), rapidFire, "Rapid fire (1 shot/tick)");
            }
        }

        noRecoil = GUI.Toggle(new Rect(40, 394, 460, 24), noRecoil, "No recoil");
        infiniteHealth = GUI.Toggle(new Rect(40, 424, 460, 24), infiniteHealth, "Infinite health");
        infiniteAmmo = GUI.Toggle(new Rect(40, 454, 460, 24), infiniteAmmo, "Infinite ammo (log only — identifying correct fields)");
        debugLogging = GUI.Toggle(new Rect(40, 484, 460, 24), debugLogging, "Verbose diagnostics (logs every second)");
        showRuntimeStatus = GUI.Toggle(new Rect(40, 514, 460, 24), showRuntimeStatus, "Show runtime status");
        if (showRuntimeStatus)
        {
            GUI.Label(new Rect(40, 538, 460, 24), $"Update: {(controllerRunning ? "running" : "waiting")} | Boxes: {espBoxes.Count} | {featureStatus}");
            GUI.Label(new Rect(40, 562, 460, 24), $"Aimbot: {aimStatus}");
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
