using System;
using BoneLib;
using HarmonyLib;
using Il2CppSLZ.Marrow;
using UnityEngine;

namespace BePrime.Nerve;

/// <summary>
/// Point + Pinch locomotion.
/// Aim with the hand, pinch thumb toward open index → walk that way.
/// </summary>
public static class PinchLoco
{
    private const float PinchStart = 0.42f;
    private const float PinchFull = 0.78f;
    private const float PointIndexMax = 0.55f;

    private const float AxisSmooth = 12f;
    private const float ThrottleDeadzone = 0.08f;
    private const float StickOn = 0.60f;
    private const float StickOff = 0.50f;

    /// <summary>Which hand is currently driving walk (for grip suppression).</summary>
    public static bool DrivingLeft { get; private set; }
    public static bool DrivingRight { get; private set; }

    private static Vector2 _smoothed;
    private static bool _wasDriving;
    private static bool _stickLatched;

    public static void Reset()
    {
        DrivingLeft = false;
        DrivingRight = false;
        _smoothed = Vector2.zero;
        _wasDriving = false;
        _stickLatched = false;
    }

    [HarmonyPatch(typeof(OpenControllerRig), nameof(OpenControllerRig.OnUpdate))]
    private static class RigOnUpdatePatch
    {
        private static void Postfix(OpenControllerRig __instance)
        {
            if (!HandSync.SessionReady || !NerveMod.PinchLoco || __instance == null)
                return;

            try
            {
                if (Player.Head == null || Player.ControllerRig == null)
                    return;

                BuildAxis(out Vector2 target, out bool active, out bool leftDrive, out bool rightDrive);

                DrivingLeft = active && leftDrive;
                DrivingRight = active && rightDrive;

                if (active)
                {
                    float dt = Time.unscaledDeltaTime;
                    float t = 1f - Mathf.Exp(-AxisSmooth * Mathf.Max(dt, 0.0001f));
                    _smoothed = Vector2.Lerp(_smoothed, target, t);

                    if (_smoothed.magnitude < ThrottleDeadzone)
                        _smoothed = Vector2.zero;

                    float mag = _smoothed.magnitude;
                    if (_stickLatched)
                        _stickLatched = mag >= StickOff;
                    else
                        _stickLatched = mag > StickOn;

                    __instance._axisPrimary = _smoothed;
                    __instance._axisPrimaryDirty = true;
                    __instance._primaryStickTouch = mag > ThrottleDeadzone;
                    __instance._primaryStick = _stickLatched;
                    __instance._primaryStickDown = false;
                    __instance._primaryStickUp = false;
                    _wasDriving = true;
                }
                else if (_wasDriving)
                {
                    // Clear our own injection so bare-hand mode doesn't keep walking.
                    _smoothed = Vector2.zero;
                    _stickLatched = false;
                    __instance._axisPrimary = Vector2.zero;
                    __instance._axisPrimaryDirty = true;
                    __instance._primaryStickTouch = false;
                    __instance._primaryStick = false;
                    __instance._primaryStickDown = false;
                    __instance._primaryStickUp = false;
                    _wasDriving = false;
                    DrivingLeft = false;
                    DrivingRight = false;
                }
            }
            catch (Exception ex)
            {
                NerveLog.Warn("PinchLoco", ex);
            }
        }
    }

    private static void BuildAxis(out Vector2 axis, out bool active, out bool leftDrive, out bool rightDrive)
    {
        axis = Vector2.zero;
        active = false;
        leftDrive = false;
        rightDrive = false;

        float best = 0f;
        Quaternion bestRot = Quaternion.identity;
        bool found = false;
        bool bestLeft = false;

        if (EvalHand(true, out float pinchL, out Quaternion rotL) && pinchL > best)
        {
            best = pinchL;
            bestRot = rotL;
            found = true;
            bestLeft = true;
        }

        if (EvalHand(false, out float pinchR, out Quaternion rotR) && pinchR > best)
        {
            best = pinchR;
            bestRot = rotR;
            found = true;
            bestLeft = false;
        }

        if (!found || best <= ThrottleDeadzone)
            return;

        Vector3 handFwd = bestRot * Vector3.forward;
        handFwd.y = 0f;
        if (handFwd.sqrMagnitude < 0.0001f)
        {
            handFwd = Player.Head.forward;
            handFwd.y = 0f;
        }

        if (handFwd.sqrMagnitude < 0.0001f)
            return;

        handFwd.Normalize();

        Vector3 headFwd = Player.Head.forward;
        headFwd.y = 0f;
        Vector3 headRight = Player.Head.right;
        headRight.y = 0f;
        if (headFwd.sqrMagnitude < 0.0001f || headRight.sqrMagnitude < 0.0001f)
            return;

        headFwd.Normalize();
        headRight.Normalize();

        float y = Vector3.Dot(handFwd, headFwd);
        float x = Vector3.Dot(handFwd, headRight);
        Vector2 dir = new Vector2(x, y);
        if (dir.sqrMagnitude < 0.0001f)
            return;

        dir.Normalize();
        axis = dir * best;
        active = true;
        leftDrive = bestLeft;
        rightDrive = !bestLeft;
    }

    /// <summary>True when this hand's curls match the walk gesture (open index + thumb pinch).</summary>
    public static bool IsLocoGesture(bool left)
    {
        if (!HandSync.TryGetHand(left, out _, out _, out float thumb, out float index, out _, out _, out _))
            return false;
        if (index > PointIndexMax)
            return false;
        return thumb >= PinchStart;
    }

    private static bool EvalHand(bool left, out float throttle, out Quaternion rotation)
    {
        throttle = 0f;
        rotation = Quaternion.identity;

        if (!HandSync.TryGetHand(left, out _, out rotation, out float thumb, out float index, out _, out _, out _))
            return false;

        try
        {
            Hand hand = left ? Player.LeftHand : Player.RightHand;
            if (hand != null && Player.GetObjectInHand(hand) != null)
                return false;
        }
        catch (Exception ex)
        {
            NerveLog.Warn("PinchLoco.EvalHand hold-check", ex);
        }

        // Point: index open. (Also rejects fist — fist has index curled.)
        if (index > PointIndexMax)
            return false;

        // Pinch: thumb closes onto the pointed index.
        if (thumb < PinchStart)
            return false;

        throttle = Mathf.Clamp01(Mathf.InverseLerp(PinchStart, PinchFull, thumb));
        return throttle > ThrottleDeadzone;
    }
}
