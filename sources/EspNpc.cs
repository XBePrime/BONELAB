using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow.AI;
using UnityEngine;

namespace BePrime.Esp;

/// <summary>
/// Plain C# NPC tracker — no RegisterTypeInIl2Cpp / MonoBehaviour (Quest-safe).
/// </summary>
public sealed class EspNpc
{
    public static readonly List<EspNpc> All = new List<EspNpc>();

    public TriggerRefProxy Proxy;
    public int Uuid;
    public bool Dying;

    private Bounds _bounds;
    private float _nextBoundsAt;
    private bool _hasBounds;

    public AIBrain Brain => Proxy != null ? Proxy.aiManager : null;

    public bool IsDead
    {
        get
        {
            if (Proxy == null || Brain == null)
                return true;
            return Brain.isDead || Dying;
        }
    }

    public bool IsValid
    {
        get
        {
            try
            {
                return Proxy != null && Brain != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public Transform Root
    {
        get
        {
            try
            {
                return Proxy != null ? Proxy.transform.root : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public Vector3 HeadPosition
    {
        get
        {
            try
            {
                if (Proxy != null && Proxy.targetHead != null)
                    return Proxy.targetHead.position;
                if (Proxy != null)
                    return Proxy.transform.position;
            }
            catch { /* ignore */ }
            return Vector3.zero;
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

        Vector3 mid = head;
        try
        {
            if (Proxy != null && Proxy.chestTran != null)
                mid = Proxy.chestTran.position;
        }
        catch { /* ignore */ }

        float height = Mathf.Max(0.6f, (head.y - feet.y) + 0.18f);
        float width = Mathf.Clamp(height * 0.32f, 0.28f, 0.55f);
        float depth = width * 0.85f;
        Vector3 center = new Vector3(mid.x, feet.y + height * 0.5f, mid.z);
        return new Bounds(center, new Vector3(width, height, depth));
    }

    public static void Clear() => All.Clear();

    public static void Bind(TriggerRefProxy proxy)
    {
        if (proxy == null)
            return;

        AIBrain brain = proxy.aiManager;
        if (brain == null)
            return;

        try
        {
            if (brain.transform.root.name.StartsWith("OmniWay"))
                return;
        }
        catch { /* ignore */ }

        int id = proxy.transform.root.GetInstanceID();
        for (int i = 0; i < All.Count; i++)
        {
            EspNpc existing = All[i];
            if (existing != null && existing.Uuid == id)
            {
                existing.Proxy = proxy;
                existing.Dying = false;
                existing._hasBounds = false;
                return;
            }
        }

        All.Add(new EspNpc
        {
            Proxy = proxy,
            Uuid = id,
            Dying = false
        });
    }

    public static bool TryGetById(int id, out EspNpc npc)
    {
        for (int i = 0; i < All.Count; i++)
        {
            EspNpc candidate = All[i];
            if (candidate != null && candidate.Uuid == id)
            {
                npc = candidate;
                return true;
            }
        }
        npc = null;
        return false;
    }

    public static void Prune()
    {
        for (int i = All.Count - 1; i >= 0; i--)
        {
            EspNpc n = All[i];
            if (n == null || !n.IsValid)
                All.RemoveAt(i);
        }
    }
}
