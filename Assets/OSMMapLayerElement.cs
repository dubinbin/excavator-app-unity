using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public enum OSMLayerType
{
    Buildings,
    Waters,
    Landuses,
    Highways,
    Boundaries
}

public class OSMMapLayerElement : VisualElement
{
    public OSMMapData Map;
    public OSMLayerType Layer;
    public double CenterLat;
    public double CenterLon;
    public float Zoom = 1f;
    public float PixelsPerMeter = 1f;
    public Vector2 Offset;
    public int MaxPointsPerPath = 200;
    public int MaxPaths = 400;
    public int VertexBudget = 20000;

    public OSMMapLayerElement()
    {
        style.position = Position.Absolute;
        style.left = 0;
        style.top = 0;
        style.width = new Length(100, LengthUnit.Percent);
        style.height = new Length(100, LengthUnit.Percent);
        pickingMode = PickingMode.Ignore;
        generateVisualContent += OnGenerate;
    }

    void OnGenerate(MeshGenerationContext ctx)
    {
        if (Map == null) return;
        var painter = ctx.painter2D;
        int budget = VertexBudget;
        switch (Layer)
        {
            case OSMLayerType.Buildings:
                painter.fillColor = new Color(0.23f, 0.48f, 0.84f, 0.15f);
                painter.strokeColor = new Color(0.23f, 0.48f, 0.84f, 0.6f);
                DrawPolygons(painter, Map.buildings, ref budget);
                break;
            case OSMLayerType.Waters:
                painter.fillColor = new Color(0.2f, 0.5f, 1f, 0.25f);
                painter.strokeColor = new Color(0.2f, 0.5f, 1f, 0.7f);
                DrawPolygons(painter, Map.waters, ref budget);
                break;
            case OSMLayerType.Landuses:
                painter.fillColor = new Color(0.2f, 0.8f, 0.4f, 0.18f);
                painter.strokeColor = new Color(0.2f, 0.8f, 0.4f, 0.5f);
                DrawPolygons(painter, Map.landuses, ref budget);
                break;
            case OSMLayerType.Highways:
                painter.strokeColor = Color.white;
                DrawLines(painter, Map.highways, ref budget);
                break;
            case OSMLayerType.Boundaries:
                painter.fillColor = new Color(1f, 0.5f, 0.3f, 0.15f);
                painter.strokeColor = new Color(0.9f, 0.5f, 0.3f, 1f);
                DrawPolygons(painter, Map.boundaries, ref budget);
                break;
        }
    }

    void DrawPolygons(Painter2D painter, List<OSMWay> ways, ref int budget)
    {
        int drawn = 0;
        foreach (var way in ways)
        {
            if (drawn >= MaxPaths || budget <= 0) break;
            var pts = ProjectWay(way);
            if (!IsVisible(pts)) continue;
            pts = Decimate(pts, Mathf.Min(MaxPointsPerPath, Mathf.Max(16, budget / 3)), true);
            if (pts.Count < 3) continue;
            painter.BeginPath();
            painter.MoveTo(pts[0]);
            for (int i = 1; i < pts.Count; i++)
            {
                painter.LineTo(pts[i]);
                budget--;
                if (budget <= 0) break;
            }
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
            drawn++;
        }
    }

    void DrawLines(Painter2D painter, List<OSMWay> ways, ref int budget)
    {
        int drawn = 0;
        foreach (var way in ways)
        {
            if (drawn >= MaxPaths || budget <= 0) break;
            var pts = ProjectWay(way);
            if (!IsVisible(pts)) continue;
            pts = Decimate(pts, Mathf.Min(MaxPointsPerPath, Mathf.Max(16, budget)), false);
            if (pts.Count < 2) continue;
            painter.BeginPath();
            painter.MoveTo(pts[0]);
            for (int i = 1; i < pts.Count; i++)
            {
                painter.LineTo(pts[i]);
                budget--;
                if (budget <= 0) break;
            }
            painter.Stroke();
            drawn++;
        }
    }

    List<Vector2> ProjectWay(OSMWay way)
    {
        var pts = new List<Vector2>(way.refs.Count);
        for (int i = 0; i < way.refs.Count; i++)
        {
            var nid = way.refs[i];
            if (!Map.nodes.TryGetValue(nid, out var node)) continue;
            pts.Add(Project(node.lat, node.lon));
        }
        return pts;
    }

    Vector2 Project(double lat, double lon)
    {
        float w = contentRect.width;
        float h = contentRect.height;
        double r = 6378137.0;
        double cx = r * Deg2Rad(CenterLon);
        double cy = r * Math.Log(Math.Tan(Math.PI / 4.0 + Deg2Rad(CenterLat) / 2.0));
        double x = r * Deg2Rad(lon);
        double y = r * Math.Log(Math.Tan(Math.PI / 4.0 + Deg2Rad(lat) / 2.0));
        float dx = (float)(x - cx);
        float dy = (float)(y - cy);
        float scale = PixelsPerMeter * Zoom;
        float px = w * 0.5f + Offset.x + dx * scale;
        float py = h * 0.5f + Offset.y - dy * scale;
        return new Vector2(px, py);
    }

    bool IsVisible(List<Vector2> pts)
    {
        if (pts.Count == 0) return false;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }
        float w = contentRect.width, h = contentRect.height;
        float pad = 12f;
        if (maxX < -pad || minX > w + pad) return false;
        if (maxY < -pad || minY > h + pad) return false;
        return true;
    }

    List<Vector2> Decimate(List<Vector2> pts, int maxPoints, bool ensureClosed)
    {
        if (pts.Count <= maxPoints) return pts;
        int stride = Mathf.CeilToInt((float)pts.Count / maxPoints);
        var outPts = new List<Vector2>(maxPoints + 1);
        for (int i = 0; i < pts.Count; i += stride) outPts.Add(pts[i]);
        if (ensureClosed && outPts.Count > 0)
        {
            var first = outPts[0];
            var last = outPts[outPts.Count - 1];
            if (first != last) outPts.Add(first);
        }
        return outPts;
    }

    double Deg2Rad(double d) => d * Math.PI / 180.0;
}
