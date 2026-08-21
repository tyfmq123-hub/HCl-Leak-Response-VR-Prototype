using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Keeps cabinet PPE at its authored pose until a real XR Hand grab starts.
/// Near-grab assist only enlarges interaction detection; it never moves the item.
/// Compatible with XRI 3.4 XRGrabInteractable kinematic movement.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public class PPEStoredDisplayState : MonoBehaviour
{
    public enum StorageState
    {
        Stored,
        Held,
    }

    const string AssistVolumeName = "NearGrabAssistVolume";
    const float DefaultAssistDistance = 0.05f;

    [Header("PPE Stored State")]
    [SerializeField] bool startStored = true;
    [SerializeField] Transform displayAnchor;
    [SerializeField] bool restoreStoredPoseOnStart = true;

    [Header("Near Grab Assist")]
    [SerializeField] bool enableAssist = true;
    [SerializeField, Range(0.04f, 0.07f)] float assistDistance = DefaultAssistDistance;
    [SerializeField]
    [Tooltip("When false, this component never builds a world-AABB assist box. Use for rotated glove roots and authored primitive assists.")]
    bool generateAssistFromBounds = true;
    [SerializeField] Collider assistCollider;
    [SerializeField]
    [Tooltip("Existing PPEInteractableOutline already provides hover. Enable only if you want that outline treated as assist feedback; this component never adds glow, icons, or lasers.")]
    bool optionalHoverFeedback;

    PPEHandGrabbable grabbable;
    Rigidbody body;
    Vector3 storedLocalPosition;
    Quaternion storedLocalRotation;
    Vector3 storedWorldPosition;
    Quaternion storedWorldRotation;
    bool storedHadParent;
    bool capturedOriginalBody;
    bool originalUseGravity;
    bool originalIsKinematic;
    RigidbodyInterpolation originalInterpolation;
    CollisionDetectionMode originalCollisionMode;
    bool gloveMeshCollidersDisabled;

    public StorageState State { get; private set; } = StorageState.Stored;
    public bool IsStored => State == StorageState.Stored;
    public float AssistDistance => assistDistance;

    void Awake()
    {
        grabbable = GetComponent<PPEHandGrabbable>();
        body = GetComponent<Rigidbody>();

        if (GetComponent<PPEGlovePairCoordinator>() != null)
        {
            enableAssist = false;
            generateAssistFromBounds = false;
            CaptureStoredPose();
            CaptureOriginalBody();
            return;
        }

        if (GetComponent<PPEGlovePairMember>() != null)
            generateAssistFromBounds = false;
        else
            PPEPrimitiveGrabVolumes.EnsureForWearable(gameObject);

        CaptureStoredPose();
        CaptureOriginalBody();
        SanitizeDenseConvexMeshColliders();
        EnsureNearGrabAssistVolume();

        if (grabbable != null)
            grabbable.attachEaseInTime = 0f;

        if (startStored)
            ApplyStoredLock(restoreStoredPoseOnStart);
    }

    void OnEnable()
    {
        if (grabbable == null)
            grabbable = GetComponent<PPEHandGrabbable>();
        if (grabbable == null)
            return;

        grabbable.GrabStarting += ReleaseStoredForGrab;
        grabbable.OnGrabbed += OnGrabbed;
        if (assistCollider != null)
            RegisterAssistWithGrab(assistCollider);
    }

    void OnDisable()
    {
        if (grabbable == null)
            return;

        grabbable.GrabStarting -= ReleaseStoredForGrab;
        grabbable.OnGrabbed -= OnGrabbed;
    }

    void FixedUpdate()
    {
        if (State != StorageState.Stored || body == null)
            return;

        HoldStoredPose();
    }

    public void ApplyStoredLock(bool restorePose)
    {
        State = StorageState.Stored;

        if (restorePose)
            RestoreStoredPose();

        if (body == null)
            return;

        ClearNonKinematicVelocity(body);
        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.constraints = RigidbodyConstraints.None;
        body.Sleep();
        DisableThrowIfInnerGlove();
    }

    public void ReleaseStoredForGrab()
    {
        if (State != StorageState.Stored)
            return;

        RestoreStoredPose();
        if (body != null)
        {
            ClearNonKinematicVelocity(body);
            // Stay kinematic so XRI 3.4 MovementType.Kinematic can take over
            // without a gravity frame or pose jump.
            body.useGravity = false;
            body.isKinematic = true;
            DisableThrowIfInnerGlove();
            if (capturedOriginalBody)
            {
                body.interpolation = originalInterpolation;
                body.collisionDetectionMode = originalCollisionMode;
            }
        }

        State = StorageState.Held;
    }

    void OnGrabbed(PPEGrabHand _)
    {
        State = StorageState.Held;
    }

    void CaptureStoredPose()
    {
        storedHadParent = transform.parent != null;
        storedLocalPosition = transform.localPosition;
        storedLocalRotation = transform.localRotation;
        if (displayAnchor != null)
        {
            storedWorldPosition = displayAnchor.position;
            storedWorldRotation = displayAnchor.rotation;
        }
        else
        {
            storedWorldPosition = transform.position;
            storedWorldRotation = transform.rotation;
        }
    }

    void CaptureOriginalBody()
    {
        if (body == null || capturedOriginalBody)
            return;

        originalUseGravity = body.useGravity;
        originalIsKinematic = body.isKinematic;
        originalInterpolation = body.interpolation;
        originalCollisionMode = body.collisionDetectionMode;
        capturedOriginalBody = true;
    }

    public bool AuthoredUseGravity => originalUseGravity;
    public bool AuthoredIsKinematic => originalIsKinematic;

    public void DisableGeneratedWorldAabbAssist()
    {
        generateAssistFromBounds = false;
        enableAssist = assistCollider != null;
    }

    public void UseAuthoredAssistCollider(Collider volume)
    {
        generateAssistFromBounds = false;
        assistCollider = volume;
        enableAssist = volume != null;
        if (volume != null)
        {
            volume.isTrigger = true;
            volume.enabled = true;
            RegisterAssistWithGrab(volume);
        }
    }

    void RestoreStoredPose()
    {
        if (displayAnchor != null)
        {
            transform.SetPositionAndRotation(displayAnchor.position, displayAnchor.rotation);
            return;
        }

        if (storedHadParent && transform.parent != null)
        {
            transform.localPosition = storedLocalPosition;
            transform.localRotation = storedLocalRotation;
            return;
        }

        transform.SetPositionAndRotation(storedWorldPosition, storedWorldRotation);
    }

    void HoldStoredPose()
    {
        RestoreStoredPose();
        if (body == null)
            return;

        ClearNonKinematicVelocity(body);
        if (!body.isKinematic)
            body.isKinematic = true;
        DisableThrowIfInnerGlove();
        if (body.useGravity)
            body.useGravity = false;
    }

    void DisableThrowIfInnerGlove()
    {
        if (grabbable == null)
            grabbable = GetComponent<PPEHandGrabbable>();
        grabbable?.ApplyInnerGloveThrowPolicy();
    }

    static void ClearNonKinematicVelocity(Rigidbody rigidbody)
    {
        if (rigidbody == null || rigidbody.isKinematic)
            return;

        rigidbody.linearVelocity = Vector3.zero;
        rigidbody.angularVelocity = Vector3.zero;
    }

    void EnsureNearGrabAssistVolume()
    {
        if (!enableAssist)
            return;

        var volume = assistCollider != null
            ? assistCollider
            : FindExistingAssistCollider();
        if (volume == null && generateAssistFromBounds)
            volume = CreateAssistCollider();
        if (volume == null)
            return;

        assistCollider = volume;
        volume.isTrigger = true;
        volume.enabled = true;
        RegisterAssistWithGrab(volume);
    }

    Collider FindExistingAssistCollider()
    {
        var existing = transform.Find(AssistVolumeName);
        return existing != null ? existing.GetComponent<Collider>() : null;
    }

    Collider CreateAssistCollider()
    {
        var existing = transform.Find(AssistVolumeName);
        var volumeRoot = existing != null ? existing.gameObject : new GameObject(AssistVolumeName);
        if (existing == null)
        {
            volumeRoot.transform.SetParent(transform, false);
            volumeRoot.transform.localPosition = Vector3.zero;
            volumeRoot.transform.localRotation = Quaternion.identity;
            volumeRoot.transform.localScale = Vector3.one;
            volumeRoot.layer = gameObject.layer;
        }

        var box = volumeRoot.GetComponent<BoxCollider>();
        if (box == null)
            box = volumeRoot.AddComponent<BoxCollider>();

        ComputeAssistBox(out var center, out var size);
        box.center = center;
        box.size = size;
        box.isTrigger = true;
        return box;
    }

    void ComputeAssistBox(out Vector3 localCenter, out Vector3 localSize)
    {
        var bounds = new Bounds(transform.position, Vector3.zero);
        var hasBounds = EncapsulatePhysicalColliders(ref bounds);
        if (!hasBounds)
            hasBounds = EncapsulateRenderers(ref bounds);
        if (!hasBounds)
            bounds = new Bounds(transform.position, Vector3.one * 0.12f);

        var padded = bounds;
        padded.Expand(assistDistance * 2f);

        var localMin = transform.InverseTransformPoint(padded.min);
        var localMax = transform.InverseTransformPoint(padded.max);
        localCenter = (localMin + localMax) * 0.5f;
        localSize = new Vector3(
            Mathf.Abs(localMax.x - localMin.x),
            Mathf.Abs(localMax.y - localMin.y),
            Mathf.Abs(localMax.z - localMin.z));
        localSize = Vector3.Max(localSize, Vector3.one * 0.04f);
    }

    bool EncapsulatePhysicalColliders(ref Bounds bounds)
    {
        var found = false;
        var colliders = GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var col = colliders[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;
            if (col.transform.name == AssistVolumeName)
                continue;
            if (!found)
            {
                bounds = col.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(col.bounds);
            }
        }

        return found;
    }

    bool EncapsulateRenderers(ref Bounds bounds)
    {
        var found = false;
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    void RegisterAssistWithGrab(Collider volume)
    {
        if (grabbable == null || volume == null)
            return;

        if (!grabbable.colliders.Contains(volume))
            grabbable.colliders.Add(volume);
    }

    void SanitizeDenseConvexMeshColliders()
    {
        if (gloveMeshCollidersDisabled)
            return;

        var meshColliders = GetComponentsInChildren<MeshCollider>(true);
        var disabled = 0;
        for (var i = 0; i < meshColliders.Length; i++)
        {
            var meshCollider = meshColliders[i];
            if (meshCollider == null || !meshCollider.convex)
                continue;
            if (meshCollider.sharedMesh == null)
                continue;
            if (meshCollider.sharedMesh.vertexCount <= 255)
                continue;

            meshCollider.enabled = false;
            disabled++;
        }

        if (disabled == 0)
            return;

        gloveMeshCollidersDisabled = true;
        RemoveDisabledCollidersFromGrab();
        EnsureFallbackPhysicalCollider();
    }

    void RemoveDisabledCollidersFromGrab()
    {
        if (grabbable == null)
            return;

        for (var i = grabbable.colliders.Count - 1; i >= 0; i--)
        {
            var col = grabbable.colliders[i];
            if (col == null || !col.enabled)
                grabbable.colliders.RemoveAt(i);
        }
    }

    void EnsureFallbackPhysicalCollider()
    {
        var colliders = GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var col = colliders[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;
            if (col.transform.name == AssistVolumeName)
                continue;
            return;
        }

        var box = gameObject.GetComponent<BoxCollider>();
        if (box == null)
            box = gameObject.AddComponent<BoxCollider>();
        ComputeAssistBox(out var center, out var size);
        var shrink = assistDistance * 2f;
        box.center = center;
        box.size = Vector3.Max(size - Vector3.one * shrink, Vector3.one * 0.04f);
        box.isTrigger = false;

        if (grabbable != null && !grabbable.colliders.Contains(box))
            grabbable.colliders.Add(box);
    }

#if UNITY_EDITOR
    public void EditorApplyDefaults()
    {
        startStored = true;
        restoreStoredPoseOnStart = true;
        enableAssist = true;
        generateAssistFromBounds = true;
        assistDistance = DefaultAssistDistance;
        optionalHoverFeedback = false;
    }

    public void EditorApplyPairMemberDefaults()
    {
        startStored = true;
        restoreStoredPoseOnStart = true;
        enableAssist = true;
        generateAssistFromBounds = false;
        assistDistance = DefaultAssistDistance;
        optionalHoverFeedback = false;
    }

    void OnDrawGizmosSelected()
    {
        if (assistCollider != null && enableAssist)
        {
            Gizmos.color = new Color(0.2f, 0.75f, 1f, 0.35f);
            DrawColliderGizmo(assistCollider);
        }

        var colliders = GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var col = colliders[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;
            Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.2f);
            DrawColliderGizmo(col);
        }

        Gizmos.color = State == StorageState.Stored
            ? new Color(1f, 0.85f, 0.15f, 0.9f)
            : new Color(0.5f, 0.5f, 0.5f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, 0.02f);
    }

    static void DrawColliderGizmo(Collider col)
    {
        var box = col as BoxCollider;
        if (box != null)
        {
            var matrix = col.transform.localToWorldMatrix;
            Gizmos.matrix = matrix;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;
            return;
        }

        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
#endif
}
