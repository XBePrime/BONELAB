using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow.AI;
using Il2CppSLZ.Marrow.PuppetMasta;
using UnityEngine;

namespace BePrime.Esp;

/// <summary>Plain C# NPC tracker — full-body bounds + HP.</summary>
public sealed class EspNpc
{
    public static readonly List<EspNpc> All = new List<EspNpc>();

    public TriggerRefProxy Proxy;
    public int Uuid;
    public bool Dying;

    private float _displayHp = 1f;

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

    public Vector3 ChestPosition
    {
        get
        {
            try
            {
                if (Proxy != null && Proxy.chestTran != null)
                    return Proxy.chestTran.position;
            }
            catch { /* ignore */ }
            return HeadPosition - Vector3.up * 0.35f;
        }
    }

    public Vector3 FeetPosition
    {
        get
        {
            try
            {
                if (Proxy != null && Proxy.feetTran != null)
                    return Proxy.feetTran.position;
                if (Proxy != null && Proxy.root != null)
                    return Proxy.root.transform.position;
                if (Proxy != null)
                    return Proxy.transform.root.position;
            }
            catch { /* ignore */ }
            return HeadPosition - Vector3.up * 1.7f;
        }
    }

    /// <summary>0..1 health ratio, or -1 if unknown.</summary>
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

        Vector3 head = HeadPosition;
        Vector3 feet = FeetPosition;
        Vector3 chest = ChestPosition;

        // Full body: always include head / chest / feet (works when ragdolled)
        float hp = dead ? 0f : GetHp01();
        if (hp < 0f)
            hp = dead ? 0f : 1f;

        _displayHp = Mathf.MoveTowards(_displayHp, hp, Time.unscaledDeltaTime * EspMod.HpAnimSpeed);

        frame = new EspFrame
        {
            Head = head + Vector3.up * 0.03f,
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

        int id = proxy.transform.root.GetInstanceID();
        for (int i = 0; i < All.Count; i++)
        {
            EspNpc existing = All[i];
            if (existing != null && existing.Uuid == id)
            {
                existing.Proxy = proxy;
                existing.Dying = false;
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
}
