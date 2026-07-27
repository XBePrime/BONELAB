using BoneLib;
using HarmonyLib;
using Il2CppSLZ.Marrow;
using UnityEngine;

namespace BePrime.Nerve;

/// <summary>
/// Point + Pinch locomotion.
/// Aim with the hand, pinch thumb+index → walk that way (stick injected into ControllerRig).
/// </summary>
public static class PinchLoco
{
    // Pinch (thumb tip ↔ index tip) strength window.
    private const float PinchStart = 0.42f;
    private const float PinchFull = 0.78f;

    // Soft "pointing" bias: index more open than the other fingers.
    private const float PointIndexMax = 0.55f;

    [HarmonyPatch(typeof(OpenControllerRig), nameof(OpenControllerRig.OnUpdate))]
    private static class RigOnUpdatePatch
    {
        private static void Postfix(OpenControllerRig __instance)
        {
            if (!NerveMod.Enabled || !NerveMod.PinchLoco || __instance == null)
                return;

            try
            {
                if (!Player.HandsExist || Player.Head == null)
                    return;

                if (!TryBuildAxis(out Vector2 axis, out bool active))
                {
                    return;
                }

                if (!active)
                    return;

                // Overwrite primary stick after stock aggregation — bare hands have no thumbstick.
                __instance._axisPrimary = axis;
                __instance._axisPrimaryDirty = true;
                __instance._primaryStickTouch = true;
                float mag = axis.magnitude;
                __instance._primaryStick = mag > 0.55f;
                __instance._primaryStickDown = false;
                __instance._primaryStickUp = false;
            }
            catch
            {
                // never break rig update
            }
        }
    }

    private static bool TryBuildAxis(out Vector2 axis, out bool active)
    {
        axis = Vector2.zero;
        active = false;

        float best = 0f;
        Quaternion bestRot = Quaternion.identity;
        bool found = false;

        // Prefer the hand with the stronger valid pinch; either hand works.
        if (EvalHand(true, out float pinchL, out Quaternion rotL) && pinchL > best)
        {
            best = pinchL;
            bestRot = rotL;
            found = true;
        }

        if (EvalHand(false, out float pinchR, out Quaternion rotR) && pinchR > best)
        {
            best = pinchR;
            bestRot = rotR;
            found = true;
        }

        if (!found || best <= 0.001f)
            return true;

        // Hand forward flattened to the floor.
        Vector3 handFwd = bestRot * Vector3.forward;
        handFwd.y = 0f;
        if (handFwd.sqrMagnitude < 0.0001f)
        {
            // Palm mostly up/down — fall back to head look.
            handFwd = Player.Head.forward;
            handFwd.y = 0f;
        }

        if (handFwd.sqrMagnitude < 0.0001f)
            return true;

        handFwd.Normalize();

        Vector3 headFwd = Player.Head.forward;
        headFwd.y = 0f;
        Vector3 headRight = Player.Head.right;
        headRight.y = 0f;
        if (headFwd.sqrMagnitude < 0.0001f || headRight.sqrMagnitude < 0.0001f)
            return true;

        headFwd.Normalize();
        headRight.Normalize();

        // Stick space: y = forward along look, x = strafe.
        float y = Vector3.Dot(handFwd, headFwd);
        float x = Vector3.Dot(handFwd, headRight);
        Vector2 dir = new Vector2(x, y);
        if (dir.sqrMagnitude > 1f)
            dir.Normalize();
        else if (dir.sqrMagnitude > 0.0001f)
            dir.Normalize();
        else
            return true;

        axis = dir * best;
        active = true;
        return true;
    }

    private static bool EvalHand(bool left, out float throttle, out Quaternion rotation)
    {
        throttle = 0f;
        rotation = Quaternion.identity;

        if (!HandSync.TryGetHand(left, out _, out rotation, out float thumb, out float index, out float middle, out float ring, out float pinky))
            return false;

        // Don't drive loco while that hand is holding something.
        try
        {
            Hand hand = left ? Player.LeftHand : Player.RightHand;
            if (hand != null && Player.GetObjectInHand(hand) != null)
                return false;
        }
        catch { /* ignore */ }

        // Classic pinch: thumb + index curl together.
        float pinch = Mathf.Min(thumb, index);

        // Soft point gate — index not fully fist-closed vs the rest (aiming hand).
        float others = (middle + ring + pinky) * (1f / 3f);
        bool pointing = index <= PointIndexMax || index + 0.12f < others;

        if (!pointing && pinch < PinchFull)
        {
            // Fist-only doesn't count as point+pinch.
            return false;
        }

        if (pinch < PinchStart)
            return false;

        throttle = Mathf.InverseLerp(PinchStart, PinchFull, pinch);
        throttle = Mathf.Clamp01(throttle);
        return throttle > 0.001f;
    }
}
