using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using Unity.WebRTC;

/// <summary>
/// 通过 WHEP 协议从 MediaMTX 接收 WebRTC 视频流，渲染到 UI Toolkit。
///
/// MediaMTX WHEP 端点格式: http://{host}:8889/{stream}/whep
///
/// 流程:
///   1. 创建 PeerConnection + Offer SDP
///   2. HTTP POST Offer 到 WHEP 端点
///   3. 收到 Answer SDP → 设为 RemoteDescription
///   4. 视频帧自动流入 → 渲染到 RenderTexture → 绑定 UI
///
/// 依赖: com.unity.webrtc
/// </summary>
public class WebRtcReceiver : MonoBehaviour
{
    [Header("MediaMTX WHEP")]
    [Tooltip("WHEP 端点地址，例如 http://192.168.1.100:8889/cam1/whep")]
    public string whepUrl = "http://127.0.0.1:8889/left2Url/whep";

    [Header("ICE 服务器（内网可留空）")]
    public string[] iceServers = { };

    [Header("视频设置")]
    public int videoWidth = 640;
    public int videoHeight = 480;

    [Header("重连")]
    public float reconnectDelay = 3f;

    private RenderTexture _renderTex;
    private VisualElement _videoElem;
    private Label _statusLabel;
    private bool _videoReady;
    private string _whepSessionUrl;
    private RTCPeerConnection _pc;
    private Texture _videoSourceTex;

    void Start()
    {
        BindUI();
        StartCoroutine(WebRTC.Update());
        StartCoroutine(ConnectWhep());
    }

    void Update()
    {
        if (_videoSourceTex != null && _renderTex != null)
            Graphics.Blit(_videoSourceTex, _renderTex);
    }

    void OnDestroy()
    {
        _pc?.Dispose();
        if (_renderTex != null)
        {
            _renderTex.Release();
            Destroy(_renderTex);
        }
        if (!string.IsNullOrEmpty(_whepSessionUrl))
            StartCoroutine(DeleteSession(_whepSessionUrl));
    }

    // ── UI ───────────────────────────────────────────────────

    private void BindUI()
    {
        var doc = GetComponent<UIDocument>();
        if (doc == null) return;
        var root = doc.rootVisualElement;
        _videoElem = root.Q<VisualElement>("video-feed");
        _statusLabel = root.Q<Label>("lbl-video-status");
        SetStatus("正在连接...");
    }

    private void SetStatus(string text)
    {
        if (_statusLabel == null) return;
        _statusLabel.text = text ?? "";
        _statusLabel.style.display = string.IsNullOrEmpty(text)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
    }

    private void ApplyVideoTexture()
    {
        if (_videoElem == null || _renderTex == null) return;
        _videoElem.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_renderTex));
        SetStatus(null);
        _videoReady = true;
    }

    // ── WHEP 连接流程 ────────────────────────────────────────

    private IEnumerator ConnectWhep()
    {
        SetStatus("正在建立连接...");
        _videoReady = false;

        var config = new RTCConfiguration();
        if (iceServers != null && iceServers.Length > 0)
            config.iceServers = new[] { new RTCIceServer { urls = iceServers } };

        _pc = new RTCPeerConnection(ref config);

        _pc.OnTrack = e =>
        {
            if (e.Track is VideoStreamTrack videoTrack)
            {
                _renderTex = new RenderTexture(videoWidth, videoHeight, 0);
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
            Debug.Log($"[WHEP] ICE 状态: {state}");
            if (state == RTCIceConnectionState.Disconnected ||
                state == RTCIceConnectionState.Failed)
            {
                SetStatus("连接断开，正在重连...");
                _videoReady = false;
                StartCoroutine(Reconnect());
            }
        };

        var transceiver = _pc.AddTransceiver(TrackKind.Video);
        transceiver.Direction = RTCRtpTransceiverDirection.RecvOnly;

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

        SetStatus("正在协商...");
        var postReq = new UnityWebRequest(whepUrl, "POST");
        postReq.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(offerSdp));
        postReq.downloadHandler = new DownloadHandlerBuffer();
        postReq.SetRequestHeader("Content-Type", "application/sdp");
        yield return postReq.SendWebRequest();

        if (postReq.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[WHEP] POST 失败: {postReq.error} ({postReq.responseCode})");
            SetStatus($"WHEP 连接失败 ({postReq.responseCode})");
            StartCoroutine(Reconnect());
            yield break;
        }

        _whepSessionUrl = postReq.GetResponseHeader("Location");
        if (!string.IsNullOrEmpty(_whepSessionUrl) && !_whepSessionUrl.StartsWith("http"))
        {
            var uri = new Uri(whepUrl);
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

        Debug.Log("[WHEP] 连接建立成功");
        SetStatus("等待视频帧...");
    }

    private IEnumerator WaitForIceGathering()
    {
        float timeout = 5f;
        float elapsed = 0f;
        while (_pc.GatheringState != RTCIceGatheringState.Complete && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (elapsed >= timeout)
            Debug.LogWarning("[WHEP] ICE 收集超时，使用当前候选");
    }

    private IEnumerator Reconnect()
    {
        yield return new WaitForSeconds(reconnectDelay);
        _pc?.Dispose();
        _pc = null;
        yield return ConnectWhep();
    }

    private IEnumerator DeleteSession(string url)
    {
        if (string.IsNullOrEmpty(url)) yield break;
        var req = UnityWebRequest.Delete(url);
        yield return req.SendWebRequest();
    }
}
