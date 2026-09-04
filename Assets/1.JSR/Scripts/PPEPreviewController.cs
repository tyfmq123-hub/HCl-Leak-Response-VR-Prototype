using System;
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Moves the sticky-selected PPE to a camera-front preview anchor, then restores
/// exact original parent/local transform on clear or when another PPE is selected.
/// </summary>
[DisallowMultipleComponent]
public class PPEPreviewController : MonoBehaviour
{
    [Serializable]
    public class PreviewOverride
    {
        [Tooltip("Case-insensitive substring matched against the PPE root name.")]
        public string nameContains;

        [Tooltip("Applied to the preserved world scale while previewing.")]
        public float scaleMultiplier = 1f;

        [Tooltip("Extra yaw (degrees) after facing the camera, for FBX forward-axis fixes.")]
        public float yawOffsetDegrees;

        [Tooltip("Extra pitch (degrees). Positive tips the top toward the player (useful for looking into a bag).")]
        public float pitchOffsetDegrees;

        [Tooltip("Yaw toward camera only; keep world up so the bag does not tip on X.")]
        public bool faceCameraYawOnly;

        [Tooltip("Extra offset in preview-anchor local space (closer = negative Z if +Z faces camera-forward child).")]
        public Vector3 anchorLocalOffset;
    }

    [Header("Anchor")]
    [SerializeField] Transform previewAnchor;
    [SerializeField] Vector3 anchorLocalPosition = new Vector3(0f, -0.12f, 0.85f);

    [Header("Motion")]
    [SerializeField] [Range(0.1f, 2f)] float moveDuration = 0.4f;
    [SerializeField] [Range(0.1f, 2f)] float rotationDuration = 0.4f;
    [SerializeField] bool faceCamera = true;
    [SerializeField]
    [Tooltip("Keep this off so the previewed PPE blocks the controller ray and remains the closest selectable target.")]
    bool disableCollidersDuringPreview;
    [SerializeField]
    [Tooltip("Put each PPE's renderer bounds center on the preview anchor (fixes left/right mesh pivot offsets).")]
    bool centerOnRendererBounds = true;

    [Header("Scale")]
    [Tooltip("Normalize every PPE so its longest world-axis matches this size (meters) for VR glanceability.")]
    [SerializeField] bool useUniformPreviewSize = true;
    [SerializeField] [Range(0.2f, 0.8f)] float targetPreviewMaxExtent = 0.45f;
    [SerializeField] float defaultPreviewScaleMultiplier = 1f;
    [SerializeField] List<PreviewOverride> previewOverrides = new List<PreviewOverride>
    {
        new PreviewOverride { nameContains = "PPE_ChemicalSuit", scaleMultiplier = 2.1f, yawOffsetDegrees = 0f },
        new PreviewOverride
        {
            nameContains = "PPE_ChemicalSafetyBoots",
            scaleMultiplier = 1f,
            // Toe toward HMD: 90° yaw (-90 + half turn).
            yawOffsetDegrees = 90f,
            faceCameraYawOnly = true,
        },
        new PreviewOverride { nameContains = "PPE_FaceRespirator", scaleMultiplier = 1.05f, yawOffsetDegrees = 0f },
        new PreviewOverride { nameContains = "PPE_ChemicalFaceShield", scaleMultiplier = 1f, yawOffsetDegrees = 0f },
        // First-sequence inner gloves (PPE_glrove). Shelf mesh is flipped vs outer gloves.
        new PreviewOverride
        {
            nameContains = "PPE_InnerGloves",
            scaleMultiplier = 1.1f,
            yawOffsetDegrees = 180f,
            pitchOffsetDegrees = -90f,
            faceCameraYawOnly = true,
            anchorLocalOffset = new Vector3(0f, 0.04f, 0f),
        },
        // Shelf gloves lie flat (thin on Y). Yaw-only + pitch stands them for reading.
        new PreviewOverride
        {
            nameContains = "PPE_ChemicalGloves",
            scaleMultiplier = 1.1f,
            yawOffsetDegrees = 180f,
            pitchOffsetDegrees = 90f,
            faceCameraYawOnly = true,
            anchorLocalOffset = new Vector3(0f, 0.04f, 0f),
        },
        new PreviewOverride
        {
            nameContains = "PPE_Gloves",
            scaleMultiplier = 1.1f,
            yawOffsetDegrees = 180f,
            pitchOffsetDegrees = 90f,
            faceCameraYawOnly = true,
            anchorLocalOffset = new Vector3(0f, 0.04f, 0f),
        },
        new PreviewOverride
        {
            nameContains = "ResponseBag",
            scaleMultiplier = 2.0f,
            yawOffsetDegrees = 0f,
            pitchOffsetDegrees = 12f,
            faceCameraYawOnly = true,
            anchorLocalOffset = new Vector3(0f, 0.06f, 0.0f),
        },
    };

    Camera xrCamera;
    Coroutine activeRoutine;
    PPEOutlineHighlight previewing;
    TransformSnapshot? activeSnapshot;
    Collider[] disabledColliders;
    bool[] colliderWasEnabled;
    Transform bagInspectAnchor;
    Transform activePreviewParent;

    [Header("Response bag world-fixed inspect")]
    [SerializeField] float bagInspectDistance = 1.05f;
    [SerializeField] float bagInspectHeightOffset = -0.02f;

    struct TransformSnapshot
    {
        public Transform parent;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public Vector3 worldScale;
    }

    public Transform PreviewAnchor => previewAnchor;

    void Awake()
    {
        CacheCameraAndAnchor();
    }

    void OnDisable()
    {
        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }

        // Play Mode exit destroys objects — never SetParent on dying PPE.
        if (previewing != null && activeSnapshot.HasValue && IsTransformAlive(previewing.transform))
            ForceRestoreImmediate(previewing.transform, activeSnapshot.Value);
        else
            AbandonPreviewState();
    }

    void CacheCameraAndAnchor()
    {
        if (xrCamera == null)
        {
            var origin = PPEActiveXrOrigin.Resolve();
            if (origin != null && origin.Camera != null)
                xrCamera = origin.Camera;
            else
                xrCamera = Camera.main;
        }

        if (previewAnchor == null)
        {
            if (xrCamera != null)
            {
                var existing = xrCamera.transform.Find("PPE_PreviewAnchor");
                if (existing != null)
                    previewAnchor = existing;
            }
        }

        if (previewAnchor == null && xrCamera != null)
        {
            var go = new GameObject("PPE_PreviewAnchor");
            previewAnchor = go.transform;
            previewAnchor.SetParent(xrCamera.transform, false);
            previewAnchor.localPosition = anchorLocalPosition;
            previewAnchor.localRotation = Quaternion.identity;
            previewAnchor.localScale = Vector3.one;
        }
        else if (previewAnchor != null)
        {
            if (previewAnchor.parent == null && xrCamera != null)
                previewAnchor.SetParent(xrCamera.transform, false);

            previewAnchor.localPosition = anchorLocalPosition;
            previewAnchor.localRotation = Quaternion.identity;
            previewAnchor.localScale = Vector3.one;
        }
    }

    /// <summary>
    /// Called by <see cref="PPESelectionManager"/> after sticky selection changes.
    /// </summary>
    public void HandleSelectionChanged(PPEOutlineHighlight previous, PPEOutlineHighlight next)
    {
        CacheCameraAndAnchor();

        if (activeRoutine != null)
            StopCoroutine(activeRoutine);

        activeRoutine = StartCoroutine(SelectionChangeRoutine(previous, next));
    }

    /// <summary>
    /// Called by <see cref="PPESelectionManager"/> when selection is cleared.
    /// </summary>
    public void HandleSelectionCleared(PPEOutlineHighlight previous)
    {
        CacheCameraAndAnchor();

        if (activeRoutine != null)
            StopCoroutine(activeRoutine);

        // Object is already dying (Play Mode exit / destroy) — never reparent it.
        if (previous == null || !IsTransformAlive(previous.transform))
        {
            AbandonPreviewState();
            return;
        }

        activeRoutine = StartCoroutine(ClearRoutine(previous));
    }

    /// <summary>
    /// Instantly restores slot transform (if previewing) then drops preview state
    /// so the PPE root can be deactivated without reparenting hazards.
    /// </summary>
    public void PrepareForEquipDeactivate(PPEOutlineHighlight target)
    {
        if (target == null)
            return;

        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }

        if (previewing == target && activeSnapshot.HasValue && IsTransformAlive(target.transform))
            ForceRestoreImmediate(target.transform, activeSnapshot.Value);
        else if (previewing == target)
            AbandonPreviewState();
        else
            RestoreInteractionColliders();
    }

    /// <summary>
    /// Restores a bag that is mid PPE-preview, then clears preview bookkeeping.
    /// Used before the dedicated look-into-bag inspection presentation takes over.
    /// </summary>
    public void RestoreThenAbandonBagPreview(PPEOutlineHighlight target)
    {
        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }

        if (target != null && previewing == target && activeSnapshot.HasValue && IsTransformAlive(target.transform))
            ForceRestoreImmediate(target.transform, activeSnapshot.Value);

        AbandonPreviewState();
    }

    /// <summary>
    /// Drops preview bookkeeping without touching a destroyed transform.
    /// </summary>
    public void AbandonPreviewWithoutRestore(PPEOutlineHighlight target)
    {
        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }

        if (target != null && previewing != null && previewing != target)
            return;

        AbandonPreviewState();
    }

    void AbandonPreviewState()
    {
        RestoreInteractionColliders();
        previewing = null;
        activeSnapshot = null;
        activeRoutine = null;
        activePreviewParent = null;
    }

    IEnumerator SelectionChangeRoutine(PPEOutlineHighlight previous, PPEOutlineHighlight next)
    {
        // Always restore whatever is currently mid-preview before moving the new target.
        if (previewing != null && previewing != next && activeSnapshot.HasValue)
            yield return ReturnToOriginRoutine(previewing);
        else if (previous != null && previous != next && previewing == previous && activeSnapshot.HasValue)
            yield return ReturnToOriginRoutine(previous);

        if (next != null && !IsWasteContainerHighlight(next))
            yield return MoveToPreviewRoutine(next);

        activeRoutine = null;
    }

    IEnumerator ClearRoutine(PPEOutlineHighlight previous)
    {
        if (previous == null || !IsTransformAlive(previous.transform))
        {
            AbandonPreviewState();
            yield break;
        }

        if (previewing == previous || activeSnapshot.HasValue)
            yield return ReturnToOriginRoutine(previous);

        activeRoutine = null;
    }

    IEnumerator MoveToPreviewRoutine(PPEOutlineHighlight highlight)
    {
        CacheCameraAndAnchor();
        if (highlight == null || !IsTransformAlive(highlight.transform))
            yield break;

        var t = highlight.transform;
        // Same camera-follow preview anchor as other PPE so the bag stays in front of the HMD.
        var previewParent = previewAnchor;
        if (previewParent == null)
            yield break;

        activePreviewParent = previewParent;

        var snapshot = CaptureSnapshot(t);
        activeSnapshot = snapshot;
        previewing = highlight;

        if (disableCollidersDuringPreview)
            DisableInteractionColliders(t);

        GetOverride(t.name, out var scaleMul, out var yawOffset, out var pitchOffset, out var anchorOffset, out var yawOnly);
        var targetWorldScale = ComputePreviewWorldScale(t, snapshot.worldScale, scaleMul);

        var startRot = t.rotation;
        var startScale = t.lossyScale;
        var startCenter = TryGetRendererBounds(t, out var startBounds) ? startBounds.center : t.position;

        // Final rotation is fixed when selection begins (not updated every frame after settle).
        var targetRot = faceCamera
            ? ComputeFaceCameraRotation(previewParent.position, yawOffset, pitchOffset, yawOnly)
            : startRot;

        // Parent under anchor while keeping current world pose, then animate.
        if (!TrySetParent(t, previewParent, true))
        {
            AbandonPreviewState();
            yield break;
        }

        var duration = Mathf.Max(moveDuration, rotationDuration, 0.01f);
        var elapsed = 0f;
        while (elapsed < duration)
        {
            if (!IsTransformAlive(t) || activePreviewParent == null)
            {
                AbandonPreviewState();
                yield break;
            }

            elapsed += Time.deltaTime;
            var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            var moveU = moveDuration <= 0.01f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / moveDuration));
            var rotU = rotationDuration <= 0.01f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / rotationDuration));

            var targetCenter = GetPreviewTargetCenter(anchorOffset);
            var desiredCenter = Vector3.Lerp(startCenter, targetCenter, moveU);
            t.rotation = Quaternion.Slerp(startRot, targetRot, rotU);
            SetWorldScale(t, Vector3.Lerp(startScale, targetWorldScale, u));
            ApplyPreviewPosition(t, desiredCenter);
            yield return null;
        }

        if (!IsTransformAlive(t) || activePreviewParent == null)
        {
            AbandonPreviewState();
            yield break;
        }

        t.rotation = targetRot;
        SetWorldScale(t, targetWorldScale);
        ApplyPreviewPosition(t, GetPreviewTargetCenter(anchorOffset));
    }

    static bool IsResponseBagHighlight(PPEOutlineHighlight target)
    {
        if (target == null)
            return false;
        return target.GetComponent<ResponseBagPrepareInteractable>() != null
            || target.GetComponentInParent<ResponseBagPrepareInteractable>() != null;
    }

    static bool IsWasteContainerHighlight(PPEOutlineHighlight target)
    {
        if (target == null)
            return false;
        return target.GetComponent<WasteContainerPrepareInteractable>() != null
            || target.GetComponentInParent<WasteContainerPrepareInteractable>() != null;
    }

    Transform EnsureBagInspectAnchorPlaced()
    {
        CacheCameraAndAnchor();
        if (xrCamera == null)
            return null;

        if (bagInspectAnchor == null)
        {
            var go = GameObject.Find("ResponseBag_InspectAnchor");
            if (go == null)
                go = new GameObject("ResponseBag_InspectAnchor");
            bagInspectAnchor = go.transform;
            bagInspectAnchor.SetParent(null, true);
        }

        // Place once at selection time — does not follow the HMD afterward.
        var cam = xrCamera.transform;
        var flat = cam.forward;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f)
            flat = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        if (flat.sqrMagnitude < 0.0001f)
            flat = Vector3.forward;
        flat.Normalize();

        bagInspectAnchor.position = cam.position + flat * bagInspectDistance + Vector3.up * bagInspectHeightOffset;
        bagInspectAnchor.rotation = Quaternion.LookRotation(flat, Vector3.up);
        bagInspectAnchor.localScale = Vector3.one;
        return bagInspectAnchor;
    }

    Vector3 GetPreviewTargetCenter(Vector3 anchorLocalOffset)
    {
        var parent = activePreviewParent != null ? activePreviewParent : previewAnchor;
        if (parent == null)
            return Vector3.zero;

        if (anchorLocalOffset.sqrMagnitude < 0.0001f)
            return parent.position;

        // Offset in the anchor local frame so "closer" stays view-relative at place time.
        return parent.TransformPoint(anchorLocalOffset);
    }

    void ApplyPreviewPosition(Transform t, Vector3 desiredVisualCenter)
    {
        t.position = desiredVisualCenter;

        if (!centerOnRendererBounds)
            return;

        if (!TryGetRendererBounds(t, out var bounds))
            return;

        t.position += desiredVisualCenter - bounds.center;
    }

    static bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
            return false;

        var renderers = root.GetComponentsInChildren<Renderer>();
        var found = false;
        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            if (!found)
            {
                bounds = r.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return found;
    }

    IEnumerator ReturnToOriginRoutine(PPEOutlineHighlight highlight)
    {
        if (highlight == null || !activeSnapshot.HasValue || !IsTransformAlive(highlight.transform))
        {
            AbandonPreviewState();
            yield break;
        }

        var t = highlight.transform;
        var snapshot = activeSnapshot.Value;

        var startPos = t.position;
        var startRot = t.rotation;
        var startScale = t.lossyScale;

        var targetPos = snapshot.parent != null
            ? snapshot.parent.TransformPoint(snapshot.localPosition)
            : snapshot.localPosition;
        var targetRot = snapshot.parent != null
            ? snapshot.parent.rotation * snapshot.localRotation
            : snapshot.localRotation;
        var targetScale = snapshot.worldScale;

        // Animate in world space, then re-parent exactly.
        if (!TrySetParent(t, null, true))
        {
            AbandonPreviewState();
            yield break;
        }

        var duration = Mathf.Max(moveDuration, rotationDuration, 0.01f);
        var elapsed = 0f;
        while (elapsed < duration)
        {
            if (!IsTransformAlive(t))
            {
                AbandonPreviewState();
                yield break;
            }

            elapsed += Time.deltaTime;
            var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            var moveU = moveDuration <= 0.01f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / moveDuration));
            var rotU = rotationDuration <= 0.01f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / rotationDuration));

            // Refresh target in case shelf parent moved (should be static).
            targetPos = snapshot.parent != null
                ? snapshot.parent.TransformPoint(snapshot.localPosition)
                : snapshot.localPosition;
            targetRot = snapshot.parent != null
                ? snapshot.parent.rotation * snapshot.localRotation
                : snapshot.localRotation;

            t.position = Vector3.Lerp(startPos, targetPos, moveU);
            t.rotation = Quaternion.Slerp(startRot, targetRot, rotU);
            SetWorldScale(t, Vector3.Lerp(startScale, targetScale, u));
            yield return null;
        }

        ForceRestoreImmediate(t, snapshot);
    }

    void ForceRestoreImmediate(Transform t, TransformSnapshot snapshot)
    {
        if (!IsTransformAlive(t))
        {
            AbandonPreviewState();
            return;
        }

        if (!TrySetParent(t, snapshot.parent, false))
        {
            AbandonPreviewState();
            return;
        }

        t.localPosition = snapshot.localPosition;
        t.localRotation = snapshot.localRotation;
        t.localScale = snapshot.localScale;
        RestoreInteractionColliders();

        if (previewing != null && previewing.transform == t)
            previewing = null;
        activeSnapshot = null;
    }

    static bool IsTransformAlive(Transform t)
    {
        // Unity overloads == for destroyed objects.
        return t != null && t.gameObject != null;
    }

    static bool TrySetParent(Transform t, Transform parent, bool worldPositionStays)
    {
        if (!IsTransformAlive(t))
            return false;

        try
        {
            t.SetParent(parent, worldPositionStays);
            return true;
        }
        catch (UnityException)
        {
            return false;
        }
    }

    TransformSnapshot CaptureSnapshot(Transform t)
    {
        return new TransformSnapshot
        {
            parent = t.parent,
            localPosition = t.localPosition,
            localRotation = t.localRotation,
            localScale = t.localScale,
            worldScale = t.lossyScale,
        };
    }

    Quaternion ComputeFaceCameraRotation(
        Vector3 previewWorldPosition,
        float yawOffsetDegrees,
        float pitchOffsetDegrees = 0f,
        bool yawOnly = false)
    {
        if (xrCamera == null)
            return Quaternion.identity;

        Quaternion look;
        if (yawOnly)
        {
            // Keep world-up so open bags are not tipped ~90° on X.
            var flat = -xrCamera.transform.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f)
                flat = Vector3.forward;
            look = Quaternion.LookRotation(flat.normalized, Vector3.up);
        }
        else
        {
            // Preview sits in front of the HMD. Aim object +Z toward the player (-camera.forward)
            // so the front of the mesh faces the user. yaw/pitch correct FBX axis and bag opening.
            look = Quaternion.LookRotation(-xrCamera.transform.forward, xrCamera.transform.up);
        }

        if (Mathf.Abs(yawOffsetDegrees) > 0.01f)
            look *= Quaternion.Euler(0f, yawOffsetDegrees, 0f);
        if (Mathf.Abs(pitchOffsetDegrees) > 0.01f)
            look *= Quaternion.Euler(pitchOffsetDegrees, 0f, 0f);
        return look;
    }

    Vector3 ComputePreviewWorldScale(Transform t, Vector3 snapshotWorldScale, float scaleMul)
    {
        scaleMul = Mathf.Max(0.01f, scaleMul);

        if (!useUniformPreviewSize || t == null)
            return snapshotWorldScale * scaleMul;

        if (!TryGetRendererBounds(t, out var bounds))
            return snapshotWorldScale * scaleMul;

        var maxExtent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (maxExtent < 0.001f)
            return snapshotWorldScale * scaleMul;

        // bounds are world-sized at the current lossyScale (== snapshot when selection starts).
        var uniformFactor = targetPreviewMaxExtent / maxExtent;
        return snapshotWorldScale * uniformFactor * scaleMul;
    }

    void GetOverride(
        string objectName,
        out float scaleMultiplier,
        out float yawOffsetDegrees,
        out float pitchOffsetDegrees,
        out Vector3 anchorLocalOffset,
        out bool faceCameraYawOnly)
    {
        scaleMultiplier = defaultPreviewScaleMultiplier;
        yawOffsetDegrees = 0f;
        pitchOffsetDegrees = 0f;
        anchorLocalOffset = Vector3.zero;
        faceCameraYawOnly = false;

        if (string.IsNullOrEmpty(objectName) || previewOverrides == null)
            return;

        for (var i = 0; i < previewOverrides.Count; i++)
        {
            var rule = previewOverrides[i];
            if (rule == null || string.IsNullOrEmpty(rule.nameContains))
                continue;
            if (objectName.IndexOf(rule.nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            scaleMultiplier = rule.scaleMultiplier;
            yawOffsetDegrees = rule.yawOffsetDegrees;
            pitchOffsetDegrees = rule.pitchOffsetDegrees;
            anchorLocalOffset = rule.anchorLocalOffset;
            faceCameraYawOnly = rule.faceCameraYawOnly;
            return;
        }
    }

    static void SetWorldScale(Transform t, Vector3 worldScale)
    {
        if (t.parent == null)
        {
            t.localScale = worldScale;
            return;
        }

        var parentScale = t.parent.lossyScale;
        t.localScale = new Vector3(
            SafeDivide(worldScale.x, parentScale.x),
            SafeDivide(worldScale.y, parentScale.y),
            SafeDivide(worldScale.z, parentScale.z));
    }

    static float SafeDivide(float numerator, float denominator)
    {
        return Mathf.Abs(denominator) < 0.00001f ? numerator : numerator / denominator;
    }

    void DisableInteractionColliders(Transform root)
    {
        RestoreInteractionColliders();

        disabledColliders = root.GetComponentsInChildren<Collider>(true);
        colliderWasEnabled = new bool[disabledColliders.Length];
        for (var i = 0; i < disabledColliders.Length; i++)
        {
            var col = disabledColliders[i];
            if (col == null)
                continue;

            // Keep bag-content hover colliders active so names/outlines still work while previewed.
            if (col.GetComponentInParent<ResponseBagContentHoverItem>() != null)
            {
                colliderWasEnabled[i] = col.enabled;
                continue;
            }

            colliderWasEnabled[i] = col.enabled;
            col.enabled = false;
        }
    }

    void RestoreInteractionColliders()
    {
        if (disabledColliders == null)
            return;

        for (var i = 0; i < disabledColliders.Length; i++)
        {
            var col = disabledColliders[i];
            if (col == null)
                continue;
            col.enabled = colliderWasEnabled != null && i < colliderWasEnabled.Length && colliderWasEnabled[i];
        }

        disabledColliders = null;
        colliderWasEnabled = null;
    }
}
