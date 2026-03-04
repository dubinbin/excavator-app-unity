using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

/// <summary>
/// MQTT 连接管理器，挂载到场景中的 GameObject 上使用。
/// 依赖：Assets/Plugins/ 下需有 M2Mqtt.dll
/// </summary>
public class MqttManager : MonoBehaviour
{
    [Header("Broker Settings")]
    [Tooltip("MQTT Broker 地址，例如 192.168.1.100 或 broker.hivemq.com")]
    public string brokerHost = "127.0.0.1";

    [Tooltip("MQTT Broker 端口，默认 1883（TLS 为 8883）")]
    public int brokerPort = 1883;

    [Tooltip("客户端 ID，留空则自动生成")]
    public string clientId = "";

    [Header("Auth (Optional)")]
    public string username = "";
    public string password = "";

    [Header("Topics")]
    [Tooltip("启动时自动订阅的主题列表")]
    public string[] subscribeTopics = { "excavator/sensor", "01/map/elevation", "01/sensor/rtk_lio" };

    [Tooltip("发布数据的默认主题")]
    public string publishTopic = "excavator/control";

    // 连接状态
    public bool IsConnected => client != null && client.IsConnected;

    // 收到消息时触发，参数为 (topic, message)
    public event Action<string, string> OnMessageReceived;

    // 连接成功/断开时触发
    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<RtkGpsMsg> OnRtkUpdated;

    private MqttClient client;
    private readonly Queue<(string topic, string msg)> messageQueue = new();
    private readonly object queueLock = new();

    void Start()
    {
        Connect();
    }

    void Update()
    {
        // 在主线程中派发收到的消息（M2Mqtt 回调在子线程）
        lock (queueLock)
        {
            while (messageQueue.Count > 0)
            {
                var (topic, msg) = messageQueue.Dequeue();
                OnMessageReceived?.Invoke(topic, msg);
                DispatchByTopic(topic, msg);
            }
        }
    }

    void OnDestroy()
    {
        Disconnect();
    }

    // ── 公开 API ─────────────────────────────────────────────

    public void Connect()
    {
        try
        {
            string id = string.IsNullOrEmpty(clientId)
                ? "unity-" + SystemInfo.deviceUniqueIdentifier[..8]
                : clientId;

            client = new MqttClient(brokerHost, brokerPort, false, null, null,
                MqttSslProtocols.None);

            client.MqttMsgPublishReceived += OnMqttMessageReceived;
            client.ConnectionClosed += OnConnectionClosed;

            byte code = string.IsNullOrEmpty(username)
                ? client.Connect(id)
                : client.Connect(id, username, password);

            if (code == MqttMsgConnack.CONN_ACCEPTED)
            {
                Debug.Log($"[MQTT] 已连接到 {brokerHost}:{brokerPort}");
                SubscribeAll();
                OnConnected?.Invoke();
            }
            else
            {
                Debug.LogError($"[MQTT] 连接被拒绝，返回码: {code}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MQTT] 连接失败: {e.Message}");
        }
    }

    public void Disconnect()
    {
        if (client != null && client.IsConnected)
        {
            client.Disconnect();
            Debug.Log("[MQTT] 已断开连接");
        }
    }

    /// <summary>发布字符串消息</summary>
    public void Publish(string topic, string message, bool retain = false)
    {
        if (!IsConnected)
        {
            Debug.LogWarning("[MQTT] 未连接，无法发布消息");
            return;
        }
        byte[] payload = Encoding.UTF8.GetBytes(message);
        client.Publish(topic, payload, MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, retain);
    }

    /// <summary>发布到默认主题</summary>
    public void Publish(string message) => Publish(publishTopic, message);

    /// <summary>动态订阅主题</summary>
    public void Subscribe(string topic)
    {
        if (!IsConnected) return;
        client.Subscribe(new[] { topic },
            new[] { MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE });
        Debug.Log($"[MQTT] 已订阅: {topic}");
    }

    // ── 内部方法 ─────────────────────────────────────────────

    private void SubscribeAll()
    {
        if (subscribeTopics == null || subscribeTopics.Length == 0) return;
        var qosLevels = new byte[subscribeTopics.Length];
        for (int i = 0; i < qosLevels.Length; i++)
            qosLevels[i] = MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE;

        client.Subscribe(subscribeTopics, qosLevels);
        Debug.Log($"[MQTT] 已订阅 {subscribeTopics.Length} 个主题");
    }

    private void OnMqttMessageReceived(object sender, MqttMsgPublishEventArgs e)
    {
        string topic = e.Topic;
        string msg = Encoding.UTF8.GetString(e.Message);
        lock (queueLock)
        {
            messageQueue.Enqueue((topic, msg));
        }
    }

    private void DispatchByTopic(string topic, string msg)
    {
        switch (topic)
        {
            case "01/map/elevation":
                Debug.Log($"[MQTT] 高度图: {msg}");
                HandleElevation(msg);
                break;
            case "excavator/sensor":
                HandleSensor(msg);
                break;
            case "01/status":
                HandleStatus(msg);
                break;
            case "01/sensor/rtk_lio":
                HandleRtkLio(msg);
                break;
        }
    }

    private void HandleStatus(string msg)
    {
        Debug.Log($"[MQTT] 状态更新: {msg}");
    }

    private void HandleSensor(string msg)
    {
        Debug.Log($"[MQTT] 传感器数据: {msg}");
    }

    private void OnConnectionClosed(object sender, EventArgs e)
    {
        Debug.LogWarning("[MQTT] 连接已关闭");
        OnDisconnected?.Invoke();
    }

    private void HandleRtkLio(string msg)
    {
        try
        {
            var rtk = JsonUtility.FromJson<RtkGpsMsg>(msg);
            if (rtk == null || rtk.rtk_status == null || rtk.position == null)
            {
                Debug.LogWarning("[MQTT] RTK GPS 数据解析失败或为空");
                return;
            }
            OnRtkUpdated?.Invoke(rtk);
        }
        catch (Exception e)
        {
            Debug.LogError($"[MQTT] RTK GPS 解析异常: {e.Message}");
        }
    }

    private void HandleElevation(string msg)
    {
        try
        {
            var elevation = JsonUtility.FromJson<ElevationMsg>(msg);
            if (elevation?.metadata == null || elevation.data == null)
            {
                Debug.LogWarning("[MQTT] 高程图 JSON 解析失败或数据为空");
                return;
            }
            var handler = FindFirstObjectByType<HandleElevationMap>();
            if (handler != null)
                handler.OnElevationDataReceived(elevation);
            else
                Debug.LogWarning("[MQTT] 场景中未找到 HandleElevationMap，无法更新地形");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MQTT] 高程图解析异常: {e.Message}");
        }
    }
}
