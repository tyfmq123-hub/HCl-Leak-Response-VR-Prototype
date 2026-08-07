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

        XRGrabInteractable subscribedGrabInteractable;
        Coroutine pendingReleaseRoutine;
        bool processing;

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
            bool newConsumeSourceOnSuccess)
        {
            bool wasSubscribed = subscribedGrabInteractable != null;
            if (wasSubscribed)
                Unsubscribe();

            itemTypeId = newItemTypeId;
            grabInteractable = existingGrabInteractable;
            consumeSourceOnSuccess = newConsumeSourceOnSuccess;

            if (wasSubscribed && isActiveAndEnabled)
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

            bool succeeded = group != null &&
                             group.TryInstallAll(itemTypeId, out int installedCount) &&
                             installedCount > 0;
            processing = false;

            if (succeeded && consumeSourceOnSuccess)
                gameObject.SetActive(false);
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
