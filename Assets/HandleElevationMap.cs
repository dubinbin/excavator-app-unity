using UnityEngine;

public class HandleElevationMap : MonoBehaviour
{
    [Tooltip("留空则使用 TerrainData 所在 Terrain")]
    public Terrain terrain;
    [Tooltip("若 Terrain 已赋值，可留空")]
    public TerrainData terrainData;

    /// <summary>由 MqttManager 在收到 01/map/elevation 时调用</summary>
    public void OnElevationDataReceived(ElevationMsg msg)
    {
        if (msg.data_type != "int16") return;

        int w = msg.metadata.width;
        int h = msg.metadata.height;

        // Unity heightmap 需要 float[height+1, width+1]，范围 0~1
        // 但你的栅格是 width × height 个高度值（cell-centered）
        float[,] heights = new float[h + 1, w + 1];  // 注意顺序：y,x

        float range = msg.metadata.max_elevation - msg.metadata.min_elevation;
        if (range <= 0) range = 1f; // 防除0

        float scale = 1f / range;   // 归一化到0~1

        // 你的 data 是 row-major：第 i 行，第 j 列 → data[i * w + j]
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int raw = msg.data[y * w + x];

                // 处理 nodata（假设 -32768 为无效）
                if (raw == -32768) raw = (int)(msg.metadata.min_elevation / msg.metadata.height_resolution);

                float meters = raw * msg.metadata.height_resolution;

                float normalized = (meters - msg.metadata.min_elevation) * scale;
                normalized *= 0.2f;              // 再缩小到原来的 20%
                normalized = Mathf.Clamp01(normalized);

                // 边界复制（Unity 需要多一行/列）
                heights[y, x] = normalized;

                // 简单边界填充（或 bilinear 插值更好）
                if (y == h - 1) heights[y + 1, x] = normalized;
                if (x == w - 1) heights[y, x + 1] = normalized;
                if (y == h - 1 && x == w - 1) heights[y + 1, x + 1] = normalized;
            }
        }

        // 应用高度
        var td = terrain != null ? terrain.terrainData : terrainData;
        if (td == null)
        {
            UnityEngine.Debug.LogWarning("[HandleElevationMap] 未设置 Terrain 或 TerrainData");
            return;
        }
        td.SetHeights(0, 0, heights);
    }
}