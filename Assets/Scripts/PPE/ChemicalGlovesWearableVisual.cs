using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// When chemical gloves are worn, hides Quest controller meshes, shows VR hand
/// meshes, and disables XR Ray Interactors so take-out uses camera gaze + Grip.
/// Quest meshes are hidden individually so Visuals (hand parent) can stay active.
/// </summary>
[DisallowMultipleComponent]
public class ChemicalGlovesWearableVisual : MonoBehaviour
{
    [Header("Wearable Gloves")]
    [SerializeField] GameObject leftGlove;
    [SerializeField] GameObject rightGlove;

    [Header("Quest Controller Meshes (hidden while gloves worn)")]
    [SerializeField] GameObject[] questControllerMeshes;

    [Header("Ray Interactors (disabled while gloves worn)")]
    [SerializeField] bool disableRayInteractorsWhileWorn = true;
    [SerializeField] XRRayInteractor[] rayInteractors;

    [Header("Start")]
    [Tooltip("Host should start inactive in the scene; gloves start inactive under Visuals.")]
    [SerializeField] bool startHidden = true;

    bool applyingPersistentState;

    public bool IsVisible =>
        (leftGlove != null && leftGlove.activeSelf) ||
        (rightGlove != null && rightGlove.activeSelf);

    void OnEnable()
    {
        if (applyingPersistentState)
            return;
        ShowWearable();
    }

    void OnDisable()
    {
        HideWearable();
    }

    public void ShowWearable()
    {
        SetGlovesActive(true);
        SetQuestMeshesActive(false);
        SetRayInteractorsEnabled(false);

        Debug.Log(
            "[Chemical Gloves Wearable] Showing gloves — Quest meshes + rays hidden.",
            this);
    }

    public void HideWearable()
    {
        SetGlovesActive(false);
        SetQuestMeshesActive(true);
        SetRayInteractorsEnabled(true);
    }

    public void ResetWearable()
    {
        HideWearable();
    }

    /// <summary>
    /// Applies the cross-scene glove visual without changing the Stage XR profile's
    /// ray/interactor state. PPE-room ShowWearable keeps its original behaviour.
    /// </summary>
    public void ApplyPersistentState(bool leftWorn, bool rightWorn)
    {
        var anyWorn = leftWorn || rightWorn;
        applyingPersistentState = true;
        if (gameObject.activeSelf != anyWorn)
            gameObject.SetActive(anyWorn);
        applyingPersistentState = false;

        if (leftGlove != null && leftGlove.activeSelf != leftWorn)
            leftGlove.SetActive(leftWorn);
        if (rightGlove != null && rightGlove.activeSelf != rightWorn)
            rightGlove.SetActive(rightWorn);

        SetQuestMeshesActive(!anyWorn);
    }

    void SetGlovesActive(bool active)
    {
        if (leftGlove != null && leftGlove.activeSelf != active)
            leftGlove.SetActive(active);
        if (rightGlove != null && rightGlove.activeSelf != active)
            rightGlove.SetActive(active);
    }

    void SetQuestMeshesActive(bool active)
    {
        if (questControllerMeshes == null)
            return;

        for (var i = 0; i < questControllerMeshes.Length; i++)
        {
            var mesh = questControllerMeshes[i];
            if (mesh == null)
                continue;
            if (mesh.activeSelf != active)
                mesh.SetActive(active);
        }
    }

    void SetRayInteractorsEnabled(bool enabled)
    {
        if (!disableRayInteractorsWhileWorn)
            return;

        if (rayInteractors == null || rayInteractors.Length == 0)
            CacheRayInteractors();

        if (rayInteractors == null)
            return;

        for (var i = 0; i < rayInteractors.Length; i++)
        {
            var ray = rayInteractors[i];
            if (ray == null)
                continue;
            if (ray.enabled != enabled)
                ray.enabled = enabled;
            if (ray.gameObject.activeSelf != enabled)
                ray.gameObject.SetActive(enabled);
        }
    }

    void CacheRayInteractors()
    {
        rayInteractors = FindObjectsByType<XRRayInteractor>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
    }

#if UNITY_EDITOR
    public void EditorWire(
        GameObject left,
        GameObject right,
        GameObject[] questMeshes)
    {
        leftGlove = left;
        rightGlove = right;
        questControllerMeshes = questMeshes ?? System.Array.Empty<GameObject>();
        disableRayInteractorsWhileWorn = true;
        CacheRayInteractors();
        startHidden = true;
        SetGlovesActive(false);
        SetQuestMeshesActive(true);
        SetRayInteractorsEnabled(true);
        gameObject.SetActive(false);
    }

    [ContextMenu("Test Show Wearable Gloves")]
    void ContextShow() => ShowWearable();

    [ContextMenu("Test Hide Wearable Gloves")]
    void ContextHide() => HideWearable();
#endif
}
