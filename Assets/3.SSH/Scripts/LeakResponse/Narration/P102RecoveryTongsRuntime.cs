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
        const float PickupRadius = 0.25f;
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
                Debug.LogError(
                    "[P102 Flow] Required Scene ghost 'INT_RecoveryTongs_01_ObjectGhost' is missing. " +
                    "Tongs pickup and return remain blocked; no runtime replacement is created.", this);
                return false;
            }

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
            BoxCollider handleGrabCollider = EnsureHandleGrabCollider(tongsRoot, handleTransform);
            EnsureJawPhysicalCollider(tongsRoot, tipTransform);
            grabInteractable = P102RuntimeUtility.GetOrAdd<XRGrabInteractable>(tongsRoot);
            grabInteractable.enabled = false;
            grabInteractable.colliders.Clear();
            if (handleGrabCollider != null)
                grabInteractable.colliders.Add(handleGrabCollider);

            Transform attachTransform = handleTransform.Find("P102_TongsBlackHandleAttach_Runtime");
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
            grabInteractable.attachTransform = attachTransform;
            grabInteractable.useDynamicAttach = true;
            grabInteractable.matchAttachPosition = true;
            grabInteractable.matchAttachRotation = false;
            grabInteractable.snapToColliderVolume = true;
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
            if (tongsRigidbody != null)
            {
                tongsRigidbody.useGravity = false;
                tongsRigidbody.isKinematic = true;
            }

            if (recoveryActionEnabled)
            {
                pickupGuidanceStarted = true;
                lastProgressTime = Time.unscaledTime;
                narration?.EnqueueOnce(P102LeakNarrationCue.RecoveryTongsPickedUp);
            }
        }

        void OnTongsReleased(SelectExitEventArgs args)
        {
            if (args.isCanceled || returned || tongsRoot == null)
                return;

            if (returnEnabled && tongsGhost != null &&
                Vector3.Distance(tongsRoot.transform.position, tongsGhost.position) <= 0.3f)
            {
                StartCoroutine(SnapTongsToGhost());
                return;
            }

            StartCoroutine(EnableReleasedTongsPhysics());
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

            return float.IsPositiveInfinity(nearestSqrDistance)
                ? (item.transform.position - worldPoint).sqrMagnitude
                : nearestSqrDistance;
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

        static Transform CreateFallbackTip(GameObject root)
        {
            Transform tip = root.transform.Find("P102_TongsJawMouth_Runtime");
            if (tip == null)
            {
                var tipObject = new GameObject("P102_TongsJawMouth_Runtime");
                tip = tipObject.transform;
                tip.SetParent(root.transform, false);
            }

            if (P102RuntimeUtility.TryGetRendererBounds(root, out Bounds bounds))
            {
                tip.position = new Vector3(
                    bounds.center.x,
                    bounds.max.y - bounds.size.y * 0.075f,
                    bounds.center.z);
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

            if (P102RuntimeUtility.TryGetRendererBounds(root, out Bounds bounds))
            {
                handle.position = new Vector3(
                    bounds.center.x,
                    bounds.min.y + bounds.size.y * 0.05f,
                    bounds.center.z);
            }
            return handle;
        }

        static BoxCollider EnsureHandleGrabCollider(GameObject root, Transform handle)
        {
            if (root == null || handle == null ||
                !P102RuntimeUtility.TryGetRendererBounds(root, out Bounds bounds))
            {
                return null;
            }

            BoxCollider collider = handle.GetComponent<BoxCollider>();
            if (collider == null)
                collider = handle.gameObject.AddComponent<BoxCollider>();

            bool hasNamedHandleBounds = P102RuntimeUtility.TryGetRendererBounds(handle.gameObject,
                out Bounds handleBounds);
            Vector3 worldCenter = hasNamedHandleBounds ? handleBounds.center : handle.position;
            Vector3 worldSize = hasNamedHandleBounds
                ? new Vector3(
                    Mathf.Max(0.12f, handleBounds.size.x * 1.15f),
                    Mathf.Max(0.08f, handleBounds.size.y * 1.15f),
                    Mathf.Max(0.08f, handleBounds.size.z * 1.15f))
                : new Vector3(
                    Mathf.Clamp(bounds.size.x * 0.52f, 0.20f, 0.42f),
                    Mathf.Clamp(bounds.size.y * 0.075f, 0.06f, 0.11f),
                    Mathf.Clamp(bounds.size.z * 0.78f, 0.06f, 0.14f));
            Vector3 handleScale = handle.lossyScale;
            collider.center = handle.InverseTransformPoint(worldCenter);
            collider.size = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(handleScale.x)),
                worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(handleScale.y)),
                worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(handleScale.z)));
            collider.isTrigger = false;
            collider.enabled = true;
            return collider;
        }

        static BoxCollider EnsureJawPhysicalCollider(GameObject root, Transform tip)
        {
            if (root == null || tip == null ||
                !P102RuntimeUtility.TryGetRendererBounds(root, out Bounds bounds))
            {
                return null;
            }

            BoxCollider collider = tip.GetComponent<BoxCollider>();
            if (collider == null)
                collider = tip.gameObject.AddComponent<BoxCollider>();

            Vector3 worldSize = new Vector3(
                Mathf.Clamp(bounds.size.x * 0.52f, 0.18f, 0.38f),
                Mathf.Clamp(bounds.size.y * 0.075f, 0.06f, 0.12f),
                Mathf.Clamp(bounds.size.z * 0.78f, 0.06f, 0.16f));
            Vector3 tipScale = tip.lossyScale;
            collider.center = Vector3.zero;
            collider.size = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(tipScale.x)),
                worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(tipScale.y)),
                worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(tipScale.z)));
            collider.isTrigger = false;
            collider.enabled = true;
            return collider;
        }
    }
}
