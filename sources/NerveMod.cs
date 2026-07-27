using System;
using System.Reflection;
using BoneLib;
using HarmonyLib;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;
using BoneMenuPage = BoneLib.BoneMenu.Page;

// CRITICAL: block MelonLoader auto-PatchAll at HarmonyInit (pre-avatar native crash).
[assembly: HarmonyDontPatchAll]

[assembly: MelonInfo(typeof(BePrime.Nerve.NerveMod), BePrime.Nerve.BuildInfo.Name, BePrime.Nerve.BuildInfo.Version, BePrime.Nerve.BuildInfo.Author, BePrime.Nerve.BuildInfo.DownloadLink)]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace BePrime.Nerve;

public class NerveMod : MelonMod
{
    public static bool Enabled = true;
    public static bool SyncWrist = true;
    public static bool SyncBones;
    public static bool ForceFullSkeleton;
    public static bool GripFromFingers = true;
    public static bool PinchLoco = true;

    private static readonly Color Accent = new Color(0.95f, 0.35f, 0.12f);
    private static readonly Color AccentAlt = new Color(0.20f, 0.85f, 0.65f);
    private static readonly Color LocoAccent = new Color(0.35f, 0.75f, 1f);

    private static int _patchStage; // 0=off 1=curls 2=+loco 3=done
    private static bool _levelSeen;
    private static float _armAt = -1f;
    private static float _nextStageAt = -1f;
    private static float _nextHeartbeatAt = -1f;
    private const float ArmDelaySeconds = 3.0f;
    private const float StageGapSeconds = 3.0f;

    public static NerveMod Instance { get; private set; }

    public override void OnInitializeMelon()
    {
        Instance = this;
        MelonLogger.Msg("NERVE boot — OnInitializeMelon");
        MelonLogger.Msg("NERVE HarmonyDontPatchAll — no Melon auto-patch");

        try
        {
            Prefs.Create();
            // Force safe defaults so old MelonPreferences can't re-enable crashy paths.
            SyncWrist = true;
            SyncBones = false;
            ForceFullSkeleton = false;
            MelonLogger.Msg("NERVE defaults: Wrist ON, Bones/Skeleton OFF");
            Prefs.MarkDirty();
            HandPerms.EnsureRequested();
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"NERVE prefs FAILED: {ex}");
        }

        Hooking.OnLevelLoaded += OnLevelLoaded;
        Hooking.OnLevelUnloaded += OnLevelUnloaded;
        MelonLogger.Msg("NERVE hooks subscribed");

        BuildMenu();
        MelonLogger.Msg("NERVE init done — patches staged AFTER avatar spawn");
        MelonLogger.Msg("Telegram: @be_primex");
    }

    private static void OnLevelLoaded(LevelInfo _)
    {
        _levelSeen = true;
        _patchStage = 0;
        _armAt = Time.unscaledTime + ArmDelaySeconds;
        _nextStageAt = -1f;
        HandSync.OnLevelLoaded();
        MelonLogger.Msg($"NERVE level loaded — stage1 in {ArmDelaySeconds:0.0}s");
    }

    private static void OnLevelUnloaded()
    {
        MelonLogger.Msg("NERVE level unloaded");
        _levelSeen = false;
        _armAt = -1f;
        _nextStageAt = -1f;
        Instance?.DisarmHarmony();
        HandSync.OnLevelUnloaded();
    }

    public override void OnUpdate()
    {
        Prefs.Tick();

        if (!_levelSeen)
            return;

        float now = Time.unscaledTime;

        if (_patchStage > 0 && now >= _nextHeartbeatAt)
        {
            _nextHeartbeatAt = now + 5f;
            MelonLogger.Msg($"NERVE heartbeat stage={_patchStage} liveL={HandSync.LiveLeft} liveR={HandSync.LiveRight} | {HandPerms.ProbeLine()} | {UnityXrHands.ProbeLine()} | {OvrHands.ProbeLine()}");
        }

        if (_patchStage == 0)
        {
            if (_armAt < 0f || now < _armAt)
                return;
            if (!RigReady(ref _armAt))
                return;
            ArmStage1_CurlsOnly();
            HandPerms.EnsureRequested();
            OvrHands.EnsureConfigured();
            UnityXrHands.ForceMarrowHandMaps();
            MelonLogger.Msg("NERVE probe: " + HandPerms.ProbeLine() + " | " + UnityXrHands.ProbeLine() + " | " + OvrHands.ProbeLine());
            return;
        }

        if (_patchStage == 1 && now >= _nextStageAt)
        {
            if (!RigReady(ref _nextStageAt))
                return;
            ArmStage2_PinchLoco();
            return;
        }
    }

    private static bool RigReady(ref float retryAt)
    {
        try
        {
            if (Player.HandsExist && Player.ControllerRig != null)
                return true;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"NERVE rig wait: {ex.Message}");
        }

        retryAt = Time.unscaledTime + 0.5f;
        return false;
    }

    private void ArmStage1_CurlsOnly()
    {
        MelonLogger.Msg("NERVE stage1 — patch OpenController curls + wrist (OVR)");
        try
        {
            PatchPostfix(typeof(OpenController), nameof(OpenController.OnUpdate), typeof(HandSync), "OnUpdatePatch");
            // Wrist is often finalized in OnVrFixedUpdate after OnUpdate — patch it too.
            try
            {
                PatchPostfix(typeof(OpenController), nameof(OpenController.OnVrFixedUpdate), typeof(HandSync), "OnVrFixedUpdatePatch");
            }
            catch (Exception wrEx)
            {
                MelonLogger.Warning($"NERVE OnVrFixedUpdate patch skipped: {wrEx.Message}");
            }

            _patchStage = 1;
            _nextStageAt = Time.unscaledTime + StageGapSeconds;
            _nextHeartbeatAt = Time.unscaledTime + 2f;
            MelonLogger.Msg("NERVE stage1 OK — OVR hand bridge armed");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"NERVE stage1 FAILED: {ex}");
            _armAt = Time.unscaledTime + 2f;
            _patchStage = 0;
        }
    }

    private void ArmStage2_PinchLoco()
    {
        MelonLogger.Msg("NERVE stage2 — patch OpenControllerRig.OnUpdate (pinch walk)");
        try
        {
            PatchPostfix(typeof(OpenControllerRig), nameof(OpenControllerRig.OnUpdate), typeof(PinchLoco), "RigOnUpdatePatch");
            _patchStage = 2;
            _nextHeartbeatAt = Time.unscaledTime + 2f;
            MelonLogger.Msg("NERVE stage2 OK — curls + pinch walk + OVR wrist");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"NERVE stage2 FAILED: {ex}");
            _nextStageAt = Time.unscaledTime + 2f;
        }
    }

    private void PatchPostfix(Type target, string methodName, Type container, string nestedName)
    {
        MethodInfo targetMethod = AccessTools.Method(target, methodName);
        if (targetMethod == null)
            throw new MissingMethodException(target.FullName, methodName);

        Type nested = container.GetNestedType(nestedName, BindingFlags.NonPublic | BindingFlags.Public);
        if (nested == null)
            throw new TypeLoadException(container.Name + "." + nestedName);

        MethodInfo postfix = AccessTools.Method(nested, "Postfix");
        if (postfix == null)
            throw new MissingMethodException(nested.FullName, "Postfix");

        HarmonyInstance.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
        MelonLogger.Msg($"NERVE patched {target.Name}.{methodName}");
    }

    private void DisarmHarmony()
    {
        if (_patchStage == 0)
            return;

        try
        {
            HarmonyInstance.UnpatchSelf();
            MelonLogger.Msg("NERVE Harmony disarmed");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"NERVE UnpatchSelf FAILED: {ex}");
        }

        _patchStage = 0;
    }

    public override void OnLateUpdate()
    {
        if (_patchStage < 1)
            return;
        // Always drive OVR→controllers in late update (even if SyncBones is off).
        HandSync.LateTick();
    }

    public override void OnDeinitializeMelon()
    {
        MelonLogger.Msg("NERVE deinit");
        DisarmHarmony();
        HandSync.OnEnabledChanged(false);
        Prefs.FlushNow();
    }

    private static void BuildMenu()
    {
        try
        {
            BoneMenuPage root = BoneMenuPage.Root.CreatePage("NERVE", Accent, 64, true);

            root.CreateBool("Enabled", Accent, Enabled, val =>
            {
                Enabled = val;
                HandSync.OnEnabledChanged(val);
                Prefs.MarkDirty();
            });

            root.CreateBool("Sync Wrist", AccentAlt, SyncWrist, val =>
            {
                SyncWrist = val;
                Prefs.MarkDirty();
                MelonLogger.Msg($"NERVE SyncWrist={val}");
            });

            root.CreateBool("Sync Bones", AccentAlt, SyncBones, val =>
            {
                SyncBones = val;
                Prefs.MarkDirty();
                MelonLogger.Msg($"NERVE SyncBones={val}");
            });

            root.CreateBool("Full Skeleton", AccentAlt, ForceFullSkeleton, val =>
            {
                ForceFullSkeleton = val;
                Prefs.MarkDirty();
                MelonLogger.Msg($"NERVE FullSkeleton={val}");
            });

            root.CreateBool("Grip From Fingers", AccentAlt, GripFromFingers, val =>
            {
                GripFromFingers = val;
                Prefs.MarkDirty();
            });

            root.CreateBool("Pinch Walk", LocoAccent, PinchLoco, val =>
                {
                    PinchLoco = val;
                    Prefs.MarkDirty();
                });

            MelonLogger.Msg("NERVE BoneMenu ok");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"NERVE BoneMenu FAILED: {ex}");
        }
    }
}
