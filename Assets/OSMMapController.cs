using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

public class OSMMapController : MonoBehaviour
{
    OSMMapElement mapElement;
    MqttManager mqtt;
    bool centerSet;
    VisualElement container;
    double minLat, minLon, maxLat, maxLon;

    void Start()
    {
        var doc = GetComponent<UIDocument>();
        if (doc == null)
            doc = FindFirstObjectByType<UIDocument>();
        container = doc != null ? doc.rootVisualElement.Q<VisualElement>("overpass-map") : null;
        mapElement = new OSMMapElement();
        if (container != null)
        {
            container.Add(mapElement);
            Debug.Log("[OSM] Map element attached");
        }
        else
        {
            Debug.LogError("[OSM] Container 'overpass-map' not found in UI, attaching to root");
            if (doc != null)
            {
                doc.rootVisualElement.Add(mapElement);
            }
        }
        string osmPath = Path.Combine(Application.dataPath, "map/map.osm");
        Debug.Log($"[OSM] Loading {osmPath}");
        if (File.Exists(osmPath))
        {
            var data = OSMLoader.LoadFromFile(osmPath);
            mapElement.SetMap(data);
             // 计算数据中心，作为初始视图中心
            double sumLat = 0, sumLon = 0;
            int count = 0;
            minLat = double.MaxValue; maxLat = double.MinValue; minLon = double.MaxValue; maxLon = double.MinValue;
            foreach (var n in data.nodes.Values)
            {
                sumLat += n.lat;
                sumLon += n.lon;
                count++;
                if (n.lat < minLat) minLat = n.lat;
                if (n.lat > maxLat) maxLat = n.lat;
                if (n.lon < minLon) minLon = n.lon;
                if (n.lon > maxLon) maxLon = n.lon;
            }
            if (count > 0)
                mapElement.SetCenter(sumLat / count, sumLon / count);
            Debug.Log($"[OSM] nodes:{data.nodes.Count} roads:{data.highways.Count} bounds:{data.boundaries.Count} bbox:[{minLat:F6},{minLon:F6}] - [{maxLat:F6},{maxLon:F6}]");
            mapElement.MarkDirtyRepaint();
            Invoke(nameof(FitAfterGeometry), 0.2f);
        }
        else
        {
            Debug.LogError("[OSM] OSM file not found");
        }
        mqtt = FindFirstObjectByType<MqttManager>();
        if (mqtt != null) mqtt.OnRtkUpdated += OnRtk;
        Invoke(nameof(LogContainerSize), 0.2f);
    }

    void OnDestroy()
    {
        if (mqtt != null) mqtt.OnRtkUpdated -= OnRtk;
    }

    void OnRtk(RtkGpsMsg rtk)
    {
        if (rtk.position != null && rtk.position.global != null)
        {
            var rot = new Quaternion(rtk.rotation.x, rtk.rotation.y, rtk.rotation.z, rtk.rotation.w);
            var vel = new Vector2(rtk.velocity.x, rtk.velocity.y);
            mapElement.SetRobotPose(rtk.position.global.latitude, rtk.position.global.longitude, rot, vel);
        }
    }

    void LogContainerSize()
    {
        if (container != null)
            Debug.Log($"[OSM] container:{container.contentRect.width:F0}x{container.contentRect.height:F0} map:{mapElement.contentRect.width:F0}x{mapElement.contentRect.height:F0}");
    }

    void FitAfterGeometry()
    {
        mapElement.FitToData(minLat, minLon, maxLat, maxLon);
        Debug.Log($"[OSM] Fit view with bbox");
    }
}
