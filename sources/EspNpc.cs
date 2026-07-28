using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow.AI;
using Il2CppSLZ.Marrow.PuppetMasta;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BePrime.Esp;

public sealed class EspNpc
{
    public static readonly List<EspNpc> All = new List<EspNpc>();

    public TriggerRefProxy Proxy;
    public int Uuid;
    public bool Dying;

    private float _displayHp = 1f;
    private bool _seenRealHp;

    public AIBrain Brain => Proxy != null ? Proxy.aiManager : null;

    public bool IsDead
    {
        get
        {
            if (Proxy == null || Brain == null) return true;
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

    /// <summary>
    /// HP as 0..1. On spawn cur_hp is often 0 — treat as full until we see real damage.
    /// </summary>
    public bool TryGetHp01(out float hp01)
    {
        hp01 = 1f;
        try
        {
            if (IsDead) { hp01 = 0f; return true; }
            if (Brain?.behaviour?.health == null) return false;

            SubBehaviourHealth h = Brain.behaviour.health;
            float max = h.maxHitPoints;
            if (max < 1f) return false;

            float cur = h.cur_hp;
            // Uninitialized / not ready
            if (!_seenRealHp)
            {
                if (cur > 0.5f)
                    _seenRealHp = true;
                else
                {
                    hp01 = 1f; // spawn default = full
                    return true;
                }
            }

            hp01 = Mathf.Clamp01(cur / max);
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

        if (!TryBody(out Vector3 feet, out Vector3 chest, out Vector3 head, out Vector3 center, out float width))
            return false;

        float hp = 1f;
        bool hasHp = TryGetHp01(out hp);
        if (dead) { hp = 0f; hasHp = true; }
        if (!hasHp) hp = 1f;

        // Snap display — no floaty lerp dance
        _displayHp = hp;

        frame = new EspFrame
        {
            Head = head,
            Feet = feet,
            Chest = chest,
            Center = center,
            Width = width,
            Hp01 = hp,
            DisplayHp = _displayHp,
            HasHp = hasHp,
            Dead = dead,
            IsPlayer = false,
            Color = dead ? EspMod.DeadColor : ResolveLiveColor(hp)
        };
        return true;
    }

    private bool TryBody(out Vector3 feet, out Vector3 chest, out Vector3 head, out Vector3 center, out float width)
    {
        feet = chest = head = center = Vector3.zero;
        width = 0.35f;

        Vector3 headP = default, chestP = default, feetP = default;
        bool hasHead = false, hasChest = false, hasFeet = false;

        try
        {
            if (Proxy != null)
            {
                if (Proxy.targetHead != null) { headP = Proxy.targetHead.position; hasHead = true; }
                if (Proxy.chestTran != null) { chestP = Proxy.chestTran.position; hasChest = true; }
                if (Proxy.feetTran != null) { feetP = Proxy.feetTran.position; hasFeet = true; }
                else if (Proxy.root != null) { feetP = Proxy.root.transform.position; hasFeet = true; }
            }
        }
        catch { return false; }

        if (!hasHead && !hasChest) return false;
        if (!hasHead) headP = chestP + Vector3.up * 0.35f;
        if (!hasChest) chestP = headP - Vector3.up * 0.35f;

        bool ignoreFeet = false;
        if (hasFeet)
        {
            float ySpan = Mathf.Abs(headP.y - feetP.y);
            float xz = Vector3.Distance(new Vector3(chestP.x, 0, chestP.z), new Vector3(feetP.x, 0, feetP.z));
            float torso = Vector3.Distance(headP, chestP);
            if (ySpan > 1.15f && xz < 0.4f && torso < 0.6f)
                ignoreFeet = true; // feet stuck at ground / platform
            if (Vector3.Distance(chestP, feetP) > 2.2f)
                ignoreFeet = true;
        }

        if (!hasFeet || ignoreFeet)
        {
            // Body-oriented length from head through chest
            Vector3 axis = chestP - headP;
            if (axis.sqrMagnitude < 0.0001f) axis = Vector3.down;
            else axis.Normalize();
            feetP = headP + axis * 1.55f;
            if (Vector3.Dot(axis, Vector3.down) > 0.55f)
                feetP = new Vector3(chestP.x, Mathf.Min(headP.y, chestP.y) - 1.2f, chestP.z);
        }

        head = headP + Vector3.up * 0.03f;
        chest = chestP;
        feet = feetP;
        center = (head + feet) * 0.5f;
        if (IsDead)
            center = chestP; // skull / box focus on torso when dead

        float h = Vector3.Distance(head, feet);
        width = Mathf.Clamp(h * 0.28f, 0.28f, 0.48f);
        return h > 0.12f;
    }

    private static Color ResolveLiveColor(float hp)
    {
        if (EspMod.Rainbow)
        {
            float hue = (Time.unscaledTime * EspMod.RainbowSpeed) % 1f;
            return Color.HSVToRGB(hue, 0.85f, 1f);
        }
        return new Color(EspMod.ColorR, EspMod.ColorG, EspMod.ColorB, 1f);
    }

    public static void Clear() => All.Clear();

    public static void Bind(TriggerRefProxy proxy)
    {
        if (proxy == null) return;
        AIBrain brain = proxy.aiManager;
        if (brain == null) return;
        try { if (brain.transform.root.name.StartsWith("OmniWay")) return; } catch { }

        int id = proxy.transform.root.GetInstanceID();
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i] != null && All[i].Uuid == id)
            {
                All[i].Proxy = proxy;
                All[i].Dying = false;
                return;
            }
        }
        All.Add(new EspNpc { Proxy = proxy, Uuid = id, _displayHp = 1f, _seenRealHp = false });
    }

    public static bool TryGetById(int id, out EspNpc npc)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i] != null && All[i].Uuid == id) { npc = All[i]; return true; }
        npc = null; return false;
    }

    public static void Prune()
    {
        for (int i = All.Count - 1; i >= 0; i--)
            if (All[i] == null || !All[i].IsValid) All.RemoveAt(i);
    }

    public static void RescanWorld()
    {
        try
        {
            foreach (TriggerRefProxy p in Object.FindObjectsOfType<TriggerRefProxy>())
                Bind(p);
        }
        catch (Exception ex) { MelonLogger.Warning($"ESP NPC rescan: {ex.Message}"); }
    }
}
