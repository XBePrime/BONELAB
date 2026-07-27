using System;
using Il2CppSLZ.Marrow.Input;
using Il2CppSLZ.Marrow.Utilities;
using MelonLoader;
using UnityEngine;
using UnityEngine.XR;
using XrHand = UnityEngine.XR.Hand;
using XrBone = UnityEngine.XR.Bone;
using Il2CppListBone = Il2CppSystem.Collections.Generic.List<UnityEngine.XR.Bone>;
using Il2CppListDevice = Il2CppSystem.Collections.Generic.List<UnityEngine.XR.InputDevice>;

namespace BePrime.Nerve;

/// <summary>
/// Unity XR InputDevices hand path — this is what Marrow HandActionMap uses.
/// OVRPlugin hand tracking is often disabled on Quest/OpenXR BONELAB builds.
/// </summary>
internal static class UnityXrHands
{
    private static readonly Il2CppListDevice _devices = new Il2CppListDevice();
    private static readonly Il2CppListBone _boneBuf = new Il2CppListBone();
    private static float _lastForceAt = -999f;
    private static bool _loggedDevices;

    public static void ForceMarrowHandMaps()
    {
        // Don't spam native device picks every curl sample — once per frame is enough.
        float now = Time.unscaledTime;
        if (now - _lastForceAt < 0.014f)
            return;
        _lastForceAt = now;

        try
        {
            if (!MarrowGame.IsInitialized || MarrowGame.xr == null)
                return;

            TryForceMap(MarrowGame.xr.LeftHand, true);
            TryForceMap(MarrowGame.xr.RightHand, false);
        }
        catch (Exception ex)
        {
            NerveLog.Warn("ForceMarrowHandMaps", ex);
        }
    }

    private static void TryForceMap(XRHand hand, bool left)
    {
        if (hand == null)
            return;

        HandActionMap map = hand.TryCast<HandActionMap>();
        if (map == null)
            return;

        try
        {
            map.TryPickBestHandDevice(left);
            map.OnPostNewInputUpdate();
        }
        catch (Exception ex)
        {
            NerveLog.Warn("HandActionMap force", ex);
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
            if (!TryFindDevice(left, out InputDevice device))
                return false;

            bool tracked = false;
            device.TryGetFeatureValue(CommonUsages.isTracked, out tracked);
            if (!tracked)
                return false;

            var chars = device.characteristics;
            bool isHandDevice = (chars & InputDeviceCharacteristics.HandTracking) != 0;
            bool hasHandData = device.TryGetFeatureValue(CommonUsages.handData, out XrHand hand);

            // Controllers are Left/Right + tracked — do NOT treat them as bare hands.
            if (!isHandDevice && !hasHandData)
                return false;

            device.TryGetFeatureValue(CommonUsages.devicePosition, out position);
            device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);

            // Preferred: real hand skeleton → curls.
            if (hasHandData)
            {
                thumb = FingerCurl(hand, HandFinger.Thumb);
                index = FingerCurl(hand, HandFinger.Index);
                middle = FingerCurl(hand, HandFinger.Middle);
                ring = FingerCurl(hand, HandFinger.Ring);
                pinky = FingerCurl(hand, HandFinger.Pinky);

                if (hand.TryGetRootBone(out XrBone root) && root.TryGetPosition(out Vector3 rp))
                {
                    position = rp;
                    if (root.TryGetRotation(out Quaternion rr))
                        rotation = rr;
                }

                return true;
            }

            // HandTracking device without handData: try legacy finger floats.
            float i = 0f, m = 0f, r = 0f, p = 0f;
            bool any =
                device.TryGetFeatureValue(CommonUsages.indexFinger, out i) |
                device.TryGetFeatureValue(CommonUsages.middleFinger, out m) |
                device.TryGetFeatureValue(CommonUsages.ringFinger, out r) |
                device.TryGetFeatureValue(CommonUsages.pinkyFinger, out p);

            if (!any)
                return false;

            index = Clamp01(i);
            middle = Clamp01(m);
            ring = Clamp01(r);
            pinky = Clamp01(p);
            thumb = Clamp01((index + middle) * 0.35f);
            return true;
        }
        catch (Exception ex)
        {
            NerveLog.Warn("UnityXrHands.TrySample", ex);
            return false;
        }
    }

    private static bool TryFindDevice(bool left, out InputDevice device)
    {
        device = default;
        _devices.Clear();

        InputDeviceCharacteristics side = left
            ? InputDeviceCharacteristics.Left
            : InputDeviceCharacteristics.Right;

        // Prefer dedicated hand-tracking devices.
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.HandTracking | side, _devices);

        if (_devices.Count == 0)
        {
            // Broader: any left/right tracked device that exposes handData.
            InputDevices.GetDevicesWithCharacteristics(side, _devices);
        }

        if (_devices.Count == 0)
        {
            InputDevices.GetDevices(_devices);
        }

        for (int i = 0; i < _devices.Count; i++)
        {
            InputDevice d = _devices[i];
            if (!d.isValid)
                continue;

            var chars = d.characteristics;
            bool sideOk = left
                ? (chars & InputDeviceCharacteristics.Left) != 0
                : (chars & InputDeviceCharacteristics.Right) != 0;
            if (!sideOk && _devices.Count > 1)
                continue;

            bool hasHand = (chars & InputDeviceCharacteristics.HandTracking) != 0;
            bool hasData = d.TryGetFeatureValue(CommonUsages.handData, out XrHand _);
            if (hasHand || hasData || sideOk)
            {
                device = d;
                return true;
            }
        }

        return false;
    }

    private static float FingerCurl(XrHand hand, HandFinger finger)
    {
        _boneBuf.Clear();
        if (!hand.TryGetFingerBones(finger, _boneBuf) || _boneBuf.Count < 3)
            return 0f;

        float bend = 0f;
        int joints = 0;
        for (int i = 0; i < _boneBuf.Count - 2; i++)
        {
            if (!_boneBuf[i].TryGetPosition(out Vector3 a)) continue;
            if (!_boneBuf[i + 1].TryGetPosition(out Vector3 b)) continue;
            if (!_boneBuf[i + 2].TryGetPosition(out Vector3 c)) continue;

            Vector3 d0 = a - b;
            Vector3 d1 = c - b;
            if (d0.sqrMagnitude < 1e-8f || d1.sqrMagnitude < 1e-8f)
                continue;

            // Straight finger ≈ 180°, curled ≈ 90° or less.
            float ang = Vector3.Angle(d0, d1);
            bend += Mathf.Clamp01((180f - ang) / 90f);
            joints++;
        }

        if (joints <= 0)
            return 0f;
        return Clamp01(bend / joints);
    }

    public static string ProbeLine()
    {
        try
        {
            _devices.Clear();
            InputDevices.GetDevices(_devices);

            int total = _devices.Count;
            int hands = 0;
            int handData = 0;
            int tracked = 0;
            string sample = "";

            for (int i = 0; i < _devices.Count; i++)
            {
                InputDevice d = _devices[i];
                if (!d.isValid) continue;
                var c = d.characteristics;
                if ((c & InputDeviceCharacteristics.HandTracking) != 0) hands++;
                if (d.TryGetFeatureValue(CommonUsages.handData, out XrHand _)) handData++;
                if (d.TryGetFeatureValue(CommonUsages.isTracked, out bool t) && t) tracked++;

                if (!_loggedDevices)
                {
                    sample += $"[{d.name}|{c}] ";
                }
            }

            bool featL = false, featR = false;
            try
            {
                if (MarrowGame.IsInitialized && MarrowGame.xr != null)
                {
                    var lm = MarrowGame.xr.LeftHand?.TryCast<HandActionMap>();
                    var rm = MarrowGame.xr.RightHand?.TryCast<HandActionMap>();
                    if (lm != null) featL = lm._hasFeature;
                    if (rm != null) featR = rm._hasFeature;
                }
            }
            catch { /* ignore */ }

            if (!_loggedDevices)
            {
                _loggedDevices = true;
                MelonLogger.Msg($"NERVE XR devices ({total}): {sample}");
            }

            return $"xrDev={total} handChar={hands} handData={handData} tracked={tracked} marrowFeat L={featL} R={featR}";
        }
        catch (Exception ex)
        {
            return "xrProbe ERR " + ex.Message;
        }
    }

    private static float Clamp01(float v)
    {
        if (v < 0f) return 0f;
        if (v > 1f) return 1f;
        return v;
    }
}
