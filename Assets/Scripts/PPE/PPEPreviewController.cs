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
    }

    [Header("Anchor")]
    [SerializeField] Transform previewAnchor;
    [SerializeField] Vector3 anchorLocalPosition = new Vector3(0f, -0.2f, 0.65f);

    [Header("Motion")]
    [SerializeField] [Range(0.1f, 2f)] float moveDuration = 0.4f;
    [SerializeField] [Range(0.1f, 2f)] float rotationDuration = 0.4f;
    [SerializeField] bool faceCamera = true;
    [SerializeField] bool disableCollidersDuringPreview = true;
    [SerializeField]
    [Tooltip("Put each PPE's renderer bounds center on the preview anchor (fixes left/right mesh pivot offsets).")]
    bool centerOnRendererBounds = true;

    [Header("Scale")]
    [SerializeField] float defaultPreviewScaleMultiplier = 1f;
    [SerializeField] List<PreviewOverride> previewOverrides = new List<PreviewOverride>
    {
        new PreviewOverride { nameContains = "PPE_ChemicalSuit", scaleMultiplier = 0.28f, yawOffsetDegrees = 0f },
        new PreviewOverride { nameContains = "PPE_ChemicalSafetyBoots", scaleMultiplier = 0.55f, yawOffsetDegrees = 0f },
        new PreviewOverride { nameContains = "PPE_FaceRespirator", scaleMultiplier = 1.05f, yawOffsetDegrees = 0f },
        new PreviewOverride { nameContains = "PPE_ChemicalFaceShield", scaleMultiplier = 0.85f, yawOffsetDegrees = 0f },
        new PreviewOverride { nameContains = "PPE_ChemicalGloves", scaleMultiplier = 1.05f, yawOffsetDegrees = 0f },
        new PreviewOverride { nameContains = "PPE_Gloves", scaleMultiplier = 1.05f, yawOffsetDegrees = 0f },
    };

    Camera xrCamera;
    Coroutine activeRoutine;
    PPEOutlineHighlight previewing;
    TransformSnapshot? activeSnapshot;
    Collider[] disabledColliders;
    bool[] colliderWasEnabled;

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
            var origin = FindFirstObjectByType<XROrigin>();
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
    }

    IEnumerator SelectionChangeRoutine(PPEOutlineHighlight previous, PPEOutlineHighlight next)
    {
        // Always restore whatever is currently mid-preview before moving the new target.
        if (previewing != null && previewing != next && activeSnapshot.HasValue)
            yield return ReturnToOriginRoutine(previewing);
        else if (previous != null && previous != next && previewing == previous && activeSnapshot.HasValue)
            yield return ReturnToOriginRoutine(previous);

        if (next != null)
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
        if (highlight == null || previewAnchor == null || !IsTransformAlive(highlight.transform))
            yield break;

        var t = highlight.transform;
        var snapshot = CaptureSnapshot(t);
        activeSnapshot = snapshot;
        previewing = highlight;

        if (disableCollidersDuringPreview)
            DisableInteractionColliders(t);

        GetOverride(t.name, out var scaleMul, out var yawOffset);
        var targetWorldScale = snapshot.worldScale * scaleMul;

        var startRot = t.rotation;
        var startScale = t.lossyScale;
        var startCenter = TryGetRendererBounds(t, out var startBounds) ? startBounds.center : t.position;

        // Final rotation is fixed when selection begins (not updated every frame after settle).
        var targetRot = faceCamera
            ? ComputeFaceCameraRotation(previewAnchor.position, yawOffset)
            : startRot;

        // Parent under anchor while keeping current world pose, then animate.
        if (!TrySetParent(t, previewAnchor, true))
        {
            AbandonPreviewState();
            yield break;
        }

        var duration = Mathf.Max(moveDuration, rotationDuration, 0.01f);
        var elapsed = 0f;
        while (elapsed < duration)
        {
            if (!IsTransformAlive(t) || previewAnchor == null)
            {
                AbandonPreviewState();
                yield break;
            }

            elapsed += Time.deltaTime;
            var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            var moveU = moveDuration <= 0.01f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / moveDuration));
            var rotU = rotationDuration <= 0.01f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / rotationDuration));

            // Follow the live anchor; put mesh visual center on the shared slot (not the FBX pivot).
            var desiredCenter = Vector3.Lerp(startCenter, previewAnchor.position, moveU);
            t.rotation = Quaternion.Slerp(startRot, targetRot, rotU);
            SetWorldScale(t, Vector3.Lerp(startScale, targetWorldScale, u));
            ApplyPreviewPosition(t, desiredCenter);
            yield return null;
        }

        if (!IsTransformAlive(t) || previewAnchor == null)
        {
            AbandonPreviewState();
            yield break;
        }

        t.rotation = targetRot;
        SetWorldScale(t, targetWorldScale);
        ApplyPreviewPosition(t, previewAnchor.position);
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

    Quaternion ComputeFaceCameraRotation(Vector3 previewWorldPosition, float yawOffsetDegrees)
    {
        if (xrCamera == null)
            return Quaternion.identity;

        // Preview sits in front of the HMD. Aim object +Z toward the player (-camera.forward)
        // so the front of the mesh faces the user. yawOffset corrects FBX forward-axis quirks.
        var look = Quaternion.LookRotation(-xrCamera.transform.forward, xrCamera.transform.up);
        if (Mathf.Abs(yawOffsetDegrees) > 0.01f)
            look *= Quaternion.Euler(0f, yawOffsetDegrees, 0f);
        return look;
    }

    void GetOverride(string objectName, out float scaleMultiplier, out float yawOffsetDegrees)
    {
        scaleMultiplier = defaultPreviewScaleMultiplier;
        yawOffsetDegrees = 0f;

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
