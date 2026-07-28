using UnityEngine;
using Object = UnityEngine.Object;

namespace BePrime.Esp;

/// <summary>Big emoji-style ☠️ skull — centered on corpse.</summary>
public static class EspSkull
{
    private static Texture2D _tex;
    private static Material _mat;

    public static Material Material { get { Ensure(); return _mat; } }
    public static Texture2D Texture { get { Ensure(); return _tex; } }

    public static void Ensure()
    {
        if (_tex != null && _mat != null)
            return;

        // Classic ☠️ silhouette: round white skull, black sockets, nose, jaw teeth
        const int S = 256;
        _tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        _tex.filterMode = FilterMode.Bilinear;
        _tex.wrapMode = TextureWrapMode.Clamp;
        _tex.hideFlags = HideFlags.HideAndDontSave;

        var px = new Color32[S * S];
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color32(0, 0, 0, 0);

        var white = new Color32(255, 255, 255, 255);
        var black = new Color32(0, 0, 0, 255);

        // Soft shadow ring
        FillCircle(px, S, 128, 128, 110, new Color32(0, 0, 0, 70));

        // Main cranium (emoji round head)
        FillCircle(px, S, 128, 140, 92, white);
        // Jaw block
        FillRect(px, S, 58, 48, 140, 70, white);
        // Round jaw bottom
        FillCircle(px, S, 128, 55, 55, white);

        // Eye sockets — large black circles like ☠️
        FillCircle(px, S, 95, 145, 28, black);
        FillCircle(px, S, 161, 145, 28, black);

        // Nose — inverted heart / triangle
        FillTriangle(px, S, 128, 95, 112, 125, 144, 125, black);
        FillCircle(px, S, 128, 118, 10, black);

        // Mouth opening
        FillRect(px, S, 78, 58, 100, 28, black);
        // Teeth — vertical white bars over black mouth
        for (int t = 0; t < 6; t++)
        {
            int x = 86 + t * 14;
            FillRect(px, S, x, 58, 8, 28, white);
        }
        // Horizontal tooth gap line
        FillRect(px, S, 78, 70, 100, 4, black);

        // Crossbones behind (☠️ style) — two diagonals under skull
        DrawBone(px, S, 40, 40, 216, 100, white, black);
        DrawBone(px, S, 216, 40, 40, 100, white, black);

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
        _mat.SetInt("_ZWrite", 0);
    }

    public static void Dispose()
    {
        if (_mat != null) { Object.Destroy(_mat); _mat = null; }
        if (_tex != null) { Object.Destroy(_tex); _tex = null; }
    }

    private static void FillCircle(Color32[] px, int s, int cx, int cy, int r, Color32 col)
    {
        int r2 = r * r;
        for (int y = cy - r; y <= cy + r; y++)
        for (int x = cx - r; x <= cx + r; x++)
        {
            if ((uint)x >= (uint)s || (uint)y >= (uint)s) continue;
            int dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy <= r2)
                Blend(px, s, x, y, col);
        }
    }

    private static void FillRect(Color32[] px, int s, int x, int y, int w, int h, Color32 col)
    {
        for (int yy = y; yy < y + h; yy++)
        for (int xx = x; xx < x + w; xx++)
            if ((uint)xx < (uint)s && (uint)yy < (uint)s)
                Blend(px, s, xx, yy, col);
    }

    private static void FillTriangle(Color32[] px, int s, int x0, int y0, int x1, int y1, int x2, int y2, Color32 col)
    {
        int minX = Mathf.Min(x0, Mathf.Min(x1, x2));
        int maxX = Mathf.Max(x0, Mathf.Max(x1, x2));
        int minY = Mathf.Min(y0, Mathf.Min(y1, y2));
        int maxY = Mathf.Max(y0, Mathf.Max(y1, y2));
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            if ((uint)x >= (uint)s || (uint)y >= (uint)s) continue;
            if (PointInTri(x, y, x0, y0, x1, y1, x2, y2))
                Blend(px, s, x, y, col);
        }
    }

    private static bool PointInTri(int px, int py, int x0, int y0, int x1, int y1, int x2, int y2)
    {
        float d1 = Sign(px, py, x0, y0, x1, y1);
        float d2 = Sign(px, py, x1, y1, x2, y2);
        float d3 = Sign(px, py, x2, y2, x0, y0);
        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(hasNeg && hasPos);
    }

    private static float Sign(int px, int py, int x0, int y0, int x1, int y1)
        => (px - x1) * (y0 - y1) - (x0 - x1) * (py - y1);

    private static void DrawBone(Color32[] px, int s, int x0, int y0, int x1, int y1, Color32 bone, Color32 outline)
    {
        // Thick diagonal shaft
        int steps = 120;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
            int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
            FillCircle(px, s, x, y, 7, outline);
            FillCircle(px, s, x, y, 5, bone);
        }
        // Knobs at ends
        FillCircle(px, s, x0, y0, 14, outline);
        FillCircle(px, s, x0, y0, 11, bone);
        FillCircle(px, s, x1, y1, 14, outline);
        FillCircle(px, s, x1, y1, 11, bone);
    }

    private static void Blend(Color32[] px, int s, int x, int y, Color32 src)
    {
        if (src.a == 255)
        {
            px[y * s + x] = src;
            return;
        }
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
