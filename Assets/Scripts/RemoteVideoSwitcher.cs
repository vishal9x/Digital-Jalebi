using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Video;

/// <summary>
/// Switches between remote Addressable videos on the placed wall panel.
/// Wire UI Button OnClick to PlayNext / PlayPrevious (Inspector only — do not also assign nextButton field).
/// </summary>
public class RemoteVideoSwitcher : MonoBehaviour
{
    [Header("Configuration")]
    public RemoteVideoCatalog catalog;
    public ARWallPlacement wallPlacement;

    [Header("UI (optional)")]
    public TMP_Text videoNameLabel;

    [Header("Startup")]
    public bool waitForAddressablesReady = true;

    int _currentIndex;

    public int CurrentIndex => _currentIndex;

    /// <summary>Call when wall is placed so index matches the prefab's default video.</summary>
    public void OnWallPlaced(VideoPlayerController controller)
    {
        if (catalog == null || controller == null)
            return;

        // Match catalog index to whatever clip the wall prefab is configured to load first.
        string guid = controller.InitialAddressableGuid;
        int match = catalog.FindIndexByGuid(guid);
        _currentIndex = match >= 0 ? match : 0;
        UpdateUi();
        Debug.Log($"[VideoSwitcher] Wall placed — catalog index {_currentIndex} ({catalog.GetDisplayName(_currentIndex)})");
    }

    public void PlayNext()
    {
        if (!TryStepIndex(1, out int newIndex))
            return;

        _currentIndex = newIndex;
        PlayCurrent();
    }

    public void PlayPrevious()
    {
        if (!TryStepIndex(-1, out int newIndex))
            return;

        _currentIndex = newIndex;
        PlayCurrent();
    }

    public void PlayVideoAtIndex(int index)
    {
        if (catalog == null || !catalog.TryGetEntry(index, out _))
        {
            Debug.LogWarning($"[VideoSwitcher] Invalid index {index}.");
            return;
        }

        _currentIndex = index;
        PlayCurrent();
    }

    bool TryStepIndex(int direction, out int newIndex)
    {
        newIndex = _currentIndex;
        if (catalog == null || catalog.Count == 0)
        {
            Debug.LogWarning("[VideoSwitcher] No catalog assigned.");
            return false;
        }

        int validCount = catalog.ValidEntryCount;
        if (validCount < 2)
        {
            Debug.LogWarning($"[VideoSwitcher] Need 2+ valid videos in catalog (have {validCount}).");
            return false;
        }

        int attempts = catalog.Count;
        int idx = _currentIndex;
        while (attempts-- > 0)
        {
            idx = (idx + direction + catalog.Count) % catalog.Count;
            if (catalog.TryGetEntry(idx, out _))
            {
                newIndex = idx;
                return true;
            }
        }

        Debug.LogWarning("[VideoSwitcher] No valid Addressable entry found in catalog.");
        return false;
    }

    void PlayCurrent()
    {
        if (!catalog.TryGetEntry(_currentIndex, out RemoteVideoCatalog.Entry entry))
        {
            Debug.LogWarning($"[VideoSwitcher] Entry {_currentIndex} missing or invalid Addressable ref.");
            return;
        }

        if (waitForAddressablesReady && !AddressablesInitializer.IsReady)
        {
            Debug.LogWarning("[VideoSwitcher] Addressables not ready — wait for [ADDR] Initialised OK.");
            return;
        }

        VideoPlayerController vpc = FindActiveVideoController();
        if (vpc == null)
        {
            Debug.LogWarning("[VideoSwitcher] Place a wall first, then switch videos.");
            return;
        }

        Debug.Log($"[VideoSwitcher] Switching to index {_currentIndex}: '{catalog.GetDisplayName(_currentIndex)}' GUID={entry.video.AssetGUID}");
        vpc.SwitchToAddressable(entry.video);
        UpdateUi();
    }

    VideoPlayerController FindActiveVideoController()
    {
        if (wallPlacement != null && wallPlacement.ActiveVideoController != null)
            return wallPlacement.ActiveVideoController;

        return FindObjectOfType<VideoPlayerController>();
    }

    void UpdateUi()
    {
        if (videoNameLabel != null && catalog != null && catalog.Count > 0)
            videoNameLabel.text = catalog.GetDisplayName(_currentIndex);
    }
}
