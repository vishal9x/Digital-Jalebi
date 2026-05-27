using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles touch gestures on the placed AR wall video panel:
///   • Two-finger pinch  → scale the panel (clamped to min/max)
///   • Two-finger twist  → rotate the panel in-plane (around its wall-normal axis)
///
/// Attach this component to the wall prefab (same root as VideoPlayerController).
/// No extra setup required — it uses Unity's new Input System automatically.
/// </summary>
public class ARWallInteraction : MonoBehaviour
{
    [Header("Scale")]
    [Tooltip("Minimum uniform scale multiplier the user can pinch down to.")]
    public float minScale = 0.3f;
    [Tooltip("Maximum uniform scale multiplier the user can pinch up to.")]
    public float maxScale = 3.0f;

    [Header("Rotation")]
    [Tooltip("Allow two-finger twist to rotate the panel around its wall-normal axis.")]
    public bool allowRotation = true;
    [Tooltip("Rotation speed multiplier applied to the detected twist angle.")]
    public float rotationSensitivity = 1.0f;

    // Previous-frame state
    float _prevPinchDist = -1f;
    float _prevTwistAngle = 0f;
    bool _gestureActive = false;

    void Update()
    {
        Touchscreen ts = Touchscreen.current;
        if (ts == null)
        {
            ResetGestureState();
            return;
        }

        // Collect up to 2 active touch positions
        Vector2 pos0 = Vector2.zero, pos1 = Vector2.zero;
        int activeCount = 0;

        foreach (var touch in ts.touches)
        {
            if (touch.press.isPressed)
            {
                if (activeCount == 0) pos0 = touch.position.ReadValue();
                else if (activeCount == 1) pos1 = touch.position.ReadValue();
                activeCount++;

                if (activeCount >= 2) break;
            }
        }

        if (activeCount < 2)
        {
            ResetGestureState();
            return;
        }

        float currentDist = Vector2.Distance(pos0, pos1);
        float currentAngle = Mathf.Atan2(pos1.y - pos0.y, pos1.x - pos0.x) * Mathf.Rad2Deg;

        if (_gestureActive)
        {
            // --- Pinch to scale ---
            if (_prevPinchDist > 0f)
            {
                float scaleDelta = currentDist / _prevPinchDist;
                Vector3 current = transform.localScale;
                float newX = Mathf.Clamp(current.x * scaleDelta, minScale, maxScale);
                // Keep uniform scale: derive the ratio that clamps correctly
                float ratio = newX / current.x;
                transform.localScale = current * ratio;
            }

            // --- Twist to rotate ---
            if (allowRotation && _prevPinchDist > 0f)
            {
                float twistDelta = Mathf.DeltaAngle(_prevTwistAngle, currentAngle) * rotationSensitivity;
                // Rotate around the wall-normal axis (local forward = wall normal pointing toward camera)
                transform.Rotate(transform.forward, -twistDelta, Space.World);
            }
        }

        _prevPinchDist = currentDist;
        _prevTwistAngle = currentAngle;
        _gestureActive = true;
    }

    void ResetGestureState()
    {
        _prevPinchDist = -1f;
        _gestureActive = false;
    }

    void OnDisable()
    {
        ResetGestureState();
    }
}
