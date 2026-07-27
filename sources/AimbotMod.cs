using System;
using System.Collections;
using BoneLib;
using HarmonyLib;
using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.AI;
using Il2CppSLZ.Marrow.PuppetMasta;
using MelonLoader;
using UnityEngine;
using BoneMenuPage = BoneLib.BoneMenu.Page;

[assembly: MelonInfo(typeof(BePrime.Aimbot.AimbotMod), BePrime.Aimbot.BuildInfo.Name, BePrime.Aimbot.BuildInfo.Version, BePrime.Aimbot.BuildInfo.Author, BePrime.Aimbot.BuildInfo.DownloadLink)]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]
[assembly: MelonOptionalDependencies("LabFusion")]

namespace BePrime.Aimbot;

public class AimbotMod : MelonMod
{
    public enum TargetBone
    {
        Closest,
        Head,
        Chest
    }

    public enum MovementCompensationSmoothing
    {
        None,
        Adaptive,
        Low,
        Medium,
        High,
        VeryHigh
    }

    public static bool TriggerBotEnabled;
    public static bool AimBotEnabled = true;
    public static bool BulletDrop = true;
    public static bool MovementCompensation = true;
    public static bool Acceleration = true;
    public static bool HeadshotsOnly;
    public static bool TargetNpcs = true;
    public static bool TargetPlayers = true;
    public static TargetBone Target = TargetBone.Head;
    public static float AimBotFov = 180f;
    public static MovementCompensationSmoothing Smoothing = MovementCompensationSmoothing.Adaptive;

    public static bool FusionLoaded { get; private set; }

    private static BaseGameController _controller;
    private static readonly Color Accent = new Color(0.85f, 0.12f, 0.18f);
    private static readonly Color AccentAlt = new Color(0.59f, 0.04f, 0.80f);

    public override void OnInitializeMelon()
    {
        FusionLoaded = AccessTools.TypeByName("LabFusion.Entities.NetworkPlayer") != null;

        Prefs.Create();

        Hooking.OnLevelLoaded += _ =>
        {
            NpcTarget.All.Clear();
            PlayerTarget.Clear();
        };
        Hooking.OnLevelUnloaded += () =>
        {
            NpcTarget.All.Clear();
            PlayerTarget.Clear();
        };

        HarmonyInstance.Patch(
            typeof(Gun).GetMethod("Start", AccessTools.all),
            postfix: new HarmonyMethod(typeof(AimbotMod), nameof(GunPatch)));

        HarmonyInstance.Patch(
            typeof(BehaviourBaseNav).GetMethod("KillStart", AccessTools.all),
            postfix: new HarmonyMethod(typeof(AimbotMod), nameof(KillStartPatch)));

        HarmonyInstance.Patch(
            typeof(BehaviourCrablet).GetMethod("KillStart", AccessTools.all),
            postfix: new HarmonyMethod(typeof(AimbotMod), nameof(KillStartPatchCrablet)));

        HarmonyInstance.Patch(
            typeof(TriggerRefProxy).GetMethod("Start", AccessTools.all),
            postfix: new HarmonyMethod(typeof(AimbotMod), nameof(AiPatch)));

        HarmonyInstance.Patch(
            typeof(AIBrain).GetMethod("OnResurrection", AccessTools.all),
            postfix: new HarmonyMethod(typeof(AimbotMod), nameof(AiResurrectionPatch)));

        HarmonyInstance.Patch(
            typeof(BaseGameController).GetMethod("OnEnable", AccessTools.all),
            postfix: new HarmonyMethod(typeof(AimbotMod), nameof(ControllerPatch)));

        BuildMenu();

        MelonLogger.Msg($"{BuildInfo.Name} v{BuildInfo.Version} by {BuildInfo.Author} loaded.");
        MelonLogger.Msg(FusionLoaded
            ? "LabFusion detected — player targeting enabled."
            : "LabFusion not found — NPC targeting only.");
        MelonLogger.Msg("Telegram: @be_primex");
    }

    public override void OnUpdate()
    {
        Prefs.Tick();
    }

    public override void OnFixedUpdate()
    {
        if (FusionLoaded && TargetPlayers)
            PlayerTarget.FixedTickAll();
    }

    public override void OnDeinitializeMelon()
    {
        Prefs.FlushNow();
    }

    private static void BuildMenu()
    {
        try
        {
            // maxElements high = no BoneMenu pagination/arrows (known crash source on Quest)
            BoneMenuPage root = BoneMenuPage.Root.CreatePage("AIMBOT", Accent, 64, true);

            root.CreateBool("Aimbot", Accent, AimBotEnabled, val =>
            {
                AimBotEnabled = val;
                Prefs.MarkDirty();
                SetTargetsEnabled(AimBotEnabled || TriggerBotEnabled);
            });

            // Larger FOV step = fewer callbacks while dragging; no disk I/O in callback
            root.CreateFloat("Aimbot FOV", Accent, AimBotFov, 5f, 0f, 360f, val =>
            {
                AimBotFov = Mathf.Clamp(val, 0f, 360f);
                Prefs.MarkDirty();
            });

            // CreateInt instead of CreateEnum — EnumElement is a common BoneMenu crash on Quest
            root.CreateInt("Target 0C/1H/2Chest", Accent, (int)Target, 1, 0, 2, val =>
            {
                Target = (TargetBone)Mathf.Clamp(val, 0, 2);
                Prefs.MarkDirty();
            });

            root.CreateBool("Target NPCs", Accent, TargetNpcs, val =>
            {
                TargetNpcs = val;
                Prefs.MarkDirty();
            });

            root.CreateBool("Target Players", Accent, TargetPlayers, val =>
            {
                TargetPlayers = val;
                Prefs.MarkDirty();
                // Do not Clear() here — FixedUpdate may be iterating PlayerTarget.All
            });

            root.CreateBool("Triggerbot", AccentAlt, TriggerBotEnabled, val =>
            {
                TriggerBotEnabled = val;
                Prefs.MarkDirty();
                SetTargetsEnabled(AimBotEnabled || TriggerBotEnabled);
            });

            root.CreateBool("Headshots Only", AccentAlt, HeadshotsOnly, val =>
            {
                HeadshotsOnly = val;
                Prefs.MarkDirty();
            });

            BoneMenuPage advanced = root.CreatePage("Advanced", Color.green, 64, true);
            advanced.CreateInt("Smoothing 0-5", Color.white, (int)Smoothing, 1, 0, 5, val =>
            {
                Smoothing = (MovementCompensationSmoothing)Mathf.Clamp(val, 0, 5);
                NpcTarget.ApplySmoothing(Smoothing);
                PlayerTarget.ApplySmoothing(Smoothing);
                Prefs.MarkDirty();
            });
            advanced.CreateBool("Bullet Drop", Color.white, BulletDrop, val =>
            {
                BulletDrop = val;
                Prefs.MarkDirty();
            });
            advanced.CreateBool("Move Comp", Color.white, MovementCompensation, val =>
            {
                MovementCompensation = val;
                Prefs.MarkDirty();
            });
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"AIMBOT BoneMenu build failed: {ex}");
        }
    }

    private static void SetTargetsEnabled(bool enabled)
    {
        try
        {
            // Snapshot — HashSet must not be mutated/iterated unsafely from destroyed NPC callbacks
            var snapshot = new System.Collections.Generic.List<NpcTarget>(NpcTarget.All);
            for (int i = 0; i < snapshot.Count; i++)
            {
                NpcTarget npc = snapshot[i];
                if (npc == null)
                    continue;
                try { npc.enabled = enabled; }
                catch { /* destroyed Il2Cpp object */ }
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"AIMBOT SetTargetsEnabled: {ex.Message}");
        }
    }

    public static void MarkDevTool()
    {
        if (_controller != null)
            _controller.isDevToolSpawned = _controller.isDevToolSpawned || TriggerBotEnabled || AimBotEnabled;
    }

    public static void GunPatch(Gun __instance)
    {
        if (__instance.GetComponent<GunAim>() != null)
            return;
        if (__instance.transform.root.GetComponentInChildren<AIBrain>() != null)
            return;

        __instance.gameObject.AddComponent<GunAim>();
    }

    public static void ControllerPatch(BaseGameController __instance)
    {
        _controller = __instance;
    }

    public static void KillStartPatch(BehaviourBaseNav __instance)
    {
        if (NpcTarget.TryGetById(__instance.transform.root.GetInstanceID(), out NpcTarget npc))
            MelonCoroutines.Start(KillStart(npc));
    }

    public static void KillStartPatchCrablet(BehaviourCrablet __instance)
    {
        if (NpcTarget.TryGetById(__instance.transform.root.GetInstanceID(), out NpcTarget npc))
            MelonCoroutines.Start(KillStart(npc));
    }

    public static void AiPatch(TriggerRefProxy __instance)
    {
        NpcTarget.Bind(__instance);
    }

    public static void AiResurrectionPatch(AIBrain __instance)
    {
        var proxy = __instance.GetComponentInChildren<TriggerRefProxy>();
        if (proxy != null)
            NpcTarget.Bind(proxy);
    }

    private static IEnumerator KillStart(NpcTarget npc)
    {
        npc.Dying = true;
        while (npc != null && npc.Brain != null && !npc.Brain.isDead)
            yield return null;
        if (npc != null)
            npc.Dying = false;
    }
}
