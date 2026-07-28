using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.AI;
using LabFusion.Entities;
using MelonLoader;
using UnityEngine;

namespace BePrime.Esp;

public sealed class EspPlayer
{
    public static readonly List<EspPlayer> All = new List<EspPlayer>();

    private readonly NetworkPlayer _networkPlayer;
    private float _displayHp = 1f;

    public EspPlayer(NetworkPlayer np) => _networkPlayer = np;

    public bool HasRig =>
        _networkPlayer != null && _networkPlayer.HasRig && _networkPlayer.RigRefs != null && _networkPlayer.RigRefs.IsValid;

    public RigManager RigManager => HasRig ? _networkPlayer.RigRefs.RigManager : null;
    public TriggerRefProxy Proxy => HasRig ? _networkPlayer.RigRefs.Proxy : null;

    public bool IsDead
    {
        get
        {
            try
            {
                if (!HasRig) return false;
                Player_Health health = _networkPlayer.RigRefs.Health;
                if (health == null) return false;
                return health.alive == false;
            }
            catch { return false; }
        }
    }

    public bool IsValid => HasRig && RigManager != null;

    public Vector3 HeadPos
    {
        get
        {
            if (Proxy != null && Proxy.targetHead != null) return Proxy.targetHead.position;
            if (HasRig && _networkPlayer.RigRefs.Head != null) return _networkPlayer.RigRefs.Head.position;
            return RigManager != null ? RigManager.transform.position + Vector3.up * 1.6f : Vector3.zero;
        }
    }

    public Vector3 ChestPos
    {
        get
        {
            if (Proxy != null && Proxy.chestTran != null) return Proxy.chestTran.position;
            return Vector3.Lerp(RigManager != null ? RigManager.transform.position : HeadPos - Vector3.up * 1.6f, HeadPos, 0.55f);
        }
    }

    public Vector3 FeetPos
    {
        get
        {
            if (Proxy != null && Proxy.feetTran != null) return Proxy.feetTran.position;
            return RigManager != null ? RigManager.transform.position : HeadPos - Vector3.up * 1.7f;
        }
    }

    public bool TryGetHp01(out float hp01)
    {
        hp01 = 1f;
        try
        {
            if (!HasRig) return false;
            Player_Health health = _networkPlayer.RigRefs.Health;
            if (health == null) return false;
            if (!health.alive) { hp01 = 0f; return true; }
            float max = health.max_Health;
            if (max < 0.01f) return false;
            hp01 = Mathf.Clamp01(health.curr_Health / max);
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

        Vector3 head = HeadPos;
        Vector3 chest = ChestPos;
        Vector3 feet = FeetPos;

        // Dead / ragdoll: feet often underground — build from head+chest only
        if (dead)
        {
            Vector3 axis = chest - head;
            if (axis.sqrMagnitude < 0.0001f) axis = Vector3.down;
            else axis.Normalize();
            feet = head + axis * 1.5f;
            // Keep box from sinking: lift so center stays near chest
            if (feet.y < chest.y - 1.8f)
                feet = new Vector3(chest.x, chest.y - 0.9f, chest.z);
            if (head.y < chest.y - 0.2f)
                head = chest + Vector3.up * 0.35f;
        }
        else
        {
            float h = head.y - feet.y;
            if (h < 0.5f || h > 2.6f)
                feet = new Vector3(head.x, head.y - 1.7f, head.z);
        }

        head += Vector3.up * 0.03f;
        Vector3 center = dead ? chest : (head + feet) * 0.5f;
        float height = Vector3.Distance(head, feet);
        float width = Mathf.Clamp(height * 0.26f, 0.28f, 0.42f);

        float hp = 1f;
        bool hasHp = TryGetHp01(out hp);
        if (dead) { hp = 0f; hasHp = true; }
        if (!hasHp) hp = 1f;
        _displayHp = hp; // no lerp dance

        frame = new EspFrame
        {
            Head = head,
            Feet = feet,
            Chest = chest,
            Center = center,
            Width = width,
            Hp01 = hp,
            DisplayHp = _displayHp,
            HasHp = hasHp && !dead,
            Dead = dead,
            IsPlayer = true,
            Color = dead ? EspMod.DeadColor : new Color(EspMod.ColorR, EspMod.ColorG, EspMod.ColorB, 1f)
        };
        if (!dead && EspMod.Rainbow)
        {
            float hue = (Time.unscaledTime * EspMod.RainbowSpeed) % 1f;
            frame.Color = Color.HSVToRGB(hue, 0.85f, 1f);
        }
        return true;
    }

    public static void Clear() => All.Clear();

    public static void SyncFromFusion()
    {
        if (!EspMod.FusionLoaded) return;
        try
        {
            var seen = new HashSet<NetworkPlayer>();
            foreach (NetworkPlayer np in NetworkPlayer.Players)
            {
                if (np == null || np.PlayerID == null || np.PlayerID.IsMe) continue;
                seen.Add(np);
                bool exists = false;
                for (int i = 0; i < All.Count; i++)
                    if (All[i] != null && All[i]._networkPlayer == np) { exists = true; break; }
                if (!exists) All.Add(new EspPlayer(np));
            }
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var e = All[i];
                if (e == null || e._networkPlayer == null || !seen.Contains(e._networkPlayer))
                    All.RemoveAt(i);
            }
        }
        catch (Exception ex) { MelonLogger.Warning($"ESP player sync: {ex.Message}"); }
    }
}
