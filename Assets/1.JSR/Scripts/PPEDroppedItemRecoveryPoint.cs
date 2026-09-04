using UnityEngine;

/// <summary>
/// Authored destination and floor-relative thresholds for PPE that remains
/// released below comfortable reach. Resolved once when a released PPE starts
/// monitoring; no scene-wide per-frame search is used.
/// </summary>
[DisallowMultipleComponent]
public sealed class PPEDroppedItemRecoveryPoint : MonoBehaviour
{
    [SerializeField, Range(0.18f, 0.25f)]
    [Tooltip("A released PPE is unreachable when its lowest physical point stays below floor plus this height.")]
    float minimumReachableHeight = 0.22f;

    [SerializeField, Range(1.5f, 2f)]
    float recoveryDelay = 1.75f;

    [SerializeField, Range(0.05f, 0.25f)]
    float checkInterval = 0.10f;

    [SerializeField, Range(0.65f, 0.85f)]
    [Tooltip("Authored recovery-point height above the room floor. Used only if an active XR tracking origin is unavailable.")]
    float authoredHeightAboveFloor = 0.75f;

    static PPEDroppedItemRecoveryPoint cached;

    public float RecoveryDelay => recoveryDelay;
    public float CheckInterval => checkInterval;
    public float MinimumReachableHeight => minimumReachableHeight;

    public static PPEDroppedItemRecoveryPoint Resolve()
    {
        if (cached != null && cached.gameObject.scene.IsValid())
            return cached;
        cached = FindAnyObjectByType<PPEDroppedItemRecoveryPoint>(FindObjectsInactive.Include);
        return cached;
    }

    public bool IsBelowReachableHeight(float lowestPhysicalPointY)
    {
        return lowestPhysicalPointY < ResolveFloorHeight() + minimumReachableHeight;
    }

    float ResolveFloorHeight()
    {
        var origin = PPEActiveXrOrigin.ResolveOriginTransform();
        if (origin != null && origin.gameObject.activeInHierarchy)
            return origin.position.y;
        return transform.position.y - authoredHeightAboveFloor;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        var floorY = ResolveFloorHeight();
        var center = new Vector3(transform.position.x, floorY + minimumReachableHeight, transform.position.z);
        Gizmos.color = new Color(1f, 0.78f, 0.15f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.05f);
        Gizmos.DrawLine(transform.position, new Vector3(transform.position.x, floorY, transform.position.z));
        Gizmos.color = new Color(1f, 0.35f, 0.15f, 0.65f);
        Gizmos.DrawWireCube(center, new Vector3(0.45f, 0.01f, 0.45f));
    }
#endif
}
