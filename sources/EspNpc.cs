using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow.AI;
using MelonLoader;
using UnityEngine;

namespace BePrime.Esp;

/// <summary>
/// Lightweight NPC tracker for ESP bounds.
/// </summary>
[RegisterTypeInIl2Cpp(false)]
public class EspNpc : MonoBehaviour
{
    public static readonly HashSet<EspNpc> All = new HashSet<EspNpc>();

    public TriggerRefProxy Proxy;
    public int Uuid;
    public bool Dying;

    private Bounds _bounds;
    private float _nextBoundsAt;
    private bool _hasBounds;

    public EspNpc(IntPtr ptr) : base(ptr) { }

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

    public bool IsValid => this != null && Proxy != null && Brain != null;

    public Transform Root => transform != null ? transform.root : null;

    public Vector3 HeadPosition =>
        Proxy != null && Proxy.targetHead != null ? Proxy.targetHead.position : transform.position;

    public void Start()
    {
        All.Add(this);
    }

    public void OnDestroy()
    {
        All.Remove(this);
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

        // Prefer chest if present for a tighter torso width cue.
        Vector3 mid = head;
        if (Proxy != null && Proxy.chestTran != null)
            mid = Proxy.chestTran.position;

        float height = Mathf.Max(0.6f, (head.y - feet.y) + 0.18f);
        float width = Mathf.Clamp(height * 0.32f, 0.28f, 0.55f);
        float depth = width * 0.85f;

        Vector3 center = new Vector3(mid.x, feet.y + height * 0.5f, mid.z);
        return new Bounds(center, new Vector3(width, height, depth));
    }

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

        EspNpc target = proxy.gameObject.GetComponent<EspNpc>();
        if (target == null)
            target = proxy.gameObject.AddComponent<EspNpc>();

        target.Proxy = proxy;
        target.Dying = false;
        target.Uuid = proxy.transform.root.GetInstanceID();
        All.Add(target);
    }

    public static bool TryGetById(int id, out EspNpc npc)
    {
        foreach (EspNpc candidate in All)
        {
            if (candidate != null && candidate.Uuid == id)
            {
                npc = candidate;
                return true;
            }
        }

        npc = null;
        return false;
    }
}
