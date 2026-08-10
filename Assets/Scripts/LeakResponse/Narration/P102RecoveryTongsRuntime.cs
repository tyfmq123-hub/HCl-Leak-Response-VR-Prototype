using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRProject.LeakResponse.Narration
{
    [DisallowMultipleComponent]
    public sealed class P102RecoveryTongsRuntime : MonoBehaviour
    {
        const float PickupRadius = 0.15f;
        const float IdleReminderSeconds = 15f;

        GameObject tongsRoot;
        Transform tongsGhost;
        Transform handleTransform;
        Transform tipTransform;
        XRGrabInteractable grabInteractable;
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
        float lastProgressTime;

        public bool IsConfigured => tongsRoot != null && grabInteractable != null && tipTransform != null;
        public P102RecoveryItemState HeldItem => heldItem;

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

            if (tongsRoot == null)
            {
                Debug.LogError("[P102 Flow] Recovery Tongs is missing. Recovery remains blocked.", this);
                return false;
            }

            if (tongsGhost == null)
            {
                GameObject existingRuntimeGhost =
                    P102RuntimeUtility.FindSceneObject("INT_RecoveryTongs_01_ObjectGhost_Runtime");
                tongsGhost = existingRuntimeGhost != null
                    ? existingRuntimeGhost.transform
                    : P102RuntimeUtility.CreateRuntimeGhost(
                        tongsRoot, "INT_RecoveryTongs_01_ObjectGhost_Runtime",
                        tongsRoot.transform.position, tongsRoot.transform.rotation);
            }
            if (tongsGhost != null)
                tongsGhost.gameObject.SetActive(false);

            handleTransform = FindNamedPart(tongsRoot.transform, "BlackHandle") ??
                              FindNamedPart(tongsRoot.transform, "Black Handle") ??
                              FindNamedPart(tongsRoot.transform, "Handle") ??
                              FindNamedPart(tongsRoot.transform, "Grip") ??
                              CreateFallbackHandle(tongsRoot);
            tipTransform = FindNamedPart(tongsRoot.transform, "Tip") ??
                           FindNamedPart(tongsRoot.transform, "Jaw") ??
                           CreateFallbackTip(tongsRoot);
            tongsRigidbody = P102RuntimeUtility.GetOrAdd<Rigidbody>(tongsRoot);
            P102RuntimeUtility.EnsureBoundsCollider(tongsRoot, false);
            grabInteractable = P102RuntimeUtility.GetOrAdd<XRGrabInteractable>(tongsRoot);
            grabInteractable.colliders.Clear();
            Collider[] tongsColliders = tongsRoot.GetComponents<Collider>();
            for (int i = 0; i < tongsColliders.Length; ++i)
                grabInteractable.colliders.Add(tongsColliders[i]);

            Transform attachTransform = handleTransform.Find("P102_TongsBlackHandleAttach_Runtime");
            if (attachTransform == null)
            {
                var attachObject = new GameObject("P102_TongsBlackHandleAttach_Runtime");
                attachTransform = attachObject.transform;
                attachTransform.SetParent(handleTransform, false);
            }
            attachTransform.localPosition = Vector3.zero;
            attachTransform.localRotation = Quaternion.identity;
            grabInteractable.attachTransform = attachTransform;
            grabInteractable.useDynamicAttach = false;
            grabInteractable.matchAttachPosition = true;
            grabInteractable.matchAttachRotation = true;
            grabInteractable.enabled = false;

            grabInteractable.selectEntered.AddListener(OnTongsSelected);
            grabInteractable.selectExited.AddListener(OnTongsReleased);
            grabInteractable.activated.AddListener(OnActivated);
            grabInteractable.deactivated.AddListener(OnDeactivated);
            if (narration != null)
                narration.CueCompleted += OnCueCompleted;
            if (waste != null)
                waste.ItemDeposited += OnItemDeposited;
            return true;
        }

        void Update()
        {
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

        void OnDestroy()
        {
            Unsubscribe();
        }

        void OnTongsSelected(SelectEnterEventArgs args)
        {
            if (recoveryActionEnabled)
            {
                pickupGuidanceStarted = true;
                lastProgressTime = Time.unscaledTime;
                narration?.EnqueueOnce(P102LeakNarrationCue.RecoveryTongsPickedUp);
            }
        }

        void OnTongsReleased(SelectExitEventArgs args)
        {
            if (args.isCanceled || returned || !returnEnabled || tongsGhost == null || tongsRoot == null)
                return;

            if (Vector3.Distance(tongsRoot.transform.position, tongsGhost.position) <= 0.3f)
                StartCoroutine(SnapTongsToGhost());
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

                float sqrDistance = (candidate.transform.position - tipTransform.position).sqrMagnitude;
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
                heldBody.linearVelocity = Vector3.zero;
                heldBody.angularVelocity = Vector3.zero;
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
                    heldBody.linearVelocity = Vector3.zero;
                    heldBody.angularVelocity = Vector3.zero;
                    heldBody.useGravity = heldOriginalUseGravity;
                    heldBody.isKinematic = heldOriginalIsKinematic;
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

        void OnCueCompleted(P102LeakNarrationCue cue)
        {
            if (cue == P102LeakNarrationCue.ContainerLidStowed)
            {
                recoveryActionEnabled = true;
                if (grabInteractable != null)
                    grabInteractable.enabled = true;
            }
            else if (cue == P102LeakNarrationCue.AllSocksRecovered)
            {
                returnEnabled = true;
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
            tongsRigidbody.linearVelocity = Vector3.zero;
            tongsRigidbody.angularVelocity = Vector3.zero;
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
            narration?.EnqueueOnce(P102LeakNarrationCue.RecoveryTongsReturned);
        }

        void Unsubscribe()
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.RemoveListener(OnTongsSelected);
                grabInteractable.selectExited.RemoveListener(OnTongsReleased);
                grabInteractable.activated.RemoveListener(OnActivated);
                grabInteractable.deactivated.RemoveListener(OnDeactivated);
            }
            if (narration != null)
                narration.CueCompleted -= OnCueCompleted;
            if (waste != null)
                waste.ItemDeposited -= OnItemDeposited;
        }

        void OnItemDeposited(P102RecoveryItemState item)
        {
            lastProgressTime = Time.unscaledTime;
        }

        static Transform FindNamedPart(Transform root, string token)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; ++i)
            {
                if (children[i].name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return children[i];
            }
            return null;
        }

        static Transform CreateFallbackTip(GameObject root)
        {
            var tipObject = new GameObject("P102_TongsTip_Runtime");
            tipObject.transform.SetParent(root.transform, false);
            if (P102RuntimeUtility.TryGetRendererBounds(root, out Bounds bounds))
            {
                Vector3 modelAxis = root.transform.right;
                float halfLength = ProjectedHalfExtent(bounds, modelAxis);
                tipObject.transform.position = bounds.center - modelAxis * halfLength;
            }
            return tipObject.transform;
        }

        static Transform CreateFallbackHandle(GameObject root)
        {
            var handleObject = new GameObject("P102_TongsBlackHandle_Runtime");
            handleObject.transform.SetParent(root.transform, false);
            if (P102RuntimeUtility.TryGetRendererBounds(root, out Bounds bounds))
            {
                Vector3 modelAxis = root.transform.right;
                float halfLength = ProjectedHalfExtent(bounds, modelAxis);
                handleObject.transform.position = bounds.center + modelAxis * halfLength * 0.88f;
            }
            return handleObject.transform;
        }

        static float ProjectedHalfExtent(Bounds bounds, Vector3 axis)
        {
            axis.Normalize();
            return Mathf.Abs(axis.x) * bounds.extents.x +
                   Mathf.Abs(axis.y) * bounds.extents.y +
                   Mathf.Abs(axis.z) * bounds.extents.z;
        }
    }
}
