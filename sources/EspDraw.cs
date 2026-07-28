using System;
using System.Collections.Generic;
using BoneLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace BePrime.Esp;

/// <summary>
/// World-space GL box / corner-box renderer. URP + built-in safe.
/// </summary>
public static class EspDraw
{
    private static Material _mat;
    private static bool _hooked;
    private static bool _loggedShader;
    private static Action<Camera> _postRenderAction;
    private static Action<ScriptableRenderContext, Camera> _endCameraAction;

    // Scratch buffers — zero alloc in hot path
    private static readonly Vector3[] _corners = new Vector3[8];
    private static readonly List<(Bounds bounds, Color color)> _frame = new List<(Bounds, Color)>(64);

    public static void EnsureHooked()
    {
        if (_hooked)
            return;
        _hooked = true;

        try
        {
            _postRenderAction = OnPostRender;
            Camera.onPostRender += _postRenderAction;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ESP Camera.onPostRender hook: {ex.Message}");
        }

        try
        {
            _endCameraAction = OnEndCameraRendering;
            RenderPipelineManager.endCameraRendering += _endCameraAction;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ESP URP endCameraRendering hook: {ex.Message}");
        }
    }

    public static void Unhook()
    {
        if (!_hooked)
            return;
        _hooked = false;
        try
        {
            if (_postRenderAction != null)
                Camera.onPostRender -= _postRenderAction;
        }
        catch { /* ignore */ }
        try
        {
            if (_endCameraAction != null)
                RenderPipelineManager.endCameraRendering -= _endCameraAction;
        }
        catch { /* ignore */ }
    }

    private static void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        DrawForCamera(cam);
    }

    private static void OnPostRender(Camera cam)
    {
        DrawForCamera(cam);
    }

    public static void CollectFrame()
    {
        _frame.Clear();
        if (!EspMod.Enabled)
            return;

        Vector3 eye = GetEyePosition();
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
                _frame.Add((b, Fade(baseColor, dsq, maxSq)));
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
                // Slightly brighter for players
                Color pc = baseColor;
                pc.r = Mathf.Min(1f, pc.r * 1.05f + 0.05f);
                _frame.Add((b, Fade(pc, dsq, maxSq)));
            }
        }
    }

    private static Color Fade(Color c, float dsq, float maxSq)
    {
        // Soft distance fade past 55% of max range
        float t = Mathf.Sqrt(dsq) / EspMod.MaxDistance;
        float a = t < 0.55f ? 1f : Mathf.Lerp(1f, 0.15f, (t - 0.55f) / 0.45f);
        c.a = a;
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

    private static Vector3 GetEyePosition()
    {
        try
        {
            if (Player.Head != null)
                return Player.Head.position;
        }
        catch { /* ignore */ }

        Camera cam = Camera.main;
        return cam != null ? cam.transform.position : Vector3.zero;
    }

    private static bool IsPlayerCamera(Camera cam)
    {
        if (cam == null || !cam.enabled || !cam.isActiveAndEnabled)
            return false;

        // Skip UI / overlay / scene view style cams
        if (cam.targetTexture != null && cam.cameraType == CameraType.Game)
        {
            // Still allow eye cameras that render to textures in VR
        }

        try
        {
            if (Player.Head != null)
            {
                Transform head = Player.Head;
                if (cam.transform == head || cam.transform.IsChildOf(head) || head.IsChildOf(cam.transform))
                    return true;
            }
        }
        catch { /* ignore */ }

        // Fallback: main / stereo eye cameras
        if (cam == Camera.main)
            return true;
        if (cam.stereoTargetEye != StereoTargetEyeMask.None)
            return true;

        return false;
    }

    private static void DrawForCamera(Camera cam)
    {
        if (!EspMod.Enabled || _frame.Count == 0)
            return;
        if (!IsPlayerCamera(cam))
            return;

        if (!EnsureMaterial())
            return;

        ApplyZTest();

        GL.PushMatrix();
        GL.LoadProjectionMatrix(GL.GetGPUProjectionMatrix(cam.projectionMatrix, true));
        GL.modelview = cam.worldToCameraMatrix;

        _mat.SetPass(0);

        // Soft glow pass (slightly expanded, low alpha) then crisp pass
        DrawFramePass(expand: 1.025f, alphaMul: 0.22f);
        DrawFramePass(expand: 1f, alphaMul: 1f);

        GL.PopMatrix();
    }

    private static void DrawFramePass(float expand, float alphaMul)
    {
        int style = EspMod.Style;
        float corner = EspMod.CornerSize;

        GL.Begin(GL.LINES);
        for (int i = 0; i < _frame.Count; i++)
        {
            Bounds b = _frame[i].bounds;
            if (expand != 1f)
            {
                Vector3 c = b.center;
                Vector3 s = b.size * expand;
                b = new Bounds(c, s);
            }

            Color col = _frame[i].color;
            col.a *= alphaMul;
            FillCorners(b);
            GL.Color(col);

            if (style == 0)
                DrawFullBox();
            else
                DrawCornerBox(corner);
        }
        GL.End();
    }

    private static void FillCorners(Bounds b)
    {
        Vector3 e = b.extents;
        Vector3 c = b.center;
        // 0---1
        // |   |
        // 3---2   bottom
        // 4---5
        // |   |
        // 7---6   top
        _corners[0] = c + new Vector3(-e.x, -e.y, -e.z);
        _corners[1] = c + new Vector3(e.x, -e.y, -e.z);
        _corners[2] = c + new Vector3(e.x, -e.y, e.z);
        _corners[3] = c + new Vector3(-e.x, -e.y, e.z);
        _corners[4] = c + new Vector3(-e.x, e.y, -e.z);
        _corners[5] = c + new Vector3(e.x, e.y, -e.z);
        _corners[6] = c + new Vector3(e.x, e.y, e.z);
        _corners[7] = c + new Vector3(-e.x, e.y, e.z);
    }

    private static void DrawFullBox()
    {
        // Bottom
        Seg(0, 1); Seg(1, 2); Seg(2, 3); Seg(3, 0);
        // Top
        Seg(4, 5); Seg(5, 6); Seg(6, 7); Seg(7, 4);
        // Uprights
        Seg(0, 4); Seg(1, 5); Seg(2, 6); Seg(3, 7);
    }

    private static void DrawCornerBox(float t)
    {
        // Each corner: three short axes
        Corner(0, 1, 3, 4, t);
        Corner(1, 0, 2, 5, t);
        Corner(2, 1, 3, 6, t);
        Corner(3, 0, 2, 7, t);
        Corner(4, 5, 7, 0, t);
        Corner(5, 4, 6, 1, t);
        Corner(6, 5, 7, 2, t);
        Corner(7, 4, 6, 3, t);
    }

    private static void Corner(int self, int a, int b, int c, float t)
    {
        Vector3 p = _corners[self];
        LerpSeg(p, _corners[a], t);
        LerpSeg(p, _corners[b], t);
        LerpSeg(p, _corners[c], t);
    }

    private static void LerpSeg(Vector3 from, Vector3 to, float t)
    {
        GL.Vertex(from);
        GL.Vertex(Vector3.Lerp(from, to, t));
    }

    private static void Seg(int a, int b)
    {
        GL.Vertex(_corners[a]);
        GL.Vertex(_corners[b]);
    }

    private static bool EnsureMaterial()
    {
        if (_mat != null)
            return true;

        try
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                shader = Shader.Find("GUI/Text Shader");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader == null)
            {
                if (!_loggedShader)
                {
                    _loggedShader = true;
                    MelonLogger.Error("ESP: no suitable line shader found");
                }
                return false;
            }

            _mat = new Material(shader);
            _mat.hideFlags = HideFlags.HideAndDontSave;
            _mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull", (int)CullMode.Off);
            _mat.SetInt("_ZWrite", 0);
            ApplyZTest();
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
        // Always = through walls; LessEqual = depth tested
        _mat.SetInt("_ZTest", EspMod.ThroughWalls
            ? (int)CompareFunction.Always
            : (int)CompareFunction.LessEqual);
    }
}
