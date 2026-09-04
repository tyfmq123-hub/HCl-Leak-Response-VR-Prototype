using System.Collections;
using HClTraining.SharedXR;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRProject.ObjectInstallation;

namespace VRProject.LeakResponse.Narration
{
    /// <summary>
    /// LeakResponse-owned bag Grip action. Shared XR supplies only input and haptics;
    /// the bag zone, retrieval anchor, and item state belong to this Stage.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class P102SharedBagItemHandler : MonoBehaviour
    {
        const float PresentationDuration = 0.28f;
        const string LeftControllerName = "Left Controller";
        const string RightControllerName = "Right Controller";
        const string RetrievalZoneName = "LeakResponse_BagRetrievalZone";
        const string RetrievalAnchorName = "LeakResponse_ItemRetrievalAnchor";
        static readonly Vector3 ConcealedLocalOffset = new Vector3(0f, -0.20f, 0f);
        static readonly Vector3 ReadyLocalOffset = new Vector3(0f, 0.28f, 0f);

        P102LeakNarrationController narration;
        PreplacedGhostInstallGroup sockGroup;
        PreplacedGhostInstallGroup padGroup;
        XRGrabInteractable sockSource;
        XRGrabInteractable padSource;
        SharedXRInputContract sharedInput;
        SessionEquipmentInventory sessionInventory;
        Transform leftController;
        Transform rightController;
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
        bool leftGripReleaseArmed;
        bool rightGripReleaseArmed;
        bool configured;

        public bool IsPresentingItem =>
            presentedSource != null && presentedSource.gameObject.activeInHierarchy &&
            !presentedSource.isSelected;
        public XRGrabInteractable PresentedSource => presentedSource;
        public Transform PresentationAnchor => presentationAnchor;
        public Transform LeftController => leftController;
        public Transform RightController => rightController;
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
            ClearGripReleaseArms();
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
            sharedInput.Canceled += OnSharedInputCanceled;
        }

        void UnsubscribeSharedInput()
        {
            if (sharedInput != null)
            {
                sharedInput.Performed -= OnSharedInputPerformed;
                sharedInput.Canceled -= OnSharedInputCanceled;
            }
            sharedInput = null;
        }

        void OnSharedInputPerformed(SharedXRHand hand, SharedXRControl control)
        {
            if (!configured || control != SharedXRControl.Grip)
            {
                return;
            }

            ResolveStageBindings();
            if (presentedSource != null || !IsHandInAccessZone(hand) ||
                ResolveAvailableEquipmentId() == null)
            {
                return;
            }

            // Arm the bag gesture on press, but do not expose the item yet. If the
            // item appears while Grip is held, XRI immediately selects it with that
            // same press and drops it again on release. Deployment therefore occurs
            // only after the matching Grip release.
            SetGripReleaseArmed(hand, true);
        }

        void OnSharedInputCanceled(SharedXRHand hand, SharedXRControl control)
        {
            if (control != SharedXRControl.Grip || !TakeGripReleaseArm(hand))
                return;

            RetrieveNextAvailableItem(hand);
        }

        void RetrieveNextAvailableItem(SharedXRHand hand)
        {
            if (!configured || presentedSource != null)
                return;

            ResolveStageBindings();
            string equipmentId = ResolveAvailableEquipmentId();
            if (equipmentId == null)
                return;

            // One completed press-release gesture can deploy only one item. Clear
            // the other hand's pending gesture before changing the bag state.
            ClearGripReleaseArms();

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

            sharedInput?.SendHaptic(hand, 0.35f, 0.08f);
            Debug.Log(
                $"[P102 Stage Bag] Grip released; presented Stage item '{equipmentId}'.",
                this);
        }

        string ResolveAvailableEquipmentId()
        {
            if (!sockGroup.IsInstalled && sockSource.enabled && sockAvailable)
            {
                return SharedEquipmentIds.AbsorbentSock;
            }
            if (sockGroup.IsInstalled && !padGroup.IsInstalled && padSource.enabled &&
                padAvailable)
            {
                return SharedEquipmentIds.AbsorbentPad;
            }

            return null;
        }

        void SetGripReleaseArmed(SharedXRHand hand, bool armed)
        {
            if (hand == SharedXRHand.Left)
                leftGripReleaseArmed = armed;
            else if (hand == SharedXRHand.Right)
                rightGripReleaseArmed = armed;
        }

        bool TakeGripReleaseArm(SharedXRHand hand)
        {
            bool armed = hand == SharedXRHand.Left
                ? leftGripReleaseArmed
                : hand == SharedXRHand.Right && rightGripReleaseArmed;
            SetGripReleaseArmed(hand, false);
            return armed;
        }

        void ClearGripReleaseArms()
        {
            leftGripReleaseArmed = false;
            rightGripReleaseArmed = false;
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
            ClearGripReleaseArms();
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

            source.transform.SetParent(anchor, false);
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
            return IsHandInAccessZone(SharedXRHand.Left);
        }

        public bool IsRightHandInAccessZone()
        {
            return IsHandInAccessZone(SharedXRHand.Right);
        }

        bool IsHandInAccessZone(SharedXRHand hand)
        {
            Transform controller = hand == SharedXRHand.Left
                ? leftController
                : hand == SharedXRHand.Right ? rightController : null;
            return controller != null && retrievalZone != null &&
                   retrievalZone.enabled && retrievalZone.gameObject.activeInHierarchy &&
                   retrievalZone.bounds.Contains(controller.position);
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

            if (rightController == null || !rightController.gameObject.activeInHierarchy)
                rightController = FindActiveTransform(RightControllerName);
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
            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude);
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
            ClearGripReleaseArms();
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
