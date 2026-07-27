using System;
using BoneLib;
using HarmonyLib;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.Input;
using Il2CppSLZ.Marrow.Interaction;
using Il2CppSLZ.Marrow.Utilities;
using UnityEngine;

namespace BePrime.Nerve;

/// <summary>
/// Bridges Quest / OpenXR hand tracking into BONELAB's OpenController finger + wrist path.
/// Patches OnUpdate so curls land after the game's own ProcessFingers pass (low latency overwrite).
/// </summary>
public static class HandSync
{
    private static bool _fullUpdateLeft;
    private static bool _fullUpdateRight;

    private static float _lastTrackLeft = -999f;
    private static float _lastTrackRight = -999f;

    // Hold last good pose briefly when tracking flickers.
    private const float TrackHoldSeconds = 0.12f;

    private static float _thumbL, _indexL, _middleL, _ringL, _pinkyL;
    private static float _thumbR, _indexR, _middleR, _ringR, _pinkyR;
    private static Vector3 _posL, _posR;
    private static Quaternion _rotL = Quaternion.identity, _rotR = Quaternion.identity;

    public static void Reset()
    {
        _fullUpdateLeft = false;
        _fullUpdateRight = false;
        _lastTrackLeft = -999f;
        _lastTrackRight = -999f;
    }

    public static void OnEnabledChanged(bool enabled)
    {
        if (!enabled)
        {
            XRApi xr = MarrowGame.xr;
            if (xr != null)
            {
                TryStopFullUpdate(xr.LeftHand);
                TryStopFullUpdate(xr.RightHand);
            }
            _fullUpdateLeft = false;
            _fullUpdateRight = false;
        }
    }

    [HarmonyPatch(typeof(OpenController), nameof(OpenController.OnUpdate))]
    private static class OpenControllerOnUpdatePatch
    {
        private static void Postfix(OpenController __instance)
        {
            if (!NerveMod.Enabled)
                return;

            try
            {
                SyncController(__instance);
            }
            catch (Exception ex)
            {
                // Never let a sync glitch take down the controller update.
                MelonLoader.MelonLogger.Warning($"NERVE sync: {ex.Message}");
            }
        }
    }

    private static void SyncController(OpenController oc)
    {
        if (oc == null)
            return;

        Handedness handness = oc.handedness;
        bool left = handness == Handedness.LEFT;
        if (!left && handness != Handedness.RIGHT)
            return;

        XRApi xr = MarrowGame.xr;
        if (xr == null)
            return;

        XRHand xrHand = left ? xr.LeftHand : xr.RightHand;
        XRController xrCtrl = left ? xr.LeftController : xr.RightController;
        if (xrHand == null)
            return;

        EnsureFullUpdate(xrHand, left);

        oc._noFingies = false;
        oc._runUpdates = true;

        bool tracking = IsHandTracked(xrHand);
        float now = Time.unscaledTime;

        if (tracking)
        {
            CachePose(left, xrHand);
            if (left) _lastTrackLeft = now;
            else _lastTrackRight = now;
        }
        else
        {
            float last = left ? _lastTrackLeft : _lastTrackRight;
            if (now - last > TrackHoldSeconds)
                return;
        }

        // Direct curl write — no smoothing. Flip-off / OK / fist map 1:1.
        ApplyCurls(oc, left);

        // Let stock hand-tracked finger + grip logic run with live XRHand, then re-assert curls.
        try
        {
            if (xrCtrl != null)
                oc.ProcessHandTrackedFingers(xrCtrl, xrHand);
        }
        catch
        {
            // Some loaders may not resolve this path; curls alone still drive the art rig.
        }

        ApplyCurls(oc, left);

        if (NerveMod.GripFromFingers)
            ApplyGrip(oc, left);

        if (NerveMod.SyncWrist)
            ApplyWrist(oc, left, xrCtrl);

        if (NerveMod.ForceFullSkeleton)
            TryDrawSkeleton(oc, xrHand);

        if (NerveMod.SyncBones)
            ApplyAnimatorFingers(left);
    }

    private static bool IsHandTracked(XRHand hand)
    {
        try
        {
            if (hand is Il2CppSLZ.Marrow.Input.Oculus.OculusHandActionMap oculus)
                return oculus.IsTracking || hand.IsTracking;
        }
        catch
        {
            // Fall through to XRDevice.IsTracking
        }

        return hand.IsTracking;
    }

    private static void EnsureFullUpdate(XRHand hand, bool left)
    {
        bool armed = left ? _fullUpdateLeft : _fullUpdateRight;
        if (armed)
            return;

        try
        {
            hand.StartFullUpdate();
            if (left) _fullUpdateLeft = true;
            else _fullUpdateRight = true;
        }
        catch
        {
            // StartFullUpdate may be unavailable on some XR backends.
        }
    }

    private static void TryStopFullUpdate(XRHand hand)
    {
        if (hand == null)
            return;
        try { hand.StopFullUpdate(); }
        catch { /* ignore */ }
    }

    private static void CachePose(bool left, XRHand hand)
    {
        float thumb = Clamp01(hand.ThumbCurl);
        float index = Clamp01(hand.IndexCurl);
        float middle = Clamp01(hand.MiddleCurl);
        float ring = Clamp01(hand.RingCurl);
        float pinky = Clamp01(hand.PinkyCurl);

        if (left)
        {
            _thumbL = thumb; _indexL = index; _middleL = middle; _ringL = ring; _pinkyL = pinky;
            _posL = hand.Position;
            _rotL = hand.Rotation;
        }
        else
        {
            _thumbR = thumb; _indexR = index; _middleR = middle; _ringR = ring; _pinkyR = pinky;
            _posR = hand.Position;
            _rotR = hand.Rotation;
        }
    }

    private static void ApplyCurls(OpenController oc, bool left)
    {
        if (left)
        {
            oc._processedThumb = _thumbL;
            oc._processedIndex = _indexL;
            oc._processedMiddle = _middleL;
            oc._processedRing = _ringL;
            oc._processedPinky = _pinkyL;
        }
        else
        {
            oc._processedThumb = _thumbR;
            oc._processedIndex = _indexR;
            oc._processedMiddle = _middleR;
            oc._processedRing = _ringR;
            oc._processedPinky = _pinkyR;
        }
    }

    private static void ApplyGrip(OpenController oc, bool left)
    {
        float thumb = left ? _thumbL : _thumbR;
        float index = left ? _indexL : _indexR;
        float middle = left ? _middleL : _middleR;
        float ring = left ? _ringL : _ringR;
        float pinky = left ? _pinkyL : _pinkyR;

        // Pinch (thumb+index) or fist (other three) — whichever is stronger.
        float pinch = Mathf.Min(thumb, index);
        float fist = (middle + ring + pinky) * (1f / 3f);
        float grip = Mathf.Clamp01(Mathf.Max(pinch, fist));

        oc._solvedGrip = grip;
        oc._gripForce = grip;
        // Stock grabThreshold is a static float on OpenController; avoid Il2Cpp accessor quirks.
        float threshold = 0.5f;
        try { threshold = OpenController.grabThreshold; }
        catch { /* keep default */ }
        oc.isBelowGripThreshold = grip < threshold;
    }

    private static void ApplyWrist(OpenController oc, bool left, XRController xrCtrl)
    {
        // Prefer hand pose when the controller is gone / not tracking.
        bool controllerDead = xrCtrl == null || !xrCtrl.IsTracking || !xrCtrl.IsConnected;
        if (!controllerDead)
            return;

        Vector3 pos = left ? _posL : _posR;
        Quaternion rot = left ? _rotL : _rotR;

        // OpenController tracks in rig-local space from XR poses — feed hand pose here.
        oc._localTrackPos = pos;
        oc._localTrackRot = rot;
    }

    private static void TryDrawSkeleton(OpenController oc, XRHand hand)
    {
        try
        {
            var positions = hand.Positions;
            var rotations = hand.Rotations;
            if (positions == null || rotations == null)
                return;
            if (positions.Length < 26 || rotations.Length < 26)
                return;
            oc.DrawSkeletonHand(positions, rotations);
        }
        catch
        {
            // Optional path — curls still work.
        }
    }

    private static void ApplyAnimatorFingers(bool left)
    {
        try
        {
            Hand bodyHand = left ? Player.LeftHand : Player.RightHand;
            if (bodyHand == null)
                return;

            HandPoseAnimator anim = bodyHand.Animator;
            if (anim == null)
                return;

            float thumb = left ? _thumbL : _thumbR;
            float index = left ? _indexL : _indexR;
            float middle = left ? _middleL : _middleR;
            float ring = left ? _ringL : _ringR;
            float pinky = left ? _pinkyL : _pinkyR;

            anim.CurlOverride(thumb, index, middle, ring, pinky);
            anim.SetFingers(thumb, index, middle, ring, pinky);
            anim.ApplyPoseToTransforms();
        }
        catch
        {
            // Animator path is best-effort visual reinforcement.
        }
    }

    private static float Clamp01(float v)
    {
        if (v < 0f) return 0f;
        if (v > 1f) return 1f;
        return v;
    }
}
