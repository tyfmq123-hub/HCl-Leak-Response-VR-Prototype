using System;
using UnityEngine;

/// <summary>
/// Per-item equipment metadata on a top-level PPE root.
/// Stores equip state and placement definition, but does not move objects,
/// evaluate placement, or drive XR interaction.
/// </summary>
[DisallowMultipleComponent]
public class PPEEquipable : MonoBehaviour
{
    [SerializeField]
    string itemId = "";

    [SerializeField]
    string displayName = "";

    [Header("Equipment Placement")]
    [SerializeField]
    PPEType ppeType = PPEType.Gloves;

    [SerializeField]
    [Tooltip("One or more player equipment destinations used by this PPE.")]
    PPEEquipSlot equipSlots = PPEEquipSlot.None;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Maximum distance in meters for a future placement/equip check.")]
    float equipDistance = 0.2f;

    [SerializeField]
    [Range(0f, 180f)]
    [Tooltip("Maximum orientation difference in degrees when Use Rotation Validation is enabled.")]
    float equipAngleTolerance = 35f;

    [SerializeField]
    [Tooltip("When false, placement uses distance only. Gloves/boots/suit should leave this off.")]
    bool useRotationValidation = true;

    [SerializeField]
    [Tooltip("Optional placement point. When empty, an EquipReference child is created at the visual center.")]
    Transform equipReference;

    [SerializeField]
    [Tooltip("Optional ghost/guide visual. STEP 3 stores the reference but does not instantiate it.")]
    GameObject guidePrefab;

    [Header("Ghost Pose Calibration")]
    [SerializeField]
    [Tooltip("Child Transform rotated in the Inspector until the Face/Suit Ghost faces the correct way. Runtime Ghost uses this local rotation relative to the Face/Torso target.")]
    Transform ghostPoseCalibration;

    [SerializeField]
    bool isEquipped;

    [SerializeField]
    [Tooltip("When true, the PPE root is SetActive(false) after a successful Equip.")]
    bool deactivateOnEquip = true;

    [SerializeField]
    GameObject equippedVisual;

    [SerializeField]
    AudioSource equippedAudio;

    /// <summary>Raised after this item is marked equipped.</summary>
    public event Action Equipped;

    public string ItemId => itemId;
    public string DisplayName => displayName;
    public PPEType Type => ppeType;
    public PPEEquipSlot EquipSlots => equipSlots;
    public float EquipDistance => equipDistance;
    public float EquipAngleTolerance => equipAngleTolerance;
    public bool UseRotationValidation => useRotationValidation;
    public Transform EquipReference => equipReference;
    public Transform PlacementReference =>
        equipReference != null ? equipReference : transform;

    public Transform EnsurePlacementReference()
    {
        if (equipReference != null)
            return equipReference;

        equipReference = PPEEquipReferenceUtility.Ensure(transform, transform);
        if (ppeType == PPEType.ProtectiveSuit ||
            string.Equals(itemId, PPEEquipmentManager.IdChemicalSuit, StringComparison.Ordinal))
            PPEEquipReferenceUtility.PlaceAtSuitChest(equipReference, transform);
        return PlacementReference;
    }

    public const string GhostPoseCalibrationName = "GhostPoseCalibration";

    public Transform EnsureGhostPoseCalibration()
    {
        if (!ShouldOwnGhostPoseCalibration())
            return ghostPoseCalibration;

        if (ghostPoseCalibration != null)
            return ghostPoseCalibration;

        var existing = transform.Find(GhostPoseCalibrationName);
        if (existing != null)
        {
            ghostPoseCalibration = existing;
            return ghostPoseCalibration;
        }

        var go = new GameObject(GhostPoseCalibrationName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        ghostPoseCalibration = go.transform;
        return ghostPoseCalibration;
    }

    public GameObject GuidePrefab => guidePrefab;
    public Transform GhostPoseCalibration => ghostPoseCalibration;
    public bool IsEquipped => isEquipped;

    void Awake()
    {
        if (ShouldOwnGhostPoseCalibration())
            EnsureGhostPoseCalibration();
    }

    bool ShouldOwnGhostPoseCalibration()
    {
        return !string.Equals(itemId, PPEEquipmentManager.IdResponseBag, StringComparison.Ordinal);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null || !ShouldOwnGhostPoseCalibration())
                return;
            EnsureGhostPoseCalibration();
        };
    }

    void OnDrawGizmos()
    {
        if (!ShouldOwnGhostPoseCalibration())
            return;

        var calib = ghostPoseCalibration != null
            ? ghostPoseCalibration
            : transform.Find(GhostPoseCalibrationName);
        if (calib == null)
            return;

        var previewPos = calib.position;
        var previewRot = calib.rotation;
        Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.9f);
        Gizmos.matrix = Matrix4x4.TRS(previewPos, previewRot, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.08f, 0.08f, 0.08f));
        Gizmos.color = Color.red;
        Gizmos.DrawLine(Vector3.zero, Vector3.right * 0.08f);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(Vector3.zero, Vector3.up * 0.08f);
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.08f);
        Gizmos.matrix = Matrix4x4.identity;

        var filters = GetComponentsInChildren<MeshFilter>(true);
        Gizmos.color = new Color(0.35f, 0.9f, 1f, 0.18f);
        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
                continue;
            if (filter.transform == calib)
                continue;
            Gizmos.DrawMesh(filter.sharedMesh, previewPos, previewRot, filter.transform.lossyScale);
        }
    }
#endif
    public bool DeactivateOnEquip => deactivateOnEquip;

    public bool UsesSlot(PPEEquipSlot slot)
    {
        return slot != PPEEquipSlot.None && (equipSlots & slot) == slot;
    }

    /// <summary>
    /// Returns true when this item can be equipped (not already equipped,
    /// has a valid id, and the manager does not already track this id).
    /// </summary>
    public bool CanEquip()
    {
        if (isEquipped)
            return false;

        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        var manager = PPEEquipmentManager.Instance;
        if (manager != null && manager.IsEquipped(itemId))
            return false;

        return true;
    }

    /// <summary>
    /// Marks this item equipped, optionally shows visual / plays audio.
    /// Root deactivation is owned by <see cref="PPEEquipmentManager"/>.
    /// </summary>
    public void MarkAsEquipped()
    {
        if (isEquipped)
            return;

        isEquipped = true;

        if (equippedVisual != null)
            equippedVisual.SetActive(true);

        if (equippedAudio != null)
            equippedAudio.Play();

        Equipped?.Invoke();
    }

    /// <summary>
    /// Clears local equipped state and hides optional equipped visual.
    /// Does not reactivate the PPE root (never deactivated by this component).
    /// </summary>
    public void ResetEquipState()
    {
        isEquipped = false;

        if (equippedVisual != null)
            equippedVisual.SetActive(false);
    }

#if UNITY_EDITOR
    /// <summary>Editor helper used by setup tools.</summary>
    public void EditorApplyIdentity(string id, string koreanDisplayName)
    {
        itemId = id;
        displayName = koreanDisplayName;
    }

    /// <summary>Editor helper — assign optional equipped visual (e.g. wearable gloves).</summary>
    public void EditorApplyEquippedVisual(GameObject visual)
    {
        equippedVisual = visual;
    }

    /// <summary>Editor helper for STEP 3 placement metadata.</summary>
    public void EditorApplyPlacementDefinition(
        PPEType type,
        PPEEquipSlot slots,
        float distance,
        float angleTolerance,
        GameObject guide)
    {
        ppeType = type;
        equipSlots = slots;
        equipDistance = Mathf.Max(0.01f, distance);
        equipAngleTolerance = Mathf.Clamp(angleTolerance, 0f, 180f);
        guidePrefab = guide;
    }

    /// <summary>Editor helper for proximity-first vs orientation-checked items.</summary>
    public void EditorSetUseRotationValidation(bool requireRotation)
    {
        useRotationValidation = requireRotation;
    }

    /// <summary>Editor helper for an item that remains visible on its anchor after equip.</summary>
    public void EditorSetDeactivateOnEquip(bool deactivate)
    {
        deactivateOnEquip = deactivate;
    }
#endif
}
