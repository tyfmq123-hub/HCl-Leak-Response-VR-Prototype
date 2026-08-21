using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Switches controller-tracked hand visuals between the XR Hands sample meshes
/// used before PPE is equipped and the chemical-glove meshes used afterward.
/// Quest controller meshes are hidden individually so Visuals stays active.
/// Default-hand Visuals stay at identity; glove Visuals use the captured
/// chemical-glove rotation. Tracking and ray origin are never rotated.
/// </summary>
[DisallowMultipleComponent]
public class ChemicalGlovesWearableVisual : MonoBehaviour
{
    [Header("Default Controller Hands")]
    [SerializeField] GameObject leftBareHand;
    [SerializeField] GameObject rightBareHand;

    [Header("Wearable Gloves")]
    [SerializeField] GameObject leftGlove;
    [SerializeField] GameObject rightGlove;

    [Header("Quest Controller Meshes (hidden while gloves worn)")]
    [SerializeField] GameObject[] questControllerMeshes;

    [Header("Ray Interactors")]
    [SerializeField] bool disableRayInteractorsWhileWorn = false;
    [SerializeField] XRRayInteractor[] rayInteractors;

    [Header("Start")]
    [Tooltip("Host should start inactive in the scene; gloves start inactive under Visuals.")]
    [SerializeField] bool startHidden = true;

    bool applyingPersistentState;
    bool capturedGloveRotation;
    Quaternion capturedLeftGloveRotation;
    Quaternion capturedRightGloveRotation;
    readonly Dictionary<XRRayInteractor, bool> rayEnabledBeforeWear = new();
    readonly Dictionary<XRRayInteractor, bool> rayObjectActiveBeforeWear = new();

    // Visual-only. Same grip * model correction as PPEWearableChemicalGlovesSetup.
    // Never applied to controller tracking or ray origin.
    static readonly Quaternion FallbackLeftGloveVisualRotation =
        Quaternion.Euler(70f, 0f, 0f) *
        new Quaternion(-0.44794282f, -0.3529363f, 0.5857566f, 0.57591015f);
    static readonly Quaternion FallbackRightGloveVisualRotation =
        Quaternion.Euler(70f, 0f, 0f) *
        new Quaternion(-0.44794282f, 0.3529363f, -0.5857566f, 0.57591015f);

    static ChemicalGlovesWearableVisual cachedInstance;

    public bool IsVisible =>
        (leftGlove != null && leftGlove.activeSelf) ||
        (rightGlove != null && rightGlove.activeSelf);

    void Awake()
    {
        cachedInstance = this;
        CaptureGloveVisualRotations();
    }

    void OnEnable()
    {
        cachedInstance = this;
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
        SetBareHandsActive(false, false);
        SetGlovesActive(true);
        AlignBareHandVisuals();
        AlignGloveVisuals();
        SetQuestMeshesActive(false);
        SetRayInteractorsEnabled(false);

        Debug.Log(
            "[Chemical Gloves Wearable] Showing gloves — Quest meshes + rays hidden.",
            this);
    }

    public static ChemicalGlovesWearableVisual FindActive()
    {
        if (cachedInstance != null)
            return cachedInstance;
        cachedInstance = FindAnyObjectByType<ChemicalGlovesWearableVisual>(FindObjectsInactive.Include);
        return cachedInstance;
    }

    public void SetLeftGloveEquipped(bool equipped)
    {
        var pair = PPEGlovePairCoordinator.ChemicalGlovesInstance;
        var right = pair != null
            ? pair.RightEquipped
            : rightGlove != null && rightGlove.activeSelf;
        ApplyPpeRoomState(equipped, right);
    }

    public void SetRightGloveEquipped(bool equipped)
    {
        var pair = PPEGlovePairCoordinator.ChemicalGlovesInstance;
        var left = pair != null
            ? pair.LeftEquipped
            : leftGlove != null && leftGlove.activeSelf;
        ApplyPpeRoomState(left, equipped);
    }

    /// <summary>
    /// PPE always uses controller tracking with hand meshes. Before glove equipment,
    /// show the XR Hands sample FBX; afterward switch each side to its glove mesh.
    /// Partial ChemicalGloves progress from a glove pair is preserved so App's
    /// all-or-nothing equipped flag cannot hide a single worn glove.
    /// </summary>
    public void ApplyPpeRoomState(bool leftWorn, bool rightWorn)
    {
        var pair = PPEGlovePairCoordinator.ChemicalGlovesInstance;
        if (pair != null)
        {
            leftWorn |= pair.LeftEquipped;
            rightWorn |= pair.RightEquipped;
        }

        applyingPersistentState = true;
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        applyingPersistentState = false;

        SetBareHandsActive(!leftWorn, !rightWorn);
        SetGlovesActive(leftWorn, rightWorn);
        AlignBareHandVisuals();
        if (leftWorn || rightWorn)
            AlignGloveVisuals();
        SetQuestMeshesActive(false);
        SetRayInteractorsEnabled(false);
    }

    public void ShowPpeDirectHands() => ApplyPpeRoomState(false, false);

    public void HideWearable()
    {
        SetBareHandsActive(false, false);
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

        SetBareHandsActive(false, false);
        SetGlovesActive(leftWorn, rightWorn);
        if (anyWorn)
            AlignGloveVisuals();
        SetQuestMeshesActive(!anyWorn);
    }

    void CaptureGloveVisualRotations()
    {
        if (capturedGloveRotation)
            return;

        capturedLeftGloveRotation = CaptureOrFallback(leftGlove, FallbackLeftGloveVisualRotation);
        capturedRightGloveRotation = CaptureOrFallback(rightGlove, FallbackRightGloveVisualRotation);
        capturedGloveRotation = true;
    }

    static Quaternion CaptureOrFallback(GameObject glove, Quaternion fallback)
    {
        if (glove == null)
            return fallback;

        var authored = glove.transform.localRotation;
        return authored == Quaternion.identity ? fallback : authored;
    }

    void AlignBareHandVisuals()
    {
        ApplyLocalRotation(leftBareHand, Quaternion.identity);
        ApplyLocalRotation(rightBareHand, Quaternion.identity);
    }

    void AlignGloveVisuals()
    {
        CaptureGloveVisualRotations();
        ApplyLocalRotation(leftGlove, capturedLeftGloveRotation);
        ApplyLocalRotation(rightGlove, capturedRightGloveRotation);
    }

    static void ApplyLocalRotation(GameObject target, Quaternion localRotation)
    {
        if (target == null)
            return;
        target.transform.localRotation = localRotation;
    }

    void SetGlovesActive(bool active)
        => SetGlovesActive(active, active);

    void SetGlovesActive(bool leftActive, bool rightActive)
    {
        if (leftGlove != null && leftGlove.activeSelf != leftActive)
            leftGlove.SetActive(leftActive);
        if (rightGlove != null && rightGlove.activeSelf != rightActive)
            rightGlove.SetActive(rightActive);
    }

    void SetBareHandsActive(bool leftActive, bool rightActive)
    {
        if (leftBareHand != null && leftBareHand.activeSelf != leftActive)
            leftBareHand.SetActive(leftActive);
        if (rightBareHand != null && rightBareHand.activeSelf != rightActive)
            rightBareHand.SetActive(rightActive);
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

            if (!enabled)
            {
                if (!rayEnabledBeforeWear.ContainsKey(ray))
                    rayEnabledBeforeWear.Add(ray, ray.enabled);
                if (!rayObjectActiveBeforeWear.ContainsKey(ray))
                    rayObjectActiveBeforeWear.Add(ray, ray.gameObject.activeSelf);
                ray.enabled = false;
                ray.gameObject.SetActive(false);
                continue;
            }

            if (rayEnabledBeforeWear.TryGetValue(ray, out var wasEnabled))
                ray.enabled = wasEnabled;
            else if (!Application.isPlaying)
                ray.enabled = true;

            if (rayObjectActiveBeforeWear.TryGetValue(ray, out var wasActive))
                ray.gameObject.SetActive(wasActive);
            else if (!Application.isPlaying)
                ray.gameObject.SetActive(true);
        }

        if (enabled)
        {
            rayEnabledBeforeWear.Clear();
            rayObjectActiveBeforeWear.Clear();
        }
    }

    void CacheRayInteractors()
    {
        rayInteractors = FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Include);
    }

#if UNITY_EDITOR
    public void EditorWire(
        GameObject left,
        GameObject right,
        GameObject[] questMeshes)
        => EditorWire(null, null, left, right, questMeshes);

    public void EditorWire(
        GameObject leftDefault,
        GameObject rightDefault,
        GameObject leftWearable,
        GameObject rightWearable,
        GameObject[] questMeshes)
    {
        leftBareHand = leftDefault;
        rightBareHand = rightDefault;
        leftGlove = leftWearable;
        rightGlove = rightWearable;
        capturedGloveRotation = false;
        questControllerMeshes = questMeshes ?? System.Array.Empty<GameObject>();
        disableRayInteractorsWhileWorn = false;
        rayInteractors = System.Array.Empty<XRRayInteractor>();
        startHidden = true;
        SetBareHandsActive(false, false);
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
