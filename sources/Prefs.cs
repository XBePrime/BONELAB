using MelonLoader;
using UnityEngine;

namespace BePrime.Esp;

public static class Prefs
{
    public static MelonPreferences_Category Category;

    public static MelonPreferences_Entry<bool> Enabled;
    public static MelonPreferences_Entry<int> Style;
    public static MelonPreferences_Entry<bool> Rainbow;
    public static MelonPreferences_Entry<float> ColorR;
    public static MelonPreferences_Entry<float> ColorG;
    public static MelonPreferences_Entry<float> ColorB;
    public static MelonPreferences_Entry<bool> TargetNpcs;
    public static MelonPreferences_Entry<bool> TargetPlayers;
    public static MelonPreferences_Entry<bool> ThroughWalls;
    public static MelonPreferences_Entry<float> MaxDistance;
    public static MelonPreferences_Entry<float> LineWidth;
    public static MelonPreferences_Entry<float> CornerSize;
    public static MelonPreferences_Entry<bool> ShowDead;
    public static MelonPreferences_Entry<bool> ShowHp;
    public static MelonPreferences_Entry<bool> ShowSkull;
    public static MelonPreferences_Entry<float> RainbowSpeed;
    public static MelonPreferences_Entry<float> HpAnimSpeed;

    private static bool _dirty;
    private static float _flushAt = -1f;
    private const float FlushDelay = 0.75f;

    public static void Create()
    {
        Category = MelonPreferences.CreateCategory("ESP");

        Enabled = Category.CreateEntry("Enabled", EspMod.Enabled);
        Style = Category.CreateEntry("Style", EspMod.Style);
        Rainbow = Category.CreateEntry("Rainbow", EspMod.Rainbow);
        ColorR = Category.CreateEntry("ColorR", EspMod.ColorR);
        ColorG = Category.CreateEntry("ColorG", EspMod.ColorG);
        ColorB = Category.CreateEntry("ColorB", EspMod.ColorB);
        TargetNpcs = Category.CreateEntry("TargetNpcs", EspMod.TargetNpcs);
        TargetPlayers = Category.CreateEntry("TargetPlayers", EspMod.TargetPlayers);
        ThroughWalls = Category.CreateEntry("ThroughWalls", EspMod.ThroughWalls);
        MaxDistance = Category.CreateEntry("MaxDistance", EspMod.MaxDistance);
        LineWidth = Category.CreateEntry("LineWidth", EspMod.LineWidth);
        CornerSize = Category.CreateEntry("CornerSize", EspMod.CornerSize);
        ShowDead = Category.CreateEntry("ShowDead", EspMod.ShowDead);
        ShowHp = Category.CreateEntry("ShowHp", EspMod.ShowHp);
        ShowSkull = Category.CreateEntry("ShowSkull", EspMod.ShowSkull);
        RainbowSpeed = Category.CreateEntry("RainbowSpeed", EspMod.RainbowSpeed);
        HpAnimSpeed = Category.CreateEntry("HpAnimSpeed", EspMod.HpAnimSpeed);

        EspMod.Enabled = Enabled.Value;
        EspMod.Style = Mathf.Clamp(Style.Value, 0, 1);
        EspMod.Rainbow = Rainbow.Value;
        EspMod.ColorR = Mathf.Clamp01(ColorR.Value);
        EspMod.ColorG = Mathf.Clamp01(ColorG.Value);
        EspMod.ColorB = Mathf.Clamp01(ColorB.Value);
        EspMod.TargetNpcs = TargetNpcs.Value;
        EspMod.TargetPlayers = TargetPlayers.Value;
        EspMod.ThroughWalls = ThroughWalls.Value;
        EspMod.MaxDistance = Mathf.Clamp(MaxDistance.Value, 5f, 500f);
        EspMod.LineWidth = Mathf.Clamp(LineWidth.Value, 0.002f, 0.05f);
        EspMod.CornerSize = Mathf.Clamp(CornerSize.Value, 0.1f, 0.5f);
        EspMod.ShowDead = ShowDead.Value;
        EspMod.ShowHp = ShowHp.Value;
        EspMod.ShowSkull = ShowSkull.Value;
        EspMod.RainbowSpeed = Mathf.Clamp(RainbowSpeed.Value, 0.05f, 2f);
        EspMod.HpAnimSpeed = Mathf.Clamp(HpAnimSpeed.Value, 0.5f, 8f);
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
            Enabled.Value = EspMod.Enabled;
            Style.Value = EspMod.Style;
            Rainbow.Value = EspMod.Rainbow;
            ColorR.Value = EspMod.ColorR;
            ColorG.Value = EspMod.ColorG;
            ColorB.Value = EspMod.ColorB;
            TargetNpcs.Value = EspMod.TargetNpcs;
            TargetPlayers.Value = EspMod.TargetPlayers;
            ThroughWalls.Value = EspMod.ThroughWalls;
            MaxDistance.Value = EspMod.MaxDistance;
            LineWidth.Value = EspMod.LineWidth;
            CornerSize.Value = EspMod.CornerSize;
            ShowDead.Value = EspMod.ShowDead;
            ShowHp.Value = EspMod.ShowHp;
            ShowSkull.Value = EspMod.ShowSkull;
            RainbowSpeed.Value = EspMod.RainbowSpeed;
            HpAnimSpeed.Value = EspMod.HpAnimSpeed;
            MelonPreferences.Save();
        }
        catch { /* prefs must never crash */ }
    }
}
