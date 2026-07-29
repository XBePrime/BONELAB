using MelonLoader;
using UnityEngine;

namespace BePrime.Ghost;

public static class Prefs
{
    public static MelonPreferences_Category Category;
    public static MelonPreferences_Entry<bool> Enabled;

    private static bool _dirty;
    private static float _flushAt = -1f;
    private const float FlushDelay = 0.75f;

    public static void Create()
    {
        Category = MelonPreferences.CreateCategory("Ghost");
        Enabled = Category.CreateEntry("Enabled", GhostMod.Enabled);
        GhostMod.Enabled = Enabled.Value;
    }

    public static void MarkDirty()
    {
        _dirty = true;
        _flushAt = Time.unscaledTime + FlushDelay;
    }

    public static void Tick()
    {
        if (!_dirty || _flushAt < 0f) return;
        if (Time.unscaledTime < _flushAt) return;
        FlushNow();
    }

    public static void FlushNow()
    {
        _dirty = false;
        _flushAt = -1f;
        try
        {
            Enabled.Value = GhostMod.Enabled;
            MelonPreferences.Save();
        }
        catch { /* prefs must never crash */ }
    }
}
