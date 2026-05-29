using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class ARWallPlacement : MonoBehaviour
{
    public ARRaycastManager raycastManager;
    public ARAnchorManager anchorManager;
    public ARPlaneManager planeManager;

    public GameObject wallPrefab;
    public RemoteVideoSwitcher videoSwitcher;
    public GameObject quadPreview;
    public TMP_Text instructionText;

    [Header("Tuning")]
    [SerializeField] float previewPositionLerp = 12f;
    [SerializeField] float previewRotationLerp = 12f;
    [SerializeField] float wallOffsetFromPlane = 0.02f;
    [SerializeField] bool allowReposition = true;
    [SerializeField] float placementCooldown = 3f;
    [SerializeField] float minPlaneArea = 0.4f;
    [SerializeField] float maxWallNormalY = 0.3f;
    [Tooltip("Reject walls tilted more than this (degrees) from vertical.")]
    [SerializeField] float maxPlaneTiltDegrees = 20f;
    [SerializeField] bool hidePlaneMeshesAfterPlace = true;

    readonly List<ARRaycastHit> _hits = new List<ARRaycastHit>();

    bool canPlace;
    Pose lastValidPose;
    ARPlane lastValidPlane;
    GameObject placedWallInstance;
    ARAnchor placedAnchor;
    float _lastPlaceTime = -10f;
    ARVerticalPlaneFilter _planeFilter;

    public VideoPlayerController ActiveVideoController { get; private set; }

    void Awake()
    {
        if (planeManager == null)
            planeManager = FindObjectOfType<ARPlaneManager>();

        if (planeManager != null)
        {
            planeManager.requestedDetectionMode = PlaneDetectionMode.Vertical;
            _planeFilter = planeManager.GetComponent<ARVerticalPlaneFilter>();
            if (_planeFilter == null)
                _planeFilter = planeManager.gameObject.AddComponent<ARVerticalPlaneFilter>();
        }

        if (raycastManager == null)
            raycastManager = FindObjectOfType<ARRaycastManager>();
    }

    void Start()
    {
        SetInstruction(
            "Step 1: Stand 1 m from a plain wall\n" +
            "Step 2: Move phone slowly side to side");
        if (quadPreview != null)
            quadPreview.SetActive(false);
    }

    void Update()
    {
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        canPlace = TryGetBestVerticalWall(screenCenter, out lastValidPose, out lastValidPlane);

        if (canPlace && quadPreview != null)
        {
            quadPreview.SetActive(true);
            float tPos = 1f - Mathf.Exp(-previewPositionLerp * Time.deltaTime);
            float tRot = 1f - Mathf.Exp(-previewRotationLerp * Time.deltaTime);
            Pose displayPose = BuildWallPose(lastValidPose.position, lastValidPlane);

            quadPreview.transform.position = Vector3.Lerp(quadPreview.transform.position, displayPose.position, tPos);
            quadPreview.transform.rotation = Quaternion.Slerp(quadPreview.transform.rotation, displayPose.rotation, tRot);
            SetInstruction("Wall detected!\nTap anywhere to place video");
        }
        else
        {
            if (quadPreview != null)
                quadPreview.SetActive(false);
            SetInstruction(GetScanHint());
        }

        if (TryGetTapPosition(out Vector2 tapPos))
            PlaceWall(tapPos);
    }

    string GetScanHint()
    {
        if (CountTrackedVerticalPlanes() == 0)
        {
            return "Scanning wall...\n" +
                   "• Use a plain wall (not window)\n" +
                   "• Turn on room lights\n" +
                   "• Move phone slowly left ↔ right";
        }

        return "Point phone at the wall\n" +
               "(middle of the screen)\n" +
               "Then tap to place";
    }

    int CountTrackedVerticalPlanes()
    {
        if (planeManager == null)
            return 0;

        int n = 0;
        foreach (ARPlane plane in planeManager.trackables)
        {
            if (IsValidWallPlane(plane))
                n++;
        }
        return n;
    }

    bool TryGetTapPosition(out Vector2 screenPos)
    {
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
            return true;
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPos = Mouse.current.position.ReadValue();
            return true;
        }

        screenPos = default;
        return false;
    }

    bool IsValidWallPlane(ARPlane plane)
    {
        if (plane == null || plane.trackingState != TrackingState.Tracking)
            return false;

        if (plane.alignment != PlaneAlignment.Vertical)
            return false;

        if (Mathf.Abs(plane.normal.y) > maxWallNormalY)
            return false;

        if (!IsPlaneUprightEnough(plane))
            return false;

        if (plane.size.x * plane.size.y < minPlaneArea)
            return false;

        if (_planeFilter != null && !_planeFilter.IsWallPlane(plane))
            return false;

        return true;
    }

    bool IsPlaneUprightEnough(ARPlane plane)
    {
        Vector3 flatNormal = new Vector3(plane.normal.x, 0f, plane.normal.z);
        if (flatNormal.sqrMagnitude < 0.0001f)
            return false;

        float tilt = Vector3.Angle(plane.normal, flatNormal.normalized);
        return tilt <= maxPlaneTiltDegrees;
    }

    bool TryGetBestVerticalWall(Vector2 screenPos, out Pose pose, out ARPlane plane)
    {
        pose = default;
        plane = null;

        if (raycastManager == null)
            return false;

        if (!raycastManager.Raycast(screenPos, _hits, TrackableType.PlaneWithinPolygon))
            return false;

        ARPlane bestPlane = null;
        Vector3 bestHitPoint = default;
        float bestScore = 0f;

        for (int i = 0; i < _hits.Count; i++)
        {
            ARPlane hitPlane = _hits[i].trackable as ARPlane;
            if (!IsValidWallPlane(hitPlane))
                continue;

            float area = hitPlane.size.x * hitPlane.size.y;
            float distToCenter = Vector2.Distance(screenPos, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            float score = area - distToCenter * 0.001f;

            if (score > bestScore)
            {
                bestScore = score;
                bestPlane = hitPlane;
                bestHitPoint = _hits[i].pose.position;
            }
        }

        if (bestPlane == null)
            return false;

        plane = bestPlane;
        pose = BuildWallPose(bestHitPoint, bestPlane);
        return true;
    }

    Pose BuildWallPose(Vector3 surfacePoint, ARPlane wallPlane)
    {
        Quaternion rotation = ComputeWallAlignedRotation(surfacePoint, wallPlane);
        Vector3 offset = rotation * Vector3.forward * wallOffsetFromPlane;
        return new Pose(surfacePoint + offset, rotation);
    }

    void PlaceWall(Vector2 tapPos)
    {
        if (wallPrefab == null)
        {
            Debug.LogError("[ARWall] wallPrefab not assigned.");
            return;
        }

        if (Time.time - _lastPlaceTime < placementCooldown)
        {
            Debug.Log("[ARWall] Cooldown — wait before placing again.");
            return;
        }

        if (!AddressablesInitializer.IsReady)
        {
            SetInstruction("Loading videos...\nPlease wait a few seconds");
            return;
        }

        if (!TryGetBestVerticalWall(tapPos, out Pose wallPose, out ARPlane tapPlane))
        {
            SetInstruction("Couldn't find a wall there.\nTry tapping the plain wall area");
            return;
        }

        _lastPlaceTime = Time.time;

        Quaternion rotation = wallPose.rotation;
        Vector3 surfacePoint = wallPose.position - rotation * Vector3.forward * wallOffsetFromPlane;
        Pose anchorPose = new Pose(surfacePoint, rotation);

        Debug.Log($"[ARWall] Place area={tapPlane.size.x:F2}x{tapPlane.size.y:F2} normal={tapPlane.normal} tiltOk");

        if (placedWallInstance != null && !allowReposition)
        {
            SetInstruction("Video already placed");
            return;
        }

        if (placedWallInstance != null)
            Destroy(placedWallInstance);

        ActiveVideoController = null;

        if (placedAnchor != null)
            Destroy(placedAnchor.gameObject);

        if (anchorManager != null)
        {
            placedAnchor = anchorManager.AttachAnchor(tapPlane, anchorPose);
            Transform parent = placedAnchor != null ? placedAnchor.transform : null;
            placedWallInstance = parent != null
                ? Instantiate(wallPrefab, parent)
                : Instantiate(wallPrefab, wallPose.position, wallPose.rotation);

            if (parent != null)
            {
                placedWallInstance.transform.localRotation = Quaternion.identity;
                var lockRot = placedWallInstance.GetComponent<ARWallLockRotation>();
                if (lockRot == null)
                    lockRot = placedWallInstance.AddComponent<ARWallLockRotation>();
                lockRot.Init(rotation, Vector3.forward * wallOffsetFromPlane);
            }
        }
        else
        {
            placedWallInstance = Instantiate(wallPrefab, wallPose.position, wallPose.rotation);
        }

        ActiveVideoController = placedWallInstance.GetComponentInChildren<VideoPlayerController>();

        if (videoSwitcher == null)
            videoSwitcher = FindObjectOfType<RemoteVideoSwitcher>();

        if (videoSwitcher != null && ActiveVideoController != null)
            videoSwitcher.OnWallPlaced(ActiveVideoController);

        if (quadPreview != null)
            quadPreview.SetActive(false);

        if (hidePlaneMeshesAfterPlace)
            SetPlaneMeshesVisible(false);

        SetInstruction("Loading video...\nPlease wait — don't tap again");
    }

    void SetPlaneMeshesVisible(bool visible)
    {
        if (planeManager == null)
            return;

        foreach (ARPlane plane in planeManager.trackables)
        {
            if (plane != null)
                plane.gameObject.SetActive(visible);
        }
    }

    Quaternion ComputeWallAlignedRotation(Vector3 hitPosition, ARPlane plane)
    {
        // AR Foundation: vertical plane normal = transform.up
        Vector3 normal = plane.transform.up.normalized;

        Vector3 flat = new Vector3(normal.x, 0f, normal.z);
        if (flat.sqrMagnitude > 0.0001f)
            normal = flat.normalized;

        if (Camera.main != null)
        {
            Vector3 toCamera = (Camera.main.transform.position - hitPosition).normalized;
            if (Vector3.Dot(normal, toCamera) < 0f)
                normal = -normal;
        }

        return Quaternion.LookRotation(normal, Vector3.up);
    }

    void SetInstruction(string msg)
    {
        if (instructionText != null)
            instructionText.text = msg;
    }
}
