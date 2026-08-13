using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRProject.ObjectInstallation
{
    [DisallowMultipleComponent]
    public sealed class GhostSnappableObject : MonoBehaviour
    {
        [Header("Compatibility")]
        [SerializeField] string compatibilityId;

        [Header("Existing Object References")]
        [SerializeField] XRGrabInteractable grabInteractable;
        [SerializeField] Rigidbody targetRigidbody;
        [Tooltip("Transform whose visual world scale will match the ghost slot.")]
        [SerializeField] Transform scalableRoot;
        [Tooltip("Optional pivot/mesh alignment marker. This Transform is aligned to the slot pose. Defaults to this object.")]
        [SerializeField] Transform snapAlignment;

        [Header("Runtime State (Read Only)")]
        [SerializeField] GhostObjectSnapSlot currentSlot;
        [SerializeField] bool installing;
        [SerializeField] Transform originalParent;

        int originalSiblingIndex;
        bool hasOriginalState;
        bool originalIsKinematic;
        bool originalUseGravity;
        bool originalDetectCollisions;
        RigidbodyConstraints originalConstraints;
        RigidbodyInterpolation originalInterpolation;
        CollisionDetectionMode originalCollisionDetectionMode;
        Coroutine pendingExitCheck;
        Coroutine snapRoutine;
        bool matchSlotWorldScale = true;
        bool restoreOriginalStateAfterSelection;

        public string CompatibilityId => compatibilityId;
        public XRGrabInteractable GrabInteractable => grabInteractable;
        public Rigidbody TargetRigidbody => targetRigidbody;
        public Transform ScalableRoot => scalableRoot != null ? scalableRoot : transform;
        public Transform SnapAlignment => snapAlignment != null ? snapAlignment : transform;
        public GhostObjectSnapSlot CurrentSlot => currentSlot;
        public bool IsInstalling => installing;
        public bool IsInstalled => currentSlot != null && currentSlot.IsOccupied && currentSlot.CurrentObject == this;

        Transform MotionRoot => targetRigidbody != null ? targetRigidbody.transform : transform;

        void Reset()
        {
            TryGetComponent(out grabInteractable);
            TryGetComponent(out targetRigidbody);
            scalableRoot = transform;
            snapAlignment = transform;
        }

        void Awake()
        {
            ResolveUnsetReferences();
        }

        void OnEnable()
        {
            ResolveUnsetReferences();
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.AddListener(OnSelectEntered);
                grabInteractable.selectExited.AddListener(OnSelectExited);
            }
        }

        void OnDisable()
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
                grabInteractable.selectExited.RemoveListener(OnSelectExited);
            }

            if (pendingExitCheck != null)
            {
                StopCoroutine(pendingExitCheck);
                pendingExitCheck = null;
            }

            if (installing && currentSlot != null)
                ReleaseFromSlot();
            else if (restoreOriginalStateAfterSelection)
                CompleteDeferredReleaseRestore();
        }

        void OnDestroy()
        {
            if (currentSlot != null)
                currentSlot.Release(this);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (currentSlot != null)
                ReleaseSlotForActiveSelection();
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (restoreOriginalStateAfterSelection && CurrentSelectingInteractorCount == 0)
                CompleteDeferredReleaseRestore();

            if (!args.isCanceled && pendingExitCheck == null && !installing && currentSlot == null)
                pendingExitCheck = StartCoroutine(TrySnapAfterSelectionSettles());
        }

        IEnumerator TrySnapAfterSelectionSettles()
        {
            // XRI has removed the exiting interactor before selectExited, but waiting one frame also
            // lets a same-frame two-hand transition settle before deciding this was the last hand.
            yield return null;
            pendingExitCheck = null;

            if (!CanAttemptSnap(CurrentSelectingInteractorCount, installing, currentSlot != null))
                yield break;

            TrySnapToNearestSlot();
        }

        public static bool CanAttemptSnap(int selectingInteractorCount, bool isInstalling, bool alreadyHasSlot)
        {
            return selectingInteractorCount == 0 && !isInstalling && !alreadyHasSlot;
        }

        public bool TrySnapToNearestSlot(bool completeImmediately = false)
        {
            if (!CanAttemptSnap(CurrentSelectingInteractorCount, installing, currentSlot != null))
                return false;

            GhostObjectSnapSlot nearest = GhostObjectSnapRegistry.FindNearestAvailable(
                compatibilityId,
                SnapAlignment.position);
            return TrySnapToSlot(nearest, completeImmediately);
        }

        public bool TrySnapToSlot(GhostObjectSnapSlot slot, bool completeImmediately = false)
        {
            return TrySnapToSlotInternal(slot, completeImmediately, true, true);
        }

        /// <summary>
        /// Installs a newly spawned object through the same reservation and occupancy path as a normal snap.
        /// Distance is intentionally ignored because the caller is responsible for choosing the destination slot.
        /// </summary>
        public bool TryInstallToSlot(GhostObjectSnapSlot slot, bool matchTargetWorldScale = false)
        {
            return TrySnapToSlotInternal(slot, true, false, matchTargetWorldScale);
        }

        bool TrySnapToSlotInternal(
            GhostObjectSnapSlot slot,
            bool completeImmediately,
            bool requireWithinSnapDistance,
            bool shouldMatchSlotWorldScale)
        {
            if (slot == null || !CanAttemptSnap(CurrentSelectingInteractorCount, installing, currentSlot != null) ||
                !slot.IsCompatible(compatibilityId) ||
                (requireWithinSnapDistance && !slot.IsWithinSnapDistance(SnapAlignment.position)) ||
                !ValidateRequiredReferences())
            {
                return false;
            }

            if (!slot.TryReserve(this))
                return false;

            CaptureOriginalState();
            currentSlot = slot;
            installing = true;
            matchSlotWorldScale = shouldMatchSlotWorldScale;
            PrepareRigidbodyForSnap();

            if (completeImmediately || slot.SnapDuration <= 0f)
            {
                CompleteSnap(slot);
                return true;
            }

            snapRoutine = StartCoroutine(AnimateSnap(slot));
            return true;
        }

        public bool ReleaseFromSlot()
        {
            if (currentSlot == null)
                return false;

            if (snapRoutine != null)
            {
                StopCoroutine(snapRoutine);
                snapRoutine = null;
            }

            GhostObjectSnapSlot slot = currentSlot;
            currentSlot = null;
            installing = false;
            matchSlotWorldScale = true;
            slot.Release(this);

            Transform motionRoot = MotionRoot;
            if (hasOriginalState && motionRoot != null)
            {
                motionRoot.SetParent(originalParent, true);
                if (originalParent != null && originalSiblingIndex >= 0 && originalSiblingIndex < originalParent.childCount)
                    motionRoot.SetSiblingIndex(originalSiblingIndex);
            }

            RestoreOriginalRigidbodyState();
            hasOriginalState = false;
            restoreOriginalStateAfterSelection = false;
            return true;
        }

        void ReleaseSlotForActiveSelection()
        {
            if (currentSlot == null)
                return;

            if (snapRoutine != null)
            {
                StopCoroutine(snapRoutine);
                snapRoutine = null;
            }

            GhostObjectSnapSlot slot = currentSlot;
            currentSlot = null;
            installing = false;
            matchSlotWorldScale = true;
            slot.Release(this);

            // XRGrabInteractable has already configured the Rigidbody and detached its Transform
            // before selectEntered is invoked. Restore after its final Select Exit instead of
            // overwriting the active grab's movement setup here.
            restoreOriginalStateAfterSelection = hasOriginalState;
        }

        void CompleteDeferredReleaseRestore()
        {
            if (!restoreOriginalStateAfterSelection)
                return;

            Transform motionRoot = MotionRoot;
            if (hasOriginalState && motionRoot != null)
            {
                motionRoot.SetParent(originalParent, true);
                if (originalParent != null && originalSiblingIndex >= 0 && originalSiblingIndex < originalParent.childCount)
                    motionRoot.SetSiblingIndex(originalSiblingIndex);
            }

            RestoreOriginalRigidbodyState();
            hasOriginalState = false;
            restoreOriginalStateAfterSelection = false;
        }

        public bool DetachFromCurrentSlot()
        {
            return ReleaseFromSlot();
        }

        public void Configure(
            string newCompatibilityId,
            XRGrabInteractable existingGrabInteractable,
            Rigidbody existingRigidbody,
            Transform newScalableRoot = null,
            Transform newSnapAlignment = null)
        {
            compatibilityId = newCompatibilityId;
            grabInteractable = existingGrabInteractable;
            targetRigidbody = existingRigidbody;
            scalableRoot = newScalableRoot != null ? newScalableRoot : transform;
            snapAlignment = newSnapAlignment != null ? newSnapAlignment : transform;
        }

        int CurrentSelectingInteractorCount => grabInteractable != null
            ? grabInteractable.interactorsSelecting.Count
            : 0;

        void ResolveUnsetReferences()
        {
            if (grabInteractable == null)
                TryGetComponent(out grabInteractable);
            if (targetRigidbody == null)
                TryGetComponent(out targetRigidbody);
            if (scalableRoot == null)
                scalableRoot = transform;
            if (snapAlignment == null)
                snapAlignment = transform;
        }

        bool ValidateRequiredReferences()
        {
            if (grabInteractable == null || targetRigidbody == null)
            {
                Debug.LogWarning(
                    $"[Ghost Object Snap] {name} needs an existing XRGrabInteractable and Rigidbody. No component was added automatically.",
                    this);
                return false;
            }

            Transform motionRoot = MotionRoot;
            if (!SnapAlignment.IsChildOf(motionRoot) && SnapAlignment != motionRoot)
            {
                Debug.LogWarning("[Ghost Object Snap] Snap Alignment must belong to the Rigidbody hierarchy.", this);
                return false;
            }

            return true;
        }

        void CaptureOriginalState()
        {
            Transform motionRoot = MotionRoot;
            originalParent = motionRoot.parent;
            originalSiblingIndex = motionRoot.GetSiblingIndex();

            originalIsKinematic = targetRigidbody.isKinematic;
            originalUseGravity = targetRigidbody.useGravity;
            originalDetectCollisions = targetRigidbody.detectCollisions;
            originalConstraints = targetRigidbody.constraints;
            originalInterpolation = targetRigidbody.interpolation;
            originalCollisionDetectionMode = targetRigidbody.collisionDetectionMode;
            hasOriginalState = true;
        }

        void PrepareRigidbodyForSnap()
        {
            targetRigidbody.linearVelocity = Vector3.zero;
            targetRigidbody.angularVelocity = Vector3.zero;
            targetRigidbody.useGravity = false;
            targetRigidbody.isKinematic = true;
            targetRigidbody.detectCollisions = false;
        }

        IEnumerator AnimateSnap(GhostObjectSnapSlot slot)
        {
            Transform motionRoot = MotionRoot;
            Vector3 startPosition = motionRoot.position;
            Quaternion startRotation = motionRoot.rotation;
            Vector3 startWorldScale = ScalableRoot.lossyScale;
            CalculateFinalRootPose(slot, out Vector3 targetPosition, out Quaternion targetRotation);
            Vector3 targetWorldScale = slot.TargetWorldScale;
            float duration = Mathf.Max(0.0001f, slot.SnapDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (currentSlot != slot || CurrentSelectingInteractorCount > 0)
                {
                    ReleaseFromSlot();
                    yield break;
                }

                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / duration);
                float easedTime = normalizedTime * normalizedTime * (3f - 2f * normalizedTime);

                motionRoot.SetPositionAndRotation(
                    Vector3.LerpUnclamped(startPosition, targetPosition, easedTime),
                    Quaternion.SlerpUnclamped(startRotation, targetRotation, easedTime));
                SetWorldScale(ScalableRoot, Vector3.LerpUnclamped(startWorldScale, targetWorldScale, easedTime));
                targetRigidbody.linearVelocity = Vector3.zero;
                targetRigidbody.angularVelocity = Vector3.zero;
                yield return null;
            }

            CompleteSnap(slot);
        }

        void CalculateFinalRootPose(GhostObjectSnapSlot slot, out Vector3 position, out Quaternion rotation)
        {
            Transform motionRoot = MotionRoot;
            Vector3 originalPosition = motionRoot.position;
            Quaternion originalRotation = motionRoot.rotation;
            Vector3 originalScale = ScalableRoot.localScale;

            if (matchSlotWorldScale)
                SetWorldScale(ScalableRoot, slot.TargetWorldScale);
            AlignRootToPose(motionRoot, SnapAlignment, slot.SnapPose);
            position = motionRoot.position;
            rotation = motionRoot.rotation;

            motionRoot.SetPositionAndRotation(originalPosition, originalRotation);
            ScalableRoot.localScale = originalScale;
        }

        void CompleteSnap(GhostObjectSnapSlot slot)
        {
            if (slot == null || currentSlot != slot)
                return;

            Transform motionRoot = MotionRoot;
            if (matchSlotWorldScale)
                SetWorldScale(ScalableRoot, slot.TargetWorldScale);
            AlignRootToPose(motionRoot, SnapAlignment, slot.SnapPose);
            if (matchSlotWorldScale)
                SetWorldScale(ScalableRoot, slot.TargetWorldScale);
            AlignRootToPose(motionRoot, SnapAlignment, slot.SnapPose);

            motionRoot.SetParent(slot.SnapPose, true);
            targetRigidbody.linearVelocity = Vector3.zero;
            targetRigidbody.angularVelocity = Vector3.zero;
            targetRigidbody.detectCollisions = originalDetectCollisions;

            installing = false;
            snapRoutine = null;
            slot.CommitOccupancy(this);
        }

        void RestoreOriginalRigidbodyState()
        {
            if (!hasOriginalState || targetRigidbody == null)
                return;

            targetRigidbody.linearVelocity = Vector3.zero;
            targetRigidbody.angularVelocity = Vector3.zero;
            targetRigidbody.constraints = originalConstraints;
            targetRigidbody.interpolation = originalInterpolation;
            targetRigidbody.collisionDetectionMode = originalCollisionDetectionMode;
            targetRigidbody.detectCollisions = originalDetectCollisions;
            targetRigidbody.useGravity = originalUseGravity;
            targetRigidbody.isKinematic = originalIsKinematic;
        }

        static void AlignRootToPose(Transform motionRoot, Transform alignment, Transform targetPose)
        {
            Quaternion correction = targetPose.rotation * Quaternion.Inverse(alignment.rotation);
            motionRoot.rotation = correction * motionRoot.rotation;
            motionRoot.position += targetPose.position - alignment.position;
        }

        public static void SetWorldScale(Transform target, Vector3 desiredWorldScale)
        {
            if (target == null)
                return;

            Vector3 currentWorldScale = target.lossyScale;
            Vector3 localScale = target.localScale;
            target.localScale = new Vector3(
                ScaleComponent(localScale.x, desiredWorldScale.x, currentWorldScale.x),
                ScaleComponent(localScale.y, desiredWorldScale.y, currentWorldScale.y),
                ScaleComponent(localScale.z, desiredWorldScale.z, currentWorldScale.z));
        }

        static float ScaleComponent(float localScale, float desiredWorldScale, float currentWorldScale)
        {
            return Mathf.Abs(currentWorldScale) > 0.000001f
                ? localScale * desiredWorldScale / currentWorldScale
                : localScale;
        }
    }
}
