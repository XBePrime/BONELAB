using UnityEngine;
using Object = UnityEngine.Object;

namespace BePrime.Esp;

/// <summary>Realistic minimal skull mark — no cartoon style.</summary>
public static class EspSkull
{
    private static Texture2D _tex;
    private static Material _mat;

    public static Material Material
    {
        get { Ensure(); return _mat; }
    }

    public static Texture2D Texture
    {
        get { Ensure(); return _tex; }
    }

    public static void Ensure()
    {
        if (_tex != null && _mat != null)
            return;

        const int S = 128;
        _tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        _tex.filterMode = FilterMode.Bilinear;
        _tex.wrapMode = TextureWrapMode.Clamp;
        _tex.hideFlags = HideFlags.HideAndDontSave;

        var px = new Color32[S * S];
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color32(0, 0, 0, 0);

        // Bone ivory — muted, not cartoon white
        var bone = new Color32(210, 200, 185, 255);
        var boneDark = new Color32(120, 105, 95, 255);
        var socket = new Color32(18, 14, 14, 255);
        var line = new Color32(55, 40, 35, 230);

        // Cranium
        FillEllipse(px, S, 64, 68, 36, 34, bone);
        // Shade bottom of skull
        FillEllipse(px, S, 64, 78, 34, 18, boneDark);
        FillEllipse(px, S, 64, 68, 30, 28, bone);

        // Eye sockets — deep oval, no glint
        FillEllipse(px, S, 48, 70, 10, 12, socket);
        FillEllipse(px, S, 80, 70, 10, 12, socket);

        // Nasal cavity — simple triangle wedge
        FillEllipse(px, S, 64, 52, 5, 8, socket);
        FillEllipse(px, S, 64, 48, 3, 4, socket);

        // Upper teeth bar
        FillRect(px, S, 46, 34, 36, 8, bone);
        for (int t = 0; t < 5; t++)
            FillRect(px, S, 48 + t * 7, 34, 1, 8, socket);

        // Jaw
        FillEllipse(px, S, 64, 30, 22, 12, bone);
        FillEllipse(px, S, 64, 28, 16, 7, socket); // mouth void
        // lower teeth
        FillRect(px, S, 50, 30, 28, 4, bone);
        for (int t = 0; t < 4; t++)
            FillRect(px, S, 52 + t * 7, 30, 1, 4, socket);

        // Subtle outline
        StrokeEllipse(px, S, 64, 68, 36, 34, line, 1);
        StrokeEllipse(px, S, 64, 30, 22, 12, line, 1);

        _tex.SetPixels32(px);
        _tex.Apply(false, true);

        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("GUI/Text Shader");
        if (sh == null) sh = Shader.Find("Hidden/Internal-Colored");

        _mat = new Material(sh);
        _mat.hideFlags = HideFlags.HideAndDontSave;
        _mat.mainTexture = _tex;
        _mat.color = Color.white;
        ApplyZTest();
    }

    public static void ApplyZTest()
    {
        if (_mat == null) return;
        _mat.SetInt("_ZTest", EspMod.ThroughWalls
            ? (int)UnityEngine.Rendering.CompareFunction.Always
            : (int)UnityEngine.Rendering.CompareFunction.LessEqual);
    }

    public static void Dispose()
    {
        if (_mat != null) { Object.Destroy(_mat); _mat = null; }
        if (_tex != null) { Object.Destroy(_tex); _tex = null; }
    }

    private static void FillEllipse(Color32[] px, int s, int cx, int cy, int rx, int ry, Color32 col)
    {
        int x0 = Mathf.Max(0, cx - rx - 1);
        int x1 = Mathf.Min(s - 1, cx + rx + 1);
        int y0 = Mathf.Max(0, cy - ry - 1);
        int y1 = Mathf.Min(s - 1, cy + ry + 1);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float dx = (x - cx) / (float)rx;
            float dy = (y - cy) / (float)ry;
            if (dx * dx + dy * dy <= 1f)
                Blend(px, s, x, y, col);
        }
    }

    private static void StrokeEllipse(Color32[] px, int s, int cx, int cy, int rx, int ry, Color32 col, int thick)
    {
        for (int a = 0; a < 360; a++)
        {
            float r = a * Mathf.Deg2Rad;
            for (int t = 0; t < thick; t++)
            {
                int x = cx + Mathf.RoundToInt((rx - t) * Mathf.Cos(r));
                int y = cy + Mathf.RoundToInt((ry - t) * Mathf.Sin(r));
                if ((uint)x < (uint)s && (uint)y < (uint)s)
                    px[y * s + x] = col;
            }
        }
    }

    private static void FillRect(Color32[] px, int s, int x, int y, int w, int h, Color32 col)
    {
        for (int yy = y; yy < y + h; yy++)
        for (int xx = x; xx < x + w; xx++)
            if ((uint)xx < (uint)s && (uint)yy < (uint)s)
                Blend(px, s, xx, yy, col);
    }

    private static void Blend(Color32[] px, int s, int x, int y, Color32 src)
    {
        int i = y * s + x;
        Color32 dst = px[i];
        float a = src.a / 255f;
        float ia = 1f - a;
        px[i] = new Color32(
            (byte)(src.r * a + dst.r * ia),
            (byte)(src.g * a + dst.g * ia),
            (byte)(src.b * a + dst.b * ia),
            (byte)Mathf.Min(255, dst.a + src.a));
    }
}
