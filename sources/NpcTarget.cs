using System;
using System.Collections.Generic;
using Il2CppSLZ.Marrow.AI;
using MelonLoader;
using UnityEngine;

namespace BePrime.Aimbot;

[RegisterTypeInIl2Cpp(false)]
public class NpcTarget : MonoBehaviour, IAimTarget
{
    public static readonly HashSet<NpcTarget> All = new HashSet<NpcTarget>();

    public static float TimeBetweenSnapshots = 0.05f;
    public static byte SnapshotCount = 10;

    public TriggerRefProxy Proxy;
    public Vector3[] Snapshots;
    public float LastSnapshot;
    public int Uuid;
    public bool Dying;
    public bool BulletTravel;

    private Vector3 _previousVelocity;
    private Vector3 _currentAcceleration;

    public int UuidProp => Uuid;
    int IAimTarget.Uuid => Uuid;

    public AIBrain Brain => Proxy != null ? Proxy.aiManager : null;

    public bool IsDead
    {
        get
        {
            if (Proxy == null || Brain == null)
                return true;
            return Brain.isDead || Dying || BulletTravel;
        }
    }

    public bool IsValid => this != null && Proxy != null && Brain != null && !IsDead;

    public Transform Root => transform != null ? transform.root : null;

    public Vector3 HeadPosition => Proxy != null && Proxy.targetHead != null
        ? Proxy.targetHead.position
        : transform.position;

    public Vector3 ChestPosition => Proxy != null && Proxy.chestTran != null
        ? Proxy.chestTran.position
        : HeadPosition;

    public Rigidbody HeadBody => Proxy != null ? Proxy.targetHead : null;

    public IEnumerable<Rigidbody> Bodies
    {
        get
        {
            if (Brain == null || Brain.behaviour == null || Brain.behaviour.selfRbs == null)
                yield break;

            var enumerator = Brain.behaviour.selfRbs.GetEnumerator();
            while (enumerator.MoveNext())
                yield return enumerator.Current;
        }
    }

    public NpcTarget(IntPtr ptr) : base(ptr) { }

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
            default:
                break;
        }

        foreach (var npc in All)
        {
            if (npc == null)
                continue;
            npc.Snapshots = new Vector3[SnapshotCount];
            for (int i = 0; i < SnapshotCount; i++)
                npc.Snapshots[i] = Vector3.zero;
        }
    }

    public void Start()
    {
        All.Add(this);
        Snapshots = new Vector3[SnapshotCount];
        Vector3 seed = Proxy != null && Proxy.targetHead != null ? Proxy.targetHead.velocity : Vector3.zero;
        for (int i = 0; i < SnapshotCount; i++)
            Snapshots[i] = seed;
    }

    public void OnDestroy()
    {
        All.Remove(this);
    }

    public void FixedUpdate()
    {
        if (Brain == null || Brain.isDead)
            return;

        if (AimbotMod.Smoothing == AimbotMod.MovementCompensationSmoothing.None)
        {
            Vector3 velocity = AverageBodyVelocity();
            _currentAcceleration = (velocity - _previousVelocity) / Time.fixedDeltaTime;
            _previousVelocity = velocity;
            return;
        }

        if (Time.fixedTime < LastSnapshot + TimeBetweenSnapshots)
            return;

        LastSnapshot = Time.fixedTime;
        for (int i = SnapshotCount - 1; i > 0; i--)
            Snapshots[i] = Snapshots[i - 1];
        Snapshots[0] = AverageBodyVelocity();
    }

    private Vector3 AverageBodyVelocity()
    {
        if (Brain == null || Brain.behaviour == null || Brain.behaviour.selfRbs == null || Brain.behaviour.selfRbs.Count == 0)
            return Proxy != null && Proxy.targetHead != null ? Proxy.targetHead.velocity : Vector3.zero;

        Vector3 sum = Vector3.zero;
        var enumerator = Brain.behaviour.selfRbs.GetEnumerator();
        while (enumerator.MoveNext())
            sum += enumerator.Current.velocity;
        return sum / Brain.behaviour.selfRbs.Count;
    }

    public Vector3 GetAverageVelocity()
    {
        if (AimbotMod.Smoothing == AimbotMod.MovementCompensationSmoothing.None)
            return Proxy != null && Proxy.targetHead != null ? Proxy.targetHead.velocity : Vector3.zero;

        Vector3 sum = Vector3.zero;
        int skipped = 0;
        for (byte i = 0; i < SnapshotCount; i++)
        {
            if (Snapshots[i] == Vector3.zero ||
                (AimbotMod.Smoothing == AimbotMod.MovementCompensationSmoothing.Adaptive &&
                 Vector3.Angle(Snapshots[0], Snapshots[i]) > 30f))
            {
                skipped++;
            }
            else
            {
                sum += Snapshots[i];
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
            sum += (Snapshots[i] - Snapshots[i + 1]) / TimeBetweenSnapshots / (SnapshotCount - 1);
        return sum;
    }

    public static bool TryGetById(int id, out NpcTarget npc)
    {
        foreach (var candidate in All)
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

    public static void Bind(TriggerRefProxy proxy)
    {
        if (proxy == null)
            return;

        AIBrain brain = proxy.aiManager;
        if (brain == null)
            return;

        if (brain.transform.root.name.StartsWith("OmniWay"))
            return;

        var target = proxy.gameObject.GetComponent<NpcTarget>();
        if (target == null)
            target = proxy.gameObject.AddComponent<NpcTarget>();

        target.Proxy = proxy;
        target.Dying = false;
        target.Uuid = proxy.transform.root.GetInstanceID();
        target.enabled = AimbotMod.AimBotEnabled || AimbotMod.TriggerBotEnabled;
        All.Add(target);
    }
}
