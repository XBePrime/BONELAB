using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.AI;
using LabFusion.Entities;
using LabFusion.Player;
using MelonLoader;
using UnityEngine;

namespace BePrime.Aimbot;

/// <summary>
/// Tracks LabFusion networked players as aim targets (excludes local player).
/// Isolated so it is only touched when LabFusion is loaded.
/// </summary>
public sealed class PlayerTarget : IAimTarget
{
    public static readonly List<PlayerTarget> All = new List<PlayerTarget>();

    public static float TimeBetweenSnapshots = 0.05f;
    public static byte SnapshotCount = 10;

    private readonly NetworkPlayer _networkPlayer;
    private Vector3[] _snapshots;
    private float _lastSnapshot;
    private Vector3 _previousVelocity;
    private Vector3 _currentAcceleration;

    public PlayerTarget(NetworkPlayer networkPlayer)
    {
        _networkPlayer = networkPlayer;
        _snapshots = new Vector3[SnapshotCount];
        for (int i = 0; i < SnapshotCount; i++)
            _snapshots[i] = Vector3.zero;
    }

    public NetworkPlayer NetworkPlayer => _networkPlayer;

    public int Uuid
    {
        get
        {
            if (!HasRig)
                return 0;
            return RigManager.transform.root.GetInstanceID();
        }
    }

    public RigManager RigManager =>
        _networkPlayer != null && _networkPlayer.HasRig ? _networkPlayer.RigRefs.RigManager : null;

    public TriggerRefProxy Proxy =>
        _networkPlayer != null && _networkPlayer.HasRig ? _networkPlayer.RigRefs.Proxy : null;

    public bool HasRig => _networkPlayer != null && _networkPlayer.HasRig && _networkPlayer.RigRefs != null && _networkPlayer.RigRefs.IsValid;

    public bool IsLocal
    {
        get
        {
            try
            {
                return _networkPlayer?.PlayerID != null && _networkPlayer.PlayerID.IsMe;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsDead
    {
        get
        {
            if (!HasRig)
                return true;

            try
            {
                Player_Health health = _networkPlayer.RigRefs.Health;
                if (health != null && !health.alive)
                    return true;
            }
            catch
            {
                // ignore
            }

            return false;
        }
    }

    public bool IsValid => !IsLocal && HasRig && !IsDead && Root != null;

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

    public Vector3 ChestPosition
    {
        get
        {
            if (Proxy != null && Proxy.chestTran != null)
                return Proxy.chestTran.position;
            return HeadPosition - Vector3.up * 0.35f;
        }
    }

    public Rigidbody HeadBody => Proxy != null ? Proxy.targetHead : null;

    public IEnumerable<Rigidbody> Bodies
    {
        get
        {
            if (!HasRig || RigManager.physicsRig == null)
                yield break;

            var bodies = RigManager.physicsRig.GetComponentsInChildren<Rigidbody>();
            if (bodies == null)
                yield break;

            foreach (var rb in bodies)
            {
                if (rb != null)
                    yield return rb;
            }
        }
    }

    public static void ApplySmoothing(AimbotMod.MovementCompensationSmoothing smoothing)
    {
        switch (smoothing)
        {
            case AimbotMod.MovementCompensationSmoothing.Adaptive:
                TimeBetweenSnapshots = 0.05f;
                SnapshotCount = 10;
                break;
            case AimbotMod.MovementCompensationSmoothing.Low:
                TimeBetweenSnapshots = 0.05f;
                SnapshotCount = 5;
                break;
            case AimbotMod.MovementCompensationSmoothing.Medium:
                TimeBetweenSnapshots = 0.05f;
                SnapshotCount = 10;
                break;
            case AimbotMod.MovementCompensationSmoothing.High:
                TimeBetweenSnapshots = 0.0625f;
                SnapshotCount = 16;
                break;
            case AimbotMod.MovementCompensationSmoothing.VeryHigh:
                TimeBetweenSnapshots = 0.075f;
                SnapshotCount = 20;
                break;
        }

        foreach (var player in All)
        {
            if (player == null)
                continue;
            player._snapshots = new Vector3[SnapshotCount];
            for (int i = 0; i < SnapshotCount; i++)
                player._snapshots[i] = Vector3.zero;
        }
    }

    public void FixedTick()
    {
        if (!IsValid)
            return;

        Vector3 velocity = SampleVelocity();

        if (AimbotMod.Smoothing == AimbotMod.MovementCompensationSmoothing.None)
        {
            _currentAcceleration = (velocity - _previousVelocity) / Time.fixedDeltaTime;
            _previousVelocity = velocity;
            return;
        }

        if (Time.fixedTime < _lastSnapshot + TimeBetweenSnapshots)
            return;

        _lastSnapshot = Time.fixedTime;
        for (int i = SnapshotCount - 1; i > 0; i--)
            _snapshots[i] = _snapshots[i - 1];
        _snapshots[0] = velocity;
    }

    private Vector3 SampleVelocity()
    {
        if (HeadBody != null)
            return HeadBody.velocity;

        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var rb in Bodies)
        {
            sum += rb.velocity;
            count++;
        }

        return count > 0 ? sum / count : Vector3.zero;
    }

    public Vector3 GetAverageVelocity()
    {
        if (AimbotMod.Smoothing == AimbotMod.MovementCompensationSmoothing.None)
            return SampleVelocity();

        Vector3 sum = Vector3.zero;
        int skipped = 0;
        for (byte i = 0; i < SnapshotCount; i++)
        {
            if (_snapshots[i] == Vector3.zero ||
                (AimbotMod.Smoothing == AimbotMod.MovementCompensationSmoothing.Adaptive &&
                 Vector3.Angle(_snapshots[0], _snapshots[i]) > 30f))
            {
                skipped++;
            }
            else
            {
                sum += _snapshots[i];
            }
        }

        if (skipped == SnapshotCount)
            return Vector3.zero;
        return sum / (SnapshotCount - skipped);
    }

    public Vector3 GetAverageAcceleration()
    {
        if (AimbotMod.Smoothing == AimbotMod.MovementCompensationSmoothing.None)
            return _currentAcceleration;

        Vector3 sum = Vector3.zero;
        for (byte i = 0; i < SnapshotCount - 1; i++)
            sum += (_snapshots[i] - _snapshots[i + 1]) / TimeBetweenSnapshots / (SnapshotCount - 1);
        return sum;
    }

    public static bool TryGetById(int id, out PlayerTarget target)
    {
        foreach (var candidate in All)
        {
            if (candidate != null && candidate.IsValid && candidate.Uuid == id)
            {
                target = candidate;
                return true;
            }
        }

        target = null;
        return false;
    }

    public static bool TryGetByRig(RigManager rig, out PlayerTarget target)
    {
        target = null;
        if (rig == null)
            return false;

        foreach (var candidate in All)
        {
            if (candidate != null && candidate.IsValid && candidate.RigManager == rig)
            {
                target = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sync tracked players with LabFusion's NetworkPlayer set.
    /// </summary>
    public static void SyncFromFusion()
    {
        if (!AimbotMod.FusionLoaded)
            return;

        var seen = new HashSet<NetworkPlayer>();
        foreach (var np in NetworkPlayer.Players)
        {
            if (np == null || np.PlayerID == null || np.PlayerID.IsMe)
                continue;

            seen.Add(np);

            bool exists = false;
            foreach (var existing in All)
            {
                if (existing != null && existing._networkPlayer == np)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
                All.Add(new PlayerTarget(np));
        }

        for (int i = All.Count - 1; i >= 0; i--)
        {
            var entry = All[i];
            if (entry == null || entry._networkPlayer == null || !seen.Contains(entry._networkPlayer))
                All.RemoveAt(i);
        }
    }

    public static void FixedTickAll()
    {
        if (!AimbotMod.TargetPlayers || !AimbotMod.FusionLoaded)
            return;

        try
        {
            SyncFromFusion();
            // Copy refs — menu/other threads must not Clear mid-iteration
            for (int i = 0; i < All.Count; i++)
            {
                PlayerTarget player = All[i];
                player?.FixedTick();
            }
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Warning($"AIMBOT PlayerTarget tick: {ex.Message}");
        }
    }

    public static void Clear()
    {
        All.Clear();
    }
}
