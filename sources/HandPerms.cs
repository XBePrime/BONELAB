using System;
using MelonLoader;
using UnityEngine.Android;

namespace BePrime.Nerve;

/// <summary>
/// Quest requires com.oculus.permission.HAND_TRACKING or the runtime
/// never exposes hand devices to Unity XR / OVRPlugin.
/// </summary>
internal static class HandPerms
{
    public const string HandTracking = "com.oculus.permission.HAND_TRACKING";

    private static bool _requested;

    public static void EnsureRequested()
    {
        if (_requested)
            return;
        _requested = true;

        try
        {
            bool has = Permission.HasUserAuthorizedPermission(HandTracking);
            MelonLogger.Msg(has
                ? "NERVE permission: HAND_TRACKING already granted"
                : "NERVE permission: requesting HAND_TRACKING…");

            if (!has)
                Permission.RequestUserPermission(HandTracking);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"NERVE permission request failed: {ex.Message}");
        }
    }

    public static string ProbeLine()
    {
        try
        {
            bool has = Permission.HasUserAuthorizedPermission(HandTracking);
            return has ? "perm=GRANTED" : "perm=DENIED/MISSING";
        }
        catch (Exception ex)
        {
            return "perm=ERR " + ex.Message;
        }
    }
}
