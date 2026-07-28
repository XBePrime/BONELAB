using UnityEngine;

namespace BePrime.Esp;

public struct EspFrame
{
    public Vector3 Head;
    public Vector3 Feet;
    public Vector3 Chest;
    public Vector3 Center;
    public float Width;
    public float Hp01;
    public float DisplayHp;
    public bool HasHp;
    public bool Dead;
    public bool IsPlayer;
    public Color Color;
}
