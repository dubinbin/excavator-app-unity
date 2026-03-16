using System;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 多路 WebRTC 视频管理器：在一个 UIDocument 上管理多路 WHEP 拉流。
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class MultiWebRtcManager : MonoBehaviour
{
    [Serializable]
    public class WebRtcStreamConfig
    {
        [Tooltip("WHEP 端点，例如 http://127.0.0.1:8889/left1Url/whep")]
        public string whepUrl;

        [Tooltip("UXML 中视频容器的 name，例如 video-feed-left")]
        public string containerName;

        [Tooltip("UXML 中状态 Label 的 name，例如 lbl-video-status-left")]
        public string statusLabelName = "lbl-video-status";
    }

    [Header("全局 WebRTC 设置")]
    public string[] iceServers = { };
    public int videoWidth = 640;
    public int videoHeight = 480;
    public float reconnectDelay = 3f;

    [Header("多路视频配置")]
    public WebRtcStreamConfig[] streams;

    readonly List<WebRtcSingleStream> _runningStreams = new List<WebRtcSingleStream>();
    UIDocument _doc;

    void Start()
    {
        _doc = GetComponent<UIDocument>();
        if (_doc == null)
        {
            Debug.LogError("[MultiWHEP] 未找到 UIDocument 组件");
            return;
        }

        var root = _doc.rootVisualElement;

        if (streams == null || streams.Length == 0)
        {
            Debug.LogWarning("[MultiWHEP] streams 为空，未配置任何视频流");
            return;
        }

        foreach (var cfg in streams)
        {
            if (string.IsNullOrEmpty(cfg.whepUrl))
            {
                Debug.LogWarning("[MultiWHEP] 跳过一条未配置 whepUrl 的流");
                continue;
            }

            var container = root.Q<VisualElement>(cfg.containerName);
            var status = root.Q<Label>(cfg.statusLabelName);

            if (container == null)
            {
                Debug.LogError($"[MultiWHEP] 找不到视频容器: {cfg.containerName}");
                continue;
            }

            if (status == null)
            {
                Debug.LogWarning($"[MultiWHEP] 找不到状态 Label: {cfg.statusLabelName}，该流将无法显示文字状态");
            }

            var stream = new WebRtcSingleStream(
                host: this,
                whepUrl: cfg.whepUrl,
                videoElem: container,
                statusLabel: status,
                iceServers: iceServers,
                videoWidth: videoWidth,
                videoHeight: videoHeight,
                reconnectDelay: reconnectDelay
            );

            _runningStreams.Add(stream);
            StartCoroutine(stream.Run());

            Debug.Log($"[MultiWHEP] 启动流: {cfg.whepUrl} → {cfg.containerName}");
        }

        // 建议全局只启动一次 WebRTC.Update 协程
        StartCoroutine(WebRTC.Update());
    }

    void Update()
    {
        // 把 Blit 分发给每一路流
        foreach (var s in _runningStreams)
        {
            s.Update();
        }
    }

    void OnDestroy()
    {
        foreach (var s in _runningStreams)
        {
            s.Dispose();
        }
        _runningStreams.Clear();
    }
}