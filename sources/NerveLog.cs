using System;
using MelonLoader;
using UnityEngine;

namespace BePrime.Nerve;

/// <summary>Rate-limited warnings so per-frame faults stay visible without flooding.</summary>
internal static class NerveLog
{
    private static float _nextLogAt;
    private static string _lastMsg = "";

    public static void Warn(string where, Exception ex)
    {
        Warn(where + ": " + (ex?.Message ?? "unknown"));
    }

    public static void Warn(string msg)
    {
        float now = Time.unscaledTime;
        if (msg == _lastMsg && now < _nextLogAt)
            return;

        _lastMsg = msg;
        _nextLogAt = now + 5f;
        MelonLogger.Warning($"NERVE {msg}");
    }
}
