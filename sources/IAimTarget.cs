using System.Collections.Generic;
using UnityEngine;

namespace BePrime.Aimbot;

/// <summary>
/// Shared aim-target surface for NPCs and Fusion players.
/// </summary>
public interface IAimTarget
{
    int Uuid { get; }
    bool IsValid { get; }
    bool IsDead { get; }
    Transform Root { get; }
    Vector3 HeadPosition { get; }
    Vector3 ChestPosition { get; }
    Rigidbody HeadBody { get; }
    IEnumerable<Rigidbody> Bodies { get; }
    Vector3 GetAverageVelocity();
    Vector3 GetAverageAcceleration();
}
