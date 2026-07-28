using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.AI;
using LabFusion.Entities;
using MelonLoader;
using UnityEngine;

namespace BePrime.Esp;

/// <summary>LabFusion player ESP — heart→head+3cm box + HP.</summary>
public sealed class EspPlayer
{
    public static readonly List<EspPlayer> All = new List<EspPlayer>();

    private readonly NetworkPlayer _networkPlayer;
    private float _displayHp = 1f;

    public EspPlayer(NetworkPlayer networkPlayer)
    {
        _networkPlayer = networkPlayer;
    }

    public bool HasRig =>
        _networkPlayer != null && _networkPlayer.HasRig && _networkPlayer.RigRefs != null && _networkPlayer.RigRefs.IsValid;

    public RigManager RigManager =>
        HasRig ? _networkPlayer.RigRefs.RigManager : null;

    public TriggerRefProxy Proxy =>
        HasRig ? _networkPlayer.RigRefs.Proxy : null;

    public bool IsDead
    {
        get
        {
            try
            {
                if (!HasRig)
                    return true;
                Player_Health health = _networkPlayer.RigRefs.Health;
                if (health == null)
                    return false;
                return health.alive == false;
            }
            catch
            {
                return true;
            }
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
            return RigManager != null ? RigManager.transform.position + Vector3.up * 1.6f : Vector3.zero;
        }
    }

    public Vector3 ChestPosition
    {
        get
        {
            if (Proxy != null && Proxy.chestTran != null)
                return Proxy.chestTran.position;
            // Heart ≈ mid between pelvis/root and head
            Vector3 head = HeadPosition;
            Transform root = RigManager != null ? RigManager.transform : null;
            Vector3 basePos = root != null ? root.position : head - Vector3.up * 1.6f;
            return Vector3.Lerp(basePos, head, 0.55f);
        }
    }

    public float GetHp01()
    {
        try
        {
            if (!HasRig)
                return IsDead ? 0f : -1f;
            Player_Health health = _networkPlayer.RigRefs.Health;
            if (health == null)
                return IsDead ? 0f : -1f;
            float max = health.max_Health;
            if (max <= 0.01f)
                return health.alive ? 1f : 0f;
            return Mathf.Clamp01(health.curr_Health / max);
        }
        catch
        {
            return IsDead ? 0f : -1f;
        }
    }

    public bool TryBuildFrame(out EspFrame frame)
    {
        frame = default;
        if (!IsValid)
            return false;

        bool dead = IsDead;
        if (dead && !EspMod.ShowDead)
            return false;

        Vector3 head = HeadPosition;
        Vector3 chest = ChestPosition; // heart / chest — box bottom for players
        // Players: from heart to head + 3 cm
        Vector3 top = head + Vector3.up * 0.03f;
        Vector3 bottom = chest;

        float hp = dead ? 0f : GetHp01();
        if (hp < 0f)
            hp = dead ? 0f : 1f;

        _displayHp = Mathf.MoveTowards(_displayHp, hp, Time.unscaledDeltaTime * EspMod.HpAnimSpeed);

        frame = new EspFrame
        {
            Head = top,
            Feet = bottom,
            Chest = chest,
            Hp01 = hp,
            DisplayHp = _displayHp,
            Dead = dead,
            IsPlayer = true,
            Color = ResolveColor(dead)
        };
        return true;
    }

    private static Color ResolveColor(bool dead)
    {
        if (dead)
            return new Color(1f, 0.12f, 0.12f, 1f);
        if (EspMod.Rainbow)
        {
            float h = (Time.unscaledTime * EspMod.RainbowSpeed) % 1f;
            return Color.HSVToRGB(h, 0.85f, 1f);
        }
        return new Color(EspMod.ColorR, EspMod.ColorG, EspMod.ColorB, 1f);
    }

    public static void Clear() => All.Clear();

    public static void SyncFromFusion()
    {
        if (!EspMod.FusionLoaded)
            return;

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
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                    All.Add(new EspPlayer(np));
            }

            for (int i = All.Count - 1; i >= 0; i--)
            {
                EspPlayer entry = All[i];
                if (entry == null || entry._networkPlayer == null || !seen.Contains(entry._networkPlayer))
                    All.RemoveAt(i);
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ESP player sync: {ex.Message}");
        }
    }
}
