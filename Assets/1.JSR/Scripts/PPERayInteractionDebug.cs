using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Temporary diagnostics for ray hover/select on PPE interactables.
/// Event-driven only — no Update polling.
/// </summary>
[DisallowMultipleComponent]
public class PPERayInteractionDebug : MonoBehaviour
{
    XRBaseInteractable interactable;

    void Awake()
    {
        CacheInteractable();
    }

    void OnEnable()
    {
        CacheInteractable();
        if (interactable == null)
        {
            Debug.LogWarning($"[PPERayInteractionDebug] '{name}' has no XRBaseInteractable.", this);
            return;
        }

        interactable.hoverEntered.AddListener(OnHoverEntered);
        interactable.hoverExited.AddListener(OnHoverExited);
        interactable.selectEntered.AddListener(OnSelectEntered);
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void CacheInteractable()
    {
        if (interactable == null)
            interactable = GetComponent<XRBaseInteractable>();
    }

    void Unsubscribe()
    {
        if (interactable == null)
            return;

        interactable.hoverEntered.RemoveListener(OnHoverEntered);
        interactable.hoverExited.RemoveListener(OnHoverExited);
        interactable.selectEntered.RemoveListener(OnSelectEntered);
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        Debug.Log($"[PPE Hover Enter] {name}", this);
    }

    void OnHoverExited(HoverExitEventArgs args)
    {
        Debug.Log($"[PPE Hover Exit] {name}", this);
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        // Selection logging is owned by PPEOutlineHighlight / PPESelectionManager.
    }
}
