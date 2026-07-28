using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.AI;
using LabFusion.Entities;
using MelonLoader;
using UnityEngine;

namespace BePrime.Esp;

/// <summary>Player ESP — tight head/chest/feet bones only (no RB inflate).</summary>
public sealed class EspPlayer
{
    public static readonly List<EspPlayer> All = new List<EspPlayer>();

    private readonly NetworkPlayer _networkPlayer;
    private float _displayHp = 1f;

    public EspPlayer(NetworkPlayer networkPlayer) => _networkPlayer = networkPlayer;

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
                if (!HasRig) return false;
                Player_Health health = _networkPlayer.RigRefs.Health;
                if (health == null) return false;
                return !health.alive;
            }
            catch { return false; }
        }
    }

    public bool IsValid => HasRig && RigManager != null;

    public Vector3 HeadPosition
    {
        get
        {
            if (Proxy != null && Proxy.targetHead != null)
                return Proxy.targetHead.position;
            if (HasRig && _networkPlayer.RigRefs.Head != null)
                return _networkPlayer.RigRefs.Head.position;
            return RigManager != null ? RigManager.transform.position + Vector3.up * 1.65f : Vector3.zero;
        }
    }

    public Vector3 ChestPosition
    {
        get
        {
            if (Proxy != null && Proxy.chestTran != null)
                return Proxy.chestTran.position;
            return Vector3.Lerp(
                RigManager != null ? RigManager.transform.position : HeadPosition - Vector3.up * 1.6f,
                HeadPosition, 0.55f);
        }
    }

    public Vector3 FeetPosition
    {
        get
        {
            if (Proxy != null && Proxy.feetTran != null)
                return Proxy.feetTran.position;
            if (RigManager != null)
                return RigManager.transform.position;
            return HeadPosition - Vector3.up * 1.7f;
        }
    }

    public bool TryGetHp01(out float hp01)
    {
        hp01 = 1f;
        try
        {
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

        Vector3 head = HeadPosition + Vector3.up * 0.03f;
        Vector3 chest = ChestPosition;
        Vector3 feet = FeetPosition;

        // Sanity: if feet wildly below head (avatar glitch), rebuild from head
        float h = head.y - feet.y;
        if (h < 0.4f || h > 2.8f)
        {
            feet = new Vector3(head.x, head.y - 1.7f, head.z);
            h = 1.7f;
        }

        float width = Mathf.Clamp(h * 0.26f, 0.28f, 0.42f);

        float hp = 1f;
        bool hasHp = TryGetHp01(out hp);
        if (dead) { hp = 0f; hasHp = true; }
        if (!hasHp) hp = 1f;

        _displayHp = Mathf.MoveTowards(_displayHp, hp, Time.unscaledDeltaTime * EspMod.HpAnimSpeed);

        frame = new EspFrame
        {
            Head = head,
            Feet = feet,
            Chest = chest,
            Width = width,
            Hp01 = hp,
            DisplayHp = _displayHp,
            HasHp = hasHp,
            Dead = dead,
            IsPlayer = true,
            Color = ResolveColor(dead)
        };
        return true;
    }

    private static Color ResolveColor(bool dead)
    {
        if (dead) return new Color(1f, 0.12f, 0.12f, 1f);
        if (EspMod.Rainbow)
        {
            float hue = (Time.unscaledTime * EspMod.RainbowSpeed) % 1f;
            return Color.HSVToRGB(hue, 0.85f, 1f);
        }
        return new Color(EspMod.ColorR, EspMod.ColorG, EspMod.ColorB, 1f);
    }

    public static void Clear() => All.Clear();

    public static void SyncFromFusion()
    {
        if (!EspMod.FusionLoaded) return;
        try
        {
            var seen = new HashSet<NetworkPlayer>();
            foreach (NetworkPlayer np in NetworkPlayer.Players)
            {
                if (np == null || np.PlayerID == null || np.PlayerID.IsMe)
                    continue;
                seen.Add(np);
                bool exists = false;
                for (int i = 0; i < All.Count; i++)
                {
                    if (All[i] != null && All[i]._networkPlayer == np)
                    { exists = true; break; }
                }
                if (!exists) All.Add(new EspPlayer(np));
            }
            for (int i = All.Count - 1; i >= 0; i--)
            {
                EspPlayer e = All[i];
                if (e == null || e._networkPlayer == null || !seen.Contains(e._networkPlayer))
                    All.RemoveAt(i);
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ESP player sync: {ex.Message}");
        }
    }
}
