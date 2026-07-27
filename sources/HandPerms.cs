using System;
using MelonLoader;

namespace BePrime.Nerve;

/// <summary>
/// Quest requires HAND_TRACKING in the APK AndroidManifest or the OS never
/// enumerates hand devices (Unity XR handData=0, OVRPlugin ovrEnable=False).
/// </summary>
internal static class HandPerms
{
    public const string HandTracking = "com.oculus.permission.HAND_TRACKING";

    private static bool _requested;
    private static bool _loggedBlocker;
    private static string _status = "perm=unprobed";

    public static void EnsureRequested()
    {
        if (_requested)
            return;
        _requested = true;

        Probe();
        TryRequest();
    }

    public static void Probe()
    {
        try
        {
            bool granted = UnityEngine.Android.Permission.HasUserAuthorizedPermission(HandTracking);
            _status = granted ? "perm=GRANTED" : "perm=NOT_GRANTED";
            MelonLogger.Msg("NERVE permission probe: " + _status);
        }
        catch (Exception ex)
        {
            _status = "perm=API_STRIPPED";
            MelonLogger.Warning($"NERVE permission probe failed: {ex.Message}");
        }
    }

    private static void TryRequest()
    {
        try
        {
            UnityEngine.Android.Permission.RequestUserPermission(HandTracking);
            MelonLogger.Msg("NERVE permission: RequestUserPermission(HAND_TRACKING)");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"NERVE RequestUserPermission failed: {ex.Message}");
        }
    }

    /// <summary>
    /// When XR never lists hand devices, the APK almost certainly lacks the
    /// HAND_TRACKING manifest entries Meta requires.
    /// </summary>
    public static void MaybeLogApkBlocker(string xrProbe, string ovrProbe)
    {
        bool noHands = xrProbe != null && xrProbe.Contains("handChar=0") && xrProbe.Contains("handData=0");
        bool ovrOff = ovrProbe != null && ovrProbe.Contains("ovrEnable=False");
        if (noHands && ovrOff)
            LogBlockerOnce();
    }

    public static void LogBlockerOnce()
    {
        if (_loggedBlocker)
            return;
        _loggedBlocker = true;
        MelonLogger.Error("════════════════════════════════════════");
        MelonLogger.Error("NERVE BLOCKER: Quest is not exposing hand devices to BONELAB");
        MelonLogger.Error("Meta requires APK AndroidManifest entries:");
        MelonLogger.Error("  <uses-permission android:name=\"com.oculus.permission.HAND_TRACKING\" />");
        MelonLogger.Error("  <uses-feature android:name=\"oculus.software.handtracking\" android:required=\"false\" />");
        MelonLogger.Error("Without them: ovrEnable=False AND handData=0 forever.");
        MelonLogger.Error("Fix: run HandTrackApkPatch on BONELAB.apk → reinstall via LemonLoader → allow Hand Tracking.");
        MelonLogger.Error("Tool + DLL: https://github.com/XBePrime/BONELAB/releases");
        MelonLogger.Error("════════════════════════════════════════");
    }

    public static string ProbeLine()
    {
        if (_status == "perm=unprobed")
            Probe();
        return _status;
    }
}
