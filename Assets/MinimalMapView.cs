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
        DrawAccuracyCircle(new Color(1f, 0.84f, 0f, 0.35f));
        DrawArrow(new Color(0.2f, 1f, 0.4f, 1f), 36, 3);
        tex.Apply();
        mapElem.style.backgroundImage = new StyleBackground(tex);
    }

    float MeterToPixel(float m) => (mapSize - 1) / (2f * windowMeters) * m;

    Vector2 ToScreen(Vector2 meters)
    {
        float sx = (MeterToPixel(meters.x) / windowMeters * 0.5f + 0.5f) * (mapSize - 1);
        float sy = (1f - (MeterToPixel(meters.y) / windowMeters * 0.5f + 0.5f)) * (mapSize - 1);
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

    void DrawAccuracyCircle(Color color)
    {
        if (latest == null || latest.rtk_status == null || latest.rtk_status.accuracy == null) return;
        float r = MeterToPixel(latest.rtk_status.accuracy.horizontal);
        int cx = mapSize / 2;
        int cy = mapSize / 2;
        int rr = Mathf.Max(2, Mathf.RoundToInt(r));
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

    void DrawArrow(Color color, float length, int width)
    {
        if (latest == null || latest.rotation == null) return;
        var q = new Quaternion(latest.rotation.x, latest.rotation.y, latest.rotation.z, latest.rotation.w);
        Vector3 north = new Vector3(0, 1, 0);
        Vector3 d = q * north;
        float angleRad = Mathf.Atan2(d.x, d.y);
        Vector2 dir = new Vector2(Mathf.Sin(angleRad), Mathf.Cos(angleRad));
        Vector2 center = new Vector2(mapSize / 2f, mapSize / 2f);
        Vector2 tip = center + dir * length;
        DrawLineThick((int)center.x, (int)center.y, (int)tip.x, (int)tip.y, width, color);
        Vector2 left = Rotate(dir, -140f * Mathf.Deg2Rad) * (length * 0.3f);
        Vector2 right = Rotate(dir, 140f * Mathf.Deg2Rad) * (length * 0.3f);
        DrawLineThick((int)tip.x, (int)tip.y, (int)(tip.x + left.x), (int)(tip.y + left.y), width, color);
        DrawLineThick((int)tip.x, (int)tip.y, (int)(tip.x + right.x), (int)(tip.y + right.y), width, color);
    }

    Vector2 Rotate(Vector2 v, float a) => new Vector2(v.x * Mathf.Cos(a) - v.y * Mathf.Sin(a), v.x * Mathf.Sin(a) + v.y * Mathf.Cos(a));
}
