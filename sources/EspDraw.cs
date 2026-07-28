using System;
using System.Collections.Generic;
using BoneLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BePrime.Esp;

/// <summary>
/// 2D camera-facing boxes + solid HP bars + death skulls.
/// Live positions every frame — follows dragged / ragdolled bodies.
/// </summary>
public static class EspDraw
{
    private static Transform _root;
    private static Material _lineMat;
    private static Material _hpMat;
    private static bool _loggedMat;

    private static readonly List<EspFrame> _frame = new List<EspFrame>(64);
    private static readonly List<LineRenderer> _boxLines = new List<LineRenderer>(256);
    private static readonly List<GameObject> _hpBg = new List<GameObject>(64);
    private static readonly List<GameObject> _hpFill = new List<GameObject>(64);
    private static readonly List<GameObject> _hpGlow = new List<GameObject>(64);
    private static readonly List<GameObject> _skulls = new List<GameObject>(64);

    private static readonly Vector3[] _q = new Vector3[4];

    public static void Reset()
    {
        DestroyLrs(_boxLines);
        DestroyGos(_hpBg);
        DestroyGos(_hpFill);
        DestroyGos(_hpGlow);
        DestroyGos(_skulls);
        if (_root != null)
            Object.Destroy(_root.gameObject);
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

    private static void DestroyGos(List<GameObject> list)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null) Object.Destroy(list[i]);
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

            if (!TryGetCamAxes(out Vector3 eye, out Vector3 right, out Vector3 up, out _))
                return;

            bool corners = EspMod.Style == 1;
            int linesPerBox = corners ? 8 : 4;
            EnsureLinePool(_boxLines, _frame.Count * linesPerBox, _lineMat);
            EnsureQuadPool(_hpBg, EspMod.ShowHp ? _frame.Count : 0, new Color(0.04f, 0.04f, 0.06f, 0.92f));
            EnsureQuadPool(_hpFill, EspMod.ShowHp ? _frame.Count : 0, Color.green);
            EnsureQuadPool(_hpGlow, EspMod.ShowHp ? _frame.Count : 0, new Color(1f, 1f, 1f, 0.25f));
            EnsureSkullPool(_frame.Count);

            float w = Mathf.Clamp(EspMod.LineWidth, 0.003f, 0.05f);
            float ct = EspMod.CornerSize;
            int li = 0;

            for (int t = 0; t < _frame.Count; t++)
            {
                EspFrame f = _frame[t];
                BuildBillboardRect(f, eye, right, up, out float boxH);

                Color col = f.Color;
                float dist = Vector3.Distance(eye, (f.Head + f.Feet) * 0.5f);
                float fade = dist < EspMod.MaxDistance * 0.55f
                    ? 1f
                    : Mathf.Lerp(1f, 0.25f, (dist / EspMod.MaxDistance - 0.55f) / 0.45f);
                col.a *= fade;

                if (corners)
                {
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

                if (EspMod.ShowHp && !f.Dead)
                    DrawHpBar(t, f, eye, right, up, fade);
                else
                    HideHp(t);

                if (f.Dead && EspMod.ShowSkull)
                    PlaceSkull(t, f, eye, up, boxH, fade);
                else if (t < _skulls.Count && _skulls[t] != null)
                    _skulls[t].SetActive(false);
            }

            for (; li < _boxLines.Count; li++)
                if (_boxLines[li] != null) _boxLines[li].enabled = false;

            for (int t = _frame.Count; t < _hpBg.Count; t++) HideHp(t);
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
                if (eye.sqrMagnitude > 0.01f && (mid - eye).sqrMagnitude > maxSq)
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
                if (eye.sqrMagnitude > 0.01f && (mid - eye).sqrMagnitude > maxSq)
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

        Camera cam = Camera.main;
        if (cam != null)
        {
            eye = cam.transform.position;
            fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = cam.transform.forward;
            fwd.Normalize();
            right = Vector3.Cross(Vector3.up, fwd).normalized;
            up = Vector3.up;
            return true;
        }
        return false;
    }

    private static void BuildBillboardRect(EspFrame f, Vector3 eye, Vector3 right, Vector3 up, out float boxH)
    {
        Vector3 p0 = f.Feet;
        Vector3 p1 = f.Chest;
        Vector3 p2 = f.Head;
        float bodyLen = Mathf.Max(0.2f, Vector3.Distance(f.Feet, f.Head));
        float halfW = Mathf.Clamp(bodyLen * 0.20f, 0.22f, 0.48f);

        Vector3 center = (p0 + p2) * 0.5f;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

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

        Expand(p0 - right * halfW); Expand(p0 + right * halfW);
        Expand(p1 - right * halfW); Expand(p1 + right * halfW);
        Expand(p2 - right * halfW); Expand(p2 + right * halfW);
        Expand(p0); Expand(p1); Expand(p2);

        float padX = (maxX - minX) * 0.08f + 0.03f;
        float padY = (maxY - minY) * 0.04f + 0.02f;
        minX -= padX; maxX += padX;
        minY -= padY; maxY += padY;
        boxH = Mathf.Max(0.05f, maxY - minY);

        Vector3 toCam = eye - center;
        toCam.y = 0f;
        if (toCam.sqrMagnitude > 0.001f)
            center += toCam.normalized * 0.04f;

        _q[0] = center + right * minX + up * minY;
        _q[1] = center + right * maxX + up * minY;
        _q[2] = center + right * maxX + up * maxY;
        _q[3] = center + right * minX + up * maxY;
    }

    private static void DrawHpBar(int idx, EspFrame f, Vector3 eye, Vector3 right, Vector3 up, float fade)
    {
        GameObject bg = _hpBg[idx];
        GameObject fill = _hpFill[idx];
        GameObject glow = _hpGlow[idx];
        if (bg == null || fill == null || glow == null)
            return;

        float hp = Mathf.Clamp01(f.DisplayHp);
        float pulse = 1f;
        if (hp < 0.28f && hp > 0.001f)
            pulse = 0.72f + 0.28f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 7f));

        float boxH = Vector3.Distance(_q[0], _q[3]);
        float barH = boxH;
        float barW = Mathf.Clamp(boxH * 0.07f, 0.035f, 0.07f);
        float gap = barW * 0.85f + 0.03f;

        Vector3 midL = (_q[0] + _q[3]) * 0.5f - right * gap;
        Vector3 bot = midL - up * (barH * 0.5f);
        Vector3 top = midL + up * (barH * 0.5f);

        // Face camera
        Quaternion rot = Quaternion.LookRotation(midL - eye, up);

        // Background track
        bg.SetActive(true);
        bg.transform.SetPositionAndRotation(midL, rot);
        bg.transform.localScale = new Vector3(barW, barH, 1f);
        SetQuadColor(bg, new Color(0.05f, 0.05f, 0.07f, 0.9f * fade));

        // Fill grows from bottom
        float fillH = Mathf.Max(0.001f, barH * hp * pulse);
        Vector3 fillCenter = bot + up * (fillH * 0.5f);
        fill.SetActive(hp > 0.001f);
        if (hp > 0.001f)
        {
            fill.transform.SetPositionAndRotation(fillCenter, rot);
            fill.transform.localScale = new Vector3(barW * 0.72f, fillH, 1f);
            Color fc = HpColor(hp);
            fc.a = 0.95f * fade;
            SetQuadColor(fill, fc);
        }

        // Soft glow behind fill (slightly wider)
        glow.SetActive(hp > 0.001f);
        if (hp > 0.001f)
        {
            glow.transform.SetPositionAndRotation(fillCenter - (midL - eye).normalized * 0.005f, rot);
            glow.transform.localScale = new Vector3(barW * 1.15f, fillH * 1.02f, 1f);
            Color gc = HpColor(hp);
            gc.a = (0.22f + 0.12f * Mathf.Sin(Time.unscaledTime * 3f)) * fade;
            SetQuadColor(glow, gc);
        }
    }

    private static void HideHp(int idx)
    {
        if (idx < _hpBg.Count && _hpBg[idx] != null) _hpBg[idx].SetActive(false);
        if (idx < _hpFill.Count && _hpFill[idx] != null) _hpFill[idx].SetActive(false);
        if (idx < _hpGlow.Count && _hpGlow[idx] != null) _hpGlow[idx].SetActive(false);
    }

    private static void SetQuadColor(GameObject go, Color c)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) return;
        // instance material so colors don't fight
        if (mr.material != null)
            mr.material.color = c;
    }

    private static Color HpColor(float hp)
    {
        if (hp > 0.55f)
            return Color.Lerp(new Color(0.95f, 0.9f, 0.2f), new Color(0.15f, 1f, 0.4f), (hp - 0.55f) / 0.45f);
        if (hp > 0.25f)
            return Color.Lerp(new Color(1f, 0.45f, 0.1f), new Color(0.95f, 0.9f, 0.2f), (hp - 0.25f) / 0.3f);
        return Color.Lerp(new Color(0.85f, 0.05f, 0.08f), new Color(1f, 0.45f, 0.1f), hp / 0.25f);
    }

    private static void PlaceSkull(int idx, EspFrame f, Vector3 eye, Vector3 up, float boxH, float fade)
    {
        GameObject go = _skulls[idx];
        if (go == null)
            return;
        go.SetActive(true);

        // Always on the real head point — not box groin
        float size = Mathf.Clamp(boxH * 0.28f, 0.11f, 0.26f);
        float bob = Mathf.Sin(Time.unscaledTime * 2.6f + idx) * 0.012f;
        Vector3 pos = f.Head + up * (size * 0.55f + 0.02f + bob);

        go.transform.position = pos;
        Vector3 toCam = eye - pos;
        if (toCam.sqrMagnitude < 0.001f) toCam = Vector3.forward;
        go.transform.rotation = Quaternion.LookRotation(-toCam.normalized, up);
        go.transform.localScale = new Vector3(size, size, size);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null && mr.material != null)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3.2f + idx);
            mr.material.color = new Color(1f, 0.9f, 0.9f, fade * pulse);
        }
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
            lr.numCornerVertices = 0;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.enabled = false;
            pool.Add(lr);
        }
    }

    private static void EnsureQuadPool(List<GameObject> pool, int need, Color baseColor)
    {
        EnsureMaterials();
        while (pool.Count < need)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "esp_hp_" + pool.Count;
            go.transform.SetParent(_root, false);
            Object.Destroy(go.GetComponent<Collider>());
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _hpMat;
            mr.material.color = baseColor; // instance
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            pool.Add(go);
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
        for (int i = 0; i < _hpBg.Count; i++) HideHp(i);
        for (int i = 0; i < _skulls.Count; i++)
            if (_skulls[i] != null) _skulls[i].SetActive(false);
    }

    private static bool EnsureMaterials()
    {
        if (_lineMat != null && _hpMat != null)
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
            Shader hpSh = Shader.Find("Sprites/Default");
            if (hpSh == null) hpSh = sh;
            _hpMat = MakeMat(hpSh);
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
