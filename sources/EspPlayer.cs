using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.AI;
using LabFusion.Entities;
using MelonLoader;
using UnityEngine;

namespace BePrime.Esp;

/// <summary>LabFusion player ESP — full body + death/HP.</summary>
public sealed class EspPlayer
{
    public static readonly List<EspPlayer> All = new List<EspPlayer>();

    private readonly NetworkPlayer _networkPlayer;
    private float _displayHp = 1f;
    private readonly List<Rigidbody> _rbs = new List<Rigidbody>(24);
    private float _nextRbScan = -1f;

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
                    return false; // no rig → skip frame, not "dead ESP"
                Player_Health health = _networkPlayer.RigRefs.Health;
                if (health == null)
                    return false;
                if (!health.alive)
                    return true;
                // low/zero HP while not flagged alive yet
                if (health.max_Health > 0.01f && health.curr_Health <= 0.01f)
                    return true;
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsValid => HasRig && RigManager != null;

    public Vector3 HeadHint
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

    public float GetHp01()
    {
        try
        {
            if (!HasRig)
                return -1f;
            Player_Health health = _networkPlayer.RigRefs.Health;
            if (health == null)
                return -1f;
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

        if (!TryLiveBody(out Vector3 feet, out Vector3 chest, out Vector3 head))
            return false;

        float hp = dead ? 0f : GetHp01();
        if (hp < 0f)
            hp = dead ? 0f : 1f;

        _displayHp = Mathf.MoveTowards(_displayHp, hp, Time.unscaledDeltaTime * EspMod.HpAnimSpeed);

        frame = new EspFrame
        {
            Head = head,
            Feet = feet,
            Chest = chest,
            Hp01 = hp,
            DisplayHp = _displayHp,
            Dead = dead,
            IsPlayer = true,
            Color = ResolveColor(dead)
        };
        return true;
    }

    private bool TryLiveBody(out Vector3 feet, out Vector3 chest, out Vector3 head)
    {
        feet = chest = head = Vector3.zero;
        RefreshRigidbodies();

        bool any = false;
        Vector3 min = Vector3.zero;
        Vector3 max = Vector3.zero;

        void Encapsulate(Vector3 p)
        {
            if (!any)
            {
                min = max = p;
                any = true;
            }
            else
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        for (int i = 0; i < _rbs.Count; i++)
        {
            Rigidbody rb = _rbs[i];
            if (rb == null) continue;
            Encapsulate(rb.worldCenterOfMass);
            Encapsulate(rb.position);
        }

        try
        {
            Vector3 hh = HeadHint;
            if (hh.sqrMagnitude > 0.001f)
                Encapsulate(hh);

            if (Proxy != null)
            {
                if (Proxy.chestTran != null)
                    Encapsulate(Proxy.chestTran.position);
                if (Proxy.feetTran != null)
                    Encapsulate(Proxy.feetTran.position);
            }

            if (RigManager != null)
                Encapsulate(RigManager.transform.position);
        }
        catch { /* ignore */ }

        if (!any)
            return false;

        // Full body for players (feet → head + 3cm)
        Vector3 pad = new Vector3(0.14f, 0.06f, 0.14f);
        min -= pad;
        max += pad;

        Vector3 center = (min + max) * 0.5f;
        feet = new Vector3(center.x, min.y, center.z);
        head = new Vector3(center.x, max.y + 0.03f, center.z);
        chest = new Vector3(center.x, Mathf.Lerp(min.y, max.y, 0.55f), center.z);

        Vector3 hint = HeadHint;
        if (hint.sqrMagnitude > 0.001f)
            head = new Vector3(hint.x, Mathf.Max(hint.y + 0.03f, max.y + 0.03f), hint.z);

        return (max - min).sqrMagnitude > 0.01f;
    }

    private void RefreshRigidbodies()
    {
        float now = Time.unscaledTime;
        if (_rbs.Count > 0 && now < _nextRbScan)
        {
            for (int i = _rbs.Count - 1; i >= 0; i--)
                if (_rbs[i] == null) _rbs.RemoveAt(i);
            return;
        }

        _nextRbScan = now + 0.5f;
        _rbs.Clear();
        try
        {
            if (RigManager == null)
                return;
            foreach (Rigidbody rb in RigManager.GetComponentsInChildren<Rigidbody>())
            {
                if (rb != null)
                    _rbs.Add(rb);
            }
        }
        catch { /* ignore */ }
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
