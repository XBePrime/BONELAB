using System;
using System.Collections;
using System.Linq;
using BoneLib;
using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;

namespace BePrime.Aimbot;

[RegisterTypeInIl2Cpp(false)]
public class GunAim : MonoBehaviour
{
    private Gun _gun;
    private int _uuid;
    private Quaternion _defaultRotation;
    private float _lastFired;
    private GunTrigger[] _triggers;
    private IAimTarget _cachedTarget;

    public GunAim(IntPtr ptr) : base(ptr) { }

    private bool TriggerGrabbed
    {
        get
        {
            Hand hand = _gun.triggerGrip.GetHand();
            if (hand != null && hand.manager == Player.RigManager)
                return true;

            if (_triggers == null)
                return false;

            return _triggers.Any(trigger =>
            {
                Hand h = trigger.triggerGrip.GetHand();
                return h != null && h.manager == Player.RigManager;
            });
        }
    }

    public void Awake()
    {
        _gun = GetComponent<Gun>();
        _uuid = _gun.GetInstanceID();
        _defaultRotation = _gun.firePointTransform.localRotation;
        Hooking.OnPreFireGun += OnPreFire;
        Hooking.OnPostFireGun += OnPostFire;

        var found = transform.root.GetComponentsInChildren<GunTrigger>();
        if (found != null && found.Length > 0)
        {
            _triggers = new GunTrigger[found.Length];
            for (int i = 0; i < found.Length; i++)
                _triggers[i] = found[i];
        }
    }

    public void OnDestroy()
    {
        Hooking.OnPreFireGun -= OnPreFire;
        Hooking.OnPostFireGun -= OnPostFire;
    }

    public void FixedUpdate()
    {
        if (!TriggerGrabbed || (int)_gun.slideState != 1)
            return;

        int ammo = (_gun.HasMagazine() ? _gun.AmmoCount() : 0) + (_gun.chamberedCartridge != null ? 1 : 0);
        if (ammo == 0)
            return;

        _cachedTarget = null;

        if (!AimbotMod.TriggerBotEnabled)
            return;

        float minInterval = 1f / Mathf.Clamp(_gun.roundsPerSecond, 0.01f, 600f);
        if (_lastFired + minInterval >= Time.time)
            return;

        if (AimbotMod.AimBotEnabled)
            RunAimbot(noCompensation: true);

        RunTriggerbot();
    }

    private void RunTriggerbot()
    {
        if (!Physics.Raycast(_gun.firePointTransform.position, _gun.firePointTransform.forward, out RaycastHit hit))
            return;

        if (!TryResolveTarget(hit, out IAimTarget target) || target.IsDead)
            return;

        if (AimbotMod.HeadshotsOnly)
        {
            Rigidbody head = target.HeadBody;
            if (head == null || hit.collider.attachedRigidbody != head)
                return;
        }

        float muzzleVelocity = _gun.defaultCartridge.projectile.startVelocity;
        AimbotMod.MarkDevTool();

        float travel = Vector3.Distance(hit.point, _gun.firePointTransform.position) / (muzzleVelocity * 0.95f);
        MelonCoroutines.Start(BulletTravelGate(target, travel));

        if (!AimbotMod.AimBotEnabled)
        {
            Vector3 aimPoint = hit.point;
            if (AimbotMod.MovementCompensation)
                aimPoint += GetMovementCompensation(target, hit.point, muzzleVelocity);

            _gun.firePointTransform.forward = (aimPoint - _gun.firePointTransform.position).normalized;

            if (AimbotMod.BulletDrop)
                _gun.firePointTransform.Rotate(CalculateDropAngle(aimPoint, muzzleVelocity), Space.Self);
        }

        _gun.Fire();
        _gun.firePointTransform.localRotation = _defaultRotation;
    }

    private static bool TryResolveTarget(RaycastHit hit, out IAimTarget target)
    {
        target = null;
        int rootId = hit.collider.transform.root.GetInstanceID();

        if (AimbotMod.TargetNpcs && NpcTarget.TryGetById(rootId, out NpcTarget npc) && npc.IsValid)
        {
            target = npc;
            return true;
        }

        if (AimbotMod.TargetPlayers && AimbotMod.FusionLoaded)
        {
            if (PlayerTarget.TryGetById(rootId, out PlayerTarget player) && player.IsValid)
            {
                target = player;
                return true;
            }

            var rig = hit.collider.GetComponentInParent<RigManager>();
            if (rig != null && PlayerTarget.TryGetByRig(rig, out player) && player.IsValid)
            {
                target = player;
                return true;
            }
        }

        return false;
    }

    public Vector3 GetMovementCompensation(IAimTarget target, Vector3 pos, float velocity)
    {
        Vector3 avgVelocity = target.GetAverageVelocity();
        Vector3 accel = AimbotMod.Acceleration ? target.GetAverageAcceleration() : Vector3.zero;
        float eta = Vector3.Distance(pos, _gun.firePointTransform.position) / velocity;
        Vector3 lead = avgVelocity * eta + 0.5f * accel * eta * eta;
        eta = Vector3.Distance(pos + lead, _gun.firePointTransform.position) / velocity;
        return avgVelocity * eta + 0.5f * accel * eta * eta;
    }

    private void RunAimbot(bool noCompensation)
    {
        _gun.firePointTransform.localRotation = _defaultRotation;

        IAimTarget target = FindClosestTarget();
        if (target == null)
            return;

        float muzzleVelocity = _gun.defaultCartridge.projectile.startVelocity;
        bool forceHead = AimbotMod.Target == AimbotMod.TargetBone.Head ||
                         (AimbotMod.HeadshotsOnly && AimbotMod.TriggerBotEnabled);

        Vector3 aimPoint = forceHead ? target.HeadPosition : target.ChestPosition;

        if (AimbotMod.Target == AimbotMod.TargetBone.Closest)
        {
            float bestAngle = Vector3.Angle(_gun.firePointTransform.forward, aimPoint - _gun.firePointTransform.position);
            foreach (Rigidbody body in target.Bodies)
            {
                if (body == null || body.name.Contains("Shoulder_"))
                    continue;

                Vector3 toBody = body.position - _gun.firePointTransform.position;
                float angle = Vector3.Angle(_gun.firePointTransform.forward, toBody);
                if (angle >= bestAngle)
                    continue;

                if (!Physics.Raycast(_gun.firePointTransform.position, toBody, out RaycastHit los))
                    continue;

                if (los.collider.transform.root.GetInstanceID() != target.Uuid)
                    continue;

                aimPoint = body.position;
                bestAngle = angle;
            }
        }

        if (!noCompensation && AimbotMod.MovementCompensation)
            aimPoint += GetMovementCompensation(target, aimPoint, muzzleVelocity);

        AimbotMod.MarkDevTool();

        _gun.firePointTransform.forward = (aimPoint - _gun.firePointTransform.position).normalized;

        if (!noCompensation && AimbotMod.BulletDrop)
            _gun.firePointTransform.Rotate(CalculateDropAngle(aimPoint, muzzleVelocity), Space.Self);
    }

    private Vector3 CalculateDropAngle(Vector3 targetPos, float velocity)
    {
        float yawX = 0f;
        if (Physics.gravity.x != 0f)
        {
            float dist = Vector3.Distance(
                new Vector3(0f, _gun.firePointTransform.position.y, _gun.firePointTransform.position.z),
                new Vector3(0f, targetPos.y, targetPos.z));
            float delta = _gun.firePointTransform.position.x - targetPos.x;
            float baseAngle = Mathf.Atan2(dist, delta) * 180f / Mathf.PI;
            float arg = ((-Physics.gravity.x) * dist * dist / (velocity * velocity) - delta) /
                        Mathf.Sqrt(delta * delta + dist * dist);
            if (arg <= 1f)
                yawX = 90f - (Mathf.Acos(arg) * 180f / Mathf.PI + baseAngle) / 2f;
        }

        float pitch = 0f;
        if (Physics.gravity.y != 0f)
        {
            float dist = Vector3.Distance(
                new Vector3(_gun.firePointTransform.position.x, 0f, _gun.firePointTransform.position.z),
                new Vector3(targetPos.x, 0f, targetPos.z));
            float delta = _gun.firePointTransform.position.y - targetPos.y;
            float baseAngle = Mathf.Atan2(dist, delta) * 180f / Mathf.PI;
            float arg = ((-Physics.gravity.y) * dist * dist / (velocity * velocity) - delta) /
                        Mathf.Sqrt(delta * delta + dist * dist);
            if (arg <= 1f)
                pitch = 90f - (Mathf.Acos(arg) * 180f / Mathf.PI + baseAngle) / 2f;
        }

        float yawZ = 0f;
        if (Physics.gravity.z != 0f)
        {
            float dist = Vector3.Distance(
                new Vector3(_gun.firePointTransform.position.x, _gun.firePointTransform.position.y, 0f),
                new Vector3(targetPos.x, targetPos.y, 0f));
            float delta = _gun.firePointTransform.position.x - targetPos.x;
            float baseAngle = Mathf.Atan2(dist, delta) * 180f / Mathf.PI;
            float arg = (Physics.gravity.z * dist * dist / (velocity * velocity) - delta) /
                        Mathf.Sqrt(delta * delta + dist * dist);
            if (arg <= 1f)
                yawZ = 90f - (Mathf.Acos(arg) * 180f / Mathf.PI + baseAngle) / 2f;
        }

        yawX *= Mathf.Sign(_gun.firePointTransform.position.z - targetPos.z);
        yawZ *= Mathf.Sign(_gun.firePointTransform.position.x - targetPos.x);
        return new Vector3(-pitch, -yawX - yawZ, 0f);
    }

    private void OnPreFire(Gun gun)
    {
        if (!AimbotMod.AimBotEnabled)
            return;
        if (gun.GetInstanceID() != _uuid)
            return;
        if (gun.host.HandCount() < 1)
            return;
        if (gun.host._hands[0].manager != Player.RigManager)
            return;

        RunAimbot(noCompensation: false);
    }

    private void OnPostFire(Gun gun)
    {
        if (gun.GetInstanceID() != _uuid)
            return;
        if (gun.host.HandCount() < 1)
            return;
        if (gun.host._hands[0].manager != Player.RigManager)
            return;

        gun.firePointTransform.localRotation = _defaultRotation;
        _lastFired = Time.time;
    }

    private IAimTarget FindClosestTarget()
    {
        if (_cachedTarget != null && _cachedTarget.IsValid)
            return _cachedTarget;

        float bestAngle = AimbotMod.AimBotFov / 2f;
        IAimTarget best = null;
        Vector3 origin = _gun.firePointTransform.position;
        Vector3 forward = _gun.firePointTransform.forward;

        if (AimbotMod.TargetNpcs)
        {
            foreach (var npc in NpcTarget.All)
            {
                if (npc == null || !npc.IsValid)
                    continue;

                if (!IsBetterTarget(npc, npc.HeadPosition, origin, forward, ref bestAngle))
                    continue;

                best = npc;
            }
        }

        if (AimbotMod.TargetPlayers && AimbotMod.FusionLoaded)
        {
            foreach (var player in PlayerTarget.All)
            {
                if (player == null || !player.IsValid)
                    continue;

                if (!IsBetterTarget(player, player.HeadPosition, origin, forward, ref bestAngle))
                    continue;

                best = player;
            }
        }

        _cachedTarget = best;
        return best;
    }

    private bool IsBetterTarget(IAimTarget target, Vector3 aimPos, Vector3 origin, Vector3 forward, ref float bestAngle)
    {
        Vector3 toTarget = aimPos - origin;
        float angle = Vector3.Angle(forward, toTarget);
        if (angle >= bestAngle)
            return false;

        if (!Physics.Raycast(origin, toTarget, out RaycastHit hit))
            return false;

        if (hit.collider.transform.root.GetInstanceID() != target.Uuid)
        {
            // Players: also accept hits that resolve to the same RigManager.
            if (target is PlayerTarget player)
            {
                var rig = hit.collider.GetComponentInParent<RigManager>();
                if (rig == null || rig != player.RigManager)
                    return false;
            }
            else
            {
                return false;
            }
        }

        bestAngle = angle;
        return true;
    }

    private static IEnumerator BulletTravelGate(IAimTarget target, float time)
    {
        if (target is NpcTarget npc)
        {
            float until = Time.time + time * 1.1f;
            npc.BulletTravel = true;
            while (Time.time < until)
                yield return null;
            npc.BulletTravel = false;
        }
        else
        {
            yield break;
        }
    }
}
