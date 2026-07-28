using System;
using System.Collections.Generic;
using BoneLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BePrime.Esp;

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
    private static readonly Vector3[] _q = new Vector3[4];

    private static readonly Color HpGreen = new Color(0.15f, 0.95f, 0.25f, 1f);
    private static readonly Color HpYellow = new Color(1f, 0.9f, 0.12f, 1f);
    private static readonly Color HpRed = new Color(1f, 0.12f, 0.1f, 1f);

    public static void Reset()
    {
        DestroyLrs(_boxLines); DestroyLrs(_hpTrack); DestroyLrs(_hpFill);
        if (_root != null) Object.Destroy(_root.gameObject);
        _root = null; _frame.Clear();
        if (_lineMat != null) { Object.Destroy(_lineMat); _lineMat = null; }
        if (_hpMat != null) { Object.Destroy(_hpMat); _hpMat = null; }
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
            if (!TryGetCamAxes(out Vector3 eye, out Vector3 right, out Vector3 up)) return;

            bool corners = EspMod.Style == 1;
            int per = corners ? 8 : 4;
            EnsureLinePool(_boxLines, _frame.Count * per, _lineMat);
            EnsureLinePool(_hpTrack, EspMod.ShowHp ? _frame.Count : 0, _hpMat);
            EnsureLinePool(_hpFill, EspMod.ShowHp ? _frame.Count : 0, _hpMat);

            float w = Mathf.Clamp(EspMod.LineWidth, 0.003f, 0.04f);
            float ct = EspMod.CornerSize;
            int li = 0;

            for (int t = 0; t < _frame.Count; t++)
            {
                EspFrame f = _frame[t];
                BuildRect(f, eye, right, up);

                Color col = f.Dead ? EspMod.DeadColor : f.Color;
                float dist = Vector3.Distance(eye, f.Center);
                float fade = dist < EspMod.MaxDistance * 0.55f ? 1f
                    : Mathf.Lerp(1f, 0.35f, (dist / EspMod.MaxDistance - 0.55f) / 0.45f);
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
            }

            for (; li < _boxLines.Count; li++)
                if (_boxLines[li] != null) _boxLines[li].enabled = false;
            for (int t = _frame.Count; t < _hpTrack.Count; t++)
            {
                if (_hpTrack[t] != null) _hpTrack[t].enabled = false;
                if (t < _hpFill.Count && _hpFill[t] != null) _hpFill[t].enabled = false;
            }
        }
        catch (Exception ex) { MelonLogger.Warning($"ESP draw: {ex.Message}"); }
    }

    private static void Collect()
    {
        _frame.Clear();
        if (!EspMod.Enabled) return;
        Vector3 eye = Vector3.zero;
        try { if (Player.Head != null) eye = Player.Head.position; } catch { }
        float maxSq = EspMod.MaxDistance * EspMod.MaxDistance;

        if (EspMod.TargetNpcs)
        {
            for (int i = 0; i < EspNpc.All.Count; i++)
            {
                var n = EspNpc.All[i];
                if (n == null || !n.TryBuildFrame(out EspFrame f)) continue;
                if (eye.sqrMagnitude > 0.01f && (f.Center - eye).sqrMagnitude > maxSq) continue;
                _frame.Add(f);
            }
        }
        if (EspMod.TargetPlayers && EspMod.FusionLoaded)
        {
            for (int i = 0; i < EspPlayer.All.Count; i++)
            {
                var p = EspPlayer.All[i];
                if (p == null || !p.TryBuildFrame(out EspFrame f)) continue;
                if (eye.sqrMagnitude > 0.01f && (f.Center - eye).sqrMagnitude > maxSq) continue;
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
                var h = Player.Head;
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
        catch { }
        var cam = Camera.main;
        if (cam == null) return false;
        eye = cam.transform.position;
        Vector3 f = cam.transform.forward; f.y = 0f;
        if (f.sqrMagnitude < 0.001f) f = cam.transform.forward;
        f.Normalize();
        right = Vector3.Cross(Vector3.up, f).normalized;
        up = Vector3.up;
        return true;
    }

    private static void BuildRect(EspFrame f, Vector3 eye, Vector3 right, Vector3 up)
    {
        float halfW = f.Width * 0.5f;
        Vector3 center = f.Center;
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

        Vector3 toCam = eye - center; toCam.y = 0f;
        if (toCam.sqrMagnitude > 0.001f) center += toCam.normalized * 0.03f;

        _q[0] = center + right * minX + up * minY;
        _q[1] = center + right * maxX + up * minY;
        _q[2] = center + right * maxX + up * maxY;
        _q[3] = center + right * minX + up * maxY;
    }

    private static void DrawHp(int idx, EspFrame f, Vector3 right, float fade)
    {
        float hp = Mathf.Clamp01(f.DisplayHp);
        Vector3 bot = _q[0] - right * 0.075f;
        Vector3 top = _q[3] - right * 0.075f;

        SetSeg(_hpTrack[idx], bot, top, new Color(0.05f, 0.05f, 0.07f, 0.9f * fade), 0.032f);
        if (hp <= 0.001f) { _hpFill[idx].enabled = false; return; }

        // Strict bands only
        Color fill = hp > 0.66f ? HpGreen : (hp > 0.33f ? HpYellow : HpRed);
        fill.a = fade;
        SetSeg(_hpFill[idx], bot, Vector3.Lerp(bot, top, hp), fill, 0.022f);
    }

    private static void SetSeg(LineRenderer lr, Vector3 a, Vector3 b, Color col, float w)
    {
        if (lr == null) return;
        lr.positionCount = 2;
        lr.SetPosition(0, a); lr.SetPosition(1, b);
        lr.startColor = col; lr.endColor = col;
        // Force solid vertex color (Quest shaders sometimes ignore start/end alone)
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(col, 0f), new GradientColorKey(col, 1f) },
            new[] { new GradientAlphaKey(col.a, 0f), new GradientAlphaKey(col.a, 1f) });
        lr.colorGradient = g;
        lr.startWidth = w; lr.endWidth = w;
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
            lr.numCapVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.enabled = false;
            pool.Add(lr);
        }
    }

    private static void HideAll()
    {
        for (int i = 0; i < _boxLines.Count; i++) if (_boxLines[i] != null) _boxLines[i].enabled = false;
        for (int i = 0; i < _hpTrack.Count; i++) if (_hpTrack[i] != null) _hpTrack[i].enabled = false;
        for (int i = 0; i < _hpFill.Count; i++) if (_hpFill[i] != null) _hpFill[i].enabled = false;
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
            _lineMat = MakeMat(sh); _hpMat = MakeMat(sh); ApplyZTest();
            return true;
        }
        catch (Exception ex) { MelonLogger.Error($"ESP material: {ex.Message}"); return false; }
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
