using MelonLoader;
using UnityEngine;

namespace BePrime.Nerve;

public static class Prefs
{
    public static MelonPreferences_Category Category;

    public static MelonPreferences_Entry<bool> Enabled;
    public static MelonPreferences_Entry<bool> SyncWrist;
    public static MelonPreferences_Entry<bool> SyncBones;
    public static MelonPreferences_Entry<bool> ForceFullSkeleton;
    public static MelonPreferences_Entry<bool> GripFromFingers;

    private static bool _dirty;
    private static float _flushAt = -1f;
    private const float FlushDelay = 0.75f;

    public static void Create()
    {
        Category = MelonPreferences.CreateCategory("NERVE");

        Enabled = Category.CreateEntry("Enabled", NerveMod.Enabled);
        SyncWrist = Category.CreateEntry("SyncWrist", NerveMod.SyncWrist);
        SyncBones = Category.CreateEntry("SyncBones", NerveMod.SyncBones);
        ForceFullSkeleton = Category.CreateEntry("ForceFullSkeleton", NerveMod.ForceFullSkeleton);
        GripFromFingers = Category.CreateEntry("GripFromFingers", NerveMod.GripFromFingers);

        NerveMod.Enabled = Enabled.Value;
        NerveMod.SyncWrist = SyncWrist.Value;
        NerveMod.SyncBones = SyncBones.Value;
        NerveMod.ForceFullSkeleton = ForceFullSkeleton.Value;
        NerveMod.GripFromFingers = GripFromFingers.Value;
    }

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
            Enabled.Value = NerveMod.Enabled;
            SyncWrist.Value = NerveMod.SyncWrist;
            SyncBones.Value = NerveMod.SyncBones;
            ForceFullSkeleton.Value = NerveMod.ForceFullSkeleton;
            GripFromFingers.Value = NerveMod.GripFromFingers;
            MelonPreferences.Save();
        }
        catch
        {
            // Prefs I/O must never crash the menu / frame loop.
        }
    }
}
