using System.Collections;
using HClTraining.SharedXR;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRProject.ObjectInstallation;

namespace VRProject.LeakResponse.Narration
{
    /// <summary>
    /// LeakResponse-owned meaning of Left Y. Shared XR only supplies the button and
    /// body-relative bag contract; this component chooses Sock or Pad from stage state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class P102SharedBagItemHandler : MonoBehaviour
    {
        const float PresentationDuration = 0.28f;
        static readonly Vector3 ConcealedLocalOffset = new Vector3(0f, -0.035f, -0.12f);
        static readonly Vector3 ReadyLocalOffset = new Vector3(0f, 0.045f, 0.10f);

        P102LeakNarrationController narration;
        PreplacedGhostInstallGroup sockGroup;
        PreplacedGhostInstallGroup padGroup;
        XRGrabInteractable sockSource;
        XRGrabInteractable padSource;
        SharedXRInputContract sharedInput;
        XRGrabInteractable presentedSource;
        Rigidbody presentedBody;
        Transform presentationAnchor;
        Coroutine presentationRoutine;
        Coroutine releaseRoutine;
        bool presentedOriginalIsKinematic;
        bool presentedOriginalUseGravity;
        bool configured;

        public bool IsPresentingItem =>
            presentedSource != null && presentedSource.gameObject.activeInHierarchy &&
            !presentedSource.isSelected;
        public XRGrabInteractable PresentedSource => presentedSource;
        public Transform PresentationAnchor => presentationAnchor;

        public void Configure(
            P102LeakNarrationController stageNarration,
            PreplacedGhostInstallGroup stageSockGroup,
            PreplacedGhostInstallGroup stagePadGroup,
            XRGrabInteractable existingSockSource,
            XRGrabInteractable existingPadSource)
        {
            UnsubscribeStageState();
            narration = stageNarration;
            sockGroup = stageSockGroup;
            padGroup = stagePadGroup;
            sockSource = existingSockSource;
            padSource = existingPadSource;
            configured = narration != null && sockGroup != null && padGroup != null &&
                         sockSource != null && padSource != null;

            if (!configured)
            {
                Debug.LogError("[P102 Shared Bag] Existing Sock/Pad stage references are incomplete.", this);
                return;
            }

            narration.CueCompleted += OnCueCompleted;
            sockGroup.Installed += OnGroupInstalled;
            padGroup.Installed += OnGroupInstalled;
            SetAvailability(SharedEquipmentIds.AbsorbentSock, false);
            SetAvailability(SharedEquipmentIds.AbsorbentPad, false);
            SubscribeSharedInput();
        }

        void OnEnable()
        {
            SubscribeSharedInput();
        }

        void OnDisable()
        {
            UnsubscribeSharedInput();
        }

        void SubscribeSharedInput()
        {
            var candidate = SharedXRInputContract.Instance;
            if (candidate == null || candidate == sharedInput)
                return;
            UnsubscribeSharedInput();
            sharedInput = candidate;
            sharedInput.Performed += OnSharedInputPerformed;
        }

        void UnsubscribeSharedInput()
        {
            if (sharedInput != null)
                sharedInput.Performed -= OnSharedInputPerformed;
            sharedInput = null;
        }

        void OnSharedInputPerformed(SharedXRHand hand, SharedXRControl control)
        {
            if (!configured || hand != SharedXRHand.Left ||
                control != SharedXRControl.SecondaryButton)
            {
                return;
            }

            var equipment = SharedPlayerEquipment.Instance;
            var inventory = SharedEquipmentInventory.Provider;
            if (equipment == null || inventory == null || equipment.RetrievalAnchor == null ||
                !equipment.IsLeftHandInRetrievalZone())
            {
                return;
            }

            if (narration != null && (narration.IsPlaying || narration.PendingCount > 0))
                return;

            string equipmentId = null;
            if (!sockGroup.IsInstalled && sockSource.enabled &&
                inventory.IsAvailable(SharedEquipmentIds.AbsorbentSock))
            {
                equipmentId = SharedEquipmentIds.AbsorbentSock;
            }
            else if (sockGroup.IsInstalled && !padGroup.IsInstalled && padSource.enabled &&
                     inventory.IsAvailable(SharedEquipmentIds.AbsorbentPad))
            {
                equipmentId = SharedEquipmentIds.AbsorbentPad;
            }

            if (equipmentId == null || !inventory.TryDeployAt(equipmentId, equipment.RetrievalAnchor))
                return;

            XRGrabInteractable source = equipmentId == SharedEquipmentIds.AbsorbentSock
                ? sockSource
                : padSource;
            PresentAtBagMouth(source, equipment.RetrievalAnchor);

            sharedInput?.SendHaptic(SharedXRHand.Left, 0.35f, 0.08f);
            Debug.Log($"[P102 Shared Bag] Retrieved existing stage item '{equipmentId}'.", this);
        }

        void OnCueCompleted(P102LeakNarrationCue cue)
        {
            if (cue == P102LeakNarrationCue.AbsorbentSockPlacementActivated && !sockGroup.IsInstalled)
            {
                SetAvailability(SharedEquipmentIds.AbsorbentSock, true);
            }
            else if (cue == P102LeakNarrationCue.AbsorbentPadPlacementActivated &&
                     sockGroup.IsInstalled && !padGroup.IsInstalled)
            {
                SetAvailability(SharedEquipmentIds.AbsorbentPad, true);
            }
        }

        void OnGroupInstalled(PreplacedGhostInstallGroup group)
        {
            if (group == sockGroup)
            {
                StopPresentationFor(sockSource, false);
                SetAvailability(SharedEquipmentIds.AbsorbentSock, false);
            }
            else if (group == padGroup)
            {
                StopPresentationFor(padSource, false);
                SetAvailability(SharedEquipmentIds.AbsorbentPad, false);
            }
        }

        void PresentAtBagMouth(XRGrabInteractable source, Transform anchor)
        {
            if (source == null || anchor == null)
                return;

            StopPresentation(true);
            presentedSource = source;
            presentationAnchor = anchor;
            presentedBody = source.GetComponent<Rigidbody>();
            if (presentedBody != null)
            {
                presentedOriginalIsKinematic = presentedBody.isKinematic;
                presentedOriginalUseGravity = presentedBody.useGravity;
                presentedBody.linearVelocity = Vector3.zero;
                presentedBody.angularVelocity = Vector3.zero;
                presentedBody.useGravity = false;
                presentedBody.isKinematic = true;
            }

            source.transform.SetParent(anchor, true);
            source.transform.localPosition = ConcealedLocalOffset;
            source.transform.localRotation = Quaternion.identity;
            source.selectEntered.AddListener(OnPresentedSourceSelected);
            source.selectExited.AddListener(OnPresentedSourceReleased);
            presentationRoutine = StartCoroutine(AnimatePresentation(source, anchor));
        }

        IEnumerator AnimatePresentation(XRGrabInteractable source, Transform anchor)
        {
            float elapsed = 0f;
            while (elapsed < PresentationDuration && source != null && anchor != null &&
                   source == presentedSource && !source.isSelected)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / PresentationDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                source.transform.localPosition = Vector3.LerpUnclamped(
                    ConcealedLocalOffset, ReadyLocalOffset, eased);
                source.transform.localRotation = Quaternion.identity;
                yield return null;
            }

            if (source != null && source == presentedSource && !source.isSelected &&
                source.transform.parent == anchor)
            {
                source.transform.localPosition = ReadyLocalOffset;
                source.transform.localRotation = Quaternion.identity;
            }
            presentationRoutine = null;
        }

        void OnPresentedSourceSelected(SelectEnterEventArgs args)
        {
            if (presentedSource == null)
                return;

            StopRoutine(ref presentationRoutine);
            StopRoutine(ref releaseRoutine);
            if (presentedSource.transform.parent == presentationAnchor)
                presentedSource.transform.SetParent(null, true);
        }

        void OnPresentedSourceReleased(SelectExitEventArgs args)
        {
            if (args.isCanceled || presentedSource == null)
                return;

            StopRoutine(ref releaseRoutine);
            releaseRoutine = StartCoroutine(RestoreReleasedPhysics(presentedSource));
        }

        IEnumerator RestoreReleasedPhysics(XRGrabInteractable source)
        {
            yield return null;
            var installSource = source != null
                ? source.GetComponent<PreplacedGhostInstallSource>()
                : null;
            while (source != null && source.gameObject.activeInHierarchy &&
                   installSource != null && installSource.IsProcessing)
            {
                yield return null;
            }

            releaseRoutine = null;
            if (source == null || source != presentedSource || source.isSelected)
                yield break;

            RestorePresentedBodyState();
            StopPresentation(false);
        }

        void StopPresentationFor(XRGrabInteractable source, bool restorePhysics)
        {
            if (source != null && source == presentedSource)
                StopPresentation(restorePhysics);
        }

        void StopPresentation(bool restorePhysics)
        {
            StopRoutine(ref presentationRoutine);
            StopRoutine(ref releaseRoutine);
            if (presentedSource != null)
            {
                presentedSource.selectEntered.RemoveListener(OnPresentedSourceSelected);
                presentedSource.selectExited.RemoveListener(OnPresentedSourceReleased);
                if (presentedSource.transform.parent == presentationAnchor)
                    presentedSource.transform.SetParent(null, true);
            }
            if (restorePhysics)
                RestorePresentedBodyState();

            presentedSource = null;
            presentedBody = null;
            presentationAnchor = null;
        }

        void RestorePresentedBodyState()
        {
            if (presentedBody == null)
                return;

            presentedBody.linearVelocity = Vector3.zero;
            presentedBody.angularVelocity = Vector3.zero;
            presentedBody.isKinematic = presentedOriginalIsKinematic;
            presentedBody.useGravity = presentedOriginalUseGravity;
            if (!presentedBody.isKinematic)
                presentedBody.WakeUp();
        }

        void StopRoutine(ref Coroutine routine)
        {
            if (routine == null)
                return;
            StopCoroutine(routine);
            routine = null;
        }

        static void SetAvailability(string equipmentId, bool available)
        {
            SharedEquipmentInventory.Provider?.SetAvailable(equipmentId, available);
        }

        void UnsubscribeStageState()
        {
            StopPresentation(true);
            if (narration != null)
                narration.CueCompleted -= OnCueCompleted;
            if (sockGroup != null)
                sockGroup.Installed -= OnGroupInstalled;
            if (padGroup != null)
                padGroup.Installed -= OnGroupInstalled;
            configured = false;
        }

        void OnDestroy()
        {
            UnsubscribeSharedInput();
            UnsubscribeStageState();
        }
    }
}
