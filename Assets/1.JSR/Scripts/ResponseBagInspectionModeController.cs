using UnityEngine;

/// <summary>
/// Legacy look-into-bag presentation — disabled.
/// Kept so scene/script references do not break. Does not move or scale the bag.
/// </summary>
[DisallowMultipleComponent]
public class ResponseBagInspectionModeController : MonoBehaviour
{
    public static ResponseBagInspectionModeController Instance { get; private set; }

    public bool IsInspectionActive => false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }

        Instance = this;
        enabled = false;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void EnterInspectionMode()
    {
        // Intentionally empty — bag stays in world pose; no camera-follow presentation.
    }

    public void ExitInspectionMode(bool restoreWorldPose)
    {
        var label = FindAnyObjectByType<ResponseBagItemNameLabel>(FindObjectsInactive.Include);
        if (label != null)
            label.HideImmediate();

        var items = FindObjectsByType<ResponseBagContentHoverItem>(
            FindObjectsInactive.Include);
        for (var i = 0; i < items.Length; i++)
            items[i]?.ClearVisuals();
    }

    public void HideSubtitle()
    {
        var label = FindAnyObjectByType<ResponseBagItemNameLabel>(FindObjectsInactive.Include);
        if (label != null)
            label.HideImmediate();
    }

    public void EditorWire(
        GameObject bag,
        ResponseEquipmentManager manager,
        ResponseBagContentsRevealController reveal,
        ResponseBagItemNameLabel subtitle,
        Camera camera)
    {
        // No-op for legacy editor wiring.
    }
}
