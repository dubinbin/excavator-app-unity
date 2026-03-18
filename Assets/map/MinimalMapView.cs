using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class MinimalMapView : MonoBehaviour
{
    public int mapSize = 512;
    public float windowMeters = 60f;
    public int trailMax = 600;
    public Texture2D backgroundTexture;
    Texture2D tex;
    VisualElement mapElem;
    RtkGpsMsg latest;
    readonly List<Vector2> trail = new List<Vector2>();
    Vector2 viewCenter;

    void Start()
    {
        var doc = GetComponent<UIDocument>();
        if (doc != null)
            mapElem = doc.rootVisualElement.Q<VisualElement>("overpass-map");
        var mqtt = FindFirstObjectByType<MqttManager>();
        if (mqtt != null)
            mqtt.OnRtkUpdated += OnRtk;
    }

    void OnDestroy()
    {
        var mqtt = FindFirstObjectByType<MqttManager>();
        if (mqtt != null)
            mqtt.OnRtkUpdated -= OnRtk;
    }

    void OnRtk(RtkGpsMsg rtk)
    {
        latest = rtk;
        if (rtk.position != null && rtk.position.relative != null && rtk.position.relative.translation != null)
        {
            var t = rtk.position.relative.translation;
            var p = new Vector2(t.x, t.y);
            trail.Add(p);
            if (trail.Count > trailMax) trail.RemoveAt(0);
            viewCenter = p;
        }
        Render();
    }

    void Render()
    {
        if (mapElem == null) return;
        if (tex == null)
        {
            tex = new Texture2D(mapSize, mapSize, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
        }
        if (backgroundTexture != null)
            DrawBackground();
        else
            Clear(new Color(0.07f, 0.08f, 0.1f, 1f));
        DrawGrid(10f, new Color(0.14f, 0.16f, 0.2f, 1f));
        DrawTrail(new Color(0.2f, 0.8f, 1f, 0.95f), 3);
        DrawNavigationIndicator();
        tex.Apply();
        mapElem.style.backgroundImage = new StyleBackground(tex);
    }

    float MeterToPixel(float m) => (mapSize - 1) / (2f * windowMeters) * m;

    Vector2 ToScreen(Vector2 meters)
    {
        Vector2 local = meters - viewCenter;
        float sx = (MeterToPixel(local.x) / windowMeters * 0.5f + 0.5f) * (mapSize - 1);
        float sy = (1f - (MeterToPixel(local.y) / windowMeters * 0.5f + 0.5f)) * (mapSize - 1);
        return new Vector2(sx, sy);
    }

    void Clear(Color c)
    {
        var pixels = tex.GetPixels32();
        var col = (Color32)c;
        for (int i = 0; i < pixels.Length; i++) pixels[i] = col;
        tex.SetPixels32(pixels);
    }
    void DrawBackground()
    {
        for (int y = 0; y < mapSize; y++)
        {
            float vy = (float)y / (mapSize - 1);
            for (int x = 0; x < mapSize; x++)
            {
                float vx = (float)x / (mapSize - 1);
                var c = backgroundTexture.GetPixelBilinear(vx, vy);
                tex.SetPixel(x, y, c);
            }
        }
    }

    void SetPixelSafe(int x, int y, Color color)
    {
        if (x >= 0 && x < mapSize && y >= 0 && y < mapSize)
            tex.SetPixel(x, y, color);
    }

    void DrawGrid(float stepMeters, Color color)
    {
        int step = Mathf.Max(6, Mathf.RoundToInt(MeterToPixel(stepMeters)));
        for (int x = 0; x < mapSize; x += step)
            for (int y = 0; y < mapSize; y += 1)
                SetPixelSafe(x, y, color);
        for (int y = 0; y < mapSize; y += step)
            for (int x = 0; x < mapSize; x += 1)
                SetPixelSafe(x, y, color);
    }

    void DrawThickPoint(int x, int y, int halfWidth, Color color)
    {
        for (int dx = -halfWidth; dx <= halfWidth; dx++)
            for (int dy = -halfWidth; dy <= halfWidth; dy++)
                if (dx * dx + dy * dy <= halfWidth * halfWidth)
                    SetPixelSafe(x + dx, y + dy, color);
    }

    void DrawLineThick(int x0, int y0, int x1, int y1, int width, Color color)
    {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            DrawThickPoint(x0, y0, Mathf.Max(1, width / 2), color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    void DrawTrail(Color color, int width)
    {
        if (trail.Count < 2) return;
        for (int i = 1; i < trail.Count; i++)
        {
            var a = ToScreen(trail[i - 1]);
            var b = ToScreen(trail[i]);
            DrawLineThick((int)a.x, (int)a.y, (int)b.x, (int)b.y, width, color);
        }
    }

    void DrawNavigationIndicator()
    {
        int cx = mapSize / 2;
        int cy = mapSize / 2;

        FillCircleBlend(cx, cy, 22, new Color(0.26f, 0.52f, 0.96f, 0.12f));
        FillCircleBlend(cx, cy, 15, new Color(0.26f, 0.52f, 0.96f, 0.22f));

        if (latest != null && latest.rotation != null)
        {
            var q = new Quaternion(latest.rotation.x, latest.rotation.y, latest.rotation.z, latest.rotation.w);
            Vector3 d = q * Vector3.up;
            float angleRad = Mathf.Atan2(d.x, d.y);

            Vector2 dir = new Vector2(Mathf.Sin(angleRad), Mathf.Cos(angleRad));
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector2 center = new Vector2(cx, cy);

            Vector2 tip = center + dir * 20f;
            Vector2 wingL = center - dir * 12f + perp * 12f;
            Vector2 wingR = center - dir * 12f - perp * 12f;
            Vector2 notch = center - dir * 6f;

            Color fill = new Color(0.26f, 0.52f, 0.96f, 1f);
            FillTriangle(tip, wingL, notch, fill);
            FillTriangle(tip, notch, wingR, fill);

            Color border = new Color(1f, 1f, 1f, 0.85f);
            DrawLineThick((int)tip.x, (int)tip.y, (int)wingL.x, (int)wingL.y, 2, border);
            DrawLineThick((int)wingL.x, (int)wingL.y, (int)notch.x, (int)notch.y, 2, border);
            DrawLineThick((int)notch.x, (int)notch.y, (int)wingR.x, (int)wingR.y, 2, border);
            DrawLineThick((int)wingR.x, (int)wingR.y, (int)tip.x, (int)tip.y, 2, border);
        }

        DrawThickPoint(cx, cy, 3, Color.white);

        if (latest != null && latest.rtk_status != null && latest.rtk_status.accuracy != null)
        {
            float r = MeterToPixel(latest.rtk_status.accuracy.horizontal);
            int rr = Mathf.Max(2, Mathf.RoundToInt(r));
            DrawCircleOutline(cx, cy, rr, new Color(1f, 0.84f, 0f, 0.4f));
        }
    }

    void FillCircleBlend(int cx, int cy, int radius, Color src)
    {
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                if (dx * dx + dy * dy <= radius * radius)
                    BlendPixel(cx + dx, cy + dy, src);
    }

    void BlendPixel(int x, int y, Color src)
    {
        if (x < 0 || x >= mapSize || y < 0 || y >= mapSize) return;
        Color dst = tex.GetPixel(x, y);
        float a = src.a;
        tex.SetPixel(x, y, new Color(
            dst.r * (1 - a) + src.r * a,
            dst.g * (1 - a) + src.g * a,
            dst.b * (1 - a) + src.b * a, 1f));
    }

    void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
        int maxX = Mathf.Min(mapSize - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
        int maxY = Mathf.Min(mapSize - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                if (PointInTriangle(new Vector2(x, y), a, b, c))
                    tex.SetPixel(x, y, color);
    }

    bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross2D(p, a, b);
        float d2 = Cross2D(p, b, c);
        float d3 = Cross2D(p, c, a);
        return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
    }

    float Cross2D(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    void DrawCircleOutline(int cx, int cy, int rr, Color color)
    {
        int x = rr, y = 0, err = 0;
        while (x >= y)
        {
            DrawThickPoint(cx + x, cy + y, 1, color);
            DrawThickPoint(cx + y, cy + x, 1, color);
            DrawThickPoint(cx - y, cy + x, 1, color);
            DrawThickPoint(cx - x, cy + y, 1, color);
            DrawThickPoint(cx - x, cy - y, 1, color);
            DrawThickPoint(cx - y, cy - x, 1, color);
            DrawThickPoint(cx + y, cy - x, 1, color);
            DrawThickPoint(cx + x, cy - y, 1, color);
            if (err <= 0) { y += 1; err += 2 * y + 1; }
            if (err > 0) { x -= 1; err -= 2 * x + 1; }
        }
    }
}
