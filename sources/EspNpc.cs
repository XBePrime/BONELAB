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

    /// <summary>HP the NPC had when first seen alive = 100% of the bar.</summary>
    private float _fullHp;
    private bool _hasFullHp;

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
    /// Remaining life fraction. Full bar = HP at spawn (not maxHitPoints=1000).
    /// </summary>
    public bool TryGetHp01(out float hp01)
    {
        hp01 = 1f;
        try
        {
            if (IsDead) { hp01 = 0f; return true; }
            if (Brain?.behaviour?.health == null) return false;

            float cur = Brain.behaviour.health.cur_hp;
            if (cur < 0f) cur = 0f;

            if (!_hasFullHp)
            {
                if (cur <= 0.01f)
                {
                    // Not ready yet — show full
                    hp01 = 1f;
                    return true;
                }
                _fullHp = cur;
                _hasFullHp = true;
            }

            if (cur > _fullHp)
                _fullHp = cur;

            if (_fullHp < 0.01f) { hp01 = 1f; return true; }
            hp01 = Mathf.Clamp01(cur / _fullHp);
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
            IsPlayer = false,
            Color = dead ? EspMod.DeadColor : LiveColor()
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
            if (ySpan > 1.15f && xz < 0.4f && Vector3.Distance(headP, chestP) < 0.6f)
                ignoreFeet = true;
            if (Vector3.Distance(chestP, feetP) > 2.2f)
                ignoreFeet = true;
        }

        if (!hasFeet || ignoreFeet)
        {
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
        center = IsDead ? chestP : (head + feet) * 0.5f;
        width = Mathf.Clamp(Vector3.Distance(head, feet) * 0.28f, 0.28f, 0.48f);
        return Vector3.Distance(head, feet) > 0.12f;
    }

    private static Color LiveColor()
    {
        if (EspMod.Rainbow)
            return Color.HSVToRGB((Time.unscaledTime * EspMod.RainbowSpeed) % 1f, 0.85f, 1f);
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
                All[i]._fullHp = 0f;
                All[i]._hasFullHp = false;
                return;
            }
        }
        All.Add(new EspNpc { Proxy = proxy, Uuid = id });
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
