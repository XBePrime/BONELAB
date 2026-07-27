using System;
using BoneLib;
using MelonLoader;
using UnityEngine;
using BoneMenuPage = BoneLib.BoneMenu.Page;

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

    public override void OnInitializeMelon()
    {
        Prefs.Create();

        // Patch entire mod assembly — includes nested [HarmonyPatch] types (HarmonyX + stock).
        try
        {
            HarmonyInstance.PatchAll(typeof(HandSync).Assembly);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"NERVE Harmony patch failed: {ex}");
        }

        Hooking.OnLevelLoaded += _ => HandSync.OnLevelLoaded();
        Hooking.OnLevelUnloaded += HandSync.OnLevelUnloaded;

        BuildMenu();

        MelonLogger.Msg("NERVE ready — hooks idle until level + rig are loaded.");
        MelonLogger.Msg("Telegram: @be_primex");
    }

    public override void OnUpdate()
    {
        Prefs.Tick();
    }

    public override void OnLateUpdate()
    {
        HandSync.LateTick();
    }

    public override void OnDeinitializeMelon()
    {
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
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"NERVE BoneMenu build failed: {ex}");
        }
    }
}
