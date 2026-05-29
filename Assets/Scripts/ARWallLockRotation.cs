using UnityEngine;

/// <summary>
/// Keeps the video panel upright and flush even if the AR plane mesh is slightly tilted.
/// </summary>
public class ARWallLockRotation : MonoBehaviour
{
    Quaternion _lockedWorldRotation;
    Vector3 _localOffset;

    public void Init(Quaternion worldRotation, Vector3 localOffsetFromAnchor)
    {
        _lockedWorldRotation = worldRotation;
        _localOffset = localOffsetFromAnchor;
    }

    void LateUpdate()
    {
        transform.rotation = _lockedWorldRotation;
        if (transform.parent != null)
            transform.position = transform.parent.TransformPoint(_localOffset);
    }
}
