using System;
using System.Collections;
using BoneLib;
using HarmonyLib;
using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow.AI;
using Il2CppSLZ.Marrow.PuppetMasta;
using MelonLoader;
using UnityEngine;
using BoneMenuPage = BoneLib.BoneMenu.Page;

[assembly: MelonInfo(typeof(BePrime.Esp.EspMod), BePrime.Esp.BuildInfo.Name, BePrime.Esp.BuildInfo.Version, BePrime.Esp.BuildInfo.Author, BePrime.Esp.BuildInfo.DownloadLink)]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]
[assembly: MelonOptionalDependencies("LabFusion")]

namespace BePrime.Esp;

public class EspMod : MelonMod
{
    // 0 = Full 2D Box, 1 = Corner frames
    public static int Style = 1;
    public static bool Enabled = true;
    public static bool Rainbow;
    public static float ColorR = 1f;
    public static float ColorG = 0.35f;
    public static float ColorB = 0.12f;
    public static bool TargetNpcs = true;
    public static bool TargetPlayers = true;
    public static bool ThroughWalls = true;
    public static float MaxDistance = 120f;
    public static float LineWidth = 0.008f;
    public static float CornerSize = 0.28f;
    public static bool ShowDead = true;
    public static bool ShowHp = true;
    public static bool ShowSkull = true;
    public static float RainbowSpeed = 0.35f;
    public static float HpAnimSpeed = 2.5f;

    public static bool FusionLoaded { get; private set; }
    public static bool LevelReady { get; private set; }

    private static readonly Color Accent = new Color(1f, 0.35f, 0.12f);
    private static readonly Color AccentAlt = new Color(0.2f, 0.85f, 0.75f);
    private static readonly Color AccentRain = new Color(0.95f, 0.4f, 0.9f);
    private static readonly Color AccentDead = new Color(1f, 0.15f, 0.15f);

    private static float _nextPlayerSync = -1f;
    private static float _nextPrune = -1f;

    public override void OnInitializeMelon()
    {
        FusionLoaded = AccessTools.TypeByName("LabFusion.Entities.NetworkPlayer") != null;

        Prefs.Create();

        Hooking.OnLevelLoaded += _ =>
        {
            LevelReady = true;
            EspNpc.Clear();
            EspPlayer.Clear();
            EspDraw.Reset();
        };
        Hooking.OnLevelUnloaded += () =>
        {
            LevelReady = false;
            EspNpc.Clear();
            EspPlayer.Clear();
            EspDraw.Reset();
        };

        HarmonyInstance.Patch(
            typeof(TriggerRefProxy).GetMethod("Start", AccessTools.all),
            postfix: new HarmonyMethod(typeof(EspMod), nameof(AiPatch)));

        HarmonyInstance.Patch(
            typeof(AIBrain).GetMethod("OnResurrection", AccessTools.all),
            postfix: new HarmonyMethod(typeof(EspMod), nameof(AiResurrectionPatch)));

        HarmonyInstance.Patch(
            typeof(BehaviourBaseNav).GetMethod("KillStart", AccessTools.all),
            postfix: new HarmonyMethod(typeof(EspMod), nameof(KillStartPatch)));

        HarmonyInstance.Patch(
            typeof(BehaviourCrablet).GetMethod("KillStart", AccessTools.all),
            postfix: new HarmonyMethod(typeof(EspMod), nameof(KillStartPatchCrablet)));

        BuildMenu();

        MelonLogger.Msg($"{BuildInfo.Name} v{BuildInfo.Version} by {BuildInfo.Author} loaded.");
        MelonLogger.Msg(FusionLoaded
            ? "LabFusion detected — player ESP enabled."
            : "LabFusion not found — NPC ESP only.");
        MelonLogger.Msg("Telegram: @be_primex");
    }

    public override void OnUpdate()
    {
        Prefs.Tick();
    }

    public override void OnLateUpdate()
    {
        if (!LevelReady || !Enabled)
            return;

        try
        {
            float now = Time.unscaledTime;
            if (FusionLoaded && TargetPlayers && now >= _nextPlayerSync)
            {
                _nextPlayerSync = now + 0.5f;
                EspPlayer.SyncFromFusion();
            }

            if (now >= _nextPrune)
            {
                _nextPrune = now + 1f;
                EspNpc.Prune();
            }

            EspDraw.Tick();
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ESP tick: {ex.Message}");
        }
    }

    public override void OnDeinitializeMelon()
    {
        EspDraw.Reset();
        Prefs.FlushNow();
    }

    public static void AiPatch(TriggerRefProxy __instance) => EspNpc.Bind(__instance);

    public static void AiResurrectionPatch(AIBrain __instance)
    {
        TriggerRefProxy proxy = __instance.GetComponentInChildren<TriggerRefProxy>();
        if (proxy != null)
            EspNpc.Bind(proxy);
    }

    public static void KillStartPatch(BehaviourBaseNav __instance)
    {
        if (EspNpc.TryGetById(__instance.transform.root.GetInstanceID(), out EspNpc npc))
            MelonCoroutines.Start(KillStart(npc));
    }

    public static void KillStartPatchCrablet(BehaviourCrablet __instance)
    {
        if (EspNpc.TryGetById(__instance.transform.root.GetInstanceID(), out EspNpc npc))
            MelonCoroutines.Start(KillStart(npc));
    }

    private static IEnumerator KillStart(EspNpc npc)
    {
        npc.Dying = true;
        while (npc != null && npc.Brain != null && !npc.Brain.isDead)
            yield return null;
        if (npc != null)
            npc.Dying = false;
    }

    private static void BuildMenu()
    {
        try
        {
            BoneMenuPage root = BoneMenuPage.Root.CreatePage("ESP", Accent, 64, true);

            root.CreateBool("Enabled", Accent, Enabled, val =>
            {
                Enabled = val;
                Prefs.MarkDirty();
            });

            root.CreateInt("Style 0Box/1Corners", Accent, Style, 1, 0, 1, val =>
            {
                Style = Mathf.Clamp(val, 0, 1);
                Prefs.MarkDirty();
            });

            root.CreateBool("Rainbow", AccentRain, Rainbow, val =>
            {
                Rainbow = val;
                Prefs.MarkDirty();
            });

            root.CreateFloat("Color R", Accent, ColorR, 0.05f, 0f, 1f, val =>
            {
                ColorR = Mathf.Clamp01(val);
                Prefs.MarkDirty();
            });
            root.CreateFloat("Color G", Accent, ColorG, 0.05f, 0f, 1f, val =>
            {
                ColorG = Mathf.Clamp01(val);
                Prefs.MarkDirty();
            });
            root.CreateFloat("Color B", Accent, ColorB, 0.05f, 0f, 1f, val =>
            {
                ColorB = Mathf.Clamp01(val);
                Prefs.MarkDirty();
            });

            root.CreateBool("Target NPCs", AccentAlt, TargetNpcs, val =>
            {
                TargetNpcs = val;
                Prefs.MarkDirty();
            });
            root.CreateBool("Target Players", AccentAlt, TargetPlayers, val =>
            {
                TargetPlayers = val;
                Prefs.MarkDirty();
            });

            root.CreateBool("Show HP", AccentAlt, ShowHp, val =>
            {
                ShowHp = val;
                Prefs.MarkDirty();
            });
            root.CreateBool("Show Dead", AccentDead, ShowDead, val =>
            {
                ShowDead = val;
                Prefs.MarkDirty();
            });
            root.CreateBool("Death Skull", AccentDead, ShowSkull, val =>
            {
                ShowSkull = val;
                Prefs.MarkDirty();
            });

            root.CreateBool("Through Walls", AccentAlt, ThroughWalls, val =>
            {
                ThroughWalls = val;
                Prefs.MarkDirty();
            });

            root.CreateFloat("Max Distance", AccentAlt, MaxDistance, 5f, 5f, 300f, val =>
            {
                MaxDistance = Mathf.Clamp(val, 5f, 300f);
                Prefs.MarkDirty();
            });

            BoneMenuPage fancy = root.CreatePage("Style Extra", AccentRain, 64, true);
            fancy.CreateFloat("Corner Size", Color.white, CornerSize, 0.02f, 0.1f, 0.45f, val =>
            {
                CornerSize = Mathf.Clamp(val, 0.1f, 0.45f);
                Prefs.MarkDirty();
            });
            fancy.CreateFloat("Line Width", Color.white, LineWidth, 0.001f, 0.002f, 0.04f, val =>
            {
                LineWidth = Mathf.Clamp(val, 0.002f, 0.04f);
                Prefs.MarkDirty();
            });
            fancy.CreateFloat("Rainbow Speed", Color.white, RainbowSpeed, 0.05f, 0.05f, 1.5f, val =>
            {
                RainbowSpeed = Mathf.Clamp(val, 0.05f, 1.5f);
                Prefs.MarkDirty();
            });
            fancy.CreateFloat("HP Anim Speed", Color.white, HpAnimSpeed, 0.25f, 0.5f, 8f, val =>
            {
                HpAnimSpeed = Mathf.Clamp(val, 0.5f, 8f);
                Prefs.MarkDirty();
            });

            MelonLogger.Msg("ESP BoneMenu ok");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"ESP BoneMenu FAILED: {ex}");
        }
    }
}
