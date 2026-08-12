using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRProject.ObjectInstallation
{
    /// <summary>
    /// After a supply is released, magnet-snaps it when it enters the nearest matching preplaced group.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PreplacedGhostInstallSource : MonoBehaviour
    {
        [Header("Supply Type")]
        [Tooltip("Installation type supplied by this object, for example Pad or Sock.")]
        [SerializeField] string itemTypeId;

        [Header("Existing Object Reference")]
        [Tooltip("Existing XR Grab Interactable whose final Select Exit triggers installation.")]
        [SerializeField] XRGrabInteractable grabInteractable;

        [Header("Consumption")]
        [Tooltip("Set this supply GameObject inactive only after a complete group installation succeeds.")]
        [SerializeField] bool consumeSourceOnSuccess = true;

        [Header("Magnet Snap")]
        [SerializeField, Min(0f)] float snapDuration = 0.2f;

        XRGrabInteractable subscribedGrabInteractable;
        Coroutine pendingReleaseRoutine;
        bool processing;
        bool snapCanceled;
        bool releasedForMagnet;

        public string ItemTypeId => itemTypeId;
        public XRGrabInteractable GrabInteractable => grabInteractable;
        public bool ConsumeSourceOnSuccess => consumeSourceOnSuccess;
        public bool IsProcessing => processing;

        void Reset()
        {
            TryGetComponent(out grabInteractable);
        }

        void Awake()
        {
            ResolveGrabInteractable();
        }

        void OnEnable()
        {
            ResolveGrabInteractable();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
            if (pendingReleaseRoutine != null)
            {
                StopCoroutine(pendingReleaseRoutine);
                pendingReleaseRoutine = null;
            }

            processing = false;
            releasedForMagnet = false;
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        public void Configure(
            string newItemTypeId,
            XRGrabInteractable existingGrabInteractable,
            bool newConsumeSourceOnSuccess,
            float newSnapDuration = 0.2f)
        {
            bool wasSubscribed = subscribedGrabInteractable != null;
            if (wasSubscribed)
                Unsubscribe();

            itemTypeId = newItemTypeId;
            grabInteractable = existingGrabInteractable;
            consumeSourceOnSuccess = newConsumeSourceOnSuccess;
            snapDuration = Mathf.Max(0f, newSnapDuration);
            releasedForMagnet = false;

            if (isActiveAndEnabled)
                Subscribe();
        }

        void Update()
        {
            if (!releasedForMagnet || processing || pendingReleaseRoutine != null ||
                grabInteractable == null || !grabInteractable.enabled ||
                grabInteractable.interactorsSelecting.Count != 0)
            {
                return;
            }

            if (!TryResolveMagnetTarget(out PreplacedGhostInstallGroup group, out Transform snapPose))
                return;

            processing = true;
            pendingReleaseRoutine = StartCoroutine(ProcessResolvedTarget(group, snapPose));
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            releasedForMagnet = false;
            snapCanceled = true;
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.isCanceled)
                return;

            releasedForMagnet = true;
            if (processing || pendingReleaseRoutine != null)
                return;

            processing = true;
            pendingReleaseRoutine = StartCoroutine(ProcessReleaseAfterSelectionSettles());
        }

        IEnumerator ProcessReleaseAfterSelectionSettles()
        {
            yield return null;
            pendingReleaseRoutine = null;

            if (grabInteractable == null || grabInteractable.interactorsSelecting.Count != 0 ||
                string.IsNullOrWhiteSpace(itemTypeId))
            {
                processing = false;
                yield break;
            }

            if (!TryResolveMagnetTarget(out PreplacedGhostInstallGroup group, out Transform snapPose))
            {
                processing = false;
                yield break;
            }

            yield return ProcessResolvedTarget(group, snapPose);
        }

        IEnumerator ProcessResolvedTarget(PreplacedGhostInstallGroup group, Transform snapPose)
        {
            snapCanceled = false;
            yield return AnimateSourceToGhost(snapPose);
            bool succeeded = !snapCanceled && group != null &&
                             group.TryInstallAll(itemTypeId, out int installedCount) &&
                             installedCount > 0;
            processing = false;
            pendingReleaseRoutine = null;

            if (succeeded)
            {
                releasedForMagnet = false;
                if (consumeSourceOnSuccess)
                    gameObject.SetActive(false);
            }
        }

        bool TryResolveMagnetTarget(out PreplacedGhostInstallGroup group, out Transform snapPose)
        {
            group = null;
            snapPose = null;
            if (grabInteractable == null || string.IsNullOrWhiteSpace(itemTypeId))
                return false;

            Vector3 sourcePosition = grabInteractable.transform.position;
            group = PreplacedGhostInstallGroup.FindNearestCompatible(itemTypeId, sourcePosition);
            return group != null && group.TryGetNearestInstallPose(sourcePosition, out snapPose);
        }

        IEnumerator AnimateSourceToGhost(Transform snapPose)
        {
            if (snapPose == null)
                yield break;

            Transform sourceTransform = grabInteractable.transform;
            Rigidbody body = grabInteractable.GetComponent<Rigidbody>();
            bool originalIsKinematic = body != null && body.isKinematic;
            bool originalUseGravity = body != null && body.useGravity;
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = false;
                body.isKinematic = true;
            }

            Vector3 startPosition = sourceTransform.position;
            Quaternion startRotation = sourceTransform.rotation;
            float duration = Mathf.Max(0f, snapDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (grabInteractable.interactorsSelecting.Count > 0)
                {
                    snapCanceled = true;
                    break;
                }

                elapsed += Time.deltaTime;
                float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                sourceTransform.SetPositionAndRotation(
                    Vector3.LerpUnclamped(startPosition, snapPose.position, eased),
                    Quaternion.SlerpUnclamped(startRotation, snapPose.rotation, eased));
                yield return null;
            }

            if (!snapCanceled)
                sourceTransform.SetPositionAndRotation(snapPose.position, snapPose.rotation);
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = originalUseGravity;
                body.isKinematic = originalIsKinematic;
            }
        }

        void ResolveGrabInteractable()
        {
            if (grabInteractable == null)
                TryGetComponent(out grabInteractable);
        }

        void Subscribe()
        {
            if (grabInteractable == null || subscribedGrabInteractable == grabInteractable)
                return;

            Unsubscribe();
            subscribedGrabInteractable = grabInteractable;
            subscribedGrabInteractable.selectEntered.AddListener(OnSelectEntered);
            subscribedGrabInteractable.selectExited.AddListener(OnSelectExited);
        }

        void Unsubscribe()
        {
            if (subscribedGrabInteractable == null)
                return;

            subscribedGrabInteractable.selectEntered.RemoveListener(OnSelectEntered);
            subscribedGrabInteractable.selectExited.RemoveListener(OnSelectExited);
            subscribedGrabInteractable = null;
        }
    }
}
