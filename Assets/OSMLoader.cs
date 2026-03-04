using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

public class OSMNode
{
    public long id;
    public double lat;
    public double lon;
}

public class OSMWay
{
    public long id;
    public List<long> refs = new List<long>();
    public Dictionary<string, string> tags = new Dictionary<string, string>();
}

public class OSMRelation
{
    public long id;
    public List<long> memberWayIds = new List<long>();
    public Dictionary<string, string> tags = new Dictionary<string, string>();
}

public class OSMMapData
{
    public Dictionary<long, OSMNode> nodes = new Dictionary<long, OSMNode>();
    public List<OSMWay> highways = new List<OSMWay>();
    public List<OSMWay> boundaries = new List<OSMWay>();
    public List<OSMWay> buildings = new List<OSMWay>();
    public List<OSMWay> waters = new List<OSMWay>();
    public List<OSMWay> landuses = new List<OSMWay>();
}

public static class OSMLoader
{
    public static OSMMapData LoadFromFile(string path)
    {
        var data = new OSMMapData();
        using var reader = XmlReader.Create(path, new XmlReaderSettings { IgnoreWhitespace = true });
        OSMWay currentWay = null;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.Name == "node")
            {
                var n = new OSMNode();
                n.id = long.Parse(reader.GetAttribute("id"));
                n.lat = double.Parse(reader.GetAttribute("lat"), System.Globalization.CultureInfo.InvariantCulture);
                n.lon = double.Parse(reader.GetAttribute("lon"), System.Globalization.CultureInfo.InvariantCulture);
                data.nodes[n.id] = n;
            }
            else if (reader.Name == "way")
            {
                currentWay = new OSMWay();
                currentWay.id = long.Parse(reader.GetAttribute("id"));
                ReadWay(reader, currentWay);
                if (currentWay.tags.ContainsKey("highway"))
                    data.highways.Add(currentWay);
                if (currentWay.tags.TryGetValue("boundary", out var b) && b == "administrative")
                    data.boundaries.Add(currentWay);
                if (currentWay.tags.ContainsKey("building"))
                    data.buildings.Add(currentWay);
                if (currentWay.tags.TryGetValue("natural", out var nat) && nat == "water")
                    data.waters.Add(currentWay);
                else if (currentWay.tags.ContainsKey("waterway"))
                    data.waters.Add(currentWay);
                if (currentWay.tags.ContainsKey("landuse"))
                    data.landuses.Add(currentWay);
                currentWay = null;
            }
        }
        return data;
    }

    static void ReadWay(XmlReader reader, OSMWay way)
    {
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "way") break;
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.Name == "nd")
            {
                long refId = long.Parse(reader.GetAttribute("ref"));
                way.refs.Add(refId);
            }
            else if (reader.Name == "tag")
            {
                string k = reader.GetAttribute("k");
                string v = reader.GetAttribute("v");
                way.tags[k] = v;
            }
        }
    }
}
