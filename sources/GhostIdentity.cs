using System;
using System.Collections.Generic;
using LabFusion.Entities;
using LabFusion.Player;
using LabFusion.Preferences.Client;
using MelonLoader;
using UnityEngine;

namespace BePrime.Ghost;

/// <summary>
/// Spoofs Fusion display identity (username / nickname / avatar metadata).
/// </summary>
public static class GhostIdentity
{
    public const string InvisibleName = "\u2800"; // Braille blank — renders empty

    private static bool _stored;
    private static string _realUsername = "";
    private static string _realNickname = "";
    private static string _realDescription = "";
    private static string _realAvatarTitle = "";
    private static int _realAvatarModId = -1;

    public static void CaptureRealIfNeeded()
    {
        if (_stored) return;
        try
        {
            _realUsername = LocalPlayer.Username ?? "";
            _realNickname = LocalPlayer.Metadata?.Nickname != null
                ? LocalPlayer.Metadata.Nickname.GetValueOrEmpty()
                : (ClientSettings.Nickname?.Value ?? "");
            _realDescription = LocalPlayer.Metadata?.Description != null
                ? LocalPlayer.Metadata.Description.GetValueOrEmpty()
                : (ClientSettings.Description?.Value ?? "");
            _realAvatarTitle = LocalPlayer.Metadata?.AvatarTitle != null
                ? LocalPlayer.Metadata.AvatarTitle.GetValueOrEmpty()
                : "";
            _realAvatarModId = LocalPlayer.Metadata?.AvatarModID != null
                ? LocalPlayer.Metadata.AvatarModID.GetValue()
                : -1;
            _stored = true;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Ghost capture identity: {ex.Message}");
        }
    }

    public static void ApplyInvisible()
    {
        CaptureRealIfNeeded();
        ApplyName(InvisibleName, InvisibleName);
    }

    public static void ApplyPreset(string name)
    {
        CaptureRealIfNeeded();
        ApplyName(name, name);
    }

    public static void ApplyName(string username, string nickname)
    {
        CaptureRealIfNeeded();
        try
        {
            LocalPlayer.Username = username ?? "";
            if (LocalPlayer.Metadata?.Nickname != null)
                LocalPlayer.Metadata.Nickname.SetValue(nickname ?? "");
            if (ClientSettings.Nickname != null)
                ClientSettings.Nickname.Value = nickname ?? "";
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Ghost apply name: {ex.Message}");
        }
    }

    public static void ApplyAvatarMeta(string title, int modId = -1)
    {
        CaptureRealIfNeeded();
        try
        {
            if (LocalPlayer.Metadata?.AvatarTitle != null)
                LocalPlayer.Metadata.AvatarTitle.SetValue(title ?? "");
            if (LocalPlayer.Metadata?.AvatarModID != null)
                LocalPlayer.Metadata.AvatarModID.SetValue(modId);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Ghost apply avatar meta: {ex.Message}");
        }
    }

    public static void CloneFromPlayer(PlayerID id)
    {
        if (id == null) return;
        CaptureRealIfNeeded();
        try
        {
            string user = id.Metadata.Username.GetValueOrEmpty();
            string nick = id.Metadata.Nickname.GetValueOrEmpty();
            string desc = id.Metadata.Description.GetValueOrEmpty();
            string title = id.Metadata.AvatarTitle.GetValueOrEmpty();
            int modId = id.Metadata.AvatarModID.GetValue();

            ApplyName(string.IsNullOrWhiteSpace(user) ? nick : user,
                string.IsNullOrWhiteSpace(nick) ? user : nick);

            if (LocalPlayer.Metadata?.Description != null)
                LocalPlayer.Metadata.Description.SetValue(desc ?? "");
            if (ClientSettings.Description != null)
                ClientSettings.Description.Value = desc ?? "";

            ApplyAvatarMeta(title, modId);

            // Best-effort body swap if their rig crate is known
            try
            {
                if (NetworkPlayerManager.TryGetPlayer(id.SmallID, out NetworkPlayer np) &&
                    np != null && np.HasRig && np.RigRefs?.RigManager != null)
                {
                    var aref = np.RigRefs.RigManager.AvatarCrate;
                    if (aref != null && aref.Barcode != null)
                    {
                        string barcode = aref.Barcode.ID;
                        if (!string.IsNullOrWhiteSpace(barcode))
                            LocalAvatar.AvatarOverride = barcode;
                    }
                }
            }
            catch { /* avatar body optional */ }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Ghost clone: {ex.Message}");
        }
    }

    public static void Restore()
    {
        if (!_stored) return;
        try
        {
            LocalPlayer.Username = _realUsername;
            if (LocalPlayer.Metadata?.Nickname != null)
                LocalPlayer.Metadata.Nickname.SetValue(_realNickname);
            if (ClientSettings.Nickname != null)
                ClientSettings.Nickname.Value = _realNickname;
            if (LocalPlayer.Metadata?.Description != null)
                LocalPlayer.Metadata.Description.SetValue(_realDescription);
            if (ClientSettings.Description != null)
                ClientSettings.Description.Value = _realDescription;
            if (LocalPlayer.Metadata?.AvatarTitle != null)
                LocalPlayer.Metadata.AvatarTitle.SetValue(_realAvatarTitle);
            if (LocalPlayer.Metadata?.AvatarModID != null)
                LocalPlayer.Metadata.AvatarModID.SetValue(_realAvatarModId);
            LocalAvatar.AvatarOverride = null;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Ghost restore: {ex.Message}");
        }
        finally
        {
            _stored = false;
        }
    }

    public static List<(string label, PlayerID id)> ListSessionPlayers()
    {
        var list = new List<(string, PlayerID)>();
        try
        {
            foreach (PlayerID id in PlayerIDManager.PlayerIDs)
            {
                if (id == null || id.IsMe) continue;
                string name = id.Metadata.Nickname.GetValueOrEmpty();
                if (string.IsNullOrWhiteSpace(name))
                    name = id.Metadata.Username.GetValueOrEmpty();
                if (string.IsNullOrWhiteSpace(name))
                    name = "Player";
                list.Add((name, id));
            }
        }
        catch { /* ignore */ }
        return list;
    }
}
