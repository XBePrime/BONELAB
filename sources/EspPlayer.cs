using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.AI;
using LabFusion.Entities;
using LabFusion.Player;
using LabFusion.Senders;
using LabFusion.Utilities;
using MelonLoader;
using UnityEngine;

namespace BePrime.Esp;

public sealed class EspPlayer
{
    public static readonly List<EspPlayer> All = new List<EspPlayer>();

    private static bool _hooksBound;

    private readonly NetworkPlayer _networkPlayer;

    /// <summary>Set by Fusion PlayerAction (DYING/DEATH) — remote alive flag is not synced.</summary>
    private bool _actionDead;

    public EspPlayer(NetworkPlayer np) => _networkPlayer = np;

    public NetworkPlayer Net => _networkPlayer;

    public bool HasRig =>
        _networkPlayer != null && _networkPlayer.HasRig && _networkPlayer.RigRefs != null && _networkPlayer.RigRefs.IsValid;

    public RigManager RigManager => HasRig ? _networkPlayer.RigRefs.RigManager : null;
    public TriggerRefProxy Proxy => HasRig ? _networkPlayer.RigRefs.Proxy : null;

    public bool IsDead
    {
        get
        {
            try
            {
                if (_actionDead)
                    return true;

                // Fusion synced health bar (pose.Health)
                if (_networkPlayer != null && _networkPlayer.HealthBar != null)
                {
                    if (_networkPlayer.HealthBar.Health <= 0.01f)
                        return true;
                    if (_networkPlayer.HealthBar.MaxHealth > 0.01f &&
                        _networkPlayer.HealthBar.HealthPercent <= 0.001f)
                        return true;
                }

                if (!HasRig)
                    return false;

                Player_Health health = _networkPlayer.RigRefs.Health;
                if (health != null)
                {
                    if (!health.alive)
                        return true;
                    if (health.deathIsImminent)
                        return true;
                    if (health.max_Health > 0.01f && health.curr_Health <= 0.01f)
                        return true;
                }

                // Local ragdoll shutdown (rare on remotes, still useful)
                try
                {
                    var pr = RigManager != null ? RigManager.physicsRig : null;
                    if (pr != null && pr.shutdown)
                        return true;
                }
                catch { /* ignore */ }

                return false;
            }
            catch { return false; }
        }
    }

    public bool IsValid => _networkPlayer != null && HasRig && RigManager != null;

    public Vector3 HeadPos
    {
        get
        {
            if (Proxy != null && Proxy.targetHead != null) return Proxy.targetHead.position;
            if (HasRig && _networkPlayer.RigRefs.Head != null) return _networkPlayer.RigRefs.Head.position;
            return RigManager != null ? RigManager.transform.position + Vector3.up * 1.6f : Vector3.zero;
        }
    }

    public Vector3 ChestPos
    {
        get
        {
            if (Proxy != null && Proxy.chestTran != null) return Proxy.chestTran.position;
            return Vector3.Lerp(RigManager != null ? RigManager.transform.position : HeadPos - Vector3.up * 1.6f, HeadPos, 0.55f);
        }
    }

    public Vector3 FeetPos
    {
        get
        {
            if (Proxy != null && Proxy.feetTran != null) return Proxy.feetTran.position;
            return RigManager != null ? RigManager.transform.position : HeadPos - Vector3.up * 1.7f;
        }
    }

    public bool TryGetHp01(out float hp01)
    {
        hp01 = 1f;
        try
        {
            if (IsDead) { hp01 = 0f; return true; }

            if (_networkPlayer != null && _networkPlayer.HealthBar != null &&
                _networkPlayer.HealthBar.MaxHealth > 0.01f)
            {
                hp01 = Mathf.Clamp01(_networkPlayer.HealthBar.HealthPercent);
                return true;
            }

            if (!HasRig) return false;
            Player_Health health = _networkPlayer.RigRefs.Health;
            if (health == null) return false;
            if (!health.alive) { hp01 = 0f; return true; }
            float max = health.max_Health;
            if (max < 0.01f) return false;
            hp01 = Mathf.Clamp01(health.curr_Health / max);
            return true;
        }
        catch { return false; }
    }

    public bool TryBuildFrame(out EspFrame frame)
    {
        frame = default;
        if (!IsValid) return false;

        bool dead = IsDead;
        if (dead && !EspMod.ShowDead) return false;

        Vector3 head = HeadPos;
        Vector3 chest = ChestPos;
        Vector3 feet = FeetPos;

        if (dead)
        {
            Vector3 axis = chest - head;
            if (axis.sqrMagnitude < 0.0001f) axis = Vector3.down;
            else axis.Normalize();
            feet = head + axis * 1.45f;
            if (feet.y < chest.y - 1.5f)
                feet = new Vector3(chest.x, chest.y - 0.85f, chest.z);
            if (head.y < chest.y - 0.15f)
                head = chest + Vector3.up * 0.35f;
        }
        else
        {
            float h = head.y - feet.y;
            if (h < 0.5f || h > 2.6f)
                feet = new Vector3(head.x, head.y - 1.7f, head.z);
        }

        head += Vector3.up * 0.03f;
        Vector3 center = dead ? chest : (head + feet) * 0.5f;
        float height = Vector3.Distance(head, feet);
        float width = Mathf.Clamp(height * 0.26f, 0.28f, 0.42f);

        float hp = 1f;
        bool hasHp = TryGetHp01(out hp);
        if (dead) { hp = 0f; hasHp = false; }

        frame = new EspFrame
        {
            Head = head,
            Feet = feet,
            Chest = chest,
            Center = center,
            Width = width,
            Hp01 = hp,
            DisplayHp = hp,
            HasHp = hasHp && !dead,
            Dead = dead,
            IsPlayer = true,
            Color = dead ? EspMod.DeadColor : LiveColor()
        };
        return true;
    }

    private static Color LiveColor()
    {
        if (EspMod.Rainbow)
            return Color.HSVToRGB((Time.unscaledTime * EspMod.RainbowSpeed) % 1f, 0.85f, 1f);
        return new Color(EspMod.ColorR, EspMod.ColorG, EspMod.ColorB, 1f);
    }

    public static void Clear()
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i] != null) All[i]._actionDead = false;
        All.Clear();
    }

    public static void EnsureHooks()
    {
        if (_hooksBound || !EspMod.FusionLoaded) return;
        try
        {
            MultiplayerHooking.OnPlayerAction += OnPlayerAction;
            _hooksBound = true;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ESP player action hook: {ex.Message}");
        }
    }

    private static void OnPlayerAction(PlayerID playerId, PlayerActionType type, PlayerID otherPlayer = null)
    {
        if (playerId == null || playerId.IsMe) return;

        bool markDead =
            type == PlayerActionType.DYING ||
            type == PlayerActionType.DYING_BY_OTHER_PLAYER ||
            type == PlayerActionType.DEATH ||
            type == PlayerActionType.DEATH_BY_OTHER_PLAYER;

        bool markAlive =
            type == PlayerActionType.RECOVERY ||
            type == PlayerActionType.RESPAWN;

        if (!markDead && !markAlive) return;

        for (int i = 0; i < All.Count; i++)
        {
            EspPlayer p = All[i];
            if (p == null || p._networkPlayer == null || p._networkPlayer.PlayerID == null)
                continue;
            if (p._networkPlayer.PlayerID != playerId)
                continue;

            if (markDead) p._actionDead = true;
            if (markAlive) p._actionDead = false;
            break;
        }
    }

    public static void SyncFromFusion()
    {
        if (!EspMod.FusionLoaded) return;
        EnsureHooks();
        try
        {
            var seen = new HashSet<NetworkPlayer>();
            foreach (NetworkPlayer np in NetworkPlayer.Players)
            {
                if (np == null || np.PlayerID == null || np.PlayerID.IsMe) continue;
                seen.Add(np);
                bool exists = false;
                for (int i = 0; i < All.Count; i++)
                    if (All[i] != null && All[i]._networkPlayer == np) { exists = true; break; }
                if (!exists) All.Add(new EspPlayer(np));
            }
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var e = All[i];
                if (e == null || e._networkPlayer == null || !seen.Contains(e._networkPlayer))
                    All.RemoveAt(i);
            }
        }
        catch (Exception ex) { MelonLogger.Warning($"ESP player sync: {ex.Message}"); }
    }
}
