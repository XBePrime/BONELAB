using System;
using BoneLib;
using MelonLoader;
using UnityEngine;
using BoneMenuPage = BoneLib.BoneMenu.Page;

// CRITICAL: without this, MelonLoader auto-PatchAll's every [HarmonyPatch] at HarmonyInit
// (before the avatar exists) and Quest native-crashes on spawn with no useful log.
[assembly: HarmonyDontPatchAll]

[assembly: MelonInfo(typeof(BePrime.Nerve.NerveMod), BePrime.Nerve.BuildInfo.Name, BePrime.Nerve.BuildInfo.Version, BePrime.Nerve.BuildInfo.Author, BePrime.Nerve.BuildInfo.DownloadLink)]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace BePrime.Nerve;

public class NerveMod : MelonMod
{
    public static bool Enabled = true;
    public static bool SyncWrist = true;
    public static bool SyncBones = true;
    public static bool ForceFullSkeleton = true;
    public static bool GripFromFingers = true;
    public static bool PinchLoco = true;

    private static readonly Color Accent = new Color(0.95f, 0.35f, 0.12f);
    private static readonly Color AccentAlt = new Color(0.20f, 0.85f, 0.65f);
    private static readonly Color LocoAccent = new Color(0.35f, 0.75f, 1f);

    private static bool _harmonyArmed;
    private static bool _levelSeen;
    private static float _armAt = -1f;
    private const float ArmDelaySeconds = 2.5f;

    public static NerveMod Instance { get; private set; }

    public override void OnInitializeMelon()
    {
        Instance = this;
        MelonLogger.Msg("NERVE boot — OnInitializeMelon");
        MelonLogger.Msg("NERVE HarmonyDontPatchAll active — no early auto-patch");

        try
        {
            Prefs.Create();
            MelonLogger.Msg("NERVE prefs ok");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"NERVE prefs FAILED: {ex}");
        }

        // Do NOT PatchAll here. MelonHarmonyInit already skipped via HarmonyDontPatchAll.
        // Patches arm only after level load + delay (see OnUpdate).

        Hooking.OnLevelLoaded += OnLevelLoaded;
        Hooking.OnLevelUnloaded += OnLevelUnloaded;
        MelonLogger.Msg("NERVE BoneLib hooks subscribed");

        BuildMenu();
        MelonLogger.Msg("NERVE init done — Harmony DEFERRED until after avatar spawn");
        MelonLogger.Msg("Telegram: @be_primex");
    }

    private static void OnLevelLoaded(LevelInfo _)
    {
        _levelSeen = true;
        _harmonyArmed = false;
        _armAt = Time.unscaledTime + ArmDelaySeconds;
        HandSync.OnLevelLoaded();
        MelonLogger.Msg($"NERVE level loaded — arming Harmony in {ArmDelaySeconds:0.0}s");
    }

    private static void OnLevelUnloaded()
    {
        MelonLogger.Msg("NERVE level unloaded — disarming Harmony");
        _levelSeen = false;
        _armAt = -1f;
        Instance?.DisarmHarmony();
        HandSync.OnLevelUnloaded();
    }

    public override void OnUpdate()
    {
        Prefs.Tick();

        if (_harmonyArmed || !_levelSeen || _armAt < 0f)
            return;
        if (Time.unscaledTime < _armAt)
            return;

        // Wait until rig actually exists — extra safety beyond the timer.
        try
        {
            if (!Player.HandsExist || Player.ControllerRig == null)
            {
                _armAt = Time.unscaledTime + 0.5f;
                return;
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"NERVE waiting for rig: {ex.Message}");
            _armAt = Time.unscaledTime + 0.5f;
            return;
        }

        Instance?.ArmHarmony();
    }

    private void ArmHarmony()
    {
        if (_harmonyArmed)
            return;

        MelonLogger.Msg("NERVE arming Harmony patches now…");
        try
        {
            HarmonyInstance.PatchAll(typeof(HandSync).Assembly);
            _harmonyArmed = true;
            MelonLogger.Msg("NERVE Harmony ARMED — hand sync + pinch walk live");
        }
        catch (Exception ex)
        {
            _harmonyArmed = false;
            _armAt = Time.unscaledTime + 2f;
            MelonLogger.Error($"NERVE Harmony arm FAILED (will retry): {ex}");
        }
    }

    private void DisarmHarmony()
    {
        if (!_harmonyArmed)
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

        _harmonyArmed = false;
    }

    public override void OnLateUpdate()
    {
        if (!_harmonyArmed)
            return;
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
            });

            root.CreateBool("Sync Bones", AccentAlt, SyncBones, val =>
            {
                SyncBones = val;
                Prefs.MarkDirty();
            });

            root.CreateBool("Full Skeleton", AccentAlt, ForceFullSkeleton, val =>
            {
                ForceFullSkeleton = val;
                Prefs.MarkDirty();
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
