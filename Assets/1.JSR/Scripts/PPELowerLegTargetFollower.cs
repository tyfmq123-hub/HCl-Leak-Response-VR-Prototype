using UnityEngine;

/// <summary>
/// Placement target for paired boots. Follows the dynamic lower-leg proxy on
/// <see cref="PPEPlayerBodyReference"/> (body yaw + reachable shin height).
/// Not real foot tracking. Does not inherit HMD pitch.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public sealed class PPELowerLegTargetFollower : MonoBehaviour
{
    [SerializeField] PPEGrabHand side = PPEGrabHand.Left;
    [SerializeField] PPEPlayerBodyReference bodyReference;
    [SerializeField] Transform footAnchor;
    [SerializeField, Min(0f)] float heightAboveFoot = 0.38f;

    public PPEGrabHand Side => side;
    public bool IsTracking =>
        bodyReference != null
            ? bodyReference.HasTracking
            : footAnchor != null;

    public void Configure(
        PPEGrabHand targetSide,
        Transform foot,
        float height,
        PPEPlayerBodyReference body = null)
    {
        side = targetSide;
        footAnchor = foot;
        heightAboveFoot = Mathf.Max(0f, height);
        bodyReference = body;
    }

    void LateUpdate()
    {
        if (bodyReference == null)
            bodyReference = FindAnyObjectByType<PPEPlayerBodyReference>(FindObjectsInactive.Include);

        if (bodyReference != null &&
            bodyReference.TryGetLowerLegWorldPose(side, out var position, out var rotation))
        {
            transform.SetPositionAndRotation(position, rotation);
            return;
        }

        if (footAnchor == null)
            return;

        var yaw = Quaternion.Euler(0f, footAnchor.eulerAngles.y, 0f);
        transform.SetPositionAndRotation(
            footAnchor.position + Vector3.up * heightAboveFoot,
            yaw);
    }
}
