using System;
using BoneLib;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using BoneMenuPage = BoneLib.BoneMenu.Page;

[assembly: MelonInfo(typeof(BePrime.Ghost.GhostMod), BePrime.Ghost.BuildInfo.Name, BePrime.Ghost.BuildInfo.Version, BePrime.Ghost.BuildInfo.Author, BePrime.Ghost.BuildInfo.DownloadLink)]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]
[assembly: MelonOptionalDependencies("LabFusion")]

namespace BePrime.Ghost;

public class GhostMod : MelonMod
{
    public static bool Enabled;

    public static bool FusionLoaded { get; private set; }

    private static readonly Color Accent = new Color(1f, 0.42f, 0.05f); // orange

    public override void OnInitializeMelon()
    {
        FusionLoaded = AccessTools.TypeByName("LabFusion.Player.LocalPlayer") != null;

        Prefs.Create();

        if (FusionLoaded)
        {
            GhostLobby.Install(HarmonyInstance);
            GhostIndexLock.Install(HarmonyInstance);
        }

        Hooking.OnLevelLoaded += _ =>
        {
            if (Enabled && FusionLoaded)
                GhostIdentity.CaptureRealIfNeeded();
        };
        Hooking.OnLevelUnloaded += () =>
        {
            GhostHolo.Destroy();
        };

        BuildMenu();

        MelonLogger.Msg($"{BuildInfo.Name} v{BuildInfo.Version} by {BuildInfo.Author} loaded.");
        MelonLogger.Msg(FusionLoaded
            ? "LabFusion detected — Ghost identity / lobby spoof ready."
            : "LabFusion not found — Ghost idle.");
        MelonLogger.Msg("Telegram: @be_primex");
    }

    public override void OnUpdate()
    {
        Prefs.Tick();
    }

    public override void OnLateUpdate()
    {
        if (!FusionLoaded) return;
        try
        {
            if (Enabled)
            {
                GhostIdentity.CaptureRealIfNeeded();
                GhostHolo.Tick();
            }
            else
            {
                GhostHolo.Destroy();
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Ghost tick: {ex.Message}");
        }
    }

    public override void OnDeinitializeMelon()
    {
        if (Enabled)
            SetEnabled(false);
        GhostHolo.Destroy();
        Prefs.FlushNow();
    }

    private static void SetEnabled(bool on)
    {
        Enabled = on;
        Prefs.MarkDirty();
        if (on)
        {
            if (!FusionLoaded)
            {
                MelonLogger.Warning("Ghost needs LabFusion.");
                return;
            }
            GhostHolo.Destroy();
            GhostIdentity.CaptureRealIfNeeded();
            MelonLogger.Msg("Ghost ENABLED — wrist hologram online.");
        }
        else
        {
            GhostLobby.Fakes.Clear();
            GhostLobby.Push();
            GhostIdentity.Restore();
            GhostHolo.Destroy();
            MelonLogger.Msg("Ghost DISABLED — identity restored.");
        }
    }

    private static void BuildMenu()
    {
        try
        {
            BoneMenuPage root = BoneMenuPage.Root.CreatePage("Ghost", Accent, 64, true);
            root.CreateBool("Enabled", Accent, Enabled, val => SetEnabled(val));
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Ghost BoneMenu build failed: {ex}");
        }
    }
}
