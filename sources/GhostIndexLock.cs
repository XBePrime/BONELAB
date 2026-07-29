using System;
using BoneLib;
using HarmonyLib;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.Interaction;
using UnityEngine;

namespace BePrime.Ghost;

/// <summary>
/// Keep the RIGHT index finger straight while poking the Ghost holo.
/// Only active near the panel so normal grabbing elsewhere still works.
/// </summary>
public static class GhostIndexLock
{
    public static bool Active;

    public static void Install(HarmonyLib.Harmony harmony)
    {
        try
        {
            var t = typeof(GhostIndexLock);
            harmony.Patch(
                AccessTools.Method(typeof(OpenController), nameof(OpenController.OnUpdate)),
                postfix: new HarmonyMethod(AccessTools.Method(t, nameof(OnUpdatePostfix))));
            harmony.Patch(
                AccessTools.Method(typeof(OpenController), nameof(OpenController.GetIndexCurlAxis)),
                postfix: new HarmonyMethod(AccessTools.Method(t, nameof(IndexCurlPostfix))));
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Warning($"Ghost index lock patch: {ex.Message}");
        }
    }

    public static void TickStraighten()
    {
        if (!Active) return;
        try { ForceStraight(); }
        catch { /* rig missing */ }
    }

    private static bool IsRight(OpenController oc)
    {
        if (oc == null) return false;
        try { return oc.handedness == Handedness.RIGHT; }
        catch { return false; }
    }

    private static void OnUpdatePostfix(OpenController __instance)
    {
        if (!Active || !IsRight(__instance)) return;
        try { __instance._processedIndex = 0f; }
        catch { }
    }

    private static void IndexCurlPostfix(OpenController __instance, ref float __result)
    {
        if (!Active || !IsRight(__instance)) return;
        __result = 0f;
    }

    private static void ForceStraight()
    {
        BaseController bc = Player.RightController;
        if (bc != null)
        {
            OpenController oc = bc.TryCast<OpenController>();
            if (oc != null)
                oc._processedIndex = 0f;
            else
                bc._processedIndex = 0f;
        }

        Hand hand = Player.RightHand;
        HandPoseAnimator anim = hand != null ? hand.Animator : null;
        if (anim == null) return;

        float thumb = anim._currentThumb;
        float middle = anim._currentMiddle;
        float ring = anim._currentRing;
        float pinky = anim._currentPinky;
        anim._currentIndex = 0f;
        anim.CurlOverride(thumb, 0f, middle, ring, pinky);
        anim.SetFingers(thumb, 0f, middle, ring, pinky);
        anim.ApplyPoseToTransforms();
    }
}
