using System.Collections;
using HClTraining.SharedXR;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRProject.ObjectInstallation;

namespace VRProject.LeakResponse.Narration
{
    /// <summary>
    /// LeakResponse-owned meaning of Left Y. Shared XR supplies only input and haptics;
    /// the bag zone, retrieval anchor, and item state belong to this Stage.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class P102SharedBagItemHandler : MonoBehaviour
    {
        const float PresentationDuration = 0.28f;
        const string LeftControllerName = "Left Controller";
        const string RetrievalZoneName = "LeakResponse_BagRetrievalZone";
        const string RetrievalAnchorName = "LeakResponse_ItemRetrievalAnchor";
        static readonly Vector3 ConcealedLocalOffset = new Vector3(0f, -0.035f, -0.12f);
        static readonly Vector3 ReadyLocalOffset = new Vector3(0f, 0.045f, 0.10f);

        P102LeakNarrationController narration;
        PreplacedGhostInstallGroup sockGroup;
        PreplacedGhostInstallGroup padGroup;
        XRGrabInteractable sockSource;
        XRGrabInteractable padSource;
        SharedXRInputContract sharedInput;
        SessionEquipmentInventory sessionInventory;
        Transform leftController;
        Collider retrievalZone;
        Transform retrievalAnchor;
        XRGrabInteractable presentedSource;
        Rigidbody presentedBody;
        Transform presentationAnchor;
        Coroutine presentationRoutine;
        Coroutine releaseRoutine;
        bool presentedOriginalIsKinematic;
        bool presentedOriginalUseGravity;
        bool sockAvailable;
        bool padAvailable;
        bool sockCheckedOut;
        bool padCheckedOut;
        bool configured;

        public bool IsPresentingItem =>
            presentedSource != null && presentedSource.gameObject.activeInHierarchy &&
            !presentedSource.isSelected;
        public XRGrabInteractable PresentedSource => presentedSource;
        public Transform PresentationAnchor => presentationAnchor;
        public Transform LeftController => leftController;
        public Collider RetrievalZone => retrievalZone;
        public Transform RetrievalAnchor => retrievalAnchor;

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
            sessionInventory = SessionEquipmentInventory.Ensure();
            sessionInventory?.SeedStandalone(SessionEquipmentPresets.LeakResponseStandaloneEquipment);
            ResolveStageBindings();
            configured = narration != null && sockGroup != null && padGroup != null &&
                         sockSource != null && padSource != null &&
                         retrievalZone != null && retrievalAnchor != null;

            if (!configured)
            {
                Debug.LogError(
                    "[P102 Stage Bag] Sock/Pad or LeakResponse bag references are incomplete.",
                    this);
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

            ResolveStageBindings();
            if (!IsLeftHandInAccessZone())
            {
                return;
            }

            if (narration != null && (narration.IsPlaying || narration.PendingCount > 0))
                return;

            string equipmentId = null;
            if (!sockGroup.IsInstalled && sockSource.enabled && sockAvailable)
            {
                equipmentId = SharedEquipmentIds.AbsorbentSock;
            }
            else if (sockGroup.IsInstalled && !padGroup.IsInstalled && padSource.enabled &&
                     padAvailable)
            {
                equipmentId = SharedEquipmentIds.AbsorbentPad;
            }

            if (equipmentId == null)
                return;

            XRGrabInteractable source = equipmentId == SharedEquipmentIds.AbsorbentSock
                ? sockSource
                : padSource;
            bool alreadyCheckedOut = equipmentId == SharedEquipmentIds.AbsorbentSock
                ? sockCheckedOut
                : padCheckedOut;
            bool checkedOutNow = false;
            if (!alreadyCheckedOut)
            {
                checkedOutNow = sessionInventory != null && sessionInventory.TryCheckOut(equipmentId);
                if (!checkedOutNow)
                {
                    SharedTrainingTelemetry.Current.BagItemRequested(equipmentId, false);
                    return;
                }
                SetCheckedOut(equipmentId, true);
            }

            bool deployed = PresentAtBagMouth(source, retrievalAnchor);
            SharedTrainingTelemetry.Current.BagItemRequested(equipmentId, deployed);
            if (!deployed)
            {
                if (checkedOutNow)
                {
                    sessionInventory.ReturnCheckedOut(equipmentId);
                    SetCheckedOut(equipmentId, false);
                }
                return;
            }
            SharedTrainingTelemetry.Current.EquipmentUsed(equipmentId);

            sharedInput?.SendHaptic(SharedXRHand.Left, 0.35f, 0.08f);
            Debug.Log($"[P102 Stage Bag] Retrieved existing Stage item '{equipmentId}'.", this);
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
                ConsumeCheckedOut(SharedEquipmentIds.AbsorbentSock);
                SetAvailability(SharedEquipmentIds.AbsorbentSock, false);
            }
            else if (group == padGroup)
            {
                StopPresentationFor(padSource, false);
                ConsumeCheckedOut(SharedEquipmentIds.AbsorbentPad);
                SetAvailability(SharedEquipmentIds.AbsorbentPad, false);
            }
        }

        bool PresentAtBagMouth(XRGrabInteractable source, Transform anchor)
        {
            if (source == null || anchor == null || source.isSelected)
                return false;

            StopPresentation(true);
            source.gameObject.SetActive(true);
            source.enabled = true;
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
            return true;
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

        void SetAvailability(string equipmentId, bool available)
        {
            if (equipmentId == SharedEquipmentIds.AbsorbentSock)
                sockAvailable = available;
            else if (equipmentId == SharedEquipmentIds.AbsorbentPad)
                padAvailable = available;
        }

        void SetCheckedOut(string equipmentId, bool value)
        {
            if (equipmentId == SharedEquipmentIds.AbsorbentSock)
                sockCheckedOut = value;
            else if (equipmentId == SharedEquipmentIds.AbsorbentPad)
                padCheckedOut = value;
        }

        void ConsumeCheckedOut(string equipmentId)
        {
            bool checkedOut = equipmentId == SharedEquipmentIds.AbsorbentSock
                ? sockCheckedOut
                : padCheckedOut;
            if (!checkedOut)
                return;
            sessionInventory?.ConsumeCheckedOut(equipmentId);
            SetCheckedOut(equipmentId, false);
        }

        void ReturnOutstandingCheckouts()
        {
            if (sockCheckedOut)
                sessionInventory?.ReturnCheckedOut(SharedEquipmentIds.AbsorbentSock);
            if (padCheckedOut)
                sessionInventory?.ReturnCheckedOut(SharedEquipmentIds.AbsorbentPad);
            sockCheckedOut = false;
            padCheckedOut = false;
        }

        public bool IsLeftHandInAccessZone()
        {
            return leftController != null && retrievalZone != null &&
                   retrievalZone.enabled && retrievalZone.gameObject.activeInHierarchy &&
                   retrievalZone.bounds.Contains(leftController.position);
        }

        void ResolveStageBindings()
        {
            if (retrievalZone == null || !retrievalZone.gameObject.scene.isLoaded)
            {
                Transform zoneTransform = FindStageTransform(RetrievalZoneName);
                retrievalZone = zoneTransform != null ? zoneTransform.GetComponent<Collider>() : null;
            }

            if (retrievalAnchor == null || !retrievalAnchor.gameObject.scene.isLoaded)
                retrievalAnchor = FindStageTransform(RetrievalAnchorName);

            if (leftController == null || !leftController.gameObject.activeInHierarchy)
                leftController = FindActiveTransform(LeftControllerName);
        }

        Transform FindStageTransform(string exactName)
        {
            var scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            foreach (var root in scene.GetRootGameObjects())
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < transforms.Length; i++)
                    if (transforms[i] != null && transforms[i].name == exactName)
                        return transforms[i];
            }
            return null;
        }

        static Transform FindActiveTransform(string exactName)
        {
            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var i = 0; i < transforms.Length; i++)
            {
                var candidate = transforms[i];
                if (candidate != null && candidate.name == exactName &&
                    candidate.gameObject.scene.IsValid() && candidate.gameObject.scene.isLoaded)
                {
                    return candidate;
                }
            }
            return null;
        }

        void UnsubscribeStageState()
        {
            StopPresentation(true);
            ReturnOutstandingCheckouts();
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
