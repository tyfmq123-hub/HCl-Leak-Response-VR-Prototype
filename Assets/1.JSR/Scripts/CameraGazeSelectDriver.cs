using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// When chemical gloves are worn (rays hidden), selects the waste container by
/// looking at it with the XR camera, then Grip confirms via the confirm router.
/// </summary>
[DefaultExecutionOrder(-40)]
[DisallowMultipleComponent]
public class CameraGazeSelectDriver : MonoBehaviour
{
    [Header("Gaze")]
    [SerializeField] Camera xrCamera;
    [SerializeField] [Min(0.5f)] float maxDistance = 4.5f;
    [SerializeField] LayerMask hitMask = ~0;
    [SerializeField] [Min(0.02f)] float pollInterval = 0.05f;

    [Header("Gates")]
    [Tooltip("Only run gaze select while chemical gloves are equipped (rays off).")]
    [SerializeField] bool onlyWhileGlovesEquipped = true;

    [Header("Targets")]
    [SerializeField] WasteContainerPrepareInteractable wasteContainer;

    float nextPollTime;
    WasteContainerPrepareInteractable lastGazeTarget;

    void Awake()
    {
        ResolveRefs();
    }

    void OnEnable() => ResolveRefs();

    void Update()
    {
        if (Time.unscaledTime < nextPollTime)
            return;
        nextPollTime = Time.unscaledTime + pollInterval;

        ResolveRefs();

        if (onlyWhileGlovesEquipped && !AreGlovesEquipped())
        {
            lastGazeTarget = null;
            return;
        }

        if (xrCamera == null)
            return;

        if (wasteContainer != null
            && PPEEquipmentManager.Instance != null
            && PPEEquipmentManager.Instance.IsPrepared(PPEEquipmentManager.IdWasteContainer))
        {
            lastGazeTarget = null;
            return;
        }

        var origin = xrCamera.transform.position;
        var direction = xrCamera.transform.forward;

        if (!Physics.Raycast(origin, direction, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
        {
            lastGazeTarget = null;
            return;
        }

        var target = hit.collider != null
            ? hit.collider.GetComponentInParent<WasteContainerPrepareInteractable>()
            : null;

        if (target == null || !target.isActiveAndEnabled)
        {
            lastGazeTarget = null;
            return;
        }

        if (target == lastGazeTarget)
            return;

        lastGazeTarget = target;
        target.NotifyGazeSelected();
    }

    void ResolveRefs()
    {
        if (xrCamera == null)
        {
            var origin = FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
            if (origin != null && origin.Camera != null)
                xrCamera = origin.Camera;
            if (xrCamera == null)
                xrCamera = Camera.main;
        }

        if (wasteContainer == null)
        {
            wasteContainer = FindAnyObjectByType<WasteContainerPrepareInteractable>(
                FindObjectsInactive.Exclude);
        }
    }

    static bool AreGlovesEquipped()
    {
        var mgr = PPEEquipmentManager.Instance;
        return mgr != null && mgr.IsEquipped(PPEEquipmentManager.IdChemicalGloves);
    }

#if UNITY_EDITOR
    public void EditorWire(Camera cam, WasteContainerPrepareInteractable container)
    {
        xrCamera = cam;
        wasteContainer = container;
        onlyWhileGlovesEquipped = true;
    }
#endif
}
