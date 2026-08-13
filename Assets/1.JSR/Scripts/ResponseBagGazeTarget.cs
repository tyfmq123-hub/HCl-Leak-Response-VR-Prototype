using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// World-bag-only gaze dwell target. Opens ResponseBagContentsPanel after dwell.
/// Must live on ResponseBag_GazeTarget under the world bag — never the wearable.
/// </summary>
[DisallowMultipleComponent]
public class ResponseBagGazeTarget : MonoBehaviour
{
    public const string GazeLayerName = "GazeInspectable";

    [Header("References")]
    [SerializeField] ResponseEquipmentManager equipmentManager;
    [SerializeField] ResponseBagContentsPanel contentsPanel;
    [SerializeField] GameObject worldBagRoot;

    [Header("Dwell")]
    [SerializeField] [Min(0.05f)] float openDwellSeconds = 0.45f;
    [SerializeField] [Min(0f)] float lookAwayCloseDelay = 0.5f;
    [SerializeField] [Min(0.5f)] float maxGazeDistance = 3f;

    float gazeAccumulated;
    float lookAwayAccumulated;
    bool currentlyGazed;

    public float MaxGazeDistance => maxGazeDistance;
    public GameObject WorldBagRoot => worldBagRoot != null ? worldBagRoot : transform.root.gameObject;

    void Awake() => ResolveRefs();

    void OnEnable() => ResolveRefs();

    public void EditorWire(
        ResponseEquipmentManager manager,
        ResponseBagContentsPanel panel,
        GameObject bagRoot)
    {
        equipmentManager = manager;
        contentsPanel = panel;
        worldBagRoot = bagRoot;
    }

    public bool CanAcceptGaze()
    {
        // Legacy gaze inspection removed.
        return false;
    }

    /// <summary>Called by the shared camera/gaze detector while this target is hit.</summary>
    public void NotifyGazeHit(bool hitThisFrame)
    {
        if (!CanAcceptGaze())
        {
            currentlyGazed = false;
            gazeAccumulated = 0f;
            return;
        }

        if (hitThisFrame)
        {
            currentlyGazed = true;
            lookAwayAccumulated = 0f;
            gazeAccumulated += Time.unscaledDeltaTime;

            if (gazeAccumulated >= openDwellSeconds)
                TryOpenPanel();
        }
        else if (currentlyGazed)
        {
            lookAwayAccumulated += Time.unscaledDeltaTime;
            if (lookAwayAccumulated >= lookAwayCloseDelay)
            {
                currentlyGazed = false;
                gazeAccumulated = 0f;
                TryClosePanelIfAllowed();
            }
        }
        else
        {
            gazeAccumulated = 0f;
        }
    }

    void TryOpenPanel()
    {
        ResolveRefs();
        if (contentsPanel == null)
            return;

        if (contentsPanel.IsOpen)
            return;

        contentsPanel.ShowPanel();
    }

    void TryClosePanelIfAllowed()
    {
        ResolveRefs();
        if (contentsPanel == null || !contentsPanel.IsOpen)
            return;

        if (contentsPanel.ShouldKeepOpenDespiteLookAway)
            return;

        contentsPanel.HidePanel();
    }

    void ResolveRefs()
    {
        if (worldBagRoot == null)
        {
            var prepare = GetComponentInParent<ResponseBagPrepareInteractable>(true);
            worldBagRoot = prepare != null ? prepare.gameObject : transform.root.gameObject;
        }

        if (equipmentManager == null)
        {
            equipmentManager = ResponseEquipmentManager.Instance != null
                ? ResponseEquipmentManager.Instance
                : FindAnyObjectByType<ResponseEquipmentManager>();
        }

        if (contentsPanel == null)
        {
            if (equipmentManager != null)
                contentsPanel = equipmentManager.ContentsPanel;
            if (contentsPanel == null)
                contentsPanel = FindAnyObjectByType<ResponseBagContentsPanel>(FindObjectsInactive.Include);
        }
    }

    static bool IsWorldBagBeingGrabbedOrMoved(GameObject root)
    {
        if (root == null)
            return false;

        var interactable = root.GetComponent<XRBaseInteractable>();
        if (interactable == null)
            interactable = root.GetComponentInChildren<XRBaseInteractable>(true);

        return interactable != null && interactable.isSelected;
    }
}
