using System;
using System.Collections.Generic;
using BoneLib;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace BePrime.Esp;

/// <summary>
/// World-space GL box / corner-box renderer.
/// Hooks are deferred — Il2Cpp Camera statics are unsafe during Melon init.
/// </summary>
public static class EspDraw
{
    private static Material _mat;
    private static bool _hooked;
    private static bool _loggedShader;
    private static Camera.CameraCallback _postRenderCb;
    private static Il2CppSystem.Action<ScriptableRenderContext, Camera> _endCameraAction;

    // Scratch buffers — zero alloc in hot path
    private static readonly Vector3[] _corners = new Vector3[8];
    private static readonly List<(Bounds bounds, Color color)> _frame = new List<(Bounds, Color)>(64);

    /// <summary>
    /// Safe to call every frame; no-ops after first successful hook.
    /// Must NOT run in OnInitializeMelon (Quest Il2Cpp crash).
    /// </summary>
    public static void EnsureHooked()
    {
        if (_hooked)
            return;

        // Wait until Unity cameras exist — init-time static access crashes LemonLoader.
        if (Camera.main == null && Camera.allCamerasCount <= 0)
            return;

        bool any = false;

        try
        {
            // CameraCallback has implicit conversion from System.Action<Camera>
            _postRenderCb = (Action<Camera>)OnPostRender;
            Camera.CameraCallback current = Camera.onPostRender;
            Camera.onPostRender = current == null
                ? _postRenderCb
                : (Camera.CameraCallback)Il2CppSystem.Delegate.Combine(current, _postRenderCb);
            any = true;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ESP Camera.onPostRender hook: {ex.Message}");
        }

        try
        {
            _endCameraAction = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ScriptableRenderContext, Camera>>(
                (Action<ScriptableRenderContext, Camera>)OnEndCameraRendering);
            if (_endCameraAction != null)
            {
                RenderPipelineManager.add_endCameraRendering(_endCameraAction);
                any = true;
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ESP endCameraRendering hook: {ex.Message}");
        }

        if (any)
        {
            _hooked = true;
            MelonLogger.Msg("ESP draw hooks ready");
        }
    }

    public static void Unhook()
    {
        if (!_hooked)
            return;
        _hooked = false;

        try
        {
            if (_postRenderCb != null)
            {
                Camera.CameraCallback current = Camera.onPostRender;
                if (current != null)
                    Camera.onPostRender = (Camera.CameraCallback)Il2CppSystem.Delegate.Remove(current, _postRenderCb);
            }
        }
        catch { /* ignore */ }

        try
        {
            if (_endCameraAction != null)
                RenderPipelineManager.remove_endCameraRendering(_endCameraAction);
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
                Color pc = baseColor;
                pc.r = Mathf.Min(1f, pc.r * 1.05f + 0.05f);
                _frame.Add((b, Fade(pc, dsq, maxSq)));
            }
        }
    }

    private static Color Fade(Color c, float dsq, float maxSq)
    {
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
        Seg(0, 1); Seg(1, 2); Seg(2, 3); Seg(3, 0);
        Seg(4, 5); Seg(5, 6); Seg(6, 7); Seg(7, 4);
        Seg(0, 4); Seg(1, 5); Seg(2, 6); Seg(3, 7);
    }

    private static void DrawCornerBox(float t)
    {
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
        _mat.SetInt("_ZTest", EspMod.ThroughWalls
            ? (int)CompareFunction.Always
            : (int)CompareFunction.LessEqual);
    }
}
