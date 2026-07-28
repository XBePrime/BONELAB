using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow.AI;
using Il2CppSLZ.Marrow.PuppetMasta;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BePrime.Esp;

/// <summary>NPC tracker — live body bounds (follows ragdoll / grabs).</summary>
public sealed class EspNpc
{
    public static readonly List<EspNpc> All = new List<EspNpc>();

    public TriggerRefProxy Proxy;
    public int Uuid;
    public bool Dying;

    private float _displayHp = 1f;
    private readonly List<Rigidbody> _rbs = new List<Rigidbody>(32);
    private float _nextRbScan = -1f;

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
            try { return Proxy != null && Brain != null; }
            catch { return false; }
        }
    }

    public Vector3 HeadHint
    {
        get
        {
            try
            {
                if (Proxy != null && Proxy.targetHead != null)
                    return Proxy.targetHead.position;
            }
            catch { /* ignore */ }
            return Vector3.zero;
        }
    }

    public float GetHp01()
    {
        try
        {
            if (Brain == null)
                return -1f;
            BehaviourBaseNav behaviour = Brain.behaviour;
            if (behaviour == null || behaviour.health == null)
                return IsDead ? 0f : -1f;

            SubBehaviourHealth h = behaviour.health;
            float max = h.maxHitPoints;
            if (max <= 0.01f)
                return IsDead ? 0f : -1f;
            return Mathf.Clamp01(h.cur_hp / max);
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
            IsPlayer = false,
            Color = ResolveColor(dead)
        };
        return true;
    }

    /// <summary>
    /// Live AABB from rigidbodies / proxy bones — updates when body is dragged.
    /// </summary>
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
            if (rb == null)
                continue;
            Encapsulate(rb.worldCenterOfMass);
            Encapsulate(rb.position);
        }

        // Always fold in proxy landmarks when present
        try
        {
            if (Proxy != null)
            {
                if (Proxy.targetHead != null)
                    Encapsulate(Proxy.targetHead.position);
                if (Proxy.chestTran != null)
                    Encapsulate(Proxy.chestTran.position);
                if (Proxy.feetTran != null)
                    Encapsulate(Proxy.feetTran.position);
                else if (Proxy.root != null)
                    Encapsulate(Proxy.root.transform.position);
            }
        }
        catch { /* ignore */ }

        if (!any)
            return false;

        // Pad slightly so box covers volume, not just COM points
        Vector3 pad = new Vector3(0.12f, 0.08f, 0.12f);
        min -= pad;
        max += pad;

        Vector3 center = (min + max) * 0.5f;
        feet = new Vector3(center.x, min.y, center.z);
        head = new Vector3(center.x, max.y + 0.03f, center.z);
        chest = new Vector3(center.x, Mathf.Lerp(min.y, max.y, 0.55f), center.z);

        // Prefer real head bone XZ if available (skull sits on actual head)
        Vector3 hh = HeadHint;
        if (hh.sqrMagnitude > 0.001f)
        {
            head = new Vector3(hh.x, Mathf.Max(hh.y + 0.03f, max.y + 0.03f), hh.z);
            // Keep feet under body center, not under head when ragdolled sideways
        }

        return (max - min).sqrMagnitude > 0.01f;
    }

    private void RefreshRigidbodies()
    {
        float now = Time.unscaledTime;
        if (_rbs.Count > 0 && now < _nextRbScan)
        {
            // Drop nulls cheaply
            for (int i = _rbs.Count - 1; i >= 0; i--)
                if (_rbs[i] == null) _rbs.RemoveAt(i);
            return;
        }

        _nextRbScan = now + 0.5f;
        _rbs.Clear();
        try
        {
            Transform root = Proxy != null ? Proxy.transform.root : null;
            if (root == null)
                return;
            foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>())
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

        // Skip local player proxy if any
        try
        {
            if (proxy.transform.root.name.Contains("Player") && brain.behaviour == null)
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
                existing._nextRbScan = -1f;
                return;
            }
        }

        All.Add(new EspNpc
        {
            Proxy = proxy,
            Uuid = id,
            Dying = false,
            _displayHp = 1f
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

    /// <summary>Find already-spawned NPCs (Fusion lobby / late load).</summary>
    public static void RescanWorld()
    {
        try
        {
            foreach (TriggerRefProxy proxy in Object.FindObjectsOfType<TriggerRefProxy>())
                Bind(proxy);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ESP NPC rescan: {ex.Message}");
        }
    }
}
