using HClTraining.SharedXR;
using UnityEngine;
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
        P102LeakNarrationController narration;
        PreplacedGhostInstallGroup sockGroup;
        PreplacedGhostInstallGroup padGroup;
        XRGrabInteractable sockSource;
        XRGrabInteractable padSource;
        SharedXRInputContract sharedInput;
        bool configured;

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
                SetAvailability(SharedEquipmentIds.AbsorbentSock, false);
            else if (group == padGroup)
                SetAvailability(SharedEquipmentIds.AbsorbentPad, false);
        }

        static void SetAvailability(string equipmentId, bool available)
        {
            SharedEquipmentInventory.Provider?.SetAvailable(equipmentId, available);
        }

        void UnsubscribeStageState()
        {
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
