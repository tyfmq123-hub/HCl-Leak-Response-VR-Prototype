using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Marks a worn PPE mesh as visual-only. Interaction must never resolve this
/// hierarchy as an XR hand, interactor, or grab target.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-400)]
public sealed class PPEWearableVisualOnly : MonoBehaviour
{
    public const string MarkerName = "PPEWearableVisualOnly";

    void OnEnable()
    {
        Strip(gameObject);
    }

    public static bool IsInHierarchy(Transform transform)
    {
        return transform != null && transform.GetComponentInParent<PPEWearableVisualOnly>(true) != null;
    }

    public static void EnsureAndStrip(GameObject root)
    {
        if (root == null)
            return;
        if (root.GetComponent<PPEWearableVisualOnly>() == null)
            root.AddComponent<PPEWearableVisualOnly>();
        Strip(root);
    }

    public static void Strip(GameObject root)
    {
        if (root == null)
            return;

        var ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycast >= 0)
            SetLayerRecursively(root.transform, ignoreRaycast);

        var colliders = root.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
                continue;
            colliders[i].enabled = false;
        }

        var interactables = root.GetComponentsInChildren<XRBaseInteractable>(true);
        for (var i = 0; i < interactables.Length; i++)
        {
            if (interactables[i] != null)
                interactables[i].enabled = false;
        }

        var interactors = root.GetComponentsInChildren<XRBaseInteractor>(true);
        for (var i = 0; i < interactors.Length; i++)
        {
            if (interactors[i] != null)
                interactors[i].enabled = false;
        }

        var proximityFilters = root.GetComponentsInChildren<PPEDirectGrabProximityFilter>(true);
        for (var i = 0; i < proximityFilters.Length; i++)
        {
            if (proximityFilters[i] != null)
                proximityFilters[i].enabled = false;
        }
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root)
            SetLayerRecursively(child, layer);
    }
}
