using System;
using System.Collections.Generic;
using HarmonyLib;
using LabFusion.Data;
using LabFusion.Network;
using LabFusion.Representation;
using MelonLoader;
using UnityEngine;

namespace BePrime.Ghost;

/// <summary>
/// Host-only fake players injected into lobby browser metadata.
/// </summary>
public static class GhostLobby
{
    public sealed class FakeSlot
    {
        public string Username;
        public string Nickname;
        public string AvatarTitle;
        public int AvatarModId = -1;
    }

    public static readonly List<FakeSlot> Fakes = new List<FakeSlot>();

    public static readonly string[] AvatarPresets =
    {
        "Ford", "Heavy", "Peasant", "Gymmi", "PolyBlank"
    };

    public static bool IsHost
    {
        get
        {
            try { return NetworkInfo.HasServer && NetworkInfo.IsHost; }
            catch { return false; }
        }
    }

    public static string EnsureHostOrError()
    {
        if (IsHost) return null;
        return "HOST ONLY — start / host a lobby first";
    }

    public static void AddFake(string username = null, string avatarTitle = "Ford")
    {
        string err = EnsureHostOrError();
        if (err != null)
        {
            MelonLogger.Warning($"Ghost Lobby: {err}");
            GhostHolo.ShowToast(err);
            return;
        }

        int n = Fakes.Count + 1;
        Fakes.Add(new FakeSlot
        {
            Username = string.IsNullOrWhiteSpace(username) ? $"GHOST_{n:00}" : username,
            Nickname = "",
            AvatarTitle = avatarTitle ?? "Ford",
            AvatarModId = -1
        });
        Push();
    }

    public static void ClearFakes()
    {
        string err = EnsureHostOrError();
        if (err != null)
        {
            MelonLogger.Warning($"Ghost Lobby: {err}");
            GhostHolo.ShowToast(err);
            return;
        }
        Fakes.Clear();
        Push();
    }

    public static void Push()
    {
        if (!IsHost) return;
        try { LobbyInfoManager.PushLobbyUpdate(); }
        catch (Exception ex) { MelonLogger.Warning($"Ghost lobby push: {ex.Message}"); }
    }

    public static void Install(HarmonyLib.Harmony harmony)
    {
        try
        {
            var write = AccessTools.Method(typeof(PlayerList), nameof(PlayerList.WritePlayers));
            if (write != null)
            {
                harmony.Patch(write, postfix: new HarmonyMethod(typeof(GhostLobby), nameof(WritePlayersPostfix)));
            }

            var lobby = AccessTools.Method(typeof(LobbyInfo), nameof(LobbyInfo.WriteLobby));
            if (lobby != null)
            {
                harmony.Patch(lobby, postfix: new HarmonyMethod(typeof(GhostLobby), nameof(WriteLobbyPostfix)));
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Ghost lobby patch: {ex}");
        }
    }

    public static void WritePlayersPostfix(PlayerList __instance)
    {
        if (!GhostMod.Enabled || !IsHost || Fakes.Count == 0) return;
        try
        {
            var real = __instance.Players ?? Array.Empty<PlayerInfo>();
            var merged = new PlayerInfo[real.Length + Fakes.Count];
            for (int i = 0; i < real.Length; i++)
                merged[i] = real[i];

            for (int i = 0; i < Fakes.Count; i++)
            {
                FakeSlot f = Fakes[i];
                merged[real.Length + i] = new PlayerInfo
                {
                    PlatformID = $"ghost-fake-{i}-{Mathf.Abs(f.Username.GetHashCode())}",
                    Username = f.Username,
                    Nickname = f.Nickname ?? "",
                    Description = "",
                    PermissionLevel = PermissionLevel.DEFAULT,
                    AvatarTitle = f.AvatarTitle ?? "Ford",
                    AvatarModID = f.AvatarModId
                };
            }
            __instance.Players = merged;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Ghost WritePlayers: {ex.Message}");
        }
    }

    public static void WriteLobbyPostfix(LobbyInfo __instance)
    {
        if (!GhostMod.Enabled || !IsHost || Fakes.Count == 0) return;
        try
        {
            if (__instance.PlayerList?.Players != null)
                __instance.PlayerCount = __instance.PlayerList.Players.Length;
        }
        catch { /* ignore */ }
    }
}
