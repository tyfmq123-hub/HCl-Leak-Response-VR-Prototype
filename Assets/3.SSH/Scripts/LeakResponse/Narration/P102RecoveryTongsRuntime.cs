using System;
using System.Collections;
using System.Collections.Generic;
using HClTraining.SharedXR;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRProject.LeakResponse.Narration
{
    [DisallowMultipleComponent]
    public sealed class P102RecoveryTongsRuntime : MonoBehaviour
    {
        const float PickupRadius = 0.35f;
        const float IdleReminderSeconds = 15f;

        GameObject tongsRoot;
        Transform tongsGhost;
        Transform handleTransform;
        Transform tipTransform;
        Transform authoredHandPoseAttach;
        XRGrabInteractable grabInteractable;
        BoxCollider handleGrabCollider;
        BoxCollider jawPhysicalCollider;
        Rigidbody tongsRigidbody;
        P102RecoveryRuntime recovery;
        P102WasteContainerRuntime waste;
        P102LeakNarrationController narration;
        P102RecoveryItemState heldItem;
        Transform heldOriginalParent;
        Rigidbody heldBody;
        bool heldOriginalIsKinematic;
        bool heldOriginalUseGravity;
        Collider[] heldColliders = Array.Empty<Collider>();
        bool[] heldColliderStates = Array.Empty<bool>();
        bool recoveryActionEnabled;
        bool returnEnabled;
        bool returned;
        bool pickupGuidanceStarted;
        bool sockGuidanceCompleted;
        Coroutine grabConfirmationRoutine;
        float lastProgressTime;
        XRBaseInputInteractor carryingInteractor;
        P102CarryInputLock carryInputLock;
        bool returnGripArmed;
        Transform heldInteractorAttach;
        Transform tongsOriginalParent;
        Vector3 heldLocalPosition;
        Quaternion heldLocalRotation;
        bool equipmentUseReported;

        public bool IsConfigured => tongsRoot != null && grabInteractable != null && tipTransform != null;
        public P102RecoveryItemState HeldItem => heldItem;
        public Collider HandleGrabCollider => handleGrabCollider;
        public bool IsGrabEnabled => grabInteractable != null && grabInteractable.enabled;
        public bool IsCarryLocked => carryingInteractor != null && carryInputLock != null && !returned;

        public bool Configure(GameObject newTongsRoot, Transform newTongsGhost,
            P102RecoveryRuntime recoveryRuntime, P102WasteContainerRuntime wasteRuntime,
            P102LeakNarrationController narrationController)
        {
            Unsubscribe();
            tongsRoot = newTongsRoot;
            tongsGhost = newTongsGhost;
            recovery = recoveryRuntime;
            waste = wasteRuntime;
            narration = narrationController;
            recoveryActionEnabled = false;
            returnEnabled = false;
            returned = false;
            pickupGuidanceStarted = false;
            sockGuidanceCompleted = false;
            equipmentUseReported = false;

            if (tongsRoot == null)
            {
                Debug.LogError("[P102 Flow] Recovery Tongs is missing. Recovery remains blocked.", this);
                return false;
            }

            if (tongsGhost == null)
            {
                Debug.LogError(
                    "[P102 Flow] Required Scene ghost 'INT_RecoveryTongs_01_ObjectGhost' is missing. " +
                    "Tongs pickup and return remain blocked; no runtime replacement is created.", this);
                return false;
            }

            tongsRoot.SetActive(true);
            tongsOriginalParent = tongsRoot.transform.parent;
            P102RuntimeUtility.HideHandPoseGhostRenderers(tongsRoot);
            tongsRoot.transform.SetPositionAndRotation(tongsGhost.position, tongsGhost.rotation);
            tongsGhost.gameObject.SetActive(false);

            handleTransform = CreateFallbackHandle(tongsRoot);
            tipTransform = CreateFallbackTip(tongsRoot);
            tongsRigidbody = P102RuntimeUtility.GetOrAdd<Rigidbody>(tongsRoot);
            if (!tongsRigidbody.isKinematic)
            {
                tongsRigidbody.linearVelocity = Vector3.zero;
                tongsRigidbody.angularVelocity = Vector3.zero;
            }
            tongsRigidbody.useGravity = false;
            tongsRigidbody.isKinematic = true;
            BoxCollider legacyFullBoundsCollider = tongsRoot.GetComponent<BoxCollider>();
            if (legacyFullBoundsCollider != null)
                legacyFullBoundsCollider.enabled = false;
            handleGrabCollider = EnsureHandleGrabCollider(tongsRoot, handleTransform);
            jawPhysicalCollider = EnsureJawPhysicalCollider(tongsRoot, tipTransform);
            grabInteractable = P102RuntimeUtility.GetOrAdd<XRGrabInteractable>(tongsRoot);
            grabInteractable.enabled = false;
            grabInteractable.colliders.Clear();
            if (handleGrabCollider != null)
                grabInteractable.colliders.Add(handleGrabCollider);

            authoredHandPoseAttach = P102RuntimeUtility.FindDescendantTransform(
                tongsRoot, P102RuntimeUtility.RightHandPoseAttachName);
            Transform attachTransform = authoredHandPoseAttach;
            if (attachTransform == null)
            {
                attachTransform = handleTransform.Find("P102_TongsBlackHandleAttach_Runtime");
                if (attachTransform == null)
                {
                    var attachObject = new GameObject("P102_TongsBlackHandleAttach_Runtime");
                    attachTransform = attachObject.transform;
                    attachTransform.SetParent(handleTransform, false);
                }
                attachTransform.localPosition = handleGrabCollider != null
                    ? handleGrabCollider.center
                    : Vector3.zero;
                attachTransform.localRotation = Quaternion.identity;
            }

            grabInteractable.attachTransform = attachTransform;
            // The authored hand-pose rotation does not match every Quest controller attach.
            // Preserve the actual handle pose at selection so the long tool cannot snap,
            // flip, or orbit around the hand while it is being carried.
            grabInteractable.useDynamicAttach = true;
            grabInteractable.matchAttachPosition = true;
            grabInteractable.matchAttachRotation = false;
            grabInteractable.snapToColliderVolume = true;
            grabInteractable.throwOnDetach = false;
            grabInteractable.selectMode = InteractableSelectMode.Single;
            grabInteractable.movementType = XRGrabInteractable.MovementType.Kinematic;
            grabInteractable.trackPosition = true;
            grabInteractable.trackRotation = true;
            grabInteractable.smoothPosition = false;
            grabInteractable.smoothRotation = false;
            P102RuntimeUtility.ConfigureExactPoseGrabTracking(grabInteractable);
            grabInteractable.selectEntered.AddListener(OnTongsSelected);
            grabInteractable.selectExited.AddListener(OnTongsReleased);
            grabInteractable.activated.AddListener(OnActivated);
            grabInteractable.deactivated.AddListener(OnDeactivated);
            if (narration != null)
            {
                narration.CueStarted += OnCueStarted;
                narration.CueCompleted += OnCueCompleted;
            }
            if (waste != null)
            {
                waste.ItemDeposited += OnItemDeposited;
                waste.LidStowed += EnableRecoveryInteraction;
                if (waste.IsLidStowed)
                    EnableRecoveryInteraction();
            }
            return true;
        }

        void Update()
        {
            UpdateReturnInput();
            if (!recoveryActionEnabled || !pickupGuidanceStarted || heldItem != null ||
                recovery == null || narration == null || narration.IsPlaying)
                return;

            bool padsRemain = recovery.HasRemaining(P102AbsorbentKind.Pad);
            bool socksRemain = recovery.HasRemaining(P102AbsorbentKind.Sock);
            if ((!padsRemain && (!socksRemain || !sockGuidanceCompleted)) ||
                Time.unscaledTime - lastProgressTime < IdleReminderSeconds)
                return;

            narration.EnqueueRepeatable(padsRemain
                ? P102LeakNarrationCue.PadRecoveryIdle
                : P102LeakNarrationCue.SockRecoveryIdle);
            lastProgressTime = Time.unscaledTime;
        }

        void LateUpdate()
        {
            if (heldInteractorAttach == null || tongsRoot == null || grabInteractable == null ||
                !grabInteractable.isSelected)
                return;

            tongsRoot.transform.SetPositionAndRotation(
                heldInteractorAttach.TransformPoint(heldLocalPosition),
                heldInteractorAttach.rotation * heldLocalRotation);
            if (tongsRigidbody != null && !tongsRigidbody.isKinematic)
            {
                tongsRigidbody.linearVelocity = Vector3.zero;
                tongsRigidbody.angularVelocity = Vector3.zero;
            }
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        void OnTongsSelected(SelectEnterEventArgs args)
        {
            if (carryingInteractor != null && ReferenceEquals(args?.interactorObject, carryingInteractor))
                return;
            P102RuntimeUtility.SetHandPoseGhostRenderersVisible(tongsRoot, false);

            if (!equipmentUseReported)
            {
                equipmentUseReported = true;
                SharedTrainingTelemetry.Current.EquipmentUsed(SharedEquipmentIds.RecoveryTongs);
            }

            if (args?.interactorObject is XRBaseInputInteractor inputInteractor)
            {
                carryingInteractor = inputInteractor;
                returnGripArmed = false;
                carryInputLock?.Dispose();
                carryInputLock = new P102CarryInputLock(inputInteractor);
            }

            heldInteractorAttach = P102RuntimeUtility.ResolveStableControllerAnchor(
                                        args?.interactorObject) ??
                                    args?.interactorObject?.GetAttachTransform(grabInteractable);
            if (heldInteractorAttach != null && tongsRoot != null)
            {
                heldLocalPosition = heldInteractorAttach.InverseTransformPoint(
                    tongsRoot.transform.position);
                heldLocalRotation = Quaternion.Inverse(heldInteractorAttach.rotation) *
                                    tongsRoot.transform.rotation;
                // Make locomotion and snap-turn move the tool in the exact same hierarchy
                // update as the controller. This removes frame-order drift and flying.
                tongsRoot.transform.SetParent(heldInteractorAttach, true);
            }
            grabInteractable.trackPosition = false;
            grabInteractable.trackRotation = false;

            if (tongsRigidbody != null)
            {
                tongsRigidbody.useGravity = false;
                tongsRigidbody.isKinematic = true;
            }

            if (recoveryActionEnabled && !pickupGuidanceStarted)
            {
                StopGrabConfirmation();
                grabConfirmationRoutine = StartCoroutine(
                    ConfirmTongsAttached(args.interactorObject));
            }
        }

        void OnTongsReleased(SelectExitEventArgs args)
        {
            StopGrabConfirmation();
            RestoreSelectTriggers();
            heldInteractorAttach = null;
            if (tongsRoot != null)
                tongsRoot.transform.SetParent(tongsOriginalParent, true);
            if (grabInteractable != null)
            {
                grabInteractable.trackPosition = true;
                grabInteractable.trackRotation = true;
            }
            if (returned || tongsRoot == null)
                return;
            if (args.isCanceled)
            {
                if (recoveryActionEnabled)
                    P102RuntimeUtility.SetHandPoseGhostRenderersVisible(tongsRoot, true);
                return;
            }

            if (returnEnabled && tongsGhost != null &&
                Vector3.Distance(tongsRoot.transform.position, tongsGhost.position) <= 0.3f)
            {
                StartCoroutine(SnapTongsToGhost());
                return;
            }

            P102RuntimeUtility.SetHandPoseGhostRenderersVisible(tongsRoot, true);
            StartCoroutine(EnableReleasedTongsPhysics());
        }

        void UpdateReturnInput()
        {
            if (carryingInteractor == null || returned || !returnEnabled ||
                SharedTrainingPause.IsPaused)
                return;
            // The return instruction must finish and the grip must be released once.
            // Only a fresh press AT the visible return ghost can detach the tool.
            carryInputLock.ReadPhysicalGrip(out bool gripHeld, out bool gripPressed);
            if (!gripHeld)
                returnGripArmed = true;
            if (returnGripArmed && gripPressed)
            {
                returnGripArmed = false;
                TryReturnAtGhost();
            }
        }

        public bool TryReturnAtGhost()
        {
            if (!returnEnabled || returned || heldItem != null || tongsGhost == null ||
                !tongsGhost.gameObject.activeInHierarchy || tongsRoot == null ||
                Vector3.Distance(tongsRoot.transform.position, tongsGhost.position) > 0.3f ||
                carryingInteractor == null || carryInputLock == null || !grabInteractable.isSelected)
                return false;
            var interactor = carryingInteractor;
            RestoreSelectTriggers();
            grabInteractable.interactionManager.SelectExit(interactor,
                (IXRSelectInteractable)grabInteractable);
            return true;
        }

        IEnumerator ConfirmTongsAttached(IXRSelectInteractor interactor)
        {
            yield return null;
            if (grabInteractable != null && grabInteractable.isSelected && interactor != null &&
                grabInteractable.interactorsSelecting.Contains(interactor))
            {
                pickupGuidanceStarted = true;
                lastProgressTime = Time.unscaledTime;
                narration?.EnqueueOnce(P102LeakNarrationCue.RecoveryTongsPickedUp);
            }
            grabConfirmationRoutine = null;
        }

        IEnumerator EnableReleasedTongsPhysics()
        {
            // Let XR Interaction Toolkit finish its release bookkeeping first, then restore
            // normal physics so an unreturned tool cannot remain suspended in mid-air.
            yield return null;
            if (returned || tongsRigidbody == null ||
                (grabInteractable != null && grabInteractable.isSelected))
            {
                yield break;
            }

            tongsRigidbody.isKinematic = false;
            tongsRigidbody.useGravity = true;
            tongsRigidbody.WakeUp();
        }

        void OnActivated(ActivateEventArgs args)
        {
            if (!recoveryActionEnabled || heldItem != null || recovery == null || tipTransform == null)
                return;

            P102RecoveryItemState nearest = FindNearestEligible(out bool sockBlockedByPads);
            if (nearest == null)
            {
                if (sockBlockedByPads)
                    narration?.EnqueueRepeatable(P102LeakNarrationCue.SockRecoveryBeforePadsAttempted);
                return;
            }

            PickUp(nearest);
        }

        void OnDeactivated(DeactivateEventArgs args)
        {
            ReleaseHeldItem();
        }

        P102RecoveryItemState FindNearestEligible(out bool sockBlockedByPads)
        {
            sockBlockedByPads = false;
            P102RecoveryItemState nearest = null;
            float nearestSqrDistance = PickupRadius * PickupRadius;
            bool padsRemain = recovery.HasRemaining(P102AbsorbentKind.Pad);

            for (int i = 0; i < recovery.Items.Count; ++i)
            {
                P102RecoveryItemState candidate = recovery.Items[i];
                if (candidate == null || !candidate.IsRecoveryEligible)
                    continue;

                float sqrDistance = DistanceSqrToItem(candidate, tipTransform.position);
                if (jawPhysicalCollider != null)
                {
                    sqrDistance = Mathf.Min(
                        sqrDistance,
                        DistanceSqrToItem(candidate, jawPhysicalCollider.bounds));
                }
                if (candidate.Kind == P102AbsorbentKind.Sock && padsRemain)
                {
                    if (sqrDistance <= nearestSqrDistance)
                        sockBlockedByPads = true;
                    continue;
                }

                if (sqrDistance <= nearestSqrDistance)
                {
                    nearest = candidate;
                    nearestSqrDistance = sqrDistance;
                }
            }

            return nearest;
        }

        static float DistanceSqrToItem(P102RecoveryItemState item, Vector3 worldPoint)
        {
            Collider[] colliders = item.GetComponentsInChildren<Collider>(true);
            float nearestSqrDistance = float.PositiveInfinity;
            for (int i = 0; i < colliders.Length; ++i)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled)
                    continue;

                float candidateDistance =
                    (collider.ClosestPoint(worldPoint) - worldPoint).sqrMagnitude;
                if (candidateDistance < nearestSqrDistance)
                    nearestSqrDistance = candidateDistance;
            }

            // Recovery intentionally disables the installed absorbent colliders so they
            // cannot lift the XR CharacterController. Use the authored visible surfaces
            // as the pickup target instead of falling back to the object's often-distant
            // pivot, otherwise touching a large pad or sock with the jaws cannot pick it up.
            var rendererBindings = item.InstallItem != null
                ? item.InstallItem.RendererMaterials
                : null;
            if (rendererBindings != null)
            {
                for (int i = 0; i < rendererBindings.Length; ++i)
                {
                    Renderer renderer = rendererBindings[i]?.TargetRenderer;
                    if (renderer == null || !renderer.enabled ||
                        !renderer.gameObject.activeInHierarchy)
                        continue;

                    float candidateDistance = renderer.bounds.SqrDistance(worldPoint);
                    if (candidateDistance < nearestSqrDistance)
                        nearestSqrDistance = candidateDistance;
                }
            }

            return float.IsPositiveInfinity(nearestSqrDistance)
                ? (item.transform.position - worldPoint).sqrMagnitude
                : nearestSqrDistance;
        }

        static float DistanceSqrToItem(P102RecoveryItemState item, Bounds pickupBounds)
        {
            float nearestSqrDistance = float.PositiveInfinity;
            var rendererBindings = item.InstallItem != null
                ? item.InstallItem.RendererMaterials
                : null;
            if (rendererBindings != null)
            {
                for (int i = 0; i < rendererBindings.Length; ++i)
                {
                    Renderer renderer = rendererBindings[i]?.TargetRenderer;
                    if (renderer == null || !renderer.enabled ||
                        !renderer.gameObject.activeInHierarchy)
                        continue;

                    float candidateDistance = BoundsSqrDistance(
                        renderer.bounds, pickupBounds);
                    if (candidateDistance < nearestSqrDistance)
                        nearestSqrDistance = candidateDistance;
                }
            }

            return float.IsPositiveInfinity(nearestSqrDistance)
                ? pickupBounds.SqrDistance(item.transform.position)
                : nearestSqrDistance;
        }

        static float BoundsSqrDistance(Bounds first, Bounds second)
        {
            float x = Mathf.Max(0f,
                Mathf.Max(first.min.x - second.max.x, second.min.x - first.max.x));
            float y = Mathf.Max(0f,
                Mathf.Max(first.min.y - second.max.y, second.min.y - first.max.y));
            float z = Mathf.Max(0f,
                Mathf.Max(first.min.z - second.max.z, second.min.z - first.max.z));
            return x * x + y * y + z * z;
        }

        void PickUp(P102RecoveryItemState item)
        {
            heldItem = item;
            heldItem.SetHeldByTongs(true);
            heldOriginalParent = item.transform.parent;
            heldBody = item.GetComponent<Rigidbody>();
            if (heldBody != null)
            {
                heldOriginalIsKinematic = heldBody.isKinematic;
                heldOriginalUseGravity = heldBody.useGravity;
                if (!heldBody.isKinematic)
                {
                    heldBody.linearVelocity = Vector3.zero;
                    heldBody.angularVelocity = Vector3.zero;
                }
                heldBody.useGravity = false;
                heldBody.isKinematic = true;
            }

            heldColliders = item.GetComponentsInChildren<Collider>(true);
            heldColliderStates = new bool[heldColliders.Length];
            for (int i = 0; i < heldColliders.Length; ++i)
            {
                heldColliderStates[i] = heldColliders[i].enabled;
                heldColliders[i].enabled = false;
            }

            item.transform.SetParent(tipTransform, true);
            Debug.Log($"[P102 Flow] Tongs pickup: {item.name}", item);
        }

        void ReleaseHeldItem()
        {
            if (heldItem == null)
                return;

            P102RecoveryItemState released = heldItem;
            released.transform.SetParent(null, true);
            bool deposited = waste != null && waste.TryDeposit(released);
            if (!deposited)
            {
                released.SetHeldByTongs(false);
                released.transform.SetParent(heldOriginalParent, true);
                if (heldBody != null)
                {
                    heldBody.useGravity = heldOriginalUseGravity;
                    heldBody.isKinematic = heldOriginalIsKinematic;
                    if (!heldBody.isKinematic)
                    {
                        heldBody.linearVelocity = Vector3.zero;
                        heldBody.angularVelocity = Vector3.zero;
                    }
                }
                for (int i = 0; i < heldColliders.Length; ++i)
                {
                    if (heldColliders[i] != null)
                        heldColliders[i].enabled = heldColliderStates[i];
                }
                narration?.EnqueueRepeatable(P102LeakNarrationCue.ContaminatedAbsorbentDroppedOutside);
            }

            heldItem = null;
            heldBody = null;
            heldColliders = Array.Empty<Collider>();
            heldColliderStates = Array.Empty<bool>();
        }

        void OnCueStarted(P102LeakNarrationCue cue)
        {
            // Show the destination as it is mentioned, not after the entire sentence.
            // Detaching is still gated until the return instruction has completed.
            if (cue == P102LeakNarrationCue.AllSocksRecovered && tongsGhost != null)
                tongsGhost.gameObject.SetActive(true);
        }

        void OnCueCompleted(P102LeakNarrationCue cue)
        {
            if (cue == P102LeakNarrationCue.ContainerLidStowed)
            {
                EnableRecoveryInteraction();
            }
            else if (cue == P102LeakNarrationCue.AllSocksRecovered)
            {
                returnEnabled = true;
                returnGripArmed = false;
                if (tongsGhost != null)
                    tongsGhost.gameObject.SetActive(true);
                if (tongsGhost == null)
                    Debug.LogError("[P102 Flow] Recovery Tongs return ghost is missing. Lid closure remains blocked.", this);
            }
            else if (cue == P102LeakNarrationCue.SockRecoveryStarted)
            {
                sockGuidanceCompleted = true;
                lastProgressTime = Time.unscaledTime;
            }
            else if (cue == P102LeakNarrationCue.RecoveryTongsReturned)
            {
                narration.EnqueueOnce(P102LeakNarrationCue.ContainerClosureGhostActivated);
            }
        }

        IEnumerator SnapTongsToGhost()
        {
            returned = true;
            P102RuntimeUtility.SetHandPoseGhostRenderersVisible(tongsRoot, false);
            if (!tongsRigidbody.isKinematic)
            {
                tongsRigidbody.linearVelocity = Vector3.zero;
                tongsRigidbody.angularVelocity = Vector3.zero;
            }
            tongsRigidbody.useGravity = false;
            tongsRigidbody.isKinematic = true;
            Vector3 startPosition = tongsRoot.transform.position;
            Quaternion startRotation = tongsRoot.transform.rotation;
            const float duration = 0.22f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                tongsRoot.transform.SetPositionAndRotation(
                    Vector3.LerpUnclamped(startPosition, tongsGhost.position, eased),
                    Quaternion.SlerpUnclamped(startRotation, tongsGhost.rotation, eased));
                yield return null;
            }

            tongsRoot.transform.SetPositionAndRotation(tongsGhost.position, tongsGhost.rotation);
            tongsGhost.gameObject.SetActive(false);
            grabInteractable.enabled = false;
            RestoreSelectTriggers();
            narration?.EnqueueOnce(P102LeakNarrationCue.RecoveryTongsReturned);
        }

        void Unsubscribe()
        {
            StopGrabConfirmation();
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.RemoveListener(OnTongsSelected);
                grabInteractable.selectExited.RemoveListener(OnTongsReleased);
                grabInteractable.activated.RemoveListener(OnActivated);
                grabInteractable.deactivated.RemoveListener(OnDeactivated);
            }
            RestoreSelectTriggers();
            if (narration != null)
            {
                narration.CueStarted -= OnCueStarted;
                narration.CueCompleted -= OnCueCompleted;
            }
            if (waste != null)
            {
                waste.ItemDeposited -= OnItemDeposited;
                waste.LidStowed -= EnableRecoveryInteraction;
            }
        }

        void StopGrabConfirmation()
        {
            if (grabConfirmationRoutine == null)
                return;
            StopCoroutine(grabConfirmationRoutine);
            grabConfirmationRoutine = null;
        }

        void EnableRecoveryInteraction()
        {
            recoveryActionEnabled = true;
            if (grabInteractable != null && !returned)
            {
                grabInteractable.enabled = true;
                P102RuntimeUtility.SetHandPoseGhostRenderersVisible(tongsRoot, true);
            }
        }

        void RestoreSelectTriggers()
        {
            carryInputLock?.Dispose();
            carryInputLock = null;
            carryingInteractor = null;
        }

        void OnItemDeposited(P102RecoveryItemState item)
        {
            lastProgressTime = Time.unscaledTime;
        }

        static Transform CreateFallbackTip(GameObject root)
        {
            Transform tip = root.transform.Find("P102_TongsJawMouth_Runtime");
            if (tip == null)
            {
                var tipObject = new GameObject("P102_TongsJawMouth_Runtime");
                tip = tipObject.transform;
                tip.SetParent(root.transform, false);
            }

            if (TryGetRendererLocalBounds(root, out Bounds bounds))
            {
                int axis = LongestAxis(bounds.size);
                Vector3 localPosition = bounds.center;
                SetAxis(ref localPosition, axis,
                    GetAxis(bounds.max, axis) - GetAxis(bounds.size, axis) * 0.075f);
                tip.localPosition = localPosition;
                tip.localRotation = Quaternion.identity;
                tip.localScale = Vector3.one;
            }
            return tip;
        }

        static Transform CreateFallbackHandle(GameObject root)
        {
            Transform handle = root.transform.Find("P102_TongsBlackHandle_Runtime");
            if (handle == null)
            {
                var handleObject = new GameObject("P102_TongsBlackHandle_Runtime");
                handle = handleObject.transform;
                handle.SetParent(root.transform, false);
            }

            if (TryGetRendererLocalBounds(root, out Bounds bounds))
            {
                int axis = LongestAxis(bounds.size);
                Vector3 localPosition = bounds.center;
                SetAxis(ref localPosition, axis,
                    GetAxis(bounds.min, axis) + GetAxis(bounds.size, axis) * 0.18f);
                handle.localPosition = localPosition;
                handle.localRotation = Quaternion.identity;
                handle.localScale = Vector3.one;
            }
            return handle;
        }

        static BoxCollider EnsureHandleGrabCollider(GameObject root, Transform handle)
        {
            if (root == null || handle == null ||
                !TryGetRendererLocalBounds(root, out Bounds bounds))
            {
                return null;
            }

            BoxCollider collider = handle.GetComponent<BoxCollider>();
            if (collider == null)
                collider = handle.gameObject.AddComponent<BoxCollider>();

            int axis = LongestAxis(bounds.size);
            Vector3 colliderSize = bounds.size * 1.25f;
            SetAxis(ref colliderSize, axis,
                Mathf.Max(GetAxis(bounds.size, axis) * 0.38f,
                    WorldDistanceToLocal(root.transform, axis, 0.22f)));
            for (int currentAxis = 0; currentAxis < 3; ++currentAxis)
            {
                if (currentAxis == axis)
                    continue;
                SetAxis(ref colliderSize, currentAxis,
                    Mathf.Max(GetAxis(colliderSize, currentAxis),
                        WorldDistanceToLocal(root.transform, currentAxis, 0.085f)));
            }
            collider.center = Vector3.zero;
            collider.size = colliderSize;
            collider.isTrigger = false;
            collider.enabled = true;
            return collider;
        }

        static BoxCollider EnsureJawPhysicalCollider(GameObject root, Transform tip)
        {
            if (root == null || tip == null ||
                !TryGetRendererLocalBounds(root, out Bounds bounds))
            {
                return null;
            }

            BoxCollider collider = tip.GetComponent<BoxCollider>();
            if (collider == null)
                collider = tip.gameObject.AddComponent<BoxCollider>();

            int axis = LongestAxis(bounds.size);
            Vector3 localSize = bounds.size * 1.15f;
            SetAxis(ref localSize, axis,
                Mathf.Max(GetAxis(bounds.size, axis) * 0.22f,
                    WorldDistanceToLocal(root.transform, axis, 0.16f)));
            collider.center = Vector3.zero;
            collider.size = localSize;
            collider.isTrigger = false;
            collider.enabled = true;
            return collider;
        }

        static bool TryGetRendererLocalBounds(GameObject root, out Bounds localBounds)
        {
            localBounds = default;
            if (root == null)
                return false;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool hasPoint = false;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; ++rendererIndex)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null ||
                    P102RuntimeUtility.IsHandPoseGhostTransform(
                        renderer.transform, root.transform))
                    continue;

                Bounds worldBounds = renderer.bounds;
                Vector3 min = worldBounds.min;
                Vector3 max = worldBounds.max;
                for (int corner = 0; corner < 8; ++corner)
                {
                    Vector3 worldPoint = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    Vector3 localPoint = root.transform.InverseTransformPoint(worldPoint);
                    if (!hasPoint)
                    {
                        localBounds = new Bounds(localPoint, Vector3.zero);
                        hasPoint = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localPoint);
                    }
                }
            }

            return hasPoint;
        }

        static int LongestAxis(Vector3 size)
        {
            if (size.x >= size.y && size.x >= size.z)
                return 0;
            return size.y >= size.z ? 1 : 2;
        }

        static float GetAxis(Vector3 value, int axis)
        {
            return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
        }

        static void SetAxis(ref Vector3 value, int axis, float component)
        {
            if (axis == 0)
                value.x = component;
            else if (axis == 1)
                value.y = component;
            else
                value.z = component;
        }

        static float WorldDistanceToLocal(Transform transform, int axis, float worldDistance)
        {
            Vector3 scale = transform != null ? transform.lossyScale : Vector3.one;
            return worldDistance / Mathf.Max(0.0001f, Mathf.Abs(GetAxis(scale, axis)));
        }
    }
}
