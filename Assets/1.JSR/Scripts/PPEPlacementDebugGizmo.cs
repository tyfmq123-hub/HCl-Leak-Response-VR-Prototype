using UnityEngine;

/// <summary>
/// Optional Scene-view diagnostics for PPE placement. All drawing code is
/// Editor-only, so builds keep only the disabled configuration component.
/// </summary>
[DisallowMultipleComponent]
public sealed class PPEPlacementDebugGizmo : MonoBehaviour
{
    static readonly PPEEquipSlot[] ConcreteSlots =
    {
        PPEEquipSlot.Head,
        PPEEquipSlot.Face,
        PPEEquipSlot.LeftHand,
        PPEEquipSlot.RightHand,
        PPEEquipSlot.Body,
        PPEEquipSlot.LeftFoot,
        PPEEquipSlot.RightFoot,
        PPEEquipSlot.Radio,
        PPEEquipSlot.GasDetector,
    };

    [Header("Debug Toggle")]
    [SerializeField]
    [Tooltip("Master switch for Scene-view PPE placement diagnostics.")]
    bool drawDebugGizmos;

    [SerializeField] bool drawWhenNotPlaying = true;
    [SerializeField] bool showAllAnchors = true;
    [SerializeField] bool showLabels = true;

    [Header("Sources")]
    [SerializeField] PPEPlacementGuideManager placementManager;
    [SerializeField] PPEEquipmentAnchorSet anchorSet;

    [Header("Appearance")]
    [SerializeField, Min(0.005f)] float anchorRadius = 0.035f;
    [SerializeField] Color anchorColor = new Color(0.1f, 0.8f, 1f, 0.9f);
    [SerializeField] Color validColor = new Color(0.15f, 1f, 0.25f, 0.95f);
    [SerializeField] Color invalidColor = new Color(1f, 0.2f, 0.1f, 0.95f);

    public bool DrawDebugGizmos => drawDebugGizmos;
    public PPEPlacementGuideManager PlacementManager => placementManager;
    public PPEEquipmentAnchorSet AnchorSet => anchorSet;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!drawDebugGizmos || (!Application.isPlaying && !drawWhenNotPlaying))
            return;

        ResolveSources();
        if (anchorSet == null)
            return;

        var current = placementManager != null ? placementManager.ActivePpe : null;
        var targetSlots = current != null ? current.EquipSlots : PPEEquipSlot.None;
        var anyValid = false;

        for (var i = 0; i < ConcreteSlots.Length; i++)
        {
            var slot = ConcreteSlots[i];
            if (!anchorSet.TryGetAnchor(slot, out var anchor) || anchor == null)
                continue;

            var isTarget = current != null && current.UsesSlot(slot);
            if (!showAllAnchors && !isTarget)
                continue;

            var color = anchorColor;
            var valid = false;
            var distance = float.PositiveInfinity;
            var angle = float.PositiveInfinity;
            if (isTarget)
            {
                distance = Vector3.Distance(current.transform.position, anchor.position);
                angle = Quaternion.Angle(current.transform.rotation, anchor.rotation);
                valid = distance <= current.EquipDistance && angle <= current.EquipAngleTolerance;
                anyValid |= valid;
                color = valid ? validColor : invalidColor;

                Gizmos.color = color;
                Gizmos.DrawWireSphere(anchor.position, current.EquipDistance);
                Gizmos.DrawLine(current.transform.position, anchor.position);
            }

            DrawAnchorAxes(anchor, color);
            Gizmos.color = color;
            Gizmos.DrawSphere(anchor.position, anchorRadius);

            if (!showLabels)
                continue;

            var label = isTarget
                ? $"{slot}  {(valid ? "VALID" : "INVALID")}\n" +
                  $"d={distance:0.000}/{current.EquipDistance:0.000}m  " +
                  $"a={angle:0.0}/{current.EquipAngleTolerance:0.0}°"
                : slot.ToString();
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.Label(anchor.position + Vector3.up * (anchorRadius * 1.8f), label);
        }

        if (!showLabels)
            return;

        var status = current == null
            ? "Current PPE: None\nTarget Slot: None\nPlacement: INACTIVE"
            : $"Current PPE: {current.ItemId}\nTarget Slot: {targetSlots}\n" +
              $"Placement: {(anyValid ? "VALID" : "INVALID")}";
        var labelPosition = current != null
            ? current.transform.position + Vector3.up * 0.18f
            : transform.position + Vector3.up * 0.15f;
        UnityEditor.Handles.color = current == null ? anchorColor : anyValid ? validColor : invalidColor;
        UnityEditor.Handles.Label(labelPosition, status);
    }

    static void DrawAnchorAxes(Transform anchor, Color baseColor)
    {
        const float axisLength = 0.08f;
        Gizmos.color = new Color(1f, 0.2f, 0.2f, baseColor.a);
        Gizmos.DrawLine(anchor.position, anchor.position + anchor.right * axisLength);
        Gizmos.color = new Color(0.2f, 1f, 0.2f, baseColor.a);
        Gizmos.DrawLine(anchor.position, anchor.position + anchor.up * axisLength);
        Gizmos.color = new Color(0.2f, 0.45f, 1f, baseColor.a);
        Gizmos.DrawLine(anchor.position, anchor.position + anchor.forward * axisLength);
    }

    void ResolveSources()
    {
        if (placementManager == null)
            placementManager = GetComponent<PPEPlacementGuideManager>();
        if (placementManager == null)
            placementManager = FindAnyObjectByType<PPEPlacementGuideManager>(FindObjectsInactive.Include);
        if (anchorSet == null)
            anchorSet = FindAnyObjectByType<PPEEquipmentAnchorSet>(FindObjectsInactive.Include);
    }

    public void EditorWire(PPEPlacementGuideManager manager, PPEEquipmentAnchorSet equipmentAnchors)
    {
        placementManager = manager;
        anchorSet = equipmentAnchors;
        drawDebugGizmos = false;
    }
#endif
}
