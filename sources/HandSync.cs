using System;
using BoneLib;
using HarmonyLib;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.Input;
using Il2CppSLZ.Marrow.Interaction;
using Il2CppSLZ.Marrow.Utilities;
using MelonLoader;
using UnityEngine;

namespace BePrime.Nerve;

/// <summary>
/// Quest-accurate hand bridge.
/// When XR hand tracking is live, force the stock hand-tracked finger path and
/// overwrite curls / wrist from the same XRHand buffers Quest feeds the game.
/// No smoothing — values are written 1:1 every controller update + LateUpdate.
/// </summary>
public static class HandSync
{
    private static bool _fullUpdateLeft;
    private static bool _fullUpdateRight;

    private static float _lastTrackLeft = -999f;
    private static float _lastTrackRight = -999f;
    private const float TrackHoldSeconds = 0.10f;

    // Latest Quest curls / wrist (written from XRHand, read by patches + LateUpdate).
    private static float _thumbL, _indexL, _middleL, _ringL, _pinkyL;
    private static float _thumbR, _indexR, _middleR, _ringR, _pinkyR;
    private static Vector3 _posL, _posR;
    private static Quaternion _rotL = Quaternion.identity, _rotR = Quaternion.identity;
    private static bool _liveL, _liveR;

    public static void Reset()
    {
        _fullUpdateLeft = false;
        _fullUpdateRight = false;
        _lastTrackLeft = -999f;
        _lastTrackRight = -999f;
        _liveL = false;
        _liveR = false;
    }

    public static void OnEnabledChanged(bool enabled)
    {
        if (enabled)
            return;

        XRApi xr = MarrowGame.xr;
        if (xr != null)
        {
            TryStopFullUpdate(xr.LeftHand);
            TryStopFullUpdate(xr.RightHand);
        }

        _fullUpdateLeft = false;
        _fullUpdateRight = false;
        _liveL = false;
        _liveR = false;
    }

    /// <summary>LateUpdate pass — re-assert animator curls after art-rig solve.</summary>
    public static void LateTick()
    {
        if (!NerveMod.Enabled)
            return;

        if (_liveL || Holding(_lastTrackLeft))
            ApplyAnimatorFingers(true);
        if (_liveR || Holding(_lastTrackRight))
            ApplyAnimatorFingers(false);
    }

    // ── Force hand-tracked finger path whenever Quest hands are live ─────────

    [HarmonyPatch(typeof(OpenController), nameof(OpenController.ProcessFingers))]
    private static class ProcessFingersPatch
    {
        private static bool Prefix(OpenController __instance)
        {
            if (!NerveMod.Enabled || __instance == null)
                return true;

            if (!TryResolve(__instance, out bool left, out XRHand xrHand, out XRController xrCtrl))
                return true;

            EnsureFullUpdate(xrHand, left);

            if (!IsHandTracked(xrHand))
                return true; // controllers / fallback — let stock logic run

            // Quest hands are live → exclusive hand-tracked path (skip grip/trigger curl mash).
            try
            {
                CachePose(left, xrHand);
                MarkLive(left);

                __instance._noFingies = false;
                __instance._runUpdates = true;

                if (xrCtrl != null)
                    __instance.ProcessHandTrackedFingers(xrCtrl, xrHand);

                // Re-assert exact Quest curls after stock mapping (no smoothing).
                ApplyCurls(__instance, left);

                if (NerveMod.GripFromFingers)
                    ApplyGrip(__instance, left);

                if (NerveMod.SyncWrist)
                    ApplyWrist(__instance, left);

                if (NerveMod.ForceFullSkeleton)
                    TryDrawSkeleton(__instance, xrHand);

                return false; // skip original ProcessFingers
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"NERVE ProcessFingers: {ex.Message}");
                return true;
            }
        }
    }

    // Safety net: if something else rewrote curls after ProcessFingers, stamp Quest values again.
    [HarmonyPatch(typeof(OpenController), nameof(OpenController.OnUpdate))]
    private static class OnUpdatePatch
    {
        private static void Postfix(OpenController __instance)
        {
            if (!NerveMod.Enabled || __instance == null)
                return;

            try
            {
                if (!TryResolve(__instance, out bool left, out XRHand xrHand, out _))
                    return;

                EnsureFullUpdate(xrHand, left);

                if (IsHandTracked(xrHand))
                {
                    CachePose(left, xrHand);
                    MarkLive(left);
                }
                else if (!Holding(left ? _lastTrackLeft : _lastTrackRight))
                {
                    if (left) _liveL = false;
                    else _liveR = false;
                    return;
                }

                __instance._noFingies = false;
                ApplyCurls(__instance, left);

                if (NerveMod.GripFromFingers)
                    ApplyGrip(__instance, left);

                if (NerveMod.SyncWrist)
                    ApplyWrist(__instance, left);

                if (NerveMod.ForceFullSkeleton && IsHandTracked(xrHand))
                    TryDrawSkeleton(__instance, xrHand);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"NERVE OnUpdate: {ex.Message}");
            }
        }
    }

    // Physics-rate wrist so the phys hand tracks Quest palm without frame lag.
    [HarmonyPatch(typeof(OpenController), nameof(OpenController.OnVrFixedUpdate))]
    private static class OnVrFixedUpdatePatch
    {
        private static void Postfix(OpenController __instance)
        {
            if (!NerveMod.Enabled || !NerveMod.SyncWrist || __instance == null)
                return;

            try
            {
                if (!TryResolve(__instance, out bool left, out XRHand xrHand, out _))
                    return;

                if (IsHandTracked(xrHand))
                {
                    CachePose(left, xrHand);
                    MarkLive(left);
                    ApplyWrist(__instance, left);
                }
                else if (Holding(left ? _lastTrackLeft : _lastTrackRight))
                {
                    ApplyWrist(__instance, left);
                }
            }
            catch
            {
                // never break physics update
            }
        }
    }

    // Getters — anything reading curl axes gets live Quest values.
    [HarmonyPatch(typeof(OpenController), nameof(OpenController.GetThumbCurlAxis))]
    private static class ThumbCurlPatch
    {
        private static void Postfix(OpenController __instance, ref float __result)
        {
            if (TryCurl(__instance, 0, out float v)) __result = v;
        }
    }

    [HarmonyPatch(typeof(OpenController), nameof(OpenController.GetIndexCurlAxis))]
    private static class IndexCurlPatch
    {
        private static void Postfix(OpenController __instance, ref float __result)
        {
            if (TryCurl(__instance, 1, out float v)) __result = v;
        }
    }

    [HarmonyPatch(typeof(OpenController), nameof(OpenController.GetMiddleCurlAxis))]
    private static class MiddleCurlPatch
    {
        private static void Postfix(OpenController __instance, ref float __result)
        {
            if (TryCurl(__instance, 2, out float v)) __result = v;
        }
    }

    [HarmonyPatch(typeof(OpenController), nameof(OpenController.GetRingCurlAxis))]
    private static class RingCurlPatch
    {
        private static void Postfix(OpenController __instance, ref float __result)
        {
            if (TryCurl(__instance, 3, out float v)) __result = v;
        }
    }

    [HarmonyPatch(typeof(OpenController), nameof(OpenController.GetPinkyCurlAxis))]
    private static class PinkyCurlPatch
    {
        private static void Postfix(OpenController __instance, ref float __result)
        {
            if (TryCurl(__instance, 4, out float v)) __result = v;
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static bool TryResolve(OpenController oc, out bool left, out XRHand xrHand, out XRController xrCtrl)
    {
        left = false;
        xrHand = null;
        xrCtrl = null;

        Handedness h = oc.handedness;
        if (h == Handedness.LEFT) left = true;
        else if (h != Handedness.RIGHT) return false;

        XRApi xr = MarrowGame.xr;
        if (xr == null) return false;

        xrHand = left ? xr.LeftHand : xr.RightHand;
        xrCtrl = left ? xr.LeftController : xr.RightController;
        return xrHand != null;
    }

    private static bool TryCurl(OpenController oc, int finger, out float value)
    {
        value = 0f;
        if (!NerveMod.Enabled || oc == null)
            return false;

        Handedness h = oc.handedness;
        bool left = h == Handedness.LEFT;
        if (!left && h != Handedness.RIGHT)
            return false;

        bool live = left ? _liveL : _liveR;
        if (!live && !Holding(left ? _lastTrackLeft : _lastTrackRight))
            return false;

        value = finger switch
        {
            0 => left ? _thumbL : _thumbR,
            1 => left ? _indexL : _indexR,
            2 => left ? _middleL : _middleR,
            3 => left ? _ringL : _ringR,
            4 => left ? _pinkyL : _pinkyR,
            _ => 0f
        };
        return true;
    }

    private static bool IsHandTracked(XRHand hand)
    {
        if (hand == null)
            return false;

        try
        {
            if (hand is Il2CppSLZ.Marrow.Input.Oculus.OculusHandActionMap oculus && oculus.IsTracking)
                return true;
        }
        catch { /* fall through */ }

        return hand.IsTracking;
    }

    private static bool Holding(float lastTrackTime)
    {
        return Time.unscaledTime - lastTrackTime <= TrackHoldSeconds;
    }

    private static void MarkLive(bool left)
    {
        float now = Time.unscaledTime;
        if (left)
        {
            _lastTrackLeft = now;
            _liveL = true;
        }
        else
        {
            _lastTrackRight = now;
            _liveR = true;
        }
    }

    private static void EnsureFullUpdate(XRHand hand, bool left)
    {
        // Retry until StartFullUpdate sticks — needed for per-bone Positions/Rotations.
        bool armed = left ? _fullUpdateLeft : _fullUpdateRight;
        if (armed && hand._isFullUpdate)
            return;

        try
        {
            hand.StartFullUpdate();
            if (left) _fullUpdateLeft = true;
            else _fullUpdateRight = true;
        }
        catch { /* backend may not support full skeleton */ }
    }

    private static void TryStopFullUpdate(XRHand hand)
    {
        if (hand == null) return;
        try { hand.StopFullUpdate(); } catch { /* ignore */ }
    }

    private static void CachePose(bool left, XRHand hand)
    {
        // Exact Quest curl buffers — clamp only to legal [0,1], no remap/curve.
        float thumb = Clamp01(hand.ThumbCurl);
        float index = Clamp01(hand.IndexCurl);
        float middle = Clamp01(hand.MiddleCurl);
        float ring = Clamp01(hand.RingCurl);
        float pinky = Clamp01(hand.PinkyCurl);

        Vector3 pos = hand.Position;
        Quaternion rot = hand.Rotation;

        // Prefer wrist/root bone when full skeleton is streaming (closer to Quest palm).
        try
        {
            if (hand._isFullUpdate)
            {
                SimpleTransform root = hand.GetHandBone(hand, HandBone.Root);
                // Zero quaternion (0,0,0,0) means unset; identity/any real quat is fine.
                if (root.rotation.w != 0f || root.rotation.x != 0f || root.rotation.y != 0f || root.rotation.z != 0f)
                {
                    pos = root.position;
                    rot = root.rotation;
                }
            }
        }
        catch { /* Position/Rotation on XRDevice is the fallback */ }

        if (left)
        {
            _thumbL = thumb; _indexL = index; _middleL = middle; _ringL = ring; _pinkyL = pinky;
            _posL = pos; _rotL = rot;
        }
        else
        {
            _thumbR = thumb; _indexR = index; _middleR = middle; _ringR = ring; _pinkyR = pinky;
            _posR = pos; _rotR = rot;
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

        // Quest-style: pinch (thumb+index) or fist — same signals Oculus exposes as curls.
        float pinch = Mathf.Min(thumb, index);
        float fist = (middle + ring + pinky) * (1f / 3f);
        float grip = Mathf.Clamp01(Mathf.Max(pinch, fist));

        oc._solvedGrip = grip;
        oc._gripForce = grip;

        float threshold = 0.5f;
        try { threshold = OpenController.grabThreshold; } catch { /* default */ }
        oc.isBelowGripThreshold = grip < threshold;
    }

    private static void ApplyWrist(OpenController oc, bool left)
    {
        // While Quest hands are live, wrist always follows the hand — even if a
        // controller still reports "connected" on the floor.
        Vector3 pos = left ? _posL : _posR;
        Quaternion rot = left ? _rotL : _rotR;
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
        catch { /* optional */ }
    }

    private static void ApplyAnimatorFingers(bool left)
    {
        if (!NerveMod.SyncBones)
            return;

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

            // Drive both the live curl state and the pose solver from Quest values.
            anim._currentThumb = thumb;
            anim._currentIndex = index;
            anim._currentMiddle = middle;
            anim._currentRing = ring;
            anim._currentPinky = pinky;

            anim.CurlOverride(thumb, index, middle, ring, pinky);
            anim.SetFingers(thumb, index, middle, ring, pinky);
            anim.ApplyPoseToTransforms();

            // Per-joint overlay from Quest skeleton (same buffers DrawSkeletonHand uses).
            if (NerveMod.ForceFullSkeleton)
                ApplyJointRotations(anim, left);
        }
        catch { /* best-effort visual */ }
    }

    private static void ApplyJointRotations(HandPoseAnimator anim, bool left)
    {
        XRApi xr = MarrowGame.xr;
        if (xr == null)
            return;

        XRHand hand = left ? xr.LeftHand : xr.RightHand;
        if (hand == null || !hand._isFullUpdate)
            return;

        var rots = hand.Rotations;
        if (rots == null || rots.Length < 26)
            return;

        // HandActionMap stores bone locals after CalcLocalPose; AnimSpace aligns to avatar.
        Quaternion space = left ? HandActionMap.LeftAnimSpace : HandActionMap.RightAnimSpace;

        SetFingerJoint(anim.thumb1, rots[(int)HandBone.ThumbMetacarpal], space);
        SetFingerJoint(anim.thumb2, rots[(int)HandBone.ThumbProximal], space);
        SetFingerJoint(anim.thumb3, rots[(int)HandBone.ThumbDistal], space);

        SetFingerJoint(anim.index1, rots[(int)HandBone.IndexProximal], space);
        SetFingerJoint(anim.index2, rots[(int)HandBone.IndexIntermediate], space);
        SetFingerJoint(anim.index3, rots[(int)HandBone.IndexDistal], space);

        SetFingerJoint(anim.middle1, rots[(int)HandBone.MiddleProximal], space);
        SetFingerJoint(anim.middle2, rots[(int)HandBone.MiddleIntermediate], space);
        SetFingerJoint(anim.middle3, rots[(int)HandBone.MiddleDistal], space);

        SetFingerJoint(anim.ring1, rots[(int)HandBone.RingProximal], space);
        SetFingerJoint(anim.ring2, rots[(int)HandBone.RingIntermediate], space);
        SetFingerJoint(anim.ring3, rots[(int)HandBone.RingDistal], space);

        SetFingerJoint(anim.pinky1, rots[(int)HandBone.PinkyProximal], space);
        SetFingerJoint(anim.pinky2, rots[(int)HandBone.PinkyIntermediate], space);
        SetFingerJoint(anim.pinky3, rots[(int)HandBone.PinkyDistal], space);
    }

    private static void SetFingerJoint(Transform joint, Quaternion boneLocal, Quaternion animSpace)
    {
        if (joint == null)
            return;
        // Skip unset bones (Oculus can leave tips/metacarpals empty some frames).
        if (boneLocal.w == 0f && boneLocal.x == 0f && boneLocal.y == 0f && boneLocal.z == 0f)
            return;
        joint.localRotation = animSpace * boneLocal;
    }

    private static float Clamp01(float v)
    {
        if (v < 0f) return 0f;
        if (v > 1f) return 1f;
        return v;
    }

    /// <summary>Read latest cached Quest hand pose/curls for locomotion / other systems.</summary>
    public static bool TryGetHand(
        bool left,
        out Vector3 position,
        out Quaternion rotation,
        out float thumb,
        out float index,
        out float middle,
        out float ring,
        out float pinky)
    {
        position = default;
        rotation = Quaternion.identity;
        thumb = index = middle = ring = pinky = 0f;

        bool live = left ? _liveL : _liveR;
        if (!live && !Holding(left ? _lastTrackLeft : _lastTrackRight))
            return false;

        if (left)
        {
            position = _posL; rotation = _rotL;
            thumb = _thumbL; index = _indexL; middle = _middleL; ring = _ringL; pinky = _pinkyL;
        }
        else
        {
            position = _posR; rotation = _rotR;
            thumb = _thumbR; index = _indexR; middle = _middleR; ring = _ringR; pinky = _pinkyR;
        }

        return true;
    }
}
