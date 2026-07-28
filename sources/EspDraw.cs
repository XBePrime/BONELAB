using System;
using System.Collections.Generic;
using BoneLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BePrime.Esp;

/// <summary>
/// ESP boxes via LineRenderer pool. No Camera / GL / RenderPipeline hooks.
/// </summary>
public static class EspDraw
{
    private static Transform _root;
    private static Material _mat;
    private static readonly List<LineRenderer> _pool = new List<LineRenderer>(128);
    private static readonly List<(Bounds bounds, Color color)> _frame = new List<(Bounds, Color)>(64);
    private static readonly Vector3[] _c = new Vector3[8];
    private static bool _loggedMat;

    // Full box: 12 edges as pairs of corner indices
    private static readonly int[] EdgeA = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 2, 3 };
    private static readonly int[] EdgeB = { 1, 2, 3, 0, 5, 6, 7, 4, 4, 5, 6, 7 };

    // Corner style: each of 8 corners fans to 3 neighbors
    private static readonly int[] CornerSelf = { 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6, 7, 7, 7 };
    private static readonly int[] CornerTo   = { 1, 3, 4, 0, 2, 5, 1, 3, 6, 0, 2, 7, 5, 7, 0, 4, 6, 1, 5, 7, 2, 4, 6, 3 };

    public static void Reset()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i] != null)
                Object.Destroy(_pool[i].gameObject);
        }
        _pool.Clear();
        if (_root != null)
            Object.Destroy(_root.gameObject);
        _root = null;
        _frame.Clear();
    }

    public static void Tick()
    {
        try
        {
            Collect();
            if (_frame.Count == 0)
            {
                HideAll();
                return;
            }

            EnsureRoot();
            if (_root == null || !EnsureMaterial())
                return;

            ApplyZTest();

            bool corners = EspMod.Style == 1;
            int perBox = corners ? 24 : 12;
            int need = _frame.Count * perBox;
            EnsurePool(need);

            float w = Mathf.Clamp(EspMod.LineWidth, 0.002f, 0.05f);
            float ct = EspMod.CornerSize;
            int li = 0;

            for (int t = 0; t < _frame.Count; t++)
            {
                FillCorners(_frame[t].bounds);
                Color col = _frame[t].color;

                if (corners)
                {
                    for (int e = 0; e < 24; e++, li++)
                        SetSeg(_pool[li], _c[CornerSelf[e]], Vector3.Lerp(_c[CornerSelf[e]], _c[CornerTo[e]], ct), col, w);
                }
                else
                {
                    for (int e = 0; e < 12; e++, li++)
                        SetSeg(_pool[li], _c[EdgeA[e]], _c[EdgeB[e]], col, w);
                }
            }

            for (; li < _pool.Count; li++)
            {
                if (_pool[li] != null)
                    _pool[li].enabled = false;
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ESP draw: {ex.Message}");
        }
    }

    private static void SetSeg(LineRenderer lr, Vector3 a, Vector3 b, Color col, float w)
    {
        if (lr == null)
            return;
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.startColor = col;
        lr.endColor = col;
        lr.startWidth = w;
        lr.endWidth = w;
        lr.enabled = true;
    }

    private static void Collect()
    {
        _frame.Clear();
        if (!EspMod.Enabled)
            return;

        Vector3 eye = GetEye();
        float maxSq = EspMod.MaxDistance * EspMod.MaxDistance;
        Color baseColor = ResolveColor();

        if (EspMod.TargetNpcs)
        {
            foreach (EspNpc npc in EspNpc.All)
            {
                if (npc == null || !npc.TryGetBounds(out Bounds b))
                    continue;
                float dsq = (b.center - eye).sqrMagnitude;
                if (dsq > maxSq)
                    continue;
                _frame.Add((b, Fade(baseColor, dsq)));
            }
        }

        if (EspMod.TargetPlayers && EspMod.FusionLoaded)
        {
            for (int i = 0; i < EspPlayer.All.Count; i++)
            {
                EspPlayer p = EspPlayer.All[i];
                if (p == null || !p.TryGetBounds(out Bounds b))
                    continue;
                float dsq = (b.center - eye).sqrMagnitude;
                if (dsq > maxSq)
                    continue;
                _frame.Add((b, Fade(baseColor, dsq)));
            }
        }
    }

    private static Color Fade(Color c, float dsq)
    {
        float t = Mathf.Sqrt(dsq) / EspMod.MaxDistance;
        c.a = t < 0.55f ? 1f : Mathf.Lerp(1f, 0.2f, (t - 0.55f) / 0.45f);
        return c;
    }

    private static Color ResolveColor()
    {
        if (EspMod.Rainbow)
        {
            float h = (Time.unscaledTime * EspMod.RainbowSpeed) % 1f;
            return Color.HSVToRGB(h, 0.85f, 1f);
        }
        return new Color(EspMod.ColorR, EspMod.ColorG, EspMod.ColorB, 1f);
    }

    private static Vector3 GetEye()
    {
        try
        {
            if (Player.Head != null)
                return Player.Head.position;
        }
        catch { /* ignore */ }
        return Vector3.zero;
    }

    private static void FillCorners(Bounds b)
    {
        Vector3 e = b.extents;
        Vector3 c = b.center;
        _c[0] = c + new Vector3(-e.x, -e.y, -e.z);
        _c[1] = c + new Vector3(e.x, -e.y, -e.z);
        _c[2] = c + new Vector3(e.x, -e.y, e.z);
        _c[3] = c + new Vector3(-e.x, -e.y, e.z);
        _c[4] = c + new Vector3(-e.x, e.y, -e.z);
        _c[5] = c + new Vector3(e.x, e.y, -e.z);
        _c[6] = c + new Vector3(e.x, e.y, e.z);
        _c[7] = c + new Vector3(-e.x, e.y, e.z);
    }

    private static void EnsureRoot()
    {
        if (_root != null)
            return;
        var go = new GameObject("BE_PRIME_ESP");
        Object.DontDestroyOnLoad(go);
        _root = go.transform;
    }

    private static void EnsurePool(int need)
    {
        while (_pool.Count < need)
        {
            var go = new GameObject("esp_line_" + _pool.Count);
            go.transform.SetParent(_root, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = _mat;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.allowOcclusionWhenDynamic = false;
            lr.numCapVertices = 0;
            lr.numCornerVertices = 0;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.enabled = false;
            _pool.Add(lr);
        }
    }

    private static void HideAll()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i] != null)
                _pool[i].enabled = false;
        }
    }

    private static bool EnsureMaterial()
    {
        if (_mat != null)
            return true;
        try
        {
            Shader sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null)
                sh = Shader.Find("Sprites/Default");
            if (sh == null)
                sh = Shader.Find("GUI/Text Shader");
            if (sh == null)
            {
                if (!_loggedMat)
                {
                    _loggedMat = true;
                    MelonLogger.Error("ESP: no line shader");
                }
                return false;
            }

            _mat = new Material(sh);
            _mat.hideFlags = HideFlags.HideAndDontSave;
            _mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull", (int)CullMode.Off);
            _mat.SetInt("_ZWrite", 0);
            ApplyZTest();

            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i] != null)
                    _pool[i].sharedMaterial = _mat;
            }
            return true;
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"ESP material: {ex.Message}");
            return false;
        }
    }

    private static void ApplyZTest()
    {
        if (_mat == null)
            return;
        _mat.SetInt("_ZTest", EspMod.ThroughWalls
            ? (int)CompareFunction.Always
            : (int)CompareFunction.LessEqual);
    }
}
