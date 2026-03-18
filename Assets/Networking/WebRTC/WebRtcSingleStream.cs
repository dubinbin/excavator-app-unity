using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using Unity.WebRTC;

/// <summary>
/// 单路 WHEP WebRTC 拉流逻辑，不继承 MonoBehaviour。
/// 由外部传入 UI 元素和配置，配合协程使用。
/// </summary>
public class WebRtcSingleStream
{
    public string WhepUrl;
    public string[] IceServers;
    public int VideoWidth = 640;
    public int VideoHeight = 480;
    public float ReconnectDelay = 3f;

    readonly VisualElement _videoElem;
    readonly Label _statusLabel;
    readonly MonoBehaviour _host; // 用来启动协程

    RenderTexture _renderTex;
    RTCPeerConnection _pc;
    Texture _videoSourceTex;
    string _whepSessionUrl;
    bool _videoReady;
    bool _disposed;
    bool _connected;
    bool _needReconnect;

    public WebRtcSingleStream(
        MonoBehaviour host,
        string whepUrl,
        VisualElement videoElem,
        Label statusLabel,
        string[] iceServers = null,
        int videoWidth = 640,
        int videoHeight = 480,
        float reconnectDelay = 3f)
    {
        _host = host;
        WhepUrl = whepUrl;
        _videoElem = videoElem;
        _statusLabel = statusLabel;
        IceServers = iceServers ?? Array.Empty<string>();
        VideoWidth = videoWidth;
        VideoHeight = videoHeight;
        ReconnectDelay = reconnectDelay;
    }

    /// <summary>
    /// 外部在 MonoBehaviour.Update 中调用，实现 Blit。
    /// </summary>
    public void Update()
    {
        if (_videoSourceTex != null && _renderTex != null)
            Graphics.Blit(_videoSourceTex, _renderTex);
    }

    /// <summary>
    /// 外部在 MonoBehaviour.OnDestroy 调用，清理资源。
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        CleanupPc();
        if (_renderTex != null)
        {
            _renderTex.Release();
            UnityEngine.Object.Destroy(_renderTex);
            _renderTex = null;
        }
    }

    /// <summary>
    /// 主协程：建立连接 → 拉流 → 自动重连。
    /// </summary>
    public IEnumerator Run()
    {
        while (!_disposed)
        {
            _connected = false;
            _needReconnect = false;

            yield return ConnectOnce();

            if (_disposed)
                yield break;

            if (_connected)
            {
                while (!_disposed && !_needReconnect)
                    yield return null;

                if (_disposed)
                    yield break;
            }

            CleanupPc();
            SetStatus($"等待重连... ({ReconnectDelay:F0}s)");
            Debug.Log($"[WHEP] {WhepUrl} 将在 {ReconnectDelay}s 后重连");
            yield return new WaitForSeconds(ReconnectDelay);
        }
    }

    IEnumerator ConnectOnce()
    {
        SetStatus("正在建立连接...");
        _videoReady = false;

        var config = new RTCConfiguration();
        if (IceServers != null && IceServers.Length > 0)
            config.iceServers = new[] { new RTCIceServer { urls = IceServers } };

        _pc = new RTCPeerConnection(ref config);

        _pc.OnTrack = e =>
        {
            if (e.Track is VideoStreamTrack videoTrack)
            {
                _renderTex = new RenderTexture(VideoWidth, VideoHeight, 0);
                _renderTex.Create();
                videoTrack.OnVideoReceived += tex =>
                {
                    _videoSourceTex = tex;
                    if (!_videoReady)
                        ApplyVideoTexture();
                };
                Debug.Log("[WHEP] 收到视频轨道");
            }
        };

        _pc.OnIceConnectionChange = state =>
        {
            Debug.Log($"[WHEP] ICE 状态: {state} ({WhepUrl})");
            if (state == RTCIceConnectionState.Disconnected ||
                state == RTCIceConnectionState.Failed ||
                state == RTCIceConnectionState.Closed)
            {
                SetStatus("连接断开，正在重连...");
                _videoReady = false;
                _needReconnect = true;
            }
        };

        var transceiver = _pc.AddTransceiver(TrackKind.Video);
        transceiver.Direction = RTCRtpTransceiverDirection.RecvOnly;
        SetH264Preferred(transceiver);

        var offerOp = _pc.CreateOffer();
        yield return offerOp;

        if (offerOp.IsError)
        {
            Debug.LogError($"[WHEP] CreateOffer 失败: {offerOp.Error.message}");
            SetStatus("创建 Offer 失败");
            yield break;
        }

        var offerDesc = offerOp.Desc;
        var setLocalOp = _pc.SetLocalDescription(ref offerDesc);
        yield return setLocalOp;

        yield return WaitForIceGathering();

        string offerSdp = _pc.LocalDescription.sdp;
        Debug.Log($"[WHEP] Offer SDP:\n{offerSdp}");

        SetStatus("正在协商...");
        var postReq = new UnityWebRequest(WhepUrl, "POST");
        postReq.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(offerSdp));
        postReq.downloadHandler = new DownloadHandlerBuffer();
        postReq.SetRequestHeader("Content-Type", "application/sdp");
        postReq.SetRequestHeader("Accept", "*/*");
        yield return postReq.SendWebRequest();

        if (postReq.result != UnityWebRequest.Result.Success)
        {
            string body = postReq.downloadHandler?.text ?? "(empty)";
            Debug.LogError($"[WHEP] POST 失败: {postReq.error} ({postReq.responseCode})\n响应体: {body}");
            SetStatus($"WHEP 连接失败 ({postReq.responseCode})");
            yield break;
        }

        _whepSessionUrl = postReq.GetResponseHeader("Location");
        if (!string.IsNullOrEmpty(_whepSessionUrl) && !_whepSessionUrl.StartsWith("http"))
        {
            var uri = new Uri(WhepUrl);
            _whepSessionUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}{_whepSessionUrl}";
        }

        string answerSdp = postReq.downloadHandler.text;
        var answerDesc = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = answerSdp };
        var setRemoteOp = _pc.SetRemoteDescription(ref answerDesc);
        yield return setRemoteOp;

        if (setRemoteOp.IsError)
        {
            Debug.LogError($"[WHEP] SetRemoteDescription 失败: {setRemoteOp.Error.message}");
            SetStatus("协商失败");
            yield break;
        }

        Debug.Log($"[WHEP] 连接建立成功: {WhepUrl}");
        SetStatus("等待视频帧...");
        _connected = true;
    }

    IEnumerator WaitForIceGathering()
    {
        float timeout = 5f;
        float elapsed = 0f;
        while (_pc != null &&
               _pc.GatheringState != RTCIceGatheringState.Complete &&
               elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (elapsed >= timeout)
            Debug.LogWarning("[WHEP] ICE 收集超时，使用当前候选");
    }

    IEnumerator DeleteSession(string url)
    {
        if (string.IsNullOrEmpty(url)) yield break;
        var req = UnityWebRequest.Delete(url);
        yield return req.SendWebRequest();
    }

    void CleanupPc()
    {
        if (!string.IsNullOrEmpty(_whepSessionUrl))
        {
            var url = _whepSessionUrl;
            _whepSessionUrl = null;
            if (_host != null && _host.gameObject.activeInHierarchy)
                _host.StartCoroutine(DeleteSession(url));
        }

        _pc?.Dispose();
        _pc = null;
        _videoSourceTex = null;
    }

    void ApplyVideoTexture()
    {
        if (_videoElem == null || _renderTex == null) return;
        _videoElem.style.backgroundImage =
            new StyleBackground(Background.FromRenderTexture(_renderTex));
        SetStatus(null);
        _videoReady = true;
    }

    void SetStatus(string text)
    {
        if (_statusLabel == null) return;
        _statusLabel.text = text ?? "";
        _statusLabel.style.display = string.IsNullOrEmpty(text)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
    }

    void SetH264Preferred(RTCRtpTransceiver transceiver)
    {
        try
        {
            var caps = RTCRtpReceiver.GetCapabilities(TrackKind.Video);
            if (caps?.codecs == null || caps.codecs.Length == 0)
            {
                Debug.LogWarning("[WHEP] 无法获取编解码器能力");
                return;
            }

            var sb = new StringBuilder("[WHEP] 可用编解码器: ");
            foreach (var c in caps.codecs)
                sb.Append(c.mimeType).Append(' ');
            Debug.Log(sb.ToString());

            var h264 = new List<RTCRtpCodecCapability>();
            var others = new List<RTCRtpCodecCapability>();
            foreach (var c in caps.codecs)
            {
                if (c.mimeType == "video/H264")
                    h264.Add(c);
                else
                    others.Add(c);
            }

            if (h264.Count == 0)
            {
                Debug.LogWarning("[WHEP] 当前平台不支持 H264 解码，RTSP 摄像头流可能无法播放");
                return;
            }

            h264.AddRange(others);
            transceiver.SetCodecPreferences(h264.ToArray());
            Debug.Log($"[WHEP] 已将 H264 设为首选编解码器 ({h264.Count} 个候选)");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WHEP] SetCodecPreferences 异常: {ex.Message}");
        }
    }
}