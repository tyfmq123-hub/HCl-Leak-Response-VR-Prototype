using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Legacy contents-panel opener. Disabled for the prepare-on-Activate stage —
/// use <see cref="ResponseBagPrepareInteractable"/> instead.
/// Kept so scene references do not break; Select no longer opens inspection UI.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRSimpleInteractable))]
public class ResponseBagInteractable : MonoBehaviour
{
    [SerializeField] ResponseEquipmentManager equipmentManager;
    [SerializeField] ResponseBagContentsPanel contentsPanel;

    XRSimpleInteractable interactable;

    void Awake()
    {
        interactable = GetComponent<XRSimpleInteractable>();
        ResolveRefs();
    }

    void OnEnable()
    {
        if (interactable == null)
            interactable = GetComponent<XRSimpleInteractable>();
        if (interactable != null)
            interactable.selectEntered.AddListener(OnSelectEntered);
    }

    void OnDisable()
    {
        if (interactable != null)
            interactable.selectEntered.RemoveListener(OnSelectEntered);
    }

    void ResolveRefs()
    {
        if (equipmentManager == null)
        {
            equipmentManager = ResponseEquipmentManager.Instance != null
                ? ResponseEquipmentManager.Instance
                : FindAnyObjectByType<ResponseEquipmentManager>();
        }

        if (contentsPanel == null && equipmentManager != null)
            contentsPanel = equipmentManager.ContentsPanel;

        if (contentsPanel == null)
            contentsPanel = FindAnyObjectByType<ResponseBagContentsPanel>(FindObjectsInactive.Include);
    }

    void OnSelectEntered(SelectEnterEventArgs _)
    {
        // Legacy contents-panel open removed.
    }

    public void EditorWire(ResponseEquipmentManager manager, ResponseBagContentsPanel panel)
    {
        equipmentManager = manager;
        contentsPanel = panel;
    }
}
