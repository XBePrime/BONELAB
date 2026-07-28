using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.AI;
using LabFusion.Entities;
using MelonLoader;
using UnityEngine;

namespace BePrime.Esp;

/// <summary>
/// LabFusion player ESP targets (excludes local player).
/// Only touched when LabFusion is loaded.
/// </summary>
public sealed class EspPlayer
{
    public static readonly List<EspPlayer> All = new List<EspPlayer>();

    private readonly NetworkPlayer _networkPlayer;
    private Bounds _bounds;
    private float _nextBoundsAt;
    private bool _hasBounds;

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

    public Transform Root => RigManager != null ? RigManager.transform.root : null;

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

    public bool TryGetBounds(out Bounds bounds)
    {
        bounds = default;
        if (!IsValid)
            return false;
        if (IsDead && !EspMod.ShowDead)
            return false;

        float now = Time.unscaledTime;
        if (!_hasBounds || now >= _nextBoundsAt)
        {
            _bounds = BuildBounds();
            _hasBounds = true;
            _nextBoundsAt = now + EspMod.BoundsRefreshSeconds;
        }

        bounds = _bounds;
        return _bounds.size.sqrMagnitude > 0.0001f;
    }

    private Bounds BuildBounds()
    {
        Vector3 head = HeadPosition;
        Transform root = Root;
        Vector3 feet = root != null ? root.position : head - Vector3.up * 1.7f;

        float height = Mathf.Max(0.7f, (head.y - feet.y) + 0.2f);
        float width = Mathf.Clamp(height * 0.30f, 0.30f, 0.52f);
        float depth = width * 0.9f;
        Vector3 center = new Vector3(head.x, feet.y + height * 0.5f, head.z);
        return new Bounds(center, new Vector3(width, height, depth));
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
