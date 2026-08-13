using System.Collections;
using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRProject.LeakResponse.Logic;

namespace VRProject.LeakResponse.Narration
{
    [DisallowMultipleComponent]
    public sealed class P102WasteContainerRuntime : MonoBehaviour
    {
        const int RequiredDepositCount = 7;
        const float IntakeHorizontalMargin = 0.35f;
        const float IntakeHeightAboveRim = 1f;
        const float IntakeDepthBelowRim = 0.28f;

        readonly UniqueDepositGate depositGate = new UniqueDepositGate(RequiredDepositCount);

        GameObject containerRoot;
        GameObject bodyObject;
        GameObject lidObject;
        Transform sideGhost;
        Transform closedGhost;
        XRGrabInteractable lidGrab;
        Rigidbody lidRigidbody;
        Rigidbody bodyRigidbody;
        P102LeakNarrationController narration;
        HClAbsorptionVisualController absorptionController;
        Vector3 closedPosition;
        Quaternion closedRotation;
        Vector3 designatedContainerPosition;
        Quaternion designatedContainerRotation;
        Transform originalLidParent;
        Bounds depositBounds;
        bool hasDepositBounds;
        bool lidStowEnabled;
        bool finalCloseEnabled;
        bool lidStowed;
        bool snappingLid;
        int depositedPads;
        int visibleDepositedCount;

        public event Action<P102RecoveryItemState> ItemDeposited;

        public int DepositedCount => depositGate.DepositedCount;
        public int VisibleDepositedCount => visibleDepositedCount;
        public bool IsAtDesignatedPosition =>
            containerRoot != null &&
            containerRoot.activeInHierarchy &&
            Vector3.Distance(containerRoot.transform.position, designatedContainerPosition) <= 0.02f &&
            Quaternion.Angle(containerRoot.transform.rotation, designatedContainerRotation) <= 2f;
        public bool CanFinalLock =>
            depositGate.CanFinalLock &&
            visibleDepositedCount >= RequiredDepositCount &&
            IsAtDesignatedPosition &&
            absorptionController != null &&
            absorptionController.IsFinalCleanupComplete;
        public bool IsLockedClosed => depositGate.IsLocked;
        public bool IsConfigured => containerRoot != null && bodyObject != null && lidObject != null;

        public bool Configure(GameObject newContainerRoot, P102LeakNarrationController narrationController,
            HClAbsorptionVisualController absorption)
        {
            Unsubscribe();
            containerRoot = newContainerRoot;
            narration = narrationController;
            absorptionController = absorption;
            if (containerRoot == null)
            {
                Debug.LogError("[P102 Flow] Waste Container is missing. Deposit and 7/7 completion remain blocked.", this);
                return false;
            }

            containerRoot.SetActive(true);
            designatedContainerPosition = containerRoot.transform.position;
            designatedContainerRotation = containerRoot.transform.rotation;
            depositedPads = 0;
            visibleDepositedCount = 0;

            bodyObject = FindDescendant(containerRoot, "HazWasteContainer_Body");
            lidObject = FindDescendant(containerRoot, "HazWasteContainer_Lid");
            GameObject sideGhostObject = P102RuntimeUtility.FindSceneObject("HazWasteContainer_Lid_ObjectGhost");
            sideGhost = sideGhostObject != null ? sideGhostObject.transform : null;

            if (bodyObject == null || lidObject == null)
            {
                Debug.LogError("[P102 Flow] Waste Container body or separable lid is missing. The step remains incomplete.", this);
                return false;
            }

            originalLidParent = lidObject.transform.parent;
            closedPosition = lidObject.transform.position;
            closedRotation = lidObject.transform.rotation;

            EnsureContainerBodyPhysics();

            GameObject existingClosedGhost =
                P102RuntimeUtility.FindSceneObject("HazWasteContainer_Lid_ClosedGhost_Runtime");
            closedGhost = existingClosedGhost != null
                ? existingClosedGhost.transform
                : P102RuntimeUtility.CreateRuntimeGhost(
                    lidObject, "HazWasteContainer_Lid_ClosedGhost_Runtime", closedPosition, closedRotation);
            SetGhostVisible(sideGhost, false);
            SetGhostVisible(closedGhost, false);

            lidRigidbody = P102RuntimeUtility.GetOrAdd<Rigidbody>(lidObject);
            lidRigidbody.linearVelocity = Vector3.zero;
            lidRigidbody.angularVelocity = Vector3.zero;
            lidRigidbody.useGravity = false;
            lidRigidbody.isKinematic = true;
            BoxCollider lidCollider = P102RuntimeUtility.EnsureBoundsCollider(lidObject, false);
            if (lidCollider != null)
                lidCollider.enabled = false;
            BoxCollider lidHandleCollider = EnsureLidHandleGrabCollider();
            lidGrab = P102RuntimeUtility.GetOrAdd<XRGrabInteractable>(lidObject);
            lidGrab.enabled = false;
            lidGrab.colliders.Clear();
            if (lidHandleCollider != null)
                lidGrab.colliders.Add(lidHandleCollider);
            lidGrab.attachTransform = lidHandleCollider != null
                ? lidHandleCollider.transform
                : null;
            lidGrab.useDynamicAttach = false;
            lidGrab.matchAttachPosition = true;
            lidGrab.matchAttachRotation = true;
            lidGrab.snapToColliderVolume = false;
            lidGrab.selectExited.AddListener(OnLidSelectExited);
            hasDepositBounds = P102RuntimeUtility.TryGetRendererBounds(bodyObject, out depositBounds);
            if (!hasDepositBounds)
                Debug.LogError("[P102 Flow] Waste Container body bounds are unavailable. Deposits remain blocked.", this);
            if (sideGhost == null)
                Debug.LogError(
                    "[P102 Flow] Required Scene ghost 'HazWasteContainer_Lid_ObjectGhost' is missing. " +
                    "Lid stow remains blocked; no runtime replacement is created.", this);
            if (absorptionController == null)
                Debug.LogError("[P102 Flow] HClAbsorptionVisualController is missing. Final container lock remains blocked.", this);

            if (narration != null)
            {
                narration.CueStarted += OnCueStarted;
                narration.CueCompleted += OnCueCompleted;
            }
            return IsConfigured;
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        public bool IsWithinDepositZone(Vector3 worldPosition)
        {
            if (!hasDepositBounds)
                return false;

            return CreateIntakeBounds().Contains(worldPosition);
        }

        public bool TryDeposit(P102RecoveryItemState item)
        {
            if (item == null || !item.CanBeDeposited ||
                !IsItemWithinDepositZone(item) || !HasVisibleRenderer(item))
                return false;

            int uniqueId = item.GetInstanceID();
            if (!depositGate.TryDeposit(uniqueId) || !item.MarkDeposited())
                return false;

            if (item.Kind == P102AbsorbentKind.Pad)
                ++depositedPads;
            PlaceInStack(item, depositGate.DepositedCount - 1);
            ItemDeposited?.Invoke(item);
            Debug.Log($"[P102 Flow] Waste deposited {depositGate.DepositedCount}/{RequiredDepositCount}", item);

            if (depositedPads == 3)
                narration?.EnqueueOnce(P102LeakNarrationCue.AllPadsRecovered);
            if (CanFinalLock)
            {
                narration?.EnqueueOnce(P102LeakNarrationCue.AllSocksRecovered);
                Debug.Log("[P102 Flow] Waste deposited 7/7", this);
            }

            return true;
        }

        bool IsItemWithinDepositZone(P102RecoveryItemState item)
        {
            if (!hasDepositBounds || item == null)
                return false;

            Bounds intakeBounds = CreateIntakeBounds();
            return P102RuntimeUtility.TryGetRendererBounds(item.gameObject, out Bounds itemBounds)
                ? intakeBounds.Intersects(itemBounds)
                : intakeBounds.Contains(item.transform.position);
        }

        Bounds CreateIntakeBounds()
        {
            float bottom = depositBounds.max.y - IntakeDepthBelowRim;
            float top = depositBounds.max.y + IntakeHeightAboveRim;
            return new Bounds(
                new Vector3(depositBounds.center.x, (bottom + top) * 0.5f, depositBounds.center.z),
                new Vector3(
                    depositBounds.size.x + IntakeHorizontalMargin * 2f,
                    top - bottom,
                    depositBounds.size.z + IntakeHorizontalMargin * 2f));
        }

        void OnCueStarted(P102LeakNarrationCue cue)
        {
            if (cue == P102LeakNarrationCue.ContainerAndLidGhostActivated)
            {
                lidStowEnabled = true;
                SetGhostVisible(sideGhost, true);
                if (lidGrab != null)
                    lidGrab.enabled = true;
            }
            else if (cue == P102LeakNarrationCue.ContainerClosureGhostActivated)
            {
                finalCloseEnabled = CanFinalLock;
                SetGhostVisible(closedGhost, finalCloseEnabled);
                if (lidGrab != null)
                    lidGrab.enabled = finalCloseEnabled;
                if (!finalCloseEnabled)
                {
                    Debug.LogError(
                        $"[P102 Flow] Container closure remains blocked: deposited={DepositedCount}/7, " +
                        $"visible={VisibleDepositedCount}/7, designatedPosition={IsAtDesignatedPosition}, " +
                        $"leakCleanup={(absorptionController != null && absorptionController.IsFinalCleanupComplete)}.",
                        this);
                }
            }
        }

        void OnCueCompleted(P102LeakNarrationCue cue)
        {
            if (cue == P102LeakNarrationCue.AllPadsRecovered)
                narration.EnqueueOnce(P102LeakNarrationCue.SockRecoveryStarted);
        }

        void OnLidSelectExited(SelectExitEventArgs args)
        {
            if (args.isCanceled || snappingLid || lidObject == null || depositGate.IsLocked)
                return;

            if (!lidStowed && lidStowEnabled && sideGhost != null &&
                Vector3.Distance(lidObject.transform.position, sideGhost.position) <= 0.35f)
            {
                StartCoroutine(SnapLid(sideGhost.position, sideGhost.rotation, false));
                return;
            }

            if (lidStowed && finalCloseEnabled && CanFinalLock &&
                Vector3.Distance(lidObject.transform.position, closedPosition) <= 0.35f)
            {
                StartCoroutine(SnapLid(closedPosition, closedRotation, true));
            }
        }

        IEnumerator SnapLid(Vector3 targetPosition, Quaternion targetRotation, bool finalLock)
        {
            snappingLid = true;
            lidRigidbody.linearVelocity = Vector3.zero;
            lidRigidbody.angularVelocity = Vector3.zero;
            lidRigidbody.useGravity = false;
            lidRigidbody.isKinematic = true;
            Vector3 startPosition = lidObject.transform.position;
            Quaternion startRotation = lidObject.transform.rotation;
            const float duration = 0.22f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                lidObject.transform.SetPositionAndRotation(
                    Vector3.LerpUnclamped(startPosition, targetPosition, eased),
                    Quaternion.SlerpUnclamped(startRotation, targetRotation, eased));
                yield return null;
            }

            lidObject.transform.SetPositionAndRotation(targetPosition, targetRotation);
            if (finalLock)
            {
                if (depositGate.TryLock())
                {
                    SetGhostVisible(closedGhost, false);
                    lidObject.transform.SetParent(originalLidParent, true);
                    lidGrab.enabled = false;
                    narration?.EnqueueOnce(P102LeakNarrationCue.ContainerClosedTaskCompleted);
                    Debug.Log("[P102 Flow] Waste lid locked", this);
                }
            }
            else
            {
                lidStowed = true;
                SetGhostVisible(sideGhost, false);
                lidGrab.enabled = false;
                narration?.EnqueueOnce(P102LeakNarrationCue.ContainerLidStowed);
            }

            snappingLid = false;
        }

        void PlaceInStack(P102RecoveryItemState item, int index)
        {
            Transform itemTransform = item.transform;
            Rigidbody body = item.GetComponent<Rigidbody>();
            XRGrabInteractable grab = item.GetComponent<XRGrabInteractable>();
            if (grab != null)
                grab.enabled = false;
            Collider[] colliders = item.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; ++i)
                colliders[i].enabled = false;
            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.useGravity = false;
                body.isKinematic = true;
            }

            int column = index % 3;
            int level = index / 3;
            float x = depositBounds.center.x + (column - 1) * depositBounds.size.x * 0.15f;
            float y = depositBounds.min.y + depositBounds.size.y * (0.14f + level * 0.14f);
            float z = depositBounds.center.z + (index % 2 == 0 ? -1f : 1f) *
                depositBounds.size.z * 0.07f;
            Vector3 targetPosition = new Vector3(x, y, z);
            Quaternion targetRotation = bodyObject.transform.rotation *
                                        Quaternion.Euler(index % 2 == 0 ? -8f : 8f,
                                            index * 37f, index % 3 == 0 ? 10f : -6f);
            itemTransform.SetParent(bodyObject.transform, true);
            Vector3 startLocalScale = itemTransform.localScale;
            float largestItemDimension = 0f;
            if (P102RuntimeUtility.TryGetRendererBounds(item.gameObject, out Bounds itemBounds))
            {
                largestItemDimension = Mathf.Max(
                    itemBounds.size.x, Mathf.Max(itemBounds.size.y, itemBounds.size.z));
            }
            float maximumStoredDimension = Mathf.Min(depositBounds.size.x, depositBounds.size.z) * 0.22f;
            float compression = largestItemDimension > 0.0001f
                ? Mathf.Clamp(maximumStoredDimension / largestItemDimension, 0.045f, 0.18f)
                : 0.12f;
            Vector3 squeeze = item.Kind == P102AbsorbentKind.Pad
                ? new Vector3(compression, compression * 0.45f, compression)
                : new Vector3(compression, compression * 0.70f, compression);
            Vector3 targetLocalScale = Vector3.Scale(startLocalScale, squeeze);
            item.gameObject.SetActive(true);
            Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
                renderers[i].enabled = true;
            ++visibleDepositedCount;
            StartCoroutine(AnimateWasteIntoStack(
                itemTransform, targetPosition, targetRotation, startLocalScale, targetLocalScale));
        }

        IEnumerator AnimateWasteIntoStack(Transform itemTransform, Vector3 targetPosition,
            Quaternion targetRotation, Vector3 startLocalScale, Vector3 targetLocalScale)
        {
            if (itemTransform == null)
                yield break;

            Vector3 startPosition = itemTransform.position;
            Quaternion startRotation = itemTransform.rotation;
            const float duration = 0.38f;
            float elapsed = 0f;
            while (elapsed < duration && itemTransform != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                itemTransform.SetPositionAndRotation(
                    Vector3.LerpUnclamped(startPosition, targetPosition, eased),
                    Quaternion.SlerpUnclamped(startRotation, targetRotation, eased));
                itemTransform.localScale = Vector3.LerpUnclamped(
                    startLocalScale, targetLocalScale, eased);
                yield return null;
            }

            if (itemTransform != null)
            {
                itemTransform.SetPositionAndRotation(targetPosition, targetRotation);
                itemTransform.localScale = targetLocalScale;
            }
        }

        static bool HasVisibleRenderer(P102RecoveryItemState item)
        {
            if (item == null)
                return false;

            Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                if (renderers[i] != null)
                    return true;
            }

            Debug.LogError($"[P102 Flow] Contaminated item '{item.name}' has no Renderer and cannot enter the visible 1-7 stack.", item);
            return false;
        }

        void Unsubscribe()
        {
            if (lidGrab != null)
                lidGrab.selectExited.RemoveListener(OnLidSelectExited);
            if (narration != null)
            {
                narration.CueStarted -= OnCueStarted;
                narration.CueCompleted -= OnCueCompleted;
            }
        }

        void EnsureContainerBodyPhysics()
        {
            if (bodyObject == null ||
                !P102RuntimeUtility.TryGetRendererBounds(bodyObject, out Bounds bodyBounds))
            {
                return;
            }

            bodyRigidbody = P102RuntimeUtility.GetOrAdd<Rigidbody>(bodyObject);
            bodyRigidbody.linearVelocity = Vector3.zero;
            bodyRigidbody.angularVelocity = Vector3.zero;
            bodyRigidbody.useGravity = false;
            bodyRigidbody.isKinematic = true;

            const float wallThickness = 0.035f;
            float wallHeight = Mathf.Max(0.1f, bodyBounds.size.y);
            CreateOrUpdateBodyCollider(
                "P102_WasteContainerBottomCollider_Runtime",
                new Vector3(bodyBounds.center.x, bodyBounds.min.y + wallThickness * 0.5f, bodyBounds.center.z),
                new Vector3(bodyBounds.size.x, wallThickness, bodyBounds.size.z));
            CreateOrUpdateBodyCollider(
                "P102_WasteContainerLeftWallCollider_Runtime",
                new Vector3(bodyBounds.min.x + wallThickness * 0.5f, bodyBounds.center.y, bodyBounds.center.z),
                new Vector3(wallThickness, wallHeight, bodyBounds.size.z));
            CreateOrUpdateBodyCollider(
                "P102_WasteContainerRightWallCollider_Runtime",
                new Vector3(bodyBounds.max.x - wallThickness * 0.5f, bodyBounds.center.y, bodyBounds.center.z),
                new Vector3(wallThickness, wallHeight, bodyBounds.size.z));
            CreateOrUpdateBodyCollider(
                "P102_WasteContainerFrontWallCollider_Runtime",
                new Vector3(bodyBounds.center.x, bodyBounds.center.y, bodyBounds.min.z + wallThickness * 0.5f),
                new Vector3(bodyBounds.size.x, wallHeight, wallThickness));
            CreateOrUpdateBodyCollider(
                "P102_WasteContainerBackWallCollider_Runtime",
                new Vector3(bodyBounds.center.x, bodyBounds.center.y, bodyBounds.max.z - wallThickness * 0.5f),
                new Vector3(bodyBounds.size.x, wallHeight, wallThickness));
        }

        BoxCollider EnsureLidHandleGrabCollider()
        {
            if (lidObject == null ||
                !P102RuntimeUtility.TryGetRendererBounds(lidObject, out Bounds lidBounds))
            {
                return null;
            }

            Transform handle = lidObject.transform.Find("P102_LidHandleGrab_Runtime");
            if (handle == null)
            {
                var handleObject = new GameObject("P102_LidHandleGrab_Runtime");
                handle = handleObject.transform;
                handle.SetParent(lidObject.transform, true);
            }

            Vector3 worldSize = new Vector3(
                Mathf.Clamp(lidBounds.size.x * 0.46f, 0.18f, 0.42f),
                Mathf.Clamp(lidBounds.size.y * 0.22f, 0.06f, 0.12f),
                Mathf.Clamp(lidBounds.size.z * 0.18f, 0.08f, 0.20f));
            handle.position = new Vector3(
                lidBounds.center.x,
                lidBounds.max.y - worldSize.y * 0.5f,
                lidBounds.center.z);
            handle.rotation = containerRoot.transform.rotation;
            handle.localScale = Vector3.one;

            BoxCollider collider = handle.GetComponent<BoxCollider>();
            if (collider == null)
                collider = handle.gameObject.AddComponent<BoxCollider>();
            Vector3 scale = handle.lossyScale;
            collider.center = Vector3.zero;
            collider.size = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
            collider.isTrigger = false;
            collider.enabled = true;
            return collider;
        }

        void CreateOrUpdateBodyCollider(string objectName, Vector3 worldCenter, Vector3 worldSize)
        {
            Transform colliderTransform = bodyObject.transform.Find(objectName);
            if (colliderTransform == null)
            {
                var colliderObject = new GameObject(objectName);
                colliderTransform = colliderObject.transform;
                colliderTransform.SetParent(bodyObject.transform, true);
            }

            colliderTransform.position = worldCenter;
            colliderTransform.rotation = Quaternion.identity;
            colliderTransform.localScale = Vector3.one;
            BoxCollider collider = colliderTransform.GetComponent<BoxCollider>();
            if (collider == null)
                collider = colliderTransform.gameObject.AddComponent<BoxCollider>();

            Vector3 scale = colliderTransform.lossyScale;
            collider.center = Vector3.zero;
            collider.size = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
            collider.isTrigger = false;
            collider.enabled = true;
        }

        static void SetGhostVisible(Transform ghost, bool visible)
        {
            if (ghost != null)
                ghost.gameObject.SetActive(visible);
        }

        static GameObject FindDescendant(GameObject root, string exactName)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; ++i)
            {
                if (string.Equals(children[i].name, exactName, System.StringComparison.Ordinal))
                    return children[i].gameObject;
            }
            return null;
        }
    }
}
