using System;
using System.Collections.Generic;
using BoneLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BePrime.Esp;

/// <summary>2D boxes + solid HP bar + death skull. Tight bone-based frames.</summary>
public static class EspDraw
{
    private static Transform _root;
    private static Material _lineMat;
    private static Material _hpMat;
    private static bool _loggedMat;

    private static readonly List<EspFrame> _frame = new List<EspFrame>(64);
    private static readonly List<LineRenderer> _boxLines = new List<LineRenderer>(256);
    private static readonly List<LineRenderer> _hpTrack = new List<LineRenderer>(64);
    private static readonly List<LineRenderer> _hpFill = new List<LineRenderer>(64);
    private static readonly List<GameObject> _skulls = new List<GameObject>(64);
    private static readonly Vector3[] _q = new Vector3[4];

    public static void Reset()
    {
        DestroyLrs(_boxLines);
        DestroyLrs(_hpTrack);
        DestroyLrs(_hpFill);
        for (int i = 0; i < _skulls.Count; i++)
            if (_skulls[i] != null) Object.Destroy(_skulls[i]);
        _skulls.Clear();
        if (_root != null) Object.Destroy(_root.gameObject);
        _root = null;
        _frame.Clear();
        if (_lineMat != null) { Object.Destroy(_lineMat); _lineMat = null; }
        if (_hpMat != null) { Object.Destroy(_hpMat); _hpMat = null; }
        EspSkull.Dispose();
    }

    private static void DestroyLrs(List<LineRenderer> list)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null) Object.Destroy(list[i].gameObject);
        list.Clear();
    }

    public static void Tick()
    {
        try
        {
            Collect();
            if (_frame.Count == 0) { HideAll(); return; }

            EnsureRoot();
            if (_root == null || !EnsureMaterials()) return;
            ApplyZTest();
            EspSkull.ApplyZTest();

            if (!TryGetCamAxes(out Vector3 eye, out Vector3 right, out Vector3 up))
                return;

            bool corners = EspMod.Style == 1;
            int linesPerBox = corners ? 8 : 4;
            EnsureLinePool(_boxLines, _frame.Count * linesPerBox, _lineMat);
            EnsureLinePool(_hpTrack, EspMod.ShowHp ? _frame.Count : 0, _hpMat);
            EnsureLinePool(_hpFill, EspMod.ShowHp ? _frame.Count : 0, _hpMat);
            EnsureSkullPool(_frame.Count);

            float w = Mathf.Clamp(EspMod.LineWidth, 0.003f, 0.04f);
            float ct = EspMod.CornerSize;
            int li = 0;

            for (int t = 0; t < _frame.Count; t++)
            {
                EspFrame f = _frame[t];
                BuildRect(f, eye, right, up, out float boxH);

                Color col = f.Color;
                float dist = Vector3.Distance(eye, (f.Head + f.Feet) * 0.5f);
                float fade = dist < EspMod.MaxDistance * 0.55f
                    ? 1f
                    : Mathf.Lerp(1f, 0.3f, (dist / EspMod.MaxDistance - 0.55f) / 0.45f);
                col.a *= fade;

                if (corners)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        Vector3 a = _q[c];
                        SetSeg(_boxLines[li++], a, Vector3.Lerp(a, _q[(c + 1) % 4], ct), col, w);
                        SetSeg(_boxLines[li++], a, Vector3.Lerp(a, _q[(c + 3) % 4], ct), col, w);
                    }
                }
                else
                {
                    for (int e = 0; e < 4; e++)
                        SetSeg(_boxLines[li++], _q[e], _q[(e + 1) % 4], col, w);
                }

                if (EspMod.ShowHp && f.HasHp && !f.Dead)
                    DrawHp(t, f, right, fade);
                else
                {
                    if (t < _hpTrack.Count && _hpTrack[t] != null) _hpTrack[t].enabled = false;
                    if (t < _hpFill.Count && _hpFill[t] != null) _hpFill[t].enabled = false;
                }

                if (f.Dead && EspMod.ShowSkull)
                    PlaceSkull(t, f, eye, up, boxH, fade);
                else if (t < _skulls.Count && _skulls[t] != null)
                    _skulls[t].SetActive(false);
            }

            for (; li < _boxLines.Count; li++)
                if (_boxLines[li] != null) _boxLines[li].enabled = false;
            for (int t = _frame.Count; t < _hpTrack.Count; t++)
            {
                if (_hpTrack[t] != null) _hpTrack[t].enabled = false;
                if (t < _hpFill.Count && _hpFill[t] != null) _hpFill[t].enabled = false;
            }
            for (int t = _frame.Count; t < _skulls.Count; t++)
                if (_skulls[t] != null) _skulls[t].SetActive(false);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ESP draw: {ex.Message}");
        }
    }

    private static void Collect()
    {
        _frame.Clear();
        if (!EspMod.Enabled) return;

        Vector3 eye = Vector3.zero;
        try { if (Player.Head != null) eye = Player.Head.position; } catch { /* */ }
        float maxSq = EspMod.MaxDistance * EspMod.MaxDistance;

        if (EspMod.TargetNpcs)
        {
            for (int i = 0; i < EspNpc.All.Count; i++)
            {
                EspNpc n = EspNpc.All[i];
                if (n == null || !n.TryBuildFrame(out EspFrame f)) continue;
                Vector3 mid = (f.Head + f.Feet) * 0.5f;
                if (eye.sqrMagnitude > 0.01f && (mid - eye).sqrMagnitude > maxSq) continue;
                _frame.Add(f);
            }
        }

        if (EspMod.TargetPlayers && EspMod.FusionLoaded)
        {
            for (int i = 0; i < EspPlayer.All.Count; i++)
            {
                EspPlayer p = EspPlayer.All[i];
                if (p == null || !p.TryBuildFrame(out EspFrame f)) continue;
                Vector3 mid = (f.Head + f.Feet) * 0.5f;
                if (eye.sqrMagnitude > 0.01f && (mid - eye).sqrMagnitude > maxSq) continue;
                _frame.Add(f);
            }
        }
    }

    private static bool TryGetCamAxes(out Vector3 eye, out Vector3 right, out Vector3 up)
    {
        eye = Vector3.zero; right = Vector3.right; up = Vector3.up;
        try
        {
            if (Player.Head != null)
            {
                Transform h = Player.Head;
                eye = h.position;
                Vector3 fwd = h.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.001f) fwd = h.forward;
                fwd.Normalize();
                right = Vector3.Cross(Vector3.up, fwd).normalized;
                if (right.sqrMagnitude < 0.001f) right = h.right;
                up = Vector3.up;
                return true;
            }
        }
        catch { /* */ }
        Camera cam = Camera.main;
        if (cam == null) return false;
        eye = cam.transform.position;
        Vector3 f2 = cam.transform.forward; f2.y = 0f;
        if (f2.sqrMagnitude < 0.001f) f2 = cam.transform.forward;
        f2.Normalize();
        right = Vector3.Cross(Vector3.up, f2).normalized;
        up = Vector3.up;
        return true;
    }

    private static void BuildRect(EspFrame f, Vector3 eye, Vector3 right, Vector3 up, out float boxH)
    {
        float halfW = f.Width * 0.5f;
        Vector3 center = (f.Feet + f.Head) * 0.5f;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        void Exp(Vector3 p)
        {
            Vector3 d = p - center;
            float x = Vector3.Dot(d, right);
            float y = Vector3.Dot(d, up);
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }

        Exp(f.Feet - right * halfW); Exp(f.Feet + right * halfW);
        Exp(f.Chest - right * halfW); Exp(f.Chest + right * halfW);
        Exp(f.Head - right * halfW); Exp(f.Head + right * halfW);

        minX -= 0.02f; maxX += 0.02f;
        minY -= 0.02f; maxY += 0.02f;
        boxH = Mathf.Max(0.1f, maxY - minY);

        Vector3 toCam = eye - center; toCam.y = 0f;
        if (toCam.sqrMagnitude > 0.001f)
            center += toCam.normalized * 0.03f;

        _q[0] = center + right * minX + up * minY;
        _q[1] = center + right * maxX + up * minY;
        _q[2] = center + right * maxX + up * maxY;
        _q[3] = center + right * minX + up * maxY;
    }

    /// <summary>Solid HP: dark track + colored fill. No dancing glow.</summary>
    private static void DrawHp(int idx, EspFrame f, Vector3 right, float fade)
    {
        float hp = Mathf.Clamp01(f.DisplayHp);
        Vector3 bot = _q[0] - right * 0.07f;
        Vector3 top = _q[3] - right * 0.07f;

        float trackW = 0.028f;
        Color trackCol = new Color(0.08f, 0.08f, 0.1f, 0.9f * fade);
        SetSeg(_hpTrack[idx], bot, top, trackCol, trackW);

        if (hp <= 0.001f)
        {
            _hpFill[idx].enabled = false;
            return;
        }

        Vector3 fillTop = Vector3.Lerp(bot, top, hp);
        Color fill = HpColor(hp);
        fill.a = 0.95f * fade;
        SetSeg(_hpFill[idx], bot, fillTop, fill, trackW * 0.65f);
    }

    private static Color HpColor(float hp)
    {
        if (hp > 0.5f)
            return Color.Lerp(new Color(0.95f, 0.85f, 0.15f), new Color(0.2f, 0.95f, 0.35f), (hp - 0.5f) * 2f);
        return Color.Lerp(new Color(0.95f, 0.12f, 0.1f), new Color(0.95f, 0.85f, 0.15f), hp * 2f);
    }

    private static void PlaceSkull(int idx, EspFrame f, Vector3 eye, Vector3 up, float boxH, float fade)
    {
        GameObject go = _skulls[idx];
        if (go == null) return;
        go.SetActive(true);

        float size = Mathf.Clamp(boxH * 0.42f, 0.18f, 0.36f);
        Vector3 pos = f.Head + up * (size * 0.55f + 0.03f);

        go.transform.position = pos;
        Vector3 toCam = eye - pos;
        if (toCam.sqrMagnitude < 0.001f) toCam = Vector3.forward;
        go.transform.rotation = Quaternion.LookRotation(-toCam.normalized, up);
        go.transform.localScale = new Vector3(size, size, size);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null && mr.material != null)
            mr.material.color = new Color(1f, 1f, 1f, fade);
    }

    private static void SetSeg(LineRenderer lr, Vector3 a, Vector3 b, Color col, float w)
    {
        if (lr == null) return;
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.startColor = col;
        lr.endColor = col;
        lr.startWidth = w;
        lr.endWidth = w;
        lr.enabled = true;
    }

    private static void EnsureRoot()
    {
        if (_root != null) return;
        var go = new GameObject("BE_PRIME_ESP");
        Object.DontDestroyOnLoad(go);
        _root = go.transform;
    }

    private static void EnsureLinePool(List<LineRenderer> pool, int need, Material mat)
    {
        while (pool.Count < need)
        {
            var go = new GameObject("esp_lr_" + pool.Count);
            go.transform.SetParent(_root, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.allowOcclusionWhenDynamic = false;
            lr.numCapVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.enabled = false;
            pool.Add(lr);
        }
    }

    private static void EnsureSkullPool(int need)
    {
        EspSkull.Ensure();
        while (_skulls.Count < need)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "esp_skull_" + _skulls.Count;
            go.transform.SetParent(_root, false);
            Object.Destroy(go.GetComponent<Collider>());
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = EspSkull.Material;
            mr.material.mainTexture = EspSkull.Texture;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            _skulls.Add(go);
        }
    }

    private static void HideAll()
    {
        for (int i = 0; i < _boxLines.Count; i++)
            if (_boxLines[i] != null) _boxLines[i].enabled = false;
        for (int i = 0; i < _hpTrack.Count; i++)
            if (_hpTrack[i] != null) _hpTrack[i].enabled = false;
        for (int i = 0; i < _hpFill.Count; i++)
            if (_hpFill[i] != null) _hpFill[i].enabled = false;
        for (int i = 0; i < _skulls.Count; i++)
            if (_skulls[i] != null) _skulls[i].SetActive(false);
    }

    private static bool EnsureMaterials()
    {
        if (_lineMat != null && _hpMat != null) return true;
        try
        {
            Shader sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("GUI/Text Shader");
            if (sh == null)
            {
                if (!_loggedMat) { _loggedMat = true; MelonLogger.Error("ESP: no shader"); }
                return false;
            }
            _lineMat = MakeMat(sh);
            _hpMat = MakeMat(sh);
            ApplyZTest();
            return true;
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"ESP material: {ex.Message}");
            return false;
        }
    }

    private static Material MakeMat(Shader sh)
    {
        var m = new Material(sh);
        m.hideFlags = HideFlags.HideAndDontSave;
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_Cull", (int)CullMode.Off);
        m.SetInt("_ZWrite", 0);
        return m;
    }

    private static void ApplyZTest()
    {
        int z = EspMod.ThroughWalls ? (int)CompareFunction.Always : (int)CompareFunction.LessEqual;
        if (_lineMat != null) _lineMat.SetInt("_ZTest", z);
        if (_hpMat != null) _hpMat.SetInt("_ZTest", z);
    }
}
