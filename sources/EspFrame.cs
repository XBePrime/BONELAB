using UnityEngine;

namespace BePrime.Esp;

/// <summary>One frame of ESP target data for the drawer.</summary>
public struct EspFrame
{
    public Vector3 Head;
    public Vector3 Feet;
    public Vector3 Chest;
    public float Hp01;       // 0..1, -1 = unknown
    public float DisplayHp;  // smoothed
    public bool Dead;
    public bool IsPlayer;
    public Color Color;
}
