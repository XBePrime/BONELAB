using System;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace BePrime.Nerve;

/// <summary>
/// Direct Quest hand state via OVRPlugin — Marrow XRHand.IsTracking stays false
/// on LemonLoader/Quest even when bare hands are live.
/// </summary>
internal static class OvrHands
{
    private static bool _configured;
    private static bool _loggedEnable;
    private static OVRPlugin.HandState _left;
    private static OVRPlugin.HandState _right;

    public static void EnsureConfigured()
    {
        if (_configured)
            return;
        _configured = true;

        try
        {
            _left = new OVRPlugin.HandState();
            _right = new OVRPlugin.HandState();
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"NERVE OVR HandState alloc: {ex.Message}");
        }

        try
        {
            // Allow bare hands while Touch controllers still exist / sit on the floor.
            bool sim = OVRPlugin.SetSimultaneousHandsAndControllersEnabled(true);
            bool driven = OVRPlugin.SetControllerDrivenHandPoses(false);
            MelonLogger.Msg($"NERVE OVR: simultaneous={sim} controllerDrivenPoses={driven}");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"NERVE OVR configure: {ex.Message}");
        }
    }

    public static bool HandTrackingEnabled()
    {
        try
        {
            bool on = OVRPlugin.GetHandTrackingEnabled();
            if (!_loggedEnable)
            {
                _loggedEnable = true;
                MelonLogger.Msg(on
                    ? "NERVE OVR: GetHandTrackingEnabled=TRUE"
                    : "NERVE OVR: GetHandTrackingEnabled=FALSE — turn on Hand Tracking in Quest settings");
            }
            return on;
        }
        catch (Exception ex)
        {
            NerveLog.Warn("GetHandTrackingEnabled", ex);
            return false;
        }
    }

    public static bool TrySample(bool left, out Vector3 position, out Quaternion rotation,
        out float thumb, out float index, out float middle, out float ring, out float pinky)
    {
        position = default;
        rotation = Quaternion.identity;
        thumb = index = middle = ring = pinky = 0f;

        try
        {
            EnsureConfigured();

            OVRPlugin.HandState state = left ? _left : _right;
            if (state == null)
                return false;

            OVRPlugin.Hand hand = left ? OVRPlugin.Hand.HandLeft : OVRPlugin.Hand.HandRight;

            // Try Render first, then Physics — either can be valid on Quest.
            bool ok = OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref state);
            if (!ok)
                ok = OVRPlugin.GetHandState(OVRPlugin.Step.Physics, hand, ref state);
            if (!ok)
                return false;

            if (left) _left = state;
            else _right = state;

            OVRPlugin.HandStatus status = state.Status;
            if ((status & OVRPlugin.HandStatus.HandTracked) == 0)
                return false;

            OVRPlugin.Posef root = state.RootPose;
            position = OVRExtensions.FromFlippedZVector3f(root.Position);
            rotation = OVRExtensions.FromFlippedZQuatf(root.Orientation);

            if (!TryBoneCurls(state, out thumb, out index, out middle, out ring, out pinky))
                TryPinchFallback(state, out thumb, out index, out middle, out ring, out pinky);

            return true;
        }
        catch (Exception ex)
        {
            NerveLog.Warn("OvrHands.TrySample", ex);
            return false;
        }
    }

    private static bool TryBoneCurls(OVRPlugin.HandState state,
        out float thumb, out float index, out float middle, out float ring, out float pinky)
    {
        thumb = index = middle = ring = pinky = 0f;
        Il2CppStructArray<OVRPlugin.Quatf> bones = state.BoneRotations;
        if (bones == null || bones.Length < 19)
            return false;

        thumb = Curl3(bones, OVRPlugin.BoneId.Hand_Thumb1, OVRPlugin.BoneId.Hand_Thumb2, OVRPlugin.BoneId.Hand_Thumb3);
        index = Curl3(bones, OVRPlugin.BoneId.Hand_Index1, OVRPlugin.BoneId.Hand_Index2, OVRPlugin.BoneId.Hand_Index3);
        middle = Curl3(bones, OVRPlugin.BoneId.Hand_Middle1, OVRPlugin.BoneId.Hand_Middle2, OVRPlugin.BoneId.Hand_Middle3);
        ring = Curl3(bones, OVRPlugin.BoneId.Hand_Ring1, OVRPlugin.BoneId.Hand_Ring2, OVRPlugin.BoneId.Hand_Ring3);
        pinky = Curl3(bones, OVRPlugin.BoneId.Hand_Pinky1, OVRPlugin.BoneId.Hand_Pinky2, OVRPlugin.BoneId.Hand_Pinky3);
        return true;
    }

    private static void TryPinchFallback(OVRPlugin.HandState state,
        out float thumb, out float index, out float middle, out float ring, out float pinky)
    {
        thumb = index = middle = ring = pinky = 0f;
        var pinches = state.PinchStrength;
        if (pinches == null || pinches.Length < 5)
            return;
        thumb = Clamp01(pinches[0]);
        index = Clamp01(pinches[1]);
        middle = Clamp01(pinches[2]);
        ring = Clamp01(pinches[3]);
        pinky = Clamp01(pinches[4]);
    }

    /// <summary>
    /// Approximate 0..1 curl from local bone rotations (OVR bones are parent-relative).
    /// </summary>
    private static float Curl3(Il2CppStructArray<OVRPlugin.Quatf> bones,
        OVRPlugin.BoneId a, OVRPlugin.BoneId b, OVRPlugin.BoneId c)
    {
        float sum = BoneBend(bones[(int)a]) + BoneBend(bones[(int)b]) + BoneBend(bones[(int)c]);
        // ~90° per joint → full curl ≈ 2.4–2.8 normalized units
        return Clamp01(sum / 2.6f);
    }

    private static float BoneBend(OVRPlugin.Quatf q)
    {
        Quaternion u = OVRExtensions.FromFlippedZQuatf(q);
        return Quaternion.Angle(Quaternion.identity, u) / 90f;
    }

    public static string ProbeLine()
    {
        try
        {
            EnsureConfigured();
            bool enabled = false;
            try { enabled = OVRPlugin.GetHandTrackingEnabled(); } catch { /* ignore */ }

            bool lOk = false, rOk = false;
            if (_left != null)
                lOk = OVRPlugin.GetHandState(OVRPlugin.Step.Render, OVRPlugin.Hand.HandLeft, ref _left)
                   || OVRPlugin.GetHandState(OVRPlugin.Step.Physics, OVRPlugin.Hand.HandLeft, ref _left);
            if (_right != null)
                rOk = OVRPlugin.GetHandState(OVRPlugin.Step.Render, OVRPlugin.Hand.HandRight, ref _right)
                   || OVRPlugin.GetHandState(OVRPlugin.Step.Physics, OVRPlugin.Hand.HandRight, ref _right);

            string ls = lOk ? _left.Status.ToString() : "fail";
            string rs = rOk ? _right.Status.ToString() : "fail";
            bool lTrack = lOk && (_left.Status & OVRPlugin.HandStatus.HandTracked) != 0;
            bool rTrack = rOk && (_right.Status & OVRPlugin.HandStatus.HandTracked) != 0;
            return $"ovrEnable={enabled} L={lTrack}/{ls} R={rTrack}/{rs}";
        }
        catch (Exception ex)
        {
            return "ovrProbe ERR " + ex.Message;
        }
    }

    private static float Clamp01(float v)
    {
        if (v < 0f) return 0f;
        if (v > 1f) return 1f;
        return v;
    }
}
