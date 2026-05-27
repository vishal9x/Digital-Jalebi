using UnityEngine;
using UnityEngine.Video;
using System.Collections;

/// <summary>
/// Manages VideoPlayer + RenderTexture pipeline for transparent AR wall video.
/// Attach this to the wall prefab that also has a Renderer component.
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class VideoPlayerController : MonoBehaviour
{
    [Header("Video Source — assign ONE")]
    public VideoClip videoClip;
    [Tooltip("Filename inside StreamingAssets folder, e.g. myvideo.webm (recommended for Android)")]
    public string streamingAssetsFileName;
    [Tooltip("Full URL or absolute path — used only if the above two are both empty.")]
    public string videoURL;

    [Header("Render Texture")]
    [Tooltip("Match your video resolution for best quality.")]
    public int renderTextureWidth = 1920;
    public int renderTextureHeight = 1080;

    [Header("Target Renderer")]
    [Tooltip("Renderer on the quad/wall that uses the TransparentVideo shader.")]
    public Renderer targetRenderer;
    [Tooltip("Shader property name for the video texture.")]
    public string materialTextureName = "_MainTex";

    [Header("Playback")]
    public bool loop = true;
    public bool muteAudio = false;
    public float volume = 1f;

    VideoPlayer _videoPlayer;
    RenderTexture _renderTexture;
    bool _prepared = false;
    bool _preparing = false;

    void Awake()
    {
        _videoPlayer = GetComponent<VideoPlayer>();

        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();

        SetupRenderTexture();
        SetupVideoPlayer();
    }

    void SetupRenderTexture()
    {
        // ARGB32 preserves the alpha channel from WebM VP8/VP9 with alpha
        _renderTexture = new RenderTexture(renderTextureWidth, renderTextureHeight, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            useMipMap = false,
            autoGenerateMips = false,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        bool created = _renderTexture.Create();
        Debug.Log($"[VPC] RenderTexture created={created} size={renderTextureWidth}x{renderTextureHeight} format=ARGB32");
    }

    void SetupVideoPlayer()
    {
        _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        _videoPlayer.targetTexture = _renderTexture;
        _videoPlayer.isLooping = loop;
        _videoPlayer.playOnAwake = false;
        _videoPlayer.waitForFirstFrame = true;

        // Audio — auto-add AudioSource if not present
        _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        AudioSource audioSrc = GetComponent<AudioSource>();
        if (audioSrc == null)
        {
            audioSrc = gameObject.AddComponent<AudioSource>();
            Debug.Log("[VPC] AudioSource not found — added automatically.");
        }
        _videoPlayer.SetTargetAudioSource(0, audioSrc);
        audioSrc.playOnAwake = false;
        audioSrc.volume = muteAudio ? 0f : volume;

        // Source — priority: VideoClip > StreamingAssets filename > raw URL
        if (videoClip != null)
        {
            _videoPlayer.source = VideoSource.VideoClip;
            _videoPlayer.clip = videoClip;
            Debug.Log($"[VPC] Source = VideoClip: {videoClip.name}");
        }
        else if (!string.IsNullOrEmpty(streamingAssetsFileName))
        {
            string fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, streamingAssetsFileName);
            _videoPlayer.source = VideoSource.Url;
            _videoPlayer.url = fullPath;
            Debug.Log($"[VPC] Source = StreamingAssets URL: {fullPath}");
        }
        else if (!string.IsNullOrEmpty(videoURL))
        {
            _videoPlayer.source = VideoSource.Url;
            _videoPlayer.url = videoURL;
            Debug.Log($"[VPC] Source = raw URL: {videoURL}");
        }
        else
        {
            Debug.LogWarning("[VideoPlayerController] No VideoClip, StreamingAssets filename, or URL assigned.", this);
            return;
        }

        // Assign RenderTexture to material
        if (targetRenderer != null)
        {
            Material mat = targetRenderer.material;

            // Safety check: warn if property doesn't exist in the shader
            if (!mat.HasProperty(materialTextureName))
            {
                Debug.LogError($"[VPC] Shader '{mat.shader.name}' has NO property '{materialTextureName}'! " +
                    $"Change 'Material Texture Name' on VideoPlayerController to '_MainTex'.", this);
                materialTextureName = "_MainTex"; // auto-correct
            }

            mat.SetTexture(materialTextureName, _renderTexture);
            Debug.Log($"[VPC] RenderTexture assigned to material '{mat.name}' shader='{mat.shader.name}' property='{materialTextureName}'");
        }
        else
        {
            Debug.LogError("[VPC] targetRenderer is NULL — RenderTexture NOT assigned to any material!", this);
        }

        _videoPlayer.errorReceived += (vp, msg) => Debug.LogError($"[VPC] VideoPlayer error: {msg}");
        _videoPlayer.prepareCompleted += OnPrepareCompleted;
    }

    void OnEnable()
    {
        // Auto-play when the wall prefab becomes active (placed in AR scene)
        StartCoroutine(PrepareAndPlay());
    }

    void OnDisable()
    {
        _videoPlayer.Stop();
        _prepared = false;
        _preparing = false;
    }

    IEnumerator PrepareAndPlay()
    {
        Debug.Log($"[VPC] PrepareAndPlay called. prepared={_prepared} preparing={_preparing} url='{_videoPlayer.url}'");

        if (_prepared)
        {
            _videoPlayer.Play();
            Debug.Log("[VPC] Already prepared — calling Play()");
            yield break;
        }

        // Prevent two coroutines preparing simultaneously
        if (_preparing)
        {
            Debug.Log("[VPC] Already preparing — skipping duplicate call.");
            yield break;
        }

        _preparing = true;
        _videoPlayer.Prepare();
        Debug.Log("[VPC] Prepare() called — waiting...");

        float timeout = 10f;
        float elapsed = 0f;
        while (!_prepared && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        _preparing = false;

        if (_prepared)
        {
            Debug.Log($"[VPC] Prepared OK in {elapsed:F2}s — calling Play()");
            _videoPlayer.Play();
        }
        else
        {
            Debug.LogError($"[VPC] Video NOT prepared after {timeout}s timeout. url='{_videoPlayer.url}'", this);
        }
    }

    void OnPrepareCompleted(VideoPlayer vp)
    {
        _prepared = true;
        Debug.Log($"[VPC] OnPrepareCompleted — video={vp.width}x{vp.height} frames={vp.frameCount} hasAlpha={vp.pixelAspectRatioNumerator}");

        // Sync RenderTexture dimensions to actual video if they differ
        if (vp.texture != null &&
            ((int)vp.width != _renderTexture.width || (int)vp.height != _renderTexture.height))
        {
            Debug.Log($"[VPC] Resizing RenderTexture from {_renderTexture.width}x{_renderTexture.height} to {vp.width}x{vp.height}");
            _renderTexture.Release();
            _renderTexture.width = (int)vp.width;
            _renderTexture.height = (int)vp.height;
            _renderTexture.Create();

            if (targetRenderer != null)
            {
                targetRenderer.material.SetTexture(materialTextureName, _renderTexture);
                Debug.Log("[VPC] RenderTexture re-assigned after resize.");
            }
        }

        // Confirm shader is correct
        if (targetRenderer != null)
        {
            string shaderName = targetRenderer.material.shader.name;
            Debug.Log($"[VPC] Material shader = '{shaderName}' — expected 'Custom/TransparentVideo'");
            if (shaderName != "Custom/TransparentVideo")
                Debug.LogWarning("[VPC] WRONG SHADER! Change material shader to Custom/TransparentVideo in Inspector.");
        }
    }

    // --- Public API ---

    public void Play() => StartCoroutine(PrepareAndPlay());
    public void Pause() => _videoPlayer.Pause();
    public void Stop()
    {
        _videoPlayer.Stop();
        _prepared = false;
    }

    public bool IsPlaying => _videoPlayer != null && _videoPlayer.isPlaying;

    void OnDestroy()
    {
        _videoPlayer.prepareCompleted -= OnPrepareCompleted;

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
    }
}
