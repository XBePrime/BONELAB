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
/// Quest hand bridge — curl/wrist sync after stock OpenController update.
/// All hooks no-op until the level + player rig are ready (avoids native spawn crashes).
/// </summary>
public static class HandSync
{
    private static bool _fullUpdateLeft;
    private static bool _fullUpdateRight;

    private static float _lastTrackLeft = -999f;
    private static float _lastTrackRight = -999f;
    private const float TrackHoldSeconds = 0.10f;

    // Don't touch XR full-skeleton / draw APIs until spawn settles.
    private const float SpawnGraceSeconds = 2.0f;
    private static float _levelLoadedAt = -999f;

    private static float _thumbL, _indexL, _middleL, _ringL, _pinkyL;
    private static float _thumbR, _indexR, _middleR, _ringR, _pinkyR;
    private static Vector3 _posL, _posR;
    private static Quaternion _rotL = Quaternion.identity, _rotR = Quaternion.identity;
    private static bool _liveL, _liveR;

    public static void OnLevelLoaded()
    {
        Reset();
        PinchLoco.Reset();
        _levelLoadedAt = Time.unscaledTime;
    }

    public static void OnLevelUnloaded()
    {
        Reset();
        PinchLoco.Reset();
        _levelLoadedAt = -999f;
    }

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

        try
        {
            if (MarrowGame.IsInitialized && MarrowGame.xr != null)
            {
                TryStopFullUpdate(MarrowGame.xr.LeftHand);
                TryStopFullUpdate(MarrowGame.xr.RightHand);
            }
        }
        catch { /* ignore */ }

        _fullUpdateLeft = false;
        _fullUpdateRight = false;
        _liveL = false;
        _liveR = false;
    }

    /// <summary>True only when it's safe to touch the player rig / XR hands.</summary>
    public static bool SessionReady
    {
        get
        {
            if (!NerveMod.Enabled)
                return false;
            if (_levelLoadedAt < 0f)
                return false;
            try
            {
                if (!MarrowGame.IsInitialized || MarrowGame.xr == null)
                    return false;
                if (!Player.HandsExist || Player.ControllerRig == null)
                    return false;
            }
            catch
            {
                return false;
            }

            return true;
        }
    }

    private static bool SpawnSettled =>
        _levelLoadedAt >= 0f && (Time.unscaledTime - _levelLoadedAt) >= SpawnGraceSeconds;

    public static void LateTick()
    {
        if (!SessionReady || !SpawnSettled)
            return;

        try
        {
            if (_liveL || Holding(_lastTrackLeft))
                ApplyAnimatorFingers(true);
            if (_liveR || Holding(_lastTrackRight))
                ApplyAnimatorFingers(false);
        }
        catch (Exception ex)
        {
            NerveLog.Warn("LateTick", ex);
        }
    }

    // Stock ProcessFingers always runs. We only stamp Quest curls afterwards.
    [HarmonyPatch(typeof(OpenController), nameof(OpenController.OnUpdate))]
    private static class OnUpdatePatch
    {
        private static void Postfix(OpenController __instance)
        {
            if (!SessionReady || __instance == null)
                return;

            try
            {
                SyncAfterStock(__instance);
            }
            catch (Exception ex)
            {
                NerveLog.Warn("OnUpdate", ex);
            }
        }
    }

    [HarmonyPatch(typeof(OpenController), nameof(OpenController.OnVrFixedUpdate))]
    private static class OnVrFixedUpdatePatch
    {
        private static void Postfix(OpenController __instance)
        {
            if (!SessionReady || !NerveMod.SyncWrist || __instance == null)
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
            catch (Exception ex)
            {
                NerveLog.Warn("OnVrFixedUpdate", ex);
            }
        }
    }

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

    private static void SyncAfterStock(OpenController oc)
    {
        if (!TryResolve(oc, out bool left, out XRHand xrHand, out _))
            return;

        if (IsHandTracked(xrHand))
        {
            CachePose(left, xrHand);
            MarkLive(left);

            // Full skeleton APIs are native — only after spawn grace.
            if (SpawnSettled && NerveMod.ForceFullSkeleton)
                EnsureFullUpdate(xrHand, left);
        }
        else if (!Holding(left ? _lastTrackLeft : _lastTrackRight))
        {
            if (left) _liveL = false;
            else _liveR = false;
            return;
        }

        oc._noFingies = false;
        ApplyCurls(oc, left);

        if (NerveMod.GripFromFingers)
            ApplyGrip(oc, left);

        if (NerveMod.SyncWrist)
            ApplyWrist(oc, left);

        if (SpawnSettled && NerveMod.ForceFullSkeleton && IsHandTracked(xrHand))
            TryDrawSkeleton(oc, xrHand);
    }

    private static bool TryResolve(OpenController oc, out bool left, out XRHand xrHand, out XRController xrCtrl)
    {
        left = false;
        xrHand = null;
        xrCtrl = null;

        try
        {
            if (oc == null || !MarrowGame.IsInitialized)
                return false;

            XRApi xr = MarrowGame.xr;
            if (xr == null)
                return false;

            Handedness h = oc.handedness;
            if (h == Handedness.LEFT) left = true;
            else if (h != Handedness.RIGHT) return false;

            xrHand = left ? xr.LeftHand : xr.RightHand;
            xrCtrl = left ? xr.LeftController : xr.RightController;
            return xrHand != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCurl(OpenController oc, int finger, out float value)
    {
        value = 0f;
        if (!SessionReady || oc == null)
            return false;

        try
        {
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
        catch
        {
            return false;
        }
    }

    private static bool IsHandTracked(XRHand hand)
    {
        if (hand == null)
            return false;

        try
        {
            // Avoid Il2Cpp `is` casts — they can fault on Quest during early XR bring-up.
            return hand.IsTracking;
        }
        catch
        {
            return false;
        }
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
        bool armed = left ? _fullUpdateLeft : _fullUpdateRight;
        if (armed)
            return;

        try
        {
            if (hand._isFullUpdate)
            {
                if (left) _fullUpdateLeft = true;
                else _fullUpdateRight = true;
                return;
            }

            hand.StartFullUpdate();
            if (left) _fullUpdateLeft = true;
            else _fullUpdateRight = true;
        }
        catch { /* backend may not support it */ }
    }

    private static void TryStopFullUpdate(XRHand hand)
    {
        if (hand == null) return;
        try { hand.StopFullUpdate(); } catch { /* ignore */ }
    }

    private static void CachePose(bool left, XRHand hand)
    {
        float thumb = Clamp01(hand.ThumbCurl);
        float index = Clamp01(hand.IndexCurl);
        float middle = Clamp01(hand.MiddleCurl);
        float ring = Clamp01(hand.RingCurl);
        float pinky = Clamp01(hand.PinkyCurl);

        Vector3 pos = hand.Position;
        Quaternion rot = hand.Rotation;

        // Root bone only after spawn grace + full skeleton — GetHandBone is native.
        if (SpawnSettled)
        {
            try
            {
                if (hand._isFullUpdate)
                {
                    SimpleTransform root = hand.GetHandBone(hand, HandBone.Root);
                    if (root.rotation.w != 0f || root.rotation.x != 0f || root.rotation.y != 0f || root.rotation.z != 0f)
                    {
                        pos = root.position;
                        rot = root.rotation;
                    }
                }
            }
            catch { /* device pose fallback */ }
        }

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
        // Pinch-walk pose curls middle/ring/pinky — must NOT count as a grab.
        // Use gesture check (not Driving* flags) so we don't depend on rig-update order.
        if (NerveMod.PinchLoco && PinchLoco.IsLocoGesture(left))
        {
            oc._solvedGrip = 0f;
            oc._gripForce = 0f;
            oc.isBelowGripThreshold = true;
            return;
        }

        float thumb = left ? _thumbL : _thumbR;
        float index = left ? _indexL : _indexR;
        float middle = left ? _middleL : _middleR;
        float ring = left ? _ringL : _ringR;
        float pinky = left ? _pinkyL : _pinkyR;

        float pinch = Mathf.Min(thumb, index);
        float fist = (middle + ring + pinky) * (1f / 3f);
        float grip = Mathf.Clamp01(Mathf.Max(pinch, fist));

        oc._solvedGrip = grip;
        oc._gripForce = grip;

        float threshold = 0.5f;
        try { threshold = OpenController.grabThreshold; }
        catch (Exception ex) { NerveLog.Warn("grabThreshold", ex); }
        oc.isBelowGripThreshold = grip < threshold;
    }

    private static void ApplyWrist(OpenController oc, bool left)
    {
        Vector3 pos = left ? _posL : _posR;
        Quaternion rot = left ? _rotL : _rotR;
        oc._localTrackPos = pos;
        oc._localTrackRot = rot;
    }

    private static void TryDrawSkeleton(OpenController oc, XRHand hand)
    {
        try
        {
            if (!hand._isFullUpdate)
                return;
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

            anim._currentThumb = thumb;
            anim._currentIndex = index;
            anim._currentMiddle = middle;
            anim._currentRing = ring;
            anim._currentPinky = pinky;

            anim.CurlOverride(thumb, index, middle, ring, pinky);
            anim.SetFingers(thumb, index, middle, ring, pinky);
            anim.ApplyPoseToTransforms();

            // Joint overlay is the riskiest path — only after grace + full skeleton.
            if (NerveMod.ForceFullSkeleton && SpawnSettled)
                ApplyJointRotations(anim, left);
        }
        catch (Exception ex)
        {
            NerveLog.Warn("ApplyAnimatorFingers", ex);
        }
    }

    private static void ApplyJointRotations(HandPoseAnimator anim, bool left)
    {
        try
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

            Quaternion space = Quaternion.identity;
            try
            {
                space = left ? HandActionMap.LeftAnimSpace : HandActionMap.RightAnimSpace;
            }
            catch
            {
                space = Quaternion.identity;
            }

            // XRHand.Rotations are absolute (tracking space). Convert to parent-relative
            // before writing into Transform.localRotation.
            int wrist = (int)HandBone.Carpals;

            SetFingerJoint(anim.thumb1, LocalBone(rots, (int)HandBone.ThumbMetacarpal, wrist), space);
            SetFingerJoint(anim.thumb2, LocalBone(rots, (int)HandBone.ThumbProximal, (int)HandBone.ThumbMetacarpal), space);
            SetFingerJoint(anim.thumb3, LocalBone(rots, (int)HandBone.ThumbDistal, (int)HandBone.ThumbProximal), space);

            SetFingerJoint(anim.index1, LocalBone(rots, (int)HandBone.IndexProximal, wrist), space);
            SetFingerJoint(anim.index2, LocalBone(rots, (int)HandBone.IndexIntermediate, (int)HandBone.IndexProximal), space);
            SetFingerJoint(anim.index3, LocalBone(rots, (int)HandBone.IndexDistal, (int)HandBone.IndexIntermediate), space);

            SetFingerJoint(anim.middle1, LocalBone(rots, (int)HandBone.MiddleProximal, wrist), space);
            SetFingerJoint(anim.middle2, LocalBone(rots, (int)HandBone.MiddleIntermediate, (int)HandBone.MiddleProximal), space);
            SetFingerJoint(anim.middle3, LocalBone(rots, (int)HandBone.MiddleDistal, (int)HandBone.MiddleIntermediate), space);

            SetFingerJoint(anim.ring1, LocalBone(rots, (int)HandBone.RingProximal, wrist), space);
            SetFingerJoint(anim.ring2, LocalBone(rots, (int)HandBone.RingIntermediate, (int)HandBone.RingProximal), space);
            SetFingerJoint(anim.ring3, LocalBone(rots, (int)HandBone.RingDistal, (int)HandBone.RingIntermediate), space);

            SetFingerJoint(anim.pinky1, LocalBone(rots, (int)HandBone.PinkyProximal, wrist), space);
            SetFingerJoint(anim.pinky2, LocalBone(rots, (int)HandBone.PinkyIntermediate, (int)HandBone.PinkyProximal), space);
            SetFingerJoint(anim.pinky3, LocalBone(rots, (int)HandBone.PinkyDistal, (int)HandBone.PinkyIntermediate), space);
        }
        catch (Exception ex)
        {
            NerveLog.Warn("ApplyJointRotations", ex);
        }
    }

    private static Quaternion LocalBone(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Quaternion> rots, int bone, int parent)
    {
        Quaternion child = rots[bone];
        Quaternion par = rots[parent];
        if (IsUnset(child) || IsUnset(par))
            return default;
        return Quaternion.Inverse(par) * child;
    }

    private static bool IsUnset(Quaternion q)
    {
        return q.w == 0f && q.x == 0f && q.y == 0f && q.z == 0f;
    }

    private static void SetFingerJoint(Transform joint, Quaternion boneLocal, Quaternion animSpace)
    {
        if (joint == null || IsUnset(boneLocal))
            return;
        joint.localRotation = animSpace * boneLocal;
    }

    private static float Clamp01(float v)
    {
        if (v < 0f) return 0f;
        if (v > 1f) return 1f;
        return v;
    }

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
