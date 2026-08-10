using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRProject.ObjectInstallation
{
    /// <summary>
    /// Uses the final Select Exit of one supply object to activate the nearest matching preplaced group.
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

            if (isActiveAndEnabled)
                Subscribe();
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.isCanceled || processing || pendingReleaseRoutine != null)
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

            PreplacedGhostInstallGroup group = PreplacedGhostInstallGroup.FindNearestCompatible(
                itemTypeId,
                grabInteractable.transform.position);

            bool succeeded = false;
            if (group != null && group.TryGetNearestInstallPose(
                    grabInteractable.transform.position,
                    out Transform snapPose))
            {
                snapCanceled = false;
                yield return AnimateSourceToGhost(snapPose);
                succeeded = !snapCanceled &&
                            group.TryInstallAll(itemTypeId, out int installedCount) &&
                            installedCount > 0;
            }
            processing = false;

            if (succeeded && consumeSourceOnSuccess)
                gameObject.SetActive(false);
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
            subscribedGrabInteractable.selectExited.AddListener(OnSelectExited);
        }

        void Unsubscribe()
        {
            if (subscribedGrabInteractable == null)
                return;

            subscribedGrabInteractable.selectExited.RemoveListener(OnSelectExited);
            subscribedGrabInteractable = null;
        }
    }
}
