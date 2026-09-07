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
        const float SideLidCaptureDistance = 0.85f;
        const float ClosedLidCaptureDistance = 0.75f;

        enum LidSnapOutcome
        {
            Restore,
            Stow,
            FinalLock,
        }

        readonly UniqueDepositGate depositGate = new UniqueDepositGate(RequiredDepositCount);

        GameObject containerRoot;
        GameObject bodyObject;
        GameObject lidObject;
        Transform sideGhost;
        Transform closedGhost;
        XRGrabInteractable lidGrab;
        BoxCollider lidHandleCollider;
        Rigidbody lidRigidbody;
        P102LeakNarrationController narration;
        HClAbsorptionVisualController absorptionController;
        Vector3 closedPosition;
        Quaternion closedRotation;
        Vector3 designatedContainerPosition;
        Quaternion designatedContainerRotation;
        Transform originalLidParent;
        Transform heldLidControllerAnchor;
        Vector3 closedLocalPosition;
        Quaternion closedLocalRotation;
        Vector3 authoredLidLocalScale;
        Vector3 heldLidLocalPosition;
        Quaternion heldLidLocalRotation;
        Vector3 releasedLidPosition;
        Quaternion releasedLidRotation;
        Bounds depositBounds;
        bool hasDepositBounds;
        bool lidStowEnabled;
        bool finalCloseEnabled;
        bool lidStowed;
        bool snappingLid;
        Coroutine lidReleaseRoutine;
        int visibleDepositedCount;

        public event Action<P102RecoveryItemState> ItemDeposited;
        public event Action LidStowed;

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
        public Collider LidHandleCollider => lidHandleCollider;
        public bool IsLidStowed => lidStowed;

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
            visibleDepositedCount = 0;
            lidStowEnabled = false;
            finalCloseEnabled = false;
            lidStowed = false;
            snappingLid = false;

            bodyObject = FindDescendant(containerRoot, "HazWasteContainer_Body");
            lidObject = FindDescendant(containerRoot, "HazWasteContainer_Lid");
            GameObject sideGhostObject = P102RuntimeUtility.FindSceneObject("HazWasteContainer_Lid_ObjectGhost");
            sideGhost = sideGhostObject != null ? sideGhostObject.transform : null;
            if (sideGhost != null && sideGhost.parent != containerRoot.transform)
            {
                // Keep the authored world pose, but make later container moves carry the
                // lid-stow target with the Stage-owned waste container.
                sideGhost.SetParent(containerRoot.transform, true);
            }

            if (bodyObject == null || lidObject == null)
            {
                Debug.LogError("[P102 Flow] Waste Container body or separable lid is missing. The step remains incomplete.", this);
                return false;
            }

            P102WasteContainerPhysics.EnsureBodyCollision(containerRoot);

            P102RuntimeUtility.HideHandPoseGhostRenderers(lidObject);

            originalLidParent = lidObject.transform.parent;
            // The Scene owns the canonical closed pose. Recalculating it from the
            // merged FBX bounds can include the rear handle in the body's height and
            // can choose the underside of the lid as its top. Both make the lid appear
            // upside-down and suspended above the bin on device.
            lidObject.transform.SetParent(originalLidParent, true);
            closedPosition = lidObject.transform.position;
            closedRotation = lidObject.transform.rotation;
            closedLocalPosition = lidObject.transform.localPosition;
            closedLocalRotation = lidObject.transform.localRotation;
            authoredLidLocalScale = lidObject.transform.localScale;

            GameObject existingClosedGhost =
                P102RuntimeUtility.FindSceneObject("HazWasteContainer_Lid_ClosedGhost_Runtime");
            closedGhost = existingClosedGhost != null
                ? existingClosedGhost.transform
                : P102RuntimeUtility.CreateRuntimeGhost(
                    lidObject, "HazWasteContainer_Lid_ClosedGhost_Runtime", closedPosition, closedRotation);
            SetGhostVisible(sideGhost, false);
            SetGhostVisible(closedGhost, false);

            lidRigidbody = P102RuntimeUtility.GetOrAdd<Rigidbody>(lidObject);
            if (!lidRigidbody.isKinematic)
            {
                lidRigidbody.linearVelocity = Vector3.zero;
                lidRigidbody.angularVelocity = Vector3.zero;
            }
            lidRigidbody.useGravity = false;
            lidRigidbody.isKinematic = true;
            BoxCollider lidCollider = P102RuntimeUtility.EnsureBoundsCollider(lidObject, false);
            if (lidCollider != null)
                lidCollider.enabled = false;
            lidHandleCollider = EnsureLidHandleGrabCollider();
            lidGrab = P102RuntimeUtility.GetOrAdd<XRGrabInteractable>(lidObject);
            lidGrab.enabled = false;
            lidGrab.colliders.Clear();
            if (lidHandleCollider != null)
                lidGrab.colliders.Add(lidHandleCollider);
            // The imported Hand Pose attach belongs to the removed pose tool and can
            // be metres away from the visible lid. The physical handle is the only
            // valid Grab reference for this Stage.
            lidGrab.attachTransform = lidHandleCollider != null
                ? lidHandleCollider.transform
                : lidObject.transform;
            lidGrab.useDynamicAttach = false;
            lidGrab.matchAttachPosition = false;
            lidGrab.matchAttachRotation = false;
            lidGrab.snapToColliderVolume = true;
            lidGrab.throwOnDetach = false;
            lidGrab.selectMode = InteractableSelectMode.Single;
            lidGrab.movementType = XRGrabInteractable.MovementType.Kinematic;
            lidGrab.smoothPosition = false;
            lidGrab.smoothRotation = false;
            P102RuntimeUtility.ConfigureExactPoseGrabTracking(lidGrab);
            lidGrab.selectEntered.AddListener(OnLidSelectEntered);
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

        void LateUpdate()
        {
            if (lidObject == null || lidGrab == null || snappingLid || lidReleaseRoutine != null)
                return;
            if (lidGrab.isSelected)
            {
                if (heldLidControllerAnchor != null)
                    lidObject.transform.SetPositionAndRotation(
                        heldLidControllerAnchor.TransformPoint(heldLidLocalPosition),
                        heldLidControllerAnchor.rotation * heldLidLocalRotation);
                return;
            }
            // A cancelled selection or another grab component's late release update
            // must not leave an unheld lid inside the bin. Scene-authored pose is the
            // sole closed-pose authority; no bounds-based recalculation is performed.
            lidObject.transform.SetParent(originalLidParent, true);
            lidObject.transform.localScale = authoredLidLocalScale;
            if (lidStowed && !depositGate.IsLocked && sideGhost != null)
                lidObject.transform.SetPositionAndRotation(sideGhost.position, sideGhost.rotation);
            else
            {
                lidObject.transform.localPosition = closedLocalPosition;
                lidObject.transform.localRotation = closedLocalRotation;
            }
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

            int uniqueId = item.GetEntityId().GetHashCode();
            if (!depositGate.TryDeposit(uniqueId) || !item.MarkDeposited())
                return false;

            PlaceInStack(item, depositGate.DepositedCount - 1);
            ItemDeposited?.Invoke(item);
            CompleteRemainingKind(item.Kind, item);
            Debug.Log($"[P102 Flow] Waste deposited {depositGate.DepositedCount}/{RequiredDepositCount}", item);

            if (item.Kind == P102AbsorbentKind.Pad)
                narration?.EnqueueOnce(P102LeakNarrationCue.AllPadsRecovered);
            if (CanFinalLock)
            {
                narration?.EnqueueOnce(P102LeakNarrationCue.AllSocksRecovered);
                Debug.Log("[P102 Flow] Waste deposited 7/7", this);
            }

            return true;
        }

        void CompleteRemainingKind(P102AbsorbentKind kind, P102RecoveryItemState representative)
        {
            P102RecoveryRuntime recoveryRuntime =
                representative != null ? representative.GetComponentInParent<P102RecoveryRuntime>() : null;
            if (recoveryRuntime == null)
                recoveryRuntime = FindAnyObjectByType<P102RecoveryRuntime>();
            if (recoveryRuntime == null)
                return;

            for (int i = 0; i < recoveryRuntime.Items.Count; ++i)
            {
                P102RecoveryItemState other = recoveryRuntime.Items[i];
                if (other == null || other == representative || other.Kind != kind || other.IsDeposited)
                    continue;
                if (!depositGate.TryDeposit(other.GetEntityId().GetHashCode()) || !other.MarkDeposited())
                    continue;

                XRGrabInteractable otherGrab = other.GetComponent<XRGrabInteractable>();
                if (otherGrab != null)
                    otherGrab.enabled = false;
                Collider[] colliders = other.GetComponentsInChildren<Collider>(true);
                for (int colliderIndex = 0; colliderIndex < colliders.Length; ++colliderIndex)
                {
                    if (colliders[colliderIndex] != null)
                        colliders[colliderIndex].enabled = false;
                }
                Renderer[] renderers = other.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; ++rendererIndex)
                {
                    if (renderers[rendererIndex] != null)
                        renderers[rendererIndex].enabled = false;
                }
                ++visibleDepositedCount;
                ItemDeposited?.Invoke(other);
            }
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
                P102RuntimeUtility.SetHandPoseGhostRenderersVisible(lidObject, true);
                if (lidGrab != null)
                    lidGrab.enabled = true;
            }
            else if (cue == P102LeakNarrationCue.ContainerClosureGhostActivated)
            {
                finalCloseEnabled = CanFinalLock;
                SetGhostVisible(closedGhost, finalCloseEnabled);
                P102RuntimeUtility.SetHandPoseGhostRenderersVisible(
                    lidObject, finalCloseEnabled);
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

        void OnLidSelectEntered(SelectEnterEventArgs args)
        {
            StopLidReleaseRoutine();
            P102RuntimeUtility.SetHandPoseGhostRenderersVisible(lidObject, false);
            Transform selectionAttach = args?.interactorObject?.GetAttachTransform(lidGrab);
            heldLidControllerAnchor = P102RuntimeUtility.ResolveStableControllerAnchor(
                                          args?.interactorObject) ??
                                      selectionAttach;
            if (heldLidControllerAnchor != null && lidObject != null)
            {
                if (lidHandleCollider != null)
                {
                    Quaternion heldRotation = lidObject.transform.rotation;
                    Vector3 localHandlePoint = lidObject.transform.InverseTransformPoint(
                        lidHandleCollider.transform.position);
                    Vector3 scaledHandlePoint = Vector3.Scale(
                        localHandlePoint, lidObject.transform.lossyScale);
                    Vector3 heldPosition = heldLidControllerAnchor.position -
                                           heldRotation * scaledHandlePoint;
                    lidObject.transform.SetPositionAndRotation(
                        heldPosition, heldRotation);
                }

                // Keep the lid under the tracked controller, never under Near/Far's movable
                // attach transform. Thumbstick anchor manipulation must not move or rotate it.
                lidObject.transform.SetParent(heldLidControllerAnchor, true);
                heldLidLocalPosition = lidObject.transform.localPosition;
                heldLidLocalRotation = lidObject.transform.localRotation;
                lidGrab.trackPosition = false;
                lidGrab.trackRotation = false;
            }
            if (lidRigidbody == null)
                return;

            if (!lidRigidbody.isKinematic)
            {
                lidRigidbody.linearVelocity = Vector3.zero;
                lidRigidbody.angularVelocity = Vector3.zero;
            }
            lidRigidbody.useGravity = false;
            lidRigidbody.isKinematic = true;
        }

        void OnLidSelectExited(SelectExitEventArgs args)
        {
            if (lidObject != null)
            {
                releasedLidPosition = heldLidControllerAnchor != null
                    ? heldLidControllerAnchor.TransformPoint(heldLidLocalPosition) : lidObject.transform.position;
                releasedLidRotation = heldLidControllerAnchor != null
                    ? heldLidControllerAnchor.rotation * heldLidLocalRotation : lidObject.transform.rotation;
            }
            heldLidControllerAnchor = null;
            if (lidObject != null)
                lidObject.transform.SetParent(originalLidParent, true);
            if (lidGrab != null)
            {
                lidGrab.trackPosition = true;
                lidGrab.trackRotation = true;
            }
            if (args.isCanceled)
            {
                P102RuntimeUtility.SetHandPoseGhostRenderersVisible(
                    lidObject, lidGrab != null && lidGrab.enabled && !depositGate.IsLocked);
                return;
            }
            if (snappingLid || lidObject == null || depositGate.IsLocked)
                return;

            StopLidReleaseRoutine();
            lidReleaseRoutine = StartCoroutine(TrySnapLidAfterRelease());
        }

        IEnumerator TrySnapLidAfterRelease()
        {
            // XR's cached rigidbody target predates our controller-parented pose.
            // Restore the actual release pose after XRI detach bookkeeping, before
            // testing the ghost. Otherwise a far-away release can snap from the bin.
            yield return null;
            lidReleaseRoutine = null;
            if (lidGrab == null || lidGrab.isSelected || snappingLid || lidObject == null ||
                depositGate.IsLocked)
            {
                yield break;
            }

            lidObject.transform.SetPositionAndRotation(releasedLidPosition, releasedLidRotation);
            if (lidRigidbody != null)
            {
                lidRigidbody.position = releasedLidPosition;
                lidRigidbody.rotation = releasedLidRotation;
            }

            if (!lidStowed && lidStowEnabled && sideGhost != null &&
                IsLidNearGhost(sideGhost, SideLidCaptureDistance))
            {
                StartCoroutine(SnapLid(
                    sideGhost.position, sideGhost.rotation, LidSnapOutcome.Stow));
                yield break;
            }

            if (lidStowed && finalCloseEnabled && CanFinalLock &&
                IsLidNearPose(closedGhost, closedPosition, ClosedLidCaptureDistance))
            {
                StartCoroutine(SnapLid(
                    closedPosition, closedRotation, LidSnapOutcome.FinalLock));
                yield break;
            }

            // A missed release must never leave the lid frozen inside the container.
            // Return it to the last valid pose and let the player try again.
            if (lidStowed && sideGhost != null)
                StartCoroutine(SnapLid(
                    sideGhost.position, sideGhost.rotation, LidSnapOutcome.Restore));
            else
                StartCoroutine(SnapLid(
                    closedPosition, closedRotation, LidSnapOutcome.Restore));
        }

        IEnumerator SnapLid(
            Vector3 targetPosition, Quaternion targetRotation, LidSnapOutcome outcome)
        {
            snappingLid = true;
            if (lidGrab != null)
                lidGrab.enabled = false;
            if (!lidRigidbody.isKinematic)
            {
                lidRigidbody.linearVelocity = Vector3.zero;
                lidRigidbody.angularVelocity = Vector3.zero;
            }
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
            if (outcome == LidSnapOutcome.FinalLock)
            {
                P102RuntimeUtility.SetHandPoseGhostRenderersVisible(lidObject, false);
                if (depositGate.TryLock())
                {
                    SetGhostVisible(closedGhost, false);
                    lidObject.transform.SetParent(originalLidParent, true);
                    lidGrab.enabled = false;
                    narration?.EnqueueOnce(P102LeakNarrationCue.ContainerClosedTaskCompleted);
                    Debug.Log("[P102 Flow] Waste lid locked", this);
                }
            }
            else if (outcome == LidSnapOutcome.Stow)
            {
                lidStowed = true;
                P102RuntimeUtility.SetHandPoseGhostRenderersVisible(lidObject, false);
                SetGhostVisible(sideGhost, false);
                lidGrab.enabled = false;
                LidStowed?.Invoke();
                narration?.EnqueueOnce(P102LeakNarrationCue.ContainerLidStowed);
            }
            else if (lidGrab != null)
            {
                lidGrab.enabled = lidStowed ? finalCloseEnabled : lidStowEnabled;
                P102RuntimeUtility.SetHandPoseGhostRenderersVisible(
                    lidObject, lidGrab.enabled);
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
            StopLidReleaseRoutine();
            if (lidGrab != null)
            {
                lidGrab.selectEntered.RemoveListener(OnLidSelectEntered);
                lidGrab.selectExited.RemoveListener(OnLidSelectExited);
            }
            if (narration != null)
            {
                narration.CueStarted -= OnCueStarted;
                narration.CueCompleted -= OnCueCompleted;
            }
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

            // The whole visible lid is a valid touch target. Selection still snaps to the
            // authored handle attach, so the player never has to crouch to the small handle.
            Vector3 worldSize = new Vector3(
                Mathf.Max(0.20f, lidBounds.size.x * 1.08f),
                Mathf.Max(0.10f, lidBounds.size.y * 1.35f),
                Mathf.Max(0.20f, lidBounds.size.z * 1.08f));
            handle.position = new Vector3(
                lidBounds.center.x,
                lidBounds.max.y - worldSize.y * 0.35f,
                lidBounds.center.z);
            handle.rotation = containerRoot.transform.rotation;
            handle.localScale = Vector3.one;

            BoxCollider collider = handle.GetComponent<BoxCollider>();
            if (collider == null)
                collider = handle.gameObject.AddComponent<BoxCollider>();
            Vector3 scale = handle.lossyScale;
            collider.center = handle.InverseTransformPoint(lidBounds.center);
            collider.size = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
            collider.isTrigger = false;
            collider.enabled = true;
            return collider;
        }

        bool IsLidNearGhost(Transform ghost, float captureDistance)
        {
            return IsLidNearPose(ghost, ghost != null ? ghost.position : Vector3.zero,
                captureDistance);
        }

        bool IsLidNearPose(Transform ghost, Vector3 fallbackPosition, float captureDistance)
        {
            if (lidObject == null)
                return false;

            Vector3 lidCenter = P102RuntimeUtility.TryGetRendererBounds(lidObject, out Bounds lidBounds)
                ? lidBounds.center
                : lidObject.transform.position;
            Vector3 targetCenter = ghost != null &&
                                   P102RuntimeUtility.TryGetRendererBounds(ghost.gameObject,
                                       out Bounds ghostBounds)
                ? ghostBounds.center
                : fallbackPosition;
            return Vector3.Distance(lidCenter, targetCenter) <= Mathf.Max(0f, captureDistance);
        }

        void StopLidReleaseRoutine()
        {
            if (lidReleaseRoutine == null)
                return;
            StopCoroutine(lidReleaseRoutine);
            lidReleaseRoutine = null;
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
