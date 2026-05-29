using UnityEngine;
using UnityEngine.Video;
using System.Collections;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Manages VideoPlayer + RenderTexture pipeline for transparent AR wall video.
/// Attach this to the wall prefab that also has a Renderer component.
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class VideoPlayerController : MonoBehaviour
{
    [Header("Addressables (Remote — video NOT bundled in app)")]
    [Tooltip("Assign your Addressable VideoClip asset reference here. The video is downloaded at runtime from the remote bundle and takes priority over all other sources.")]
    public AssetReferenceT<VideoClip> addressableVideo;

    [Header("Video Source — fallback for Editor / local testing")]
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

    [Header("Physical Size")]
    [Tooltip("Desired display height in world-space meters. Width is calculated automatically from the video aspect ratio.")]
    public float physicalHeight = 1.0f;

    [Header("Playback")]
    public bool loop = true;
    public bool muteAudio = false;
    public float volume = 1f;

    VideoPlayer _videoPlayer;
    RenderTexture _renderTexture;
    bool _prepared = false;
    bool _preparing = false;
    AsyncOperationHandle<VideoClip> _addressableHandle;
    bool _handleValid = false;
    Coroutine _loadCoroutine;

    bool UsesAddressableSource =>
        addressableVideo != null && addressableVideo.RuntimeKeyIsValid();

    /// <summary>GUID of the default Addressable on the prefab (for video switcher sync).</summary>
    public string InitialAddressableGuid =>
        addressableVideo != null && addressableVideo.RuntimeKeyIsValid()
            ? addressableVideo.AssetGUID
            : string.Empty;

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

        // Source — priority: Addressable (remote) > VideoClip > StreamingAssets > raw URL
        // Addressable source is assigned asynchronously in LoadAddressableAndPlay(); skip here.
        if (addressableVideo != null && addressableVideo.RuntimeKeyIsValid())
        {
            Debug.Log("[VPC] Addressable video configured — source will be assigned after async download.");
        }
        else if (videoClip != null)
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
            Debug.LogWarning("[VideoPlayerController] No video source assigned. Set Addressable, VideoClip, StreamingAssets filename, or URL.", this);
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
        if (_loadCoroutine != null)
            StopCoroutine(_loadCoroutine);

        if (UsesAddressableSource)
            _loadCoroutine = StartCoroutine(LoadAddressableAndPlay());
        else
            _loadCoroutine = StartCoroutine(PrepareAndPlay());
    }

    void OnDisable()
    {
        if (_loadCoroutine != null)
        {
            StopCoroutine(_loadCoroutine);
            _loadCoroutine = null;
        }

        _videoPlayer.Stop();
        _prepared = false;
        _preparing = false;
        ReleaseAddressableHandle();
    }

    // Downloads the VideoClip via Addressables (remote bundle), assigns it to the
    // VideoPlayer, then hands off to the normal PrepareAndPlay() flow.
    IEnumerator LoadAddressableAndPlay()
    {
        // Wait until AddressablesInitializer finishes (init crash was blocking all loads).
        float waitStart = Time.realtimeSinceStartup;
        const float maxWaitForInit = 60f;
        while (!AddressablesInitializer.IsReady)
        {
            if (Time.realtimeSinceStartup - waitStart > maxWaitForInit)
            {
                Debug.LogError("[VPC] Timed out waiting for AddressablesInitializer.IsReady", this);
                yield break;
            }
            yield return null;
        }

        float loadStart = Time.realtimeSinceStartup;
        Debug.Log($"[VPC] Addressables: starting async load. key={addressableVideo.AssetGUID}");

        _addressableHandle = addressableVideo.LoadAssetAsync<VideoClip>();
        _handleValid = true;

        yield return _addressableHandle;

        float loadSeconds = Time.realtimeSinceStartup - loadStart;

        if (_addressableHandle.Status == AsyncOperationStatus.Succeeded)
        {
            VideoClip clip = _addressableHandle.Result;
            _videoPlayer.source = VideoSource.VideoClip;
            _videoPlayer.clip = clip;
            Debug.Log($"[VPC] Addressables: loaded '{clip.name}' in {loadSeconds:F1}s ({clip.width}x{clip.height})");
            yield return StartCoroutine(PrepareAndPlay());
        }
        else
        {
            Debug.LogError($"[VPC] Addressables: load FAILED after {loadSeconds:F1}s — {_addressableHandle.OperationException}", this);
        }

        _loadCoroutine = null;
    }

    void ReleaseAddressableHandle()
    {
        if (_handleValid)
        {
            Addressables.Release(_addressableHandle);
            _handleValid = false;
            Debug.Log("[VPC] Addressable handle released.");
        }
    }

    IEnumerator PrepareAndPlay()
    {
        bool hasSource = _videoPlayer.clip != null || !string.IsNullOrEmpty(_videoPlayer.url);
        if (!hasSource)
        {
            Debug.LogWarning("[VPC] PrepareAndPlay skipped — waiting for video source (Addressable still downloading?).");
            yield break;
        }

        Debug.Log($"[VPC] PrepareAndPlay. clip={(_videoPlayer.clip != null ? _videoPlayer.clip.name : "null")} url='{_videoPlayer.url}'");

        if (_prepared)
        {
            _videoPlayer.Play();
            Debug.Log("[VPC] Already prepared — calling Play()");
            yield break;
        }

        if (_preparing)
        {
            Debug.Log("[VPC] Already preparing — skipping duplicate call.");
            yield break;
        }

        _preparing = true;
        _videoPlayer.Prepare();
        Debug.Log("[VPC] Prepare() called — waiting...");

        float timeout = UsesAddressableSource ? 30f : 10f;
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
            Debug.LogError($"[VPC] Video NOT prepared after {timeout}s. clip={(_videoPlayer.clip != null ? _videoPlayer.clip.name : "null")}", this);
        }

        _loadCoroutine = null;
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

        // Apply correct aspect ratio scale to the quad
        ApplyAspectRatioToQuad((int)vp.width, (int)vp.height);

        // Confirm shader is correct
        if (targetRenderer != null)
        {
            string shaderName = targetRenderer.material.shader.name;
            Debug.Log($"[VPC] Material shader = '{shaderName}' — expected 'Custom/TransparentVideo'");
            if (shaderName != "Custom/TransparentVideo")
                Debug.LogWarning("[VPC] WRONG SHADER! Change material shader to Custom/TransparentVideo in Inspector.");
        }
    }

    // Sets the quad's localScale so the video fills physicalHeight meters tall
    // with width proportional to the actual video aspect ratio.
    void ApplyAspectRatioToQuad(int videoWidth, int videoHeight)
    {
        if (targetRenderer == null || videoHeight == 0) return;

        float aspect = (float)videoWidth / videoHeight;
        float w = physicalHeight * aspect;
        float h = physicalHeight;

        Transform t = targetRenderer.transform;
        t.localScale = new Vector3(w, h, t.localScale.z);
        Debug.Log($"[VPC] Quad scaled to {w:F3}m × {h:F3}m (video {videoWidth}×{videoHeight}, aspect {aspect:F3})");
    }

    // --- Public API ---

    /// <summary>No-op for Addressables — OnEnable runs LoadAddressableAndPlay.</summary>
    public void Play()
    {
        if (UsesAddressableSource)
            return;

        if (_loadCoroutine == null)
            _loadCoroutine = StartCoroutine(PrepareAndPlay());
    }
    public void Pause() => _videoPlayer.Pause();

    public void Stop()
    {
        _videoPlayer.Stop();
        _prepared = false;
        _preparing = false;
    }

    /// <summary>
    /// Loads a different remote Addressable video at runtime (video switching requirement).
    /// </summary>
    public void SwitchToAddressable(AssetReferenceT<VideoClip> newReference)
    {
        if (newReference == null || !newReference.RuntimeKeyIsValid())
        {
            Debug.LogWarning("[VPC] SwitchToAddressable: invalid AssetReference.", this);
            return;
        }

        if (addressableVideo != null && addressableVideo.AssetGUID == newReference.AssetGUID && _videoPlayer.isPlaying)
        {
            Debug.Log($"[VPC] Already playing GUID {newReference.AssetGUID} — skip switch.");
            return;
        }

        if (_loadCoroutine != null)
            StopCoroutine(_loadCoroutine);

        _loadCoroutine = StartCoroutine(SwitchToAddressableCoroutine(newReference));
    }

    IEnumerator SwitchToAddressableCoroutine(AssetReferenceT<VideoClip> newReference)
    {
        Debug.Log($"[VPC] Switch start → GUID {newReference.AssetGUID}");

        _videoPlayer.Stop();
        _prepared = false;
        _preparing = false;
        ReleaseAddressableHandle();

        yield return null;

        float waitStart = Time.realtimeSinceStartup;
        while (!AddressablesInitializer.IsReady)
        {
            if (Time.realtimeSinceStartup - waitStart > 60f)
            {
                Debug.LogError("[VPC] Switch aborted — Addressables not ready.", this);
                yield break;
            }
            yield return null;
        }

        float loadStart = Time.realtimeSinceStartup;
        _addressableHandle = newReference.LoadAssetAsync<VideoClip>();
        _handleValid = true;
        yield return _addressableHandle;

        float loadSeconds = Time.realtimeSinceStartup - loadStart;

        if (_addressableHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"[VPC] Switch load FAILED in {loadSeconds:F1}s — {_addressableHandle.OperationException}", this);
            _loadCoroutine = null;
            yield break;
        }

        VideoClip clip = _addressableHandle.Result;
        addressableVideo = newReference;

        _videoPlayer.source = VideoSource.VideoClip;
        _videoPlayer.url = string.Empty;
        _videoPlayer.clip = clip;

        Debug.Log($"[VPC] Switch loaded '{clip.name}' in {loadSeconds:F1}s — preparing...");

        yield return PrepareAndPlay();
        _loadCoroutine = null;
    }

    public bool IsPlaying => _videoPlayer != null && _videoPlayer.isPlaying;

    void OnDestroy()
    {
        _videoPlayer.prepareCompleted -= OnPrepareCompleted;
        ReleaseAddressableHandle();

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
    }
}
