using System.Collections;
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
        Released,
        Recovered,
        Equipped,
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
    PPEHandGrabbable grabbable;
    Rigidbody body;
    Vector3 storedLocalPosition;
    Quaternion storedLocalRotation;
    Vector3 storedLocalScale;
    Vector3 storedWorldPosition;
    Quaternion storedWorldRotation;
    Transform storedParent;
    bool storedHadParent;
    bool capturedOriginalBody;
    bool originalUseGravity;
    bool originalIsKinematic;
    RigidbodyInterpolation originalInterpolation;
    CollisionDetectionMode originalCollisionMode;
    bool gloveMeshCollidersDisabled;
    Collider[] recoveryColliders;
    Renderer[] recoveryRenderers;
    Coroutine recoveryRoutine;
    PPECabinetPhysics activeCabinetShelfSupport;
    float cabinetShelfSupportUntil;
    float storedLowestPhysicalPointY;
    bool capturedStoredLowestPoint;

    const float CabinetShelfSupportGuardSeconds = 1.25f;
    const float CabinetFallRecoveryDistance = 0.12f;
    const float CabinetFallRecoveryDelay = 0.35f;

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
        CacheRecoveryBoundsSources();
        storedLowestPhysicalPointY = LowestPhysicalPointY();
        capturedStoredLowestPoint = true;

        if (grabbable != null)
            grabbable.attachEaseInTime = 0f;

        if (startStored)
            ApplyStoredLock(restoreStoredPoseOnStart);

        IgnorePlayerLocomotionCollisions();
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
        CancelRecoveryMonitor();
        if (grabbable == null)
            return;

        grabbable.GrabStarting -= ReleaseStoredForGrab;
        grabbable.OnGrabbed -= OnGrabbed;
    }

    void FixedUpdate()
    {
        if (State == StorageState.Released && activeCabinetShelfSupport != null)
        {
            if (Time.time <= cabinetShelfSupportUntil)
                activeCabinetShelfSupport.TrySupportReleasedItem(transform, body);
            else
                CancelCabinetShelfSupport();
            return;
        }

        if (State != StorageState.Stored || body == null)
            return;

        HoldStoredPose();
    }

    public void ApplyStoredLock(bool restorePose)
    {
        CancelRecoveryMonitor();
        CancelCabinetShelfSupport();
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
        DisableWearableThrow();
        IgnorePlayerLocomotionCollisions();
    }

    /// <summary>Alias for <see cref="RestoreToStoredPose"/>.</summary>
    public void ResetToStoredPose() => RestoreToStoredPose();

    /// <summary>Cabinet pose + kinematic lock. Used at start and on full room reset only.</summary>
    public void RestoreToStoredPose()
    {
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        if (grabbable == null)
            grabbable = GetComponent<PPEHandGrabbable>();
        if (grabbable != null)
        {
            grabbable.enabled = true;
            DisableWearableThrow();
        }

        RestoreStoredHierarchy();
        ApplyStoredLock(restorePose: true);
    }

    public void NotifyEquipped()
    {
        CancelRecoveryMonitor();
        CancelCabinetShelfSupport();
        State = StorageState.Equipped;
        DisableWearableThrow();
        if (body == null)
            return;
        ClearNonKinematicVelocity(body);
        body.useGravity = false;
        body.isKinematic = true;
    }

    public void NotifyInvalidReleaseIfHeld()
    {
        if (State != StorageState.Held)
            return;

        // A cabinet wearable is either equipped by the release attempt or put
        // straight back in its authored slot. Do not hand an invalid release to
        // gravity: a small PPE can tunnel through the visual FBX shelves and
        // become unreachable.
        RestoreToStoredPose();
    }

    public void ApplyReleasedPhysics()
    {
        CancelRecoveryMonitor();
        CancelCabinetShelfSupport();
        State = StorageState.Released;
        DisableWearableThrow();
        if (body != null)
        {
            if (body.isKinematic)
                body.isKinematic = false;
            ClearNonKinematicVelocity(body);
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.None;
            IgnorePlayerLocomotionCollisions();
        }

        var cabinet = ResolveCabinetPhysics();
        if (cabinet != null && cabinet.TrySupportReleasedItem(transform, body))
        {
            activeCabinetShelfSupport = cabinet;
            cabinetShelfSupportUntil = Time.time + CabinetShelfSupportGuardSeconds;
        }

        LogPhysicsSnapshot("PPE Physics After Release");
        StartRecoveryMonitor();
    }

    public void ReleaseStoredForGrab()
    {
        if (State != StorageState.Stored &&
            State != StorageState.Released &&
            State != StorageState.Recovered)
            return;

        LogPhysicsSnapshot("PPE Physics Before Grab");
        CancelRecoveryMonitor();
        CancelCabinetShelfSupport();
        if (State == StorageState.Stored)
            RestoreStoredPose();
        if (body != null)
        {
            ClearNonKinematicVelocity(body);
            // Stay kinematic so XRI 3.4 MovementType.Kinematic can take over
            // without a gravity frame or pose jump.
            body.useGravity = false;
            body.isKinematic = true;
            DisableWearableThrow();
            if (capturedOriginalBody)
            {
                body.interpolation = originalInterpolation;
                body.collisionDetectionMode = originalCollisionMode;
            }
        }

        State = StorageState.Held;
        IgnorePlayerLocomotionCollisions();
    }

    void OnGrabbed(PPEGrabHand _)
    {
        CancelRecoveryMonitor();
        CancelCabinetShelfSupport();
        State = StorageState.Held;
    }

    void StartRecoveryMonitor()
    {
        var cabinet = ResolveCabinetPhysics();
        if (!SupportsUnreachableRecovery() && cabinet == null)
            return;

        var recoveryPoint = PPEDroppedItemRecoveryPoint.Resolve();
        if (recoveryPoint == null && cabinet == null)
            return;

        recoveryRoutine = StartCoroutine(MonitorUnreachableRelease(recoveryPoint, cabinet != null));
    }

    void CancelRecoveryMonitor()
    {
        if (recoveryRoutine == null)
            return;
        StopCoroutine(recoveryRoutine);
        recoveryRoutine = null;
    }

    void CancelCabinetShelfSupport()
    {
        activeCabinetShelfSupport = null;
        cabinetShelfSupportUntil = 0f;
    }

    PPECabinetPhysics ResolveCabinetPhysics()
    {
        var cabinet = GetComponentInParent<PPECabinetPhysics>();
        if (cabinet != null)
            return cabinet;

        return storedParent != null
            ? storedParent.GetComponentInParent<PPECabinetPhysics>()
            : null;
    }

    IEnumerator MonitorUnreachableRelease(
        PPEDroppedItemRecoveryPoint recoveryPoint,
        bool restoreToStoredShelf)
    {
        var belowThresholdSince = -1f;
        var belowStoredShelfSince = -1f;
        var wait = new WaitForSeconds(recoveryPoint != null ? recoveryPoint.CheckInterval : 0.10f);
        while (State == StorageState.Released)
        {
            if (grabbable != null && (grabbable.IsGrabbed || grabbable.isSelected))
            {
                recoveryRoutine = null;
                yield break;
            }

            var lowestPointY = LowestPhysicalPointY();
            if (restoreToStoredShelf && capturedStoredLowestPoint &&
                lowestPointY < storedLowestPhysicalPointY - CabinetFallRecoveryDistance)
            {
                if (belowStoredShelfSince < 0f)
                    belowStoredShelfSince = Time.time;
                else if (Time.time - belowStoredShelfSince >= CabinetFallRecoveryDelay)
                {
                    recoveryRoutine = null;
                    RestoreToStoredPose();
                    yield break;
                }
            }
            else
            {
                belowStoredShelfSince = -1f;
            }

            if (recoveryPoint != null && recoveryPoint.IsBelowReachableHeight(lowestPointY))
            {
                if (belowThresholdSince < 0f)
                    belowThresholdSince = Time.time;
                else if (Time.time - belowThresholdSince >= recoveryPoint.RecoveryDelay)
                {
                    RecoverAt(recoveryPoint.transform);
                    recoveryRoutine = null;
                    yield break;
                }
            }
            else
            {
                belowThresholdSince = -1f;
            }

            yield return wait;
        }

        recoveryRoutine = null;
    }

    void RecoverAt(Transform recoveryPoint)
    {
        if (State != StorageState.Released || recoveryPoint == null)
            return;

        CancelCabinetShelfSupport();

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        if (grabbable == null)
            grabbable = GetComponent<PPEHandGrabbable>();
        if (grabbable != null)
            grabbable.enabled = true;

        if (body != null)
        {
            ClearNonKinematicVelocity(body);
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.None;
        }

        transform.SetPositionAndRotation(recoveryPoint.position, recoveryPoint.rotation);
        State = StorageState.Recovered;
        DisableWearableThrow();
        IgnorePlayerLocomotionCollisions();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log(
            "[PPE Dropped Recovery] Item=" + name +
            " Result=RECOVERED Position=" + transform.position.ToString("F3"),
            this);
#endif
    }

    bool SupportsUnreachableRecovery()
    {
        if (GetComponent<PPEGlovePairMember>() != null)
            return true;

        var equipable = GetComponent<PPEEquipable>();
        if (equipable == null || string.IsNullOrEmpty(equipable.ItemId))
            return false;
        var id = equipable.ItemId;
        return string.Equals(id, PPEEquipmentManager.IdChemicalSuit, System.StringComparison.Ordinal) ||
               string.Equals(id, PPEEquipmentManager.IdFaceRespirator, System.StringComparison.Ordinal) ||
               string.Equals(id, PPEEquipmentManager.IdFaceShield, System.StringComparison.Ordinal);
    }

    float LowestPhysicalPointY()
    {
        if (recoveryColliders == null || recoveryRenderers == null)
            CacheRecoveryBoundsSources();

        var found = false;
        var lowest = transform.position.y;
        for (var i = 0; i < recoveryColliders.Length; i++)
        {
            var col = recoveryColliders[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;
            var y = col.bounds.min.y;
            if (!found || y < lowest)
            {
                lowest = y;
                found = true;
            }
        }

        if (found)
            return lowest;

        for (var i = 0; i < recoveryRenderers.Length; i++)
        {
            var renderer = recoveryRenderers[i];
            if (renderer == null || !renderer.enabled)
                continue;
            var y = renderer.bounds.min.y;
            if (!found || y < lowest)
            {
                lowest = y;
                found = true;
            }
        }

        return lowest;
    }

    void CacheRecoveryBoundsSources()
    {
        recoveryColliders = GetComponentsInChildren<Collider>(true);
        recoveryRenderers = GetComponentsInChildren<Renderer>(true);
    }

    void CaptureStoredPose()
    {
        storedParent = transform.parent;
        storedHadParent = storedParent != null;
        storedLocalPosition = transform.localPosition;
        storedLocalRotation = transform.localRotation;
        storedLocalScale = transform.localScale;
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

    void RestoreStoredHierarchy()
    {
        if (storedHadParent)
        {
            if (storedParent != null)
                transform.SetParent(storedParent, false);
        }
        else if (transform.parent != null)
        {
            transform.SetParent(null, true);
        }

        transform.localScale = storedLocalScale == Vector3.zero ? Vector3.one : storedLocalScale;
    }

    void RestoreStoredPose()
    {
        RestoreStoredHierarchy();

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
        DisableWearableThrow();
        if (body.useGravity)
            body.useGravity = false;
    }

    void DisableWearableThrow()
    {
        if (grabbable == null)
            grabbable = GetComponent<PPEHandGrabbable>();
        grabbable?.ApplyWearableThrowPolicy();
    }

    static void ClearNonKinematicVelocity(Rigidbody rigidbody)
    {
        if (rigidbody == null || rigidbody.isKinematic)
            return;

        rigidbody.linearVelocity = Vector3.zero;
        rigidbody.angularVelocity = Vector3.zero;
    }

    void LogPhysicsSnapshot(string title)
    {
        Collider col = null;
        var colliders = GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var candidate = colliders[i];
            if (candidate == null || !candidate.enabled || candidate.isTrigger)
                continue;
            col = candidate;
            break;
        }

        var velocity = Vector3.zero;
        var angular = Vector3.zero;
        if (body != null && !body.isKinematic)
        {
            velocity = body.linearVelocity;
            angular = body.angularVelocity;
        }

        Debug.Log(
            "[" + title + "]\n" +
            "Object=" + name + "\n" +
            "Rigidbody.isKinematic=" + (body != null && body.isKinematic) + "\n" +
            "UseGravity=" + (body != null && body.useGravity) + "\n" +
            "DetectCollisions=" + (body == null || body.detectCollisions) + "\n" +
            "CollisionDetectionMode=" + (body != null ? body.collisionDetectionMode.ToString() : "none") + "\n" +
            "ColliderEnabled=" + (col != null && col.enabled) + "\n" +
            "ColliderTrigger=" + (col != null && col.isTrigger) + "\n" +
            "Velocity=" + velocity.ToString("F3") + "\n" +
            "AngularVelocity=" + angular.ToString("F3"),
            this);
    }

    /// <summary>
    /// Dropped cabinet PPE is a solid Rigidbody. The XR Origin CharacterController
    /// would otherwise treat a ChemicalSuit-sized box as floor and walk the HMD up.
    /// Floor collision stays so the item still lands. Common XR is not modified.
    /// </summary>
    public void IgnorePlayerLocomotionCollisions()
    {
        var excludeFromGroundProbe = State == StorageState.Held || State == StorageState.Released;
        var ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        var controller = ResolvePlayerCharacterController();
        var colliders = GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var col = colliders[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            if (excludeFromGroundProbe && ignoreRaycastLayer >= 0)
                col.gameObject.layer = ignoreRaycastLayer;
            else
                col.gameObject.layer = gameObject.layer;

            if (controller != null)
                Physics.IgnoreCollision(col, controller, true);
        }
    }

    static CharacterController ResolvePlayerCharacterController()
    {
        var origin = PPEActiveXrOrigin.Resolve();
        if (origin == null)
            return null;

        var controller = origin.GetComponent<CharacterController>();
        if (controller != null)
            return controller;
        controller = origin.GetComponentInChildren<CharacterController>(true);
        if (controller != null)
            return controller;

        if (origin.Origin != null)
        {
            controller = origin.Origin.GetComponent<CharacterController>();
            if (controller != null)
                return controller;
            controller = origin.Origin.GetComponentInChildren<CharacterController>(true);
            if (controller != null)
                return controller;
        }

        var originTransform = PPEActiveXrOrigin.ResolveOriginTransform();
        return originTransform != null
            ? originTransform.GetComponentInChildren<CharacterController>(true)
            : null;
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
    }

    public void EditorApplyPairMemberDefaults()
    {
        startStored = true;
        restoreStoredPoseOnStart = true;
        enableAssist = true;
        generateAssistFromBounds = false;
        assistDistance = DefaultAssistDistance;
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
