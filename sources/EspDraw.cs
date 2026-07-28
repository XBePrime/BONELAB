using System;
using System.Collections.Generic;
using BoneLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BePrime.Esp;

/// <summary>
/// 2D camera-facing boxes + HP bars + death skulls.
/// LineRenderer / Quad only — no Camera event hooks.
/// </summary>
public static class EspDraw
{
    private static Transform _root;
    private static Material _lineMat;
    private static Material _barBgMat;
    private static Material _barFillMat;
    private static bool _loggedMat;

    private static readonly List<EspFrame> _frame = new List<EspFrame>(64);
    private static readonly List<LineRenderer> _boxLines = new List<LineRenderer>(256);
    private static readonly List<LineRenderer> _hpBg = new List<LineRenderer>(64);
    private static readonly List<LineRenderer> _hpFill = new List<LineRenderer>(64);
    private static readonly List<GameObject> _skulls = new List<GameObject>(64);

    // 2D rect corners scratch
    private static readonly Vector3[] _q = new Vector3[4];

    public static void Reset()
    {
        DestroyList(_boxLines);
        DestroyList(_hpBg);
        DestroyList(_hpFill);
        for (int i = 0; i < _skulls.Count; i++)
        {
            if (_skulls[i] != null)
                Object.Destroy(_skulls[i]);
        }
        _skulls.Clear();
        if (_root != null)
            Object.Destroy(_root.gameObject);
        _root = null;
        _frame.Clear();
        if (_lineMat != null) { Object.Destroy(_lineMat); _lineMat = null; }
        if (_barBgMat != null) { Object.Destroy(_barBgMat); _barBgMat = null; }
        if (_barFillMat != null) { Object.Destroy(_barFillMat); _barFillMat = null; }
        EspSkull.Dispose();
    }

    private static void DestroyList(List<LineRenderer> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null)
                Object.Destroy(list[i].gameObject);
        }
        list.Clear();
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
            if (_root == null || !EnsureMaterials())
                return;

            ApplyZTest();
            EspSkull.ApplyZTest();

            if (!TryGetCamAxes(out Vector3 eye, out Vector3 right, out Vector3 up, out Vector3 fwd))
                return;

            bool corners = EspMod.Style == 1;
            int linesPerBox = corners ? 8 : 4;
            EnsureLinePool(_boxLines, _frame.Count * linesPerBox, _lineMat);
            EnsureLinePool(_hpBg, EspMod.ShowHp ? _frame.Count : 0, _barBgMat);
            EnsureLinePool(_hpFill, EspMod.ShowHp ? _frame.Count : 0, _barFillMat);
            EnsureSkullPool(_frame.Count);

            float w = Mathf.Clamp(EspMod.LineWidth, 0.002f, 0.05f);
            float ct = EspMod.CornerSize;
            int li = 0;

            for (int t = 0; t < _frame.Count; t++)
            {
                EspFrame f = _frame[t];
                BuildBillboardRect(f, eye, right, up, fwd, out float boxH);

                Color col = f.Color;
                // Soft distance fade
                float dist = Vector3.Distance(eye, (f.Head + f.Feet) * 0.5f);
                float fade = dist < EspMod.MaxDistance * 0.55f
                    ? 1f
                    : Mathf.Lerp(1f, 0.25f, (dist / EspMod.MaxDistance - 0.55f) / 0.45f);
                col.a *= fade;

                if (corners)
                {
                    // 4 corners × 2 stubs
                    for (int c = 0; c < 4; c++)
                    {
                        Vector3 a = _q[c];
                        Vector3 b = _q[(c + 1) % 4];
                        Vector3 d = _q[(c + 3) % 4];
                        SetSeg(_boxLines[li++], a, Vector3.Lerp(a, b, ct), col, w);
                        SetSeg(_boxLines[li++], a, Vector3.Lerp(a, d, ct), col, w);
                    }
                }
                else
                {
                    for (int e = 0; e < 4; e++)
                        SetSeg(_boxLines[li++], _q[e], _q[(e + 1) % 4], col, w);
                }

                // HP bar — left of box, animated
                if (EspMod.ShowHp && !f.Dead)
                    DrawHpBar(t, f, up, right, fade);
                else
                {
                    if (t < _hpBg.Count) _hpBg[t].enabled = false;
                    if (t < _hpFill.Count) _hpFill[t].enabled = false;
                }

                // Death skull
                if (f.Dead && EspMod.ShowSkull)
                    PlaceSkull(t, f, eye, right, up, boxH, fade);
                else if (t < _skulls.Count && _skulls[t] != null)
                    _skulls[t].SetActive(false);
            }

            for (; li < _boxLines.Count; li++)
                if (_boxLines[li] != null) _boxLines[li].enabled = false;

            for (int t = _frame.Count; t < _hpBg.Count; t++)
                if (_hpBg[t] != null) _hpBg[t].enabled = false;
            for (int t = _frame.Count; t < _hpFill.Count; t++)
                if (_hpFill[t] != null) _hpFill[t].enabled = false;
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
        if (!EspMod.Enabled)
            return;

        Vector3 eye = Vector3.zero;
        try
        {
            if (Player.Head != null)
                eye = Player.Head.position;
        }
        catch { /* ignore */ }

        float maxSq = EspMod.MaxDistance * EspMod.MaxDistance;

        if (EspMod.TargetNpcs)
        {
            for (int i = 0; i < EspNpc.All.Count; i++)
            {
                EspNpc npc = EspNpc.All[i];
                if (npc == null || !npc.TryBuildFrame(out EspFrame f))
                    continue;
                Vector3 mid = (f.Head + f.Feet) * 0.5f;
                if ((mid - eye).sqrMagnitude > maxSq)
                    continue;
                _frame.Add(f);
            }
        }

        if (EspMod.TargetPlayers && EspMod.FusionLoaded)
        {
            for (int i = 0; i < EspPlayer.All.Count; i++)
            {
                EspPlayer p = EspPlayer.All[i];
                if (p == null || !p.TryBuildFrame(out EspFrame f))
                    continue;
                Vector3 mid = (f.Head + f.Feet) * 0.5f;
                if ((mid - eye).sqrMagnitude > maxSq)
                    continue;
                _frame.Add(f);
            }
        }
    }

    private static bool TryGetCamAxes(out Vector3 eye, out Vector3 right, out Vector3 up, out Vector3 fwd)
    {
        eye = Vector3.zero;
        right = Vector3.right;
        up = Vector3.up;
        fwd = Vector3.forward;
        try
        {
            if (Player.Head != null)
            {
                Transform h = Player.Head;
                eye = h.position;
                fwd = h.forward;
                right = h.right;
                up = h.up;
                // Flatten roll slightly for stable 2D boxes
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.001f)
                    fwd = h.forward;
                fwd.Normalize();
                right = Vector3.Cross(Vector3.up, fwd).normalized;
                if (right.sqrMagnitude < 0.001f)
                    right = h.right;
                up = Vector3.up;
                return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }

    /// <summary>
    /// Build a camera-facing 2D rectangle that covers head/chest/feet (full body for NPCs).
    /// </summary>
    private static void BuildBillboardRect(EspFrame f, Vector3 eye, Vector3 right, Vector3 up, Vector3 fwd, out float boxH)
    {
        // Sample body points — NPCs use full set; players already pass heart→head
        Vector3 p0 = f.Feet;
        Vector3 p1 = f.Chest;
        Vector3 p2 = f.Head;
        // Lateral padding from estimated shoulder width
        float bodyLen = Vector3.Distance(f.Feet, f.Head);
        float halfW = Mathf.Clamp(bodyLen * (f.IsPlayer ? 0.22f : 0.18f), 0.18f, 0.45f);

        Vector3 c0 = p0 - right * halfW;
        Vector3 c1 = p0 + right * halfW;
        Vector3 c2 = p1 - right * halfW;
        Vector3 c3 = p1 + right * halfW;
        Vector3 c4 = p2 - right * halfW;
        Vector3 c5 = p2 + right * halfW;

        Vector3 center = (p0 + p2) * 0.5f;
        // Project onto camera plane (right/up)
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        Expand(c0); Expand(c1); Expand(c2); Expand(c3); Expand(c4); Expand(c5);
        // Also raw points
        Expand(p0); Expand(p1); Expand(p2);

        void Expand(Vector3 p)
        {
            Vector3 d = p - center;
            float x = Vector3.Dot(d, right);
            float y = Vector3.Dot(d, up);
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        // Padding
        float padX = (maxX - minX) * 0.06f + 0.02f;
        float padY = (maxY - minY) * 0.04f + 0.02f;
        minX -= padX; maxX += padX;
        minY -= padY; maxY += padY;

        boxH = Mathf.Max(0.05f, maxY - minY);

        // Push slightly toward camera so lines sit in front of body
        Vector3 flat = center - Vector3.Dot(center - eye, Vector3.up) * Vector3.up;
        Vector3 toCam = (eye - flat);
        toCam.y = 0f;
        if (toCam.sqrMagnitude > 0.001f)
            center += toCam.normalized * 0.05f;

        _q[0] = center + right * minX + up * minY; // bottom-left
        _q[1] = center + right * maxX + up * minY; // bottom-right
        _q[2] = center + right * maxX + up * maxY; // top-right
        _q[3] = center + right * minX + up * maxY; // top-left
    }

    private static void DrawHpBar(int idx, EspFrame f, Vector3 up, Vector3 right, float fade)
    {
        // Bar to the left of the box
        Vector3 bl = _q[0];
        Vector3 tl = _q[3];
        Vector3 offset = -right * (0.06f + EspMod.LineWidth * 2f);
        Vector3 bot = bl + offset;
        Vector3 top = tl + offset;

        // Background full height
        Color bg = new Color(0.05f, 0.05f, 0.08f, 0.85f * fade);
        float bw = Mathf.Clamp(EspMod.LineWidth * 2.2f, 0.01f, 0.035f);
        SetSeg(_hpBg[idx], bot, top, bg, bw);

        // Animated fill from bottom
        float hp = Mathf.Clamp01(f.DisplayHp);
        // Pulse when low
        if (hp < 0.3f && hp > 0.001f)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 8f);
            hp *= pulse;
        }

        Vector3 fillTop = Vector3.Lerp(bot, top, hp);
        Color fill = HpColor(hp);
        fill.a *= fade;
        // Soft glow width
        SetSeg(_hpFill[idx], bot, fillTop, fill, bw * 0.72f);

        // Hide fill if empty
        if (hp <= 0.001f)
            _hpFill[idx].enabled = false;
    }

    private static Color HpColor(float hp)
    {
        // Green → yellow → red
        if (hp > 0.5f)
            return Color.Lerp(new Color(1f, 0.85f, 0.15f), new Color(0.2f, 1f, 0.35f), (hp - 0.5f) * 2f);
        return Color.Lerp(new Color(1f, 0.15f, 0.12f), new Color(1f, 0.85f, 0.15f), hp * 2f);
    }

    private static void PlaceSkull(int idx, EspFrame f, Vector3 eye, Vector3 right, Vector3 up, float boxH, float fade)
    {
        GameObject go = _skulls[idx];
        if (go == null)
            return;
        go.SetActive(true);

        Vector3 topMid = (_q[2] + _q[3]) * 0.5f;
        float size = Mathf.Clamp(boxH * 0.35f, 0.12f, 0.28f);
        // Float above box with gentle bob
        float bob = Mathf.Sin(Time.unscaledTime * 2.4f + idx) * 0.015f;
        Vector3 pos = topMid + up * (size * 0.65f + 0.04f + bob);

        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(go.transform.position - eye, up);
        go.transform.localScale = new Vector3(size, size, size);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null && mr.material != null)
        {
            Color c = mr.material.color;
            // Pulse red aura
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 3.5f + idx);
            c.a = fade * pulse;
            mr.material.color = new Color(1f, 0.85f * pulse, 0.85f * pulse, c.a);
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

    private static void EnsureRoot()
    {
        if (_root != null)
            return;
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
            lr.numCornerVertices = 0;
            lr.textureMode = LineTextureMode.Stretch;
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
        for (int i = 0; i < _hpBg.Count; i++)
            if (_hpBg[i] != null) _hpBg[i].enabled = false;
        for (int i = 0; i < _hpFill.Count; i++)
            if (_hpFill[i] != null) _hpFill[i].enabled = false;
        for (int i = 0; i < _skulls.Count; i++)
            if (_skulls[i] != null) _skulls[i].SetActive(false);
    }

    private static bool EnsureMaterials()
    {
        if (_lineMat != null && _barBgMat != null && _barFillMat != null)
            return true;
        try
        {
            Shader sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("GUI/Text Shader");
            if (sh == null)
            {
                if (!_loggedMat)
                {
                    _loggedMat = true;
                    MelonLogger.Error("ESP: no line shader");
                }
                return false;
            }

            _lineMat = MakeMat(sh);
            _barBgMat = MakeMat(sh);
            _barFillMat = MakeMat(sh);
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
        if (_barBgMat != null) _barBgMat.SetInt("_ZTest", z);
        if (_barFillMat != null) _barFillMat.SetInt("_ZTest", z);
    }
}
