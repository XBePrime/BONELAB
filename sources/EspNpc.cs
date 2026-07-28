using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow.AI;
using Il2CppSLZ.Marrow.PuppetMasta;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BePrime.Esp;

/// <summary>NPC tracker — bone landmarks only (no RB soup).</summary>
public sealed class EspNpc
{
    public static readonly List<EspNpc> All = new List<EspNpc>();

    public TriggerRefProxy Proxy;
    public int Uuid;
    public bool Dying;

    private float _displayHp = 1f;
    private bool _hasHp;

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

    public bool TryGetHp01(out float hp01)
    {
        hp01 = 1f;
        try
        {
            if (IsDead)
            {
                hp01 = 0f;
                return true;
            }
            if (Brain == null || Brain.behaviour == null || Brain.behaviour.health == null)
                return false;

            SubBehaviourHealth h = Brain.behaviour.health;
            float max = h.maxHitPoints;
            if (max < 1f)
                return false;
            hp01 = Mathf.Clamp01(h.cur_hp / max);
            // Some NPCs report 0 until first hit — treat as full while alive
            if (hp01 <= 0.001f && !IsDead && h.cur_hp <= 0.001f)
                hp01 = 1f;
            return true;
        }
        catch
        {
            return false;
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

        if (!TryBodyPoints(out Vector3 feet, out Vector3 chest, out Vector3 head, out float width))
            return false;

        float hp = 1f;
        _hasHp = TryGetHp01(out hp);
        if (dead)
        {
            hp = 0f;
            _hasHp = true;
        }
        if (!_hasHp)
            hp = 1f;

        _displayHp = Mathf.MoveTowards(_displayHp, hp, Time.unscaledDeltaTime * EspMod.HpAnimSpeed);

        frame = new EspFrame
        {
            Head = head,
            Feet = feet,
            Chest = chest,
            Width = width,
            Hp01 = hp,
            DisplayHp = _displayHp,
            HasHp = _hasHp,
            Dead = dead,
            IsPlayer = false,
            Color = ResolveColor(dead)
        };
        return true;
    }

    /// <summary>
    /// Tight body from head/chest/feet only.
    /// When ragdolled, prefer head+chest cluster so feet/root left on the floor
    /// don't stretch the box to the ground / platform height.
    /// </summary>
    private bool TryBodyPoints(out Vector3 feet, out Vector3 chest, out Vector3 head, out float width)
    {
        feet = chest = head = Vector3.zero;
        width = 0.35f;

        Vector3 headP = default;
        Vector3 chestP = default;
        Vector3 feetP = default;
        bool hasHead = false, hasChest = false, hasFeet = false;

        try
        {
            if (Proxy != null)
            {
                if (Proxy.targetHead != null)
                {
                    headP = Proxy.targetHead.position;
                    hasHead = true;
                }
                if (Proxy.chestTran != null)
                {
                    chestP = Proxy.chestTran.position;
                    hasChest = true;
                }
                if (Proxy.feetTran != null)
                {
                    feetP = Proxy.feetTran.position;
                    hasFeet = true;
                }
                else if (Proxy.root != null)
                {
                    feetP = Proxy.root.transform.position;
                    hasFeet = true;
                }
            }
        }
        catch { /* ignore */ }

        if (!hasHead && !hasChest)
            return false;

        if (!hasHead) headP = chestP + Vector3.up * 0.35f;
        if (!hasChest) chestP = hasHead ? headP - Vector3.up * 0.35f : feetP + Vector3.up * 1f;

        // Ragdoll / laid down: if feet are far from torso, ignore feet — use body length along torso
        bool ignoreFeet = false;
        if (hasFeet)
        {
            float torsoFeet = Vector3.Distance(chestP, feetP);
            float headChest = Vector3.Distance(headP, chestP);
            // Feet left on floor / platform while torso moved, or stretched absurdly
            if (torsoFeet > Mathf.Max(1.4f, headChest * 4f))
                ignoreFeet = true;
            // Horizontal body: vertical gap between feet and head is small but feet XZ far — still use feet
            // Vertical stretch artifact: large Y gap while chest-head are together on a surface
            float ySpan = Mathf.Abs(headP.y - feetP.y);
            float xzFeet = Vector3.Distance(
                new Vector3(chestP.x, 0f, chestP.z),
                new Vector3(feetP.x, 0f, feetP.z));
            if (ySpan > 1.2f && xzFeet < 0.35f && Vector3.Distance(headP, chestP) < 0.55f)
            {
                // Classic "on a table" bug: feet/root still at ground Y
                ignoreFeet = true;
            }
        }

        if (!hasFeet || ignoreFeet)
        {
            // Estimate feet from head→chest direction extended
            Vector3 down = (chestP - headP);
            if (down.sqrMagnitude < 0.0001f)
                down = Vector3.down;
            else
                down.Normalize();
            float bodyLen = 1.55f;
            feetP = headP + down * bodyLen;
            // If nearly upright, clamp feet below chest
            if (Vector3.Dot(down, Vector3.down) > 0.5f)
                feetP = new Vector3(chestP.x, Mathf.Min(chestP.y, headP.y) - 1.15f, chestP.z);
        }

        head = headP + Vector3.up * 0.03f;
        chest = chestP;
        feet = feetP;

        float h = Vector3.Distance(head, feet);
        width = Mathf.Clamp(h * 0.28f, 0.28f, 0.48f);
        return h > 0.15f;
    }

    private static Color ResolveColor(bool dead)
    {
        if (dead)
            return new Color(1f, 0.12f, 0.12f, 1f);
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
            if (All[i] != null && All[i].Uuid == id)
            {
                All[i].Proxy = proxy;
                All[i].Dying = false;
                return;
            }
        }

        All.Add(new EspNpc { Proxy = proxy, Uuid = id, Dying = false, _displayHp = 1f });
    }

    public static bool TryGetById(int id, out EspNpc npc)
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i] != null && All[i].Uuid == id)
            {
                npc = All[i];
                return true;
            }
        }
        npc = null;
        return false;
    }

    public static void Prune()
    {
        for (int i = All.Count - 1; i >= 0; i--)
            if (All[i] == null || !All[i].IsValid)
                All.RemoveAt(i);
    }

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
