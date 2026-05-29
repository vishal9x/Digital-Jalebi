using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Hides horizontal / junk planes so only vertical walls are visible during scanning.
/// Attach next to ARPlaneManager on XR Origin.
/// </summary>
[RequireComponent(typeof(ARPlaneManager))]
public class ARVerticalPlaneFilter : MonoBehaviour
{
    [Tooltip("Hide planes whose |normal.y| is above this (floor/ceiling/table).")]
    [Range(0.1f, 0.9f)]
    public float maxVerticalNormalY = 0.35f;

    [Tooltip("Hide planes smaller than this area (m²).")]
    public float minVisibleArea = 0.15f;

    ARPlaneManager _planeManager;

    void Awake()
    {
        _planeManager = GetComponent<ARPlaneManager>();
    }

    void OnEnable()
    {
        _planeManager.planesChanged += OnPlanesChanged;
        _planeManager.requestedDetectionMode = PlaneDetectionMode.Vertical;
        Debug.Log("[ARPlane] Detection mode set to Vertical only.");
    }

    void OnDisable()
    {
        if (_planeManager != null)
            _planeManager.planesChanged -= OnPlanesChanged;
    }

    void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        ApplyFilter(args.added);
        ApplyFilter(args.updated);

        foreach (ARPlane plane in args.removed)
        {
            if (plane != null)
                plane.gameObject.SetActive(false);
        }
    }

    void ApplyFilter(System.Collections.Generic.List<ARPlane> planes)
    {
        foreach (ARPlane plane in planes)
            SetPlaneVisible(plane, IsWallPlane(plane));
    }

    public bool IsWallPlane(ARPlane plane)
    {
        if (plane == null || plane.trackingState != TrackingState.Tracking)
            return false;

        if (plane.alignment != PlaneAlignment.Vertical)
            return false;

        if (Mathf.Abs(plane.normal.y) > maxVerticalNormalY)
            return false;

        float area = plane.size.x * plane.size.y;
        return area >= minVisibleArea;
    }

    void SetPlaneVisible(ARPlane plane, bool visible)
    {
        if (plane == null)
            return;

        plane.gameObject.SetActive(visible);
    }
}
