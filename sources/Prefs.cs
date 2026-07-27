using MelonLoader;
using UnityEngine;

namespace BePrime.Aimbot;

public static class Prefs
{
    public static MelonPreferences_Category Category;

    public static MelonPreferences_Entry<bool> AimBot;
    public static MelonPreferences_Entry<bool> TriggerBot;
    public static MelonPreferences_Entry<bool> TargetNpcs;
    public static MelonPreferences_Entry<bool> TargetPlayers;
    public static MelonPreferences_Entry<bool> BulletDrop;
    public static MelonPreferences_Entry<bool> MovementCompensation;
    public static MelonPreferences_Entry<int> Smoothing;
    public static MelonPreferences_Entry<float> Fov;
    public static MelonPreferences_Entry<int> Target;
    public static MelonPreferences_Entry<bool> HeadshotsOnly;

    private static bool _dirty;
    private static float _flushAt = -1f;
    private const float FlushDelay = 0.75f;

    public static void Create()
    {
        Category = MelonPreferences.CreateCategory("AIMBOT");

        AimBot = Category.CreateEntry("Aimbot", AimbotMod.AimBotEnabled);
        TriggerBot = Category.CreateEntry("Triggerbot", AimbotMod.TriggerBotEnabled);
        TargetNpcs = Category.CreateEntry("TargetNPCs", AimbotMod.TargetNpcs);
        TargetPlayers = Category.CreateEntry("TargetPlayers", AimbotMod.TargetPlayers);
        Fov = Category.CreateEntry("AimbotFOV", AimbotMod.AimBotFov);
        Target = Category.CreateEntry("Target", (int)AimbotMod.Target);
        Smoothing = Category.CreateEntry("Smoothing", (int)AimbotMod.Smoothing);
        BulletDrop = Category.CreateEntry("BulletDropCompensation", AimbotMod.BulletDrop);
        MovementCompensation = Category.CreateEntry("MovementCompensation", AimbotMod.MovementCompensation);
        HeadshotsOnly = Category.CreateEntry("HeadshotsOnly", AimbotMod.HeadshotsOnly);

        AimbotMod.AimBotEnabled = AimBot.Value;
        AimbotMod.TriggerBotEnabled = TriggerBot.Value;
        AimbotMod.TargetNpcs = TargetNpcs.Value;
        AimbotMod.TargetPlayers = TargetPlayers.Value;
        AimbotMod.AimBotFov = Mathf.Clamp(Fov.Value, 0f, 360f);
        AimbotMod.Target = ClampTarget(Target.Value);
        AimbotMod.Smoothing = ClampSmoothing(Smoothing.Value);
        AimbotMod.BulletDrop = BulletDrop.Value;
        AimbotMod.MovementCompensation = MovementCompensation.Value;
        AimbotMod.HeadshotsOnly = HeadshotsOnly.Value;

        NpcTarget.ApplySmoothing(AimbotMod.Smoothing);
        PlayerTarget.ApplySmoothing(AimbotMod.Smoothing);
    }

    /// <summary>Mark prefs dirty. Disk flush is deferred so BoneMenu callbacks stay cheap.</summary>
    public static void MarkDirty()
    {
        _dirty = true;
        _flushAt = Time.unscaledTime + FlushDelay;
    }

    public static void Tick()
    {
        if (!_dirty || _flushAt < 0f)
            return;
        if (Time.unscaledTime < _flushAt)
            return;
        FlushNow();
    }

    public static void FlushNow()
    {
        _dirty = false;
        _flushAt = -1f;
        try
        {
            AimBot.Value = AimbotMod.AimBotEnabled;
            TriggerBot.Value = AimbotMod.TriggerBotEnabled;
            TargetNpcs.Value = AimbotMod.TargetNpcs;
            TargetPlayers.Value = AimbotMod.TargetPlayers;
            Fov.Value = AimbotMod.AimBotFov;
            Target.Value = (int)AimbotMod.Target;
            Smoothing.Value = (int)AimbotMod.Smoothing;
            HeadshotsOnly.Value = AimbotMod.HeadshotsOnly;
            BulletDrop.Value = AimbotMod.BulletDrop;
            MovementCompensation.Value = AimbotMod.MovementCompensation;
            MelonPreferences.Save();
        }
        catch (System.Exception ex)
        {
            MelonLogger.Warning($"AIMBOT prefs save failed: {ex.Message}");
        }
    }

    private static AimbotMod.TargetBone ClampTarget(int v)
    {
        if (v < 0) v = 0;
        if (v > 2) v = 2;
        return (AimbotMod.TargetBone)v;
    }

    private static AimbotMod.MovementCompensationSmoothing ClampSmoothing(int v)
    {
        if (v < 0) v = 0;
        if (v > 5) v = 5;
        return (AimbotMod.MovementCompensationSmoothing)v;
    }
}
