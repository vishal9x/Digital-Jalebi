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

    public GameObject wallPrefab;

    // Assign a Quad GameObject in the Inspector to act as placement preview
    public GameObject quadPreview;

    public TMP_Text instructionText;

    [SerializeField] float previewPositionLerp = 12f;
    [SerializeField] float previewRotationLerp = 12f;
    [SerializeField] float wallOffsetFromPlane = 0.015f;
    [SerializeField] bool allowReposition = true;

    List<ARRaycastHit> hits =
        new List<ARRaycastHit>();

    bool canPlace = false;
    Pose lastValidPose;
    ARPlane lastValidPlane;
    GameObject placedWallInstance;
    ARAnchor placedAnchor;

    void Start()
    {
        if (instructionText != null)
        {
            instructionText.text =
                "Scan wall and tap";
        }

        if (quadPreview != null)
            quadPreview.SetActive(false);
    }

    void Update()
    {
        // Raycast from screen center every frame to show a stable preview on vertical walls.
        Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);

        canPlace = TryGetVerticalPose(screenCenter, out lastValidPose, out lastValidPlane);

        if (canPlace)
        {
            if (quadPreview != null)
            {
                quadPreview.SetActive(true);

                float tPos = 1f - Mathf.Exp(-previewPositionLerp * Time.deltaTime);
                float tRot = 1f - Mathf.Exp(-previewRotationLerp * Time.deltaTime);
                Quaternion targetRotation = ComputeWallAlignedRotation(lastValidPose.position, lastValidPlane);

                quadPreview.transform.position = Vector3.Lerp(quadPreview.transform.position, lastValidPose.position, tPos);
                quadPreview.transform.rotation = Quaternion.Slerp(quadPreview.transform.rotation, targetRotation, tRot);
            }

            if (instructionText != null)
                instructionText.text = "Wall detected - tap to place";
        }

        if (!canPlace)
        {
            if (quadPreview != null)
                quadPreview.SetActive(false);

            if (instructionText != null)
                instructionText.text = "Scan wall and tap";
        }

        if (TryGetTapPosition(out Vector2 tapPos))
            PlaceWall(tapPos);
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

    bool TryGetVerticalPose(Vector2 screenPos, out Pose pose, out ARPlane plane)
    {
        if (raycastManager == null)
        {
            pose = default;
            plane = null;
            return false;
        }

        if (raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon))
        {
            for (int i = 0; i < hits.Count; i++)
            {
                ARPlane hitPlane = hits[i].trackable as ARPlane;

                if (hitPlane == null)
                    continue;

                if (hitPlane.alignment != PlaneAlignment.Vertical)
                    continue;

                if (hitPlane.trackingState != TrackingState.Tracking)
                    continue;

                pose = hits[i].pose;
                plane = hitPlane;
                return true;
            }
        }

        pose = default;
        plane = null;
        return false;
    }

    void PlaceWall(Vector2 tapPos)
    {
        if (wallPrefab == null)
        {
            Debug.LogError("wallPrefab is not assigned in the Inspector!");
            return;
        }

        if (!TryGetVerticalPose(tapPos, out Pose tapPose, out ARPlane tapPlane))
        {
            if (instructionText != null)
                instructionText.text = "Tap directly on detected wall";

            return;
        }

        Quaternion targetRotation = ComputeWallAlignedRotation(tapPose.position, tapPlane);
        Vector3 offsetDirection = targetRotation * Vector3.forward;
        Vector3 targetPosition = tapPose.position + offsetDirection * wallOffsetFromPlane;

        Vector3 euler = targetRotation.eulerAngles;
        Debug.Log(
            "Wall placement rotation => quaternion: " + targetRotation +
            " | euler: " + euler +
            " | plane normal: " + tapPlane.normal);

        if (placedWallInstance != null && !allowReposition)
        {
            if (instructionText != null)
                instructionText.text = "Wall already placed";

            return;
        }

        if (placedWallInstance != null)
            Destroy(placedWallInstance);

        if (placedAnchor != null)
            Destroy(placedAnchor.gameObject);

        if (anchorManager != null)
        {
            ARAnchor anchor = anchorManager.AttachAnchor(tapPlane, tapPose);

            if (anchor != null)
            {
                placedAnchor = anchor;
                placedWallInstance = Instantiate(wallPrefab, targetPosition, targetRotation, anchor.transform);
            }
            else
            {
                placedAnchor = null;
                placedWallInstance = Instantiate(wallPrefab, targetPosition, targetRotation);
            }
        }
        else
        {
            placedAnchor = null;
            placedWallInstance = Instantiate(wallPrefab, targetPosition, targetRotation);
        }

        // Start video playback on placed wall
        VideoPlayerController vpc = placedWallInstance.GetComponentInChildren<VideoPlayerController>();
        if (vpc != null)
            vpc.Play();

        if (quadPreview != null)
            quadPreview.SetActive(false);

        if (instructionText != null)
            instructionText.text = "Wall placed! Tap another wall to move";
    }

    Quaternion ComputeWallAlignedRotation(Vector3 hitPosition, ARPlane plane)
    {
        if (plane == null)
            return Quaternion.identity;

        Vector3 wallNormal = plane.normal.normalized;
        Vector3 flatNormal = Vector3.ProjectOnPlane(wallNormal, Vector3.up);

        if (flatNormal.sqrMagnitude > 0.0001f)
            wallNormal = flatNormal.normalized;

        if (Camera.main != null)
        {
            Vector3 toCamera = (Camera.main.transform.position - hitPosition).normalized;
            if (Vector3.Dot(wallNormal, toCamera) < 0f)
                wallNormal = -wallNormal;
        }

        return Quaternion.LookRotation(wallNormal, Vector3.up);
    }
}