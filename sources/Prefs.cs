using MelonLoader;

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
    public static MelonPreferences_Entry<AimbotMod.MovementCompensationSmoothing> Smoothing;
    public static MelonPreferences_Entry<float> Fov;
    public static MelonPreferences_Entry<AimbotMod.TargetBone> Target;
    public static MelonPreferences_Entry<bool> HeadshotsOnly;

    public static void Create()
    {
        Category = MelonPreferences.CreateCategory("AIMBOT");

        AimBot = Category.CreateEntry("Aimbot", AimbotMod.AimBotEnabled);
        TriggerBot = Category.CreateEntry("Triggerbot", AimbotMod.TriggerBotEnabled);
        TargetNpcs = Category.CreateEntry("TargetNPCs", AimbotMod.TargetNpcs);
        TargetPlayers = Category.CreateEntry("TargetPlayers", AimbotMod.TargetPlayers);
        Fov = Category.CreateEntry("AimbotFOV", AimbotMod.AimBotFov);
        Target = Category.CreateEntry("Target", AimbotMod.Target);
        Smoothing = Category.CreateEntry("MovementCompensationSmoothing", AimbotMod.Smoothing);
        BulletDrop = Category.CreateEntry("BulletDropCompensation", AimbotMod.BulletDrop);
        MovementCompensation = Category.CreateEntry("MovementCompensation", AimbotMod.MovementCompensation);
        HeadshotsOnly = Category.CreateEntry("HeadshotsOnly", AimbotMod.HeadshotsOnly);

        AimbotMod.AimBotEnabled = AimBot.Value;
        AimbotMod.TriggerBotEnabled = TriggerBot.Value;
        AimbotMod.TargetNpcs = TargetNpcs.Value;
        AimbotMod.TargetPlayers = TargetPlayers.Value;
        AimbotMod.AimBotFov = Fov.Value;
        AimbotMod.Target = Target.Value;
        AimbotMod.Smoothing = Smoothing.Value;
        AimbotMod.BulletDrop = BulletDrop.Value;
        AimbotMod.MovementCompensation = MovementCompensation.Value;
        AimbotMod.HeadshotsOnly = HeadshotsOnly.Value;

        NpcTarget.ApplySmoothing(AimbotMod.Smoothing);
        PlayerTarget.ApplySmoothing(AimbotMod.Smoothing);
    }

    public static void Save()
    {
        AimBot.Value = AimbotMod.AimBotEnabled;
        TriggerBot.Value = AimbotMod.TriggerBotEnabled;
        TargetNpcs.Value = AimbotMod.TargetNpcs;
        TargetPlayers.Value = AimbotMod.TargetPlayers;
        Fov.Value = AimbotMod.AimBotFov;
        Target.Value = AimbotMod.Target;
        Smoothing.Value = AimbotMod.Smoothing;
        HeadshotsOnly.Value = AimbotMod.HeadshotsOnly;
        BulletDrop.Value = AimbotMod.BulletDrop;
        MovementCompensation.Value = AimbotMod.MovementCompensation;
        MelonPreferences.Save();
    }
}
