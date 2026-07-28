using UnityEngine;
using Object = UnityEngine.Object;

namespace BePrime.Esp;

/// <summary>Procedural high-detail skull icon for dead targets.</summary>
public static class EspSkull
{
    private static Texture2D _tex;
    private static Material _mat;

    public static Material Material
    {
        get
        {
            Ensure();
            return _mat;
        }
    }

    public static Texture2D Texture
    {
        get
        {
            Ensure();
            return _tex;
        }
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

        // Soft outer glow
        FillEllipse(px, S, 64, 62, 48, 52, new Color32(180, 20, 20, 55));
        // Cranium
        FillEllipse(px, S, 64, 70, 40, 38, new Color32(245, 245, 245, 255));
        // Lower jaw plate
        FillEllipse(px, S, 64, 42, 28, 22, new Color32(235, 235, 235, 255));
        // Eye sockets (deep)
        FillEllipse(px, S, 48, 72, 11, 13, new Color32(15, 5, 5, 255));
        FillEllipse(px, S, 80, 72, 11, 13, new Color32(15, 5, 5, 255));
        // Eye glints
        FillEllipse(px, S, 45, 75, 3, 3, new Color32(220, 40, 40, 200));
        FillEllipse(px, S, 77, 75, 3, 3, new Color32(220, 40, 40, 200));
        // Nasal cavity (inverted heart-ish triangle via ellipses)
        FillEllipse(px, S, 64, 55, 6, 9, new Color32(20, 8, 8, 255));
        FillEllipse(px, S, 64, 50, 4, 5, new Color32(20, 8, 8, 255));
        // Cheek hollows
        FillEllipse(px, S, 38, 52, 7, 8, new Color32(200, 200, 200, 255));
        FillEllipse(px, S, 90, 52, 7, 8, new Color32(200, 200, 200, 255));
        // Teeth row
        for (int t = 0; t < 6; t++)
        {
            int x = 44 + t * 7;
            FillRect(px, S, x, 30, 5, 10, new Color32(250, 250, 250, 255));
            // gap lines
            FillRect(px, S, x + 4, 30, 1, 10, new Color32(40, 20, 20, 255));
        }
        // Jaw outline shadow
        StrokeEllipse(px, S, 64, 42, 28, 22, new Color32(60, 20, 20, 220), 2);
        StrokeEllipse(px, S, 64, 70, 40, 38, new Color32(40, 10, 10, 180), 2);
        // Forehead crack detail
        DrawLine(px, S, 58, 95, 62, 78, new Color32(90, 30, 30, 200), 1);
        DrawLine(px, S, 62, 78, 70, 88, new Color32(90, 30, 30, 200), 1);

        _tex.SetPixels32(px);
        _tex.Apply(false, true);

        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null)
            sh = Shader.Find("Unlit/Transparent");
        if (sh == null)
            sh = Shader.Find("GUI/Text Shader");

        _mat = new Material(sh);
        _mat.hideFlags = HideFlags.HideAndDontSave;
        _mat.mainTexture = _tex;
        _mat.color = Color.white;
        _mat.SetInt("_ZTest", EspMod.ThroughWalls
            ? (int)UnityEngine.Rendering.CompareFunction.Always
            : (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        _mat.SetInt("_ZWrite", 0);
    }

    public static void ApplyZTest()
    {
        if (_mat == null)
            return;
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
        float rx2 = rx * rx;
        float ry2 = ry * ry;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x - cx) / (float)rx;
                float dy = (y - cy) / (float)ry;
                if (dx * dx + dy * dy <= 1f)
                    Blend(px, s, x, y, col);
            }
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
                if (x >= 0 && x < s && y >= 0 && y < s)
                    px[y * s + x] = col;
            }
        }
    }

    private static void FillRect(Color32[] px, int s, int x, int y, int w, int h, Color32 col)
    {
        for (int yy = y; yy < y + h; yy++)
        for (int xx = x; xx < x + w; xx++)
        {
            if (xx >= 0 && xx < s && yy >= 0 && yy < s)
                Blend(px, s, xx, yy, col);
        }
    }

    private static void DrawLine(Color32[] px, int s, int x0, int y0, int x1, int y1, Color32 col, int thick)
    {
        int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 0f : i / (float)steps;
            int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
            int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
            for (int dy = -thick; dy <= thick; dy++)
            for (int dx = -thick; dx <= thick; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx >= 0 && xx < s && yy >= 0 && yy < s)
                    px[yy * s + xx] = col;
            }
        }
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
            (byte)Mathf.Clamp(dst.a + src.a, 0, 255));
    }
}
