using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Reusable PPE interactable attached to every selectable PPE root.
/// Phase 1: hover highlight only. Selection is stubbed for later phases.
/// </summary>
[RequireComponent(typeof(XRBaseInteractable))]
[RequireComponent(typeof(PPEHighlight))]
public class PPEItem : MonoBehaviour
{
    [SerializeField]
    PPEType type = PPEType.Gloves;

    [SerializeField]
    PPEHighlight highlight;

    XRBaseInteractable interactable;
    bool isHovered;
    bool loggedMissingManager;

    /// <summary>Raised when the interactable is selected. No wear/move behavior yet.</summary>
    public event Action<PPEItem> Selected;

    public PPEType Type => type;

    void Awake()
    {
        interactable = GetComponent<XRBaseInteractable>();
        if (highlight == null)
            highlight = GetComponent<PPEHighlight>();

        if (interactable == null)
            Debug.LogWarning($"PPEItem on '{name}' is missing XRBaseInteractable.", this);

        if (highlight == null)
            Debug.LogWarning($"PPEItem on '{name}' is missing PPEHighlight.", this);
    }

    void OnEnable()
    {
        TryRegister();

        if (interactable == null)
            return;

        interactable.hoverEntered.AddListener(OnHoverEntered);
        interactable.hoverExited.AddListener(OnHoverExited);
        interactable.selectEntered.AddListener(OnSelectEntered);
    }

    void Start()
    {
        // Manager may initialize after this object enables.
        TryRegister();
    }

    void OnDisable()
    {
        if (interactable != null)
        {
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
            interactable.selectEntered.RemoveListener(OnSelectEntered);
        }

        if (isHovered && PPEManager.Instance != null)
            PPEManager.Instance.NotifyHoverExited(this);

        if (PPEManager.Instance != null)
            PPEManager.Instance.UnregisterItem(this);

        isHovered = false;
        ClearHighlight();
    }

    void TryRegister()
    {
        if (PPEManager.Instance == null)
        {
            if (!loggedMissingManager)
            {
                Debug.LogWarning(
                    $"PPEItem '{name}' could not register — no PPEManager in the scene.",
                    this);
                loggedMissingManager = true;
            }
            return;
        }

        loggedMissingManager = false;
        PPEManager.Instance.RegisterItem(this);
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        isHovered = true;

        if (PPEManager.Instance != null)
            PPEManager.Instance.NotifyHoverEntered(this);

        // Prefer mesh outline (PPEOutlineHighlight). Skip cube PPEHighlight when outline exists.
        if (highlight != null && GetComponent<PPEOutlineHighlight>() == null)
            highlight.SetHighlighted(true);
    }

    void OnHoverExited(HoverExitEventArgs args)
    {
        isHovered = false;

        if (PPEManager.Instance != null)
            PPEManager.Instance.NotifyHoverExited(this);

        ClearHighlight();
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        // Future phases: preview / wear. Phase 1 intentionally does nothing to the transform.
        OnSelected();
    }

    /// <summary>Hook for future selection / equip flow. Does not move or wear the object.</summary>
    protected virtual void OnSelected()
    {
        Selected?.Invoke(this);
    }

    /// <summary>Clears highlight without changing hover bookkeeping (used by PPEManager).</summary>
    public void ClearHighlight()
    {
        if (highlight != null)
            highlight.SetHighlighted(false);
    }
}
