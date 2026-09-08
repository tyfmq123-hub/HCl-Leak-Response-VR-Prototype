using System.Collections;
using System.Collections.Generic;
using HClTraining.SharedXR;
using JH.EntrySafety;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRProject.LeakResponse.Narration;

namespace HClTraining.App
{
    [DisallowMultipleComponent]
    public sealed class StageLifecycleAdapter : MonoBehaviour
    {
        ScenarioFlowManager scenarioFlow;
        TrainingSessionState session;
        PersistentEquipmentInventory inventory;
        PPEStateController ppeState;

        PPESequenceManager ppeSequence;
        PPESequenceStep? ppeTrackedStep;
        EntrySafetyScenarioController entryController;
        EntrySafetyStateMachine entryState;
        EntrySafetyStep entryTrackedStep;
        P102LeakNarrationController leakNarration;
        Scene boundScene;
        TrainingStage boundStage;

        readonly List<XRBaseInteractable> narrationBlockedInteractables = new List<XRBaseInteractable>();
        bool stageActionsBlocked;
        bool menuPaused;
        bool leakArrivalCompleted;
        bool transitionReadyForInteraction;
        bool stageTimingStarted;
        string pendingInteractionId = string.Empty;
        string pendingExpectedHand = string.Empty;
        string pendingExpectedControl = string.Empty;
        string pendingPromptSource = string.Empty;
        int pendingExpectedOrder;
        int nextInteractionOrder;
        Coroutine ppeCompletionRoutine;
        InputAction ppeContinueAction;
        SharedXRInputContract ppeContinueInput;
        bool ppeContinueRequested;

        public void Initialize(
            ScenarioFlowManager flow,
            TrainingSessionState state,
            PersistentEquipmentInventory equipment,
            PPEStateController ppe)
        {
            scenarioFlow = flow;
            session = state;
            inventory = equipment;
            ppeState = ppe;
        }

        public IEnumerator BindStage(Scene scene, TrainingStage stage)
        {
            Unbind();

            // Runtime bootstrap components finish their one-frame wiring before equipment is stored.
            yield return null;
            yield return null;

            inventory.BeginStage(scene, stage);
            boundScene = scene;
            boundStage = stage;
            switch (stage)
            {
                case TrainingStage.PPE:
                    ppeSequence = FindSceneComponent<PPESequenceManager>(scene);
                    if (ppeSequence == null)
                    {
                        ReportMissing("PPESequenceManager");
                        yield break;
                    }
                    RegisterPpeCheckItems();
                    ppeTrackedStep = ppeSequence.CurrentRequiredStep;
                    ppeSequence.OnRequiredStepChanged += OnPpeRequiredStepChanged;
                    ppeSequence.OnPPESequenceCompleted += OnPpeCompleted;
                    ppeState.BindPpeStage();
                    SetStageActionsBlocked(PPETTSManager.Instance != null && PPETTSManager.Instance.IsBusy);
                    if (ppeTrackedStep.HasValue)
                        QueueInteractionPrompt(PpeInteractionId(ppeTrackedStep.Value), "Either", "Grip");
                    break;

                case TrainingStage.PreEntry:
                    entryController = FindSceneComponent<EntrySafetyScenarioController>(scene);
                    entryState = entryController != null ? entryController.StateMachine : null;
                    if (entryState == null)
                    {
                        ReportMissing("EntrySafetyScenarioController.StateMachine");
                        yield break;
                    }
                    entryController.ConfigureLeakResponseHandoff(
                        session.SelectedMode == TrainingMode.Full);
                    RegisterPreEntryCheckItems();
                    entryTrackedStep = entryState.CurrentStep;
                    inventory.SetAvailable(PersistentEquipmentInventory.GasDetector, true);
                    entryState.StepChanged += OnEntryStepChanged;
                    entryState.SectionCompleted += OnEntryCompleted;
                    if (IsTrackedEntryStep(entryTrackedStep))
                        QueueEntryInteractionPrompt(entryTrackedStep);
                    break;

                case TrainingStage.LeakResponse:
                    leakNarration = FindSceneComponent<P102LeakNarrationController>(scene);
                    if (leakNarration == null)
                    {
                        ReportMissing("P102LeakNarrationController");
                        yield break;
                    }
                    leakNarration.CueStarted += OnLeakCueStarted;
                    leakNarration.CueCompleted += OnLeakCueCompleted;
                    RegisterLeakResponseCheckItems();
                    leakArrivalCompleted = false;
                    SetStageActionsBlocked(true);
                    break;
            }
        }

        void LateUpdate()
        {
            if (!boundScene.IsValid() || !boundScene.isLoaded)
                return;

            var shouldBlock = menuPaused;
            switch (boundStage)
            {
                case TrainingStage.PPE:
                    shouldBlock |= PPETTSManager.Instance != null && PPETTSManager.Instance.IsBusy;
                    break;
                case TrainingStage.PreEntry:
                    // Direct and simulated inputs are gated by EntrySafetyScenarioController.
                    break;
                case TrainingStage.LeakResponse:
                    shouldBlock |= !leakArrivalCompleted ||
                                   (leakNarration != null && (leakNarration.IsPlaying || leakNarration.PendingCount > 0));
                    break;
            }

            SetStageActionsBlocked(shouldBlock);
            if (stageActionsBlocked)
                CaptureAndDisableStageInteractables();
            TryStartStageTiming();
            TryFlushInteractionPrompt();
        }

        public void NotifyTransitionReadyForInteraction()
        {
            transitionReadyForInteraction = true;
            TryStartStageTiming();
        }

        public void Unbind()
        {
            Unbind(deactivateBoundStage: true);
        }

        void Unbind(bool deactivateBoundStage)
        {
            transitionReadyForInteraction = false;
            stageTimingStarted = false;
            pendingInteractionId = string.Empty;
            pendingExpectedHand = string.Empty;
            pendingExpectedControl = string.Empty;
            pendingPromptSource = string.Empty;
            pendingExpectedOrder = 0;
            nextInteractionOrder = 0;
            StopStageNarration();
            SetStageActionsBlocked(false);

            if (ppeCompletionRoutine != null)
            {
                StopCoroutine(ppeCompletionRoutine);
                ppeCompletionRoutine = null;
            }
            ppeContinueAction?.Disable();
            if (ppeContinueInput != null)
                ppeContinueInput.Performed -= OnSharedPpeContinue;
            ppeContinueInput = null;
            ppeContinueRequested = false;

            if (ppeSequence != null)
            {
                ppeSequence.OnRequiredStepChanged -= OnPpeRequiredStepChanged;
                ppeSequence.OnPPESequenceCompleted -= OnPpeCompleted;
            }
            if (entryState != null)
            {
                entryState.StepChanged -= OnEntryStepChanged;
                entryState.SectionCompleted -= OnEntryCompleted;
            }
            if (deactivateBoundStage && entryController != null &&
                boundStage == TrainingStage.PreEntry && boundScene.IsValid() && boundScene.isLoaded)
                entryController.DeactivateForLeakResponseHandoff();
            if (leakNarration != null)
            {
                leakNarration.CueStarted -= OnLeakCueStarted;
                leakNarration.CueCompleted -= OnLeakCueCompleted;
            }
            ppeState?.UnbindPpeStage();
            ppeSequence = null;
            ppeTrackedStep = null;
            entryController = null;
            entryState = null;
            leakNarration = null;
            boundScene = default;
            boundStage = TrainingStage.None;
            leakArrivalCompleted = false;
        }

        void TryStartStageTiming()
        {
            if (stageTimingStarted || !transitionReadyForInteraction || stageActionsBlocked || menuPaused ||
                !boundScene.IsValid() || !boundScene.isLoaded || boundStage == TrainingStage.None ||
                session == null || session.CurrentStage != boundStage)
            {
                return;
            }

            if (session.BeginCurrentStageTiming())
            {
                stageTimingStarted = true;
                StartCurrentCheckItem();
            }
        }

        /// <summary>
        /// App menu pause blocks only Stage interactables. Common XR tracking and App UI
        /// interactors remain active so the player can look around and operate the menu.
        /// </summary>
        public void SetMenuPaused(bool paused)
        {
            menuPaused = paused;
            if (paused)
                SetStageActionsBlocked(true);
        }

        void OnPpeCompleted()
        {
            ppeState.CommitPpeResult();
            if (ppeCompletionRoutine == null)
                ppeCompletionRoutine = StartCoroutine(WaitForPpeContinue());
        }

        void OnPpeRequiredStepChanged(PPESequenceStep? next)
        {
            if (ppeTrackedStep.HasValue)
            {
                var previous = ppeTrackedStep.Value;
                CompleteInteraction(PpeInteractionId(previous));
                session.CompleteCheckItem(
                    TrainingStage.PPE,
                    PpeItemId(previous),
                    PpeItemLabel(previous));
            }

            ppeTrackedStep = next;
            if (stageTimingStarted && next.HasValue)
            {
                session.StartCheckItem(
                    TrainingStage.PPE,
                    PpeItemId(next.Value),
                    PpeItemLabel(next.Value));
            }
            if (next.HasValue)
                QueueInteractionPrompt(PpeInteractionId(next.Value), "Either", "Grip");
        }

        IEnumerator WaitForPpeContinue()
        {
            // Completion listeners may enqueue the final narration in the same event dispatch.
            yield return null;
            while (PPETTSManager.Instance != null && PPETTSManager.Instance.IsBusy)
                yield return null;

            var feedback = FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);
            feedback?.ShowMessage(
                "\uC900\uBE44\uAC00 \uC644\uB8CC\uB418\uC5C8\uC2B5\uB2C8\uB2E4. \uC624\uB978\uC190 A \uBC84\uD2BC\uC744 \uB20C\uB7EC \uB2E4\uC74C \uB2E8\uACC4\uB85C \uC774\uB3D9\uD558\uC138\uC694.",
                3600f);

            ppeContinueInput = AppBootstrap.Instance != null ? AppBootstrap.Instance.SharedInput : null;
            if (ppeContinueInput != null)
            {
                ppeContinueRequested = false;
                ppeContinueInput.Performed += OnSharedPpeContinue;
                while (!ppeContinueRequested)
                    yield return null;
                ppeContinueInput.Performed -= OnSharedPpeContinue;
                ppeContinueInput = null;
            }
            else
            {
                EnsurePpeContinueAction();
                ppeContinueAction.Enable();
                yield return null;
                while (ppeContinueAction.IsPressed())
                    yield return null;
                while (!ppeContinueAction.WasPerformedThisFrame())
                    yield return null;
                ppeContinueAction.Disable();
            }

            feedback?.HideImmediate();
            ppeCompletionRoutine = null;
            scenarioFlow.CompleteCurrentStage();
        }

        void EnsurePpeContinueAction()
        {
            if (ppeContinueAction != null)
                return;

            ppeContinueAction = new InputAction("PPE Continue", InputActionType.Button);
            ppeContinueAction.AddBinding("<XRController>{RightHand}/primaryButton");
            ppeContinueAction.AddBinding("<OculusTouchController>{RightHand}/primaryButton");
#if UNITY_EDITOR
            ppeContinueAction.AddBinding("<Keyboard>/a");
#endif
        }

        void OnSharedPpeContinue(SharedXRHand hand, SharedXRControl control)
        {
            if (hand == SharedXRHand.Right && control == SharedXRControl.PrimaryButton)
                ppeContinueRequested = true;
        }

        void OnEntryCompleted()
        {
            CompleteInteraction(EntryInteractionId(entryTrackedStep));
            session.CompleteCheckItem(
                TrainingStage.PreEntry,
                EntryItemId(entryTrackedStep),
                EntryItemLabel(entryTrackedStep));

            // SectionCompleted is raised synchronously from the Stage's final A action.
            // Silence and clear the PreEntry guidance before any fade or Scene unload so
            // its narration cannot bleed into the completion/next-Stage presentation.
            entryController?.StopNarration();

            if (session.SelectedMode == TrainingMode.Full)
            {
                if (!scenarioFlow.CompletePreEntryAndHandoff())
                {
                    Debug.LogError(
                        "[Stage Lifecycle] Full PreEntry completed, but the additive LeakResponse handoff could not start.",
                        this);
                }
                return;
            }

            scenarioFlow.CompleteCurrentStage();
        }

        void OnEntryStepChanged(EntrySafetyStep previous, EntrySafetyStep next)
        {
            if (IsTrackedEntryStep(previous))
            {
                CompleteInteraction(EntryInteractionId(previous));
                session.CompleteCheckItem(
                    TrainingStage.PreEntry,
                    EntryItemId(previous),
                    EntryItemLabel(previous));
            }
            entryTrackedStep = next;
            if (stageTimingStarted && IsTrackedEntryStep(next))
            {
                session.StartCheckItem(
                    TrainingStage.PreEntry,
                    EntryItemId(next),
                    EntryItemLabel(next));
            }
            if (IsTrackedEntryStep(next))
                QueueEntryInteractionPrompt(next);
        }

        void OnLeakCueStarted(P102LeakNarrationCue cue)
        {
            switch (cue)
            {
                case P102LeakNarrationCue.SuctionValveActivated:
                    session.CompleteCheckItem(
                        TrainingStage.LeakResponse, "leak.situation", "누출 상황 확인");
                    session.StartCheckItem(
                        TrainingStage.LeakResponse, "leak.suction_valve", "흡입측 밸브 차단");
                    break;
                case P102LeakNarrationCue.SuctionValveClosed:
                    CompleteInteraction("LeakResponse_SuctionValveClose");
                    break;
                case P102LeakNarrationCue.DischargeValveActivated:
                    session.StartCheckItem(
                        TrainingStage.LeakResponse, "leak.discharge_valve", "토출측 밸브 차단");
                    break;
                case P102LeakNarrationCue.BothValvesClosed:
                    CompleteInteraction("LeakResponse_DischargeValveClose");
                    break;
                case P102LeakNarrationCue.LeakReductionCompleted:
                    CompleteInteraction("LeakResponse_LeakPointGaze");
                    break;
                case P102LeakNarrationCue.AbsorbentSockPlacementActivated:
                    session.StartCheckItem(
                        TrainingStage.LeakResponse, "leak.absorbent_sock", "흡착삭 설치");
                    break;
                case P102LeakNarrationCue.AllAbsorbentSocksPlaced:
                    CompleteInteraction("LeakResponse_AbsorbentSockPlacement");
                    break;
                case P102LeakNarrationCue.AbsorbentPadPlacementActivated:
                    session.StartCheckItem(
                        TrainingStage.LeakResponse, "leak.absorbent_pad", "흡착패드 설치");
                    break;
                case P102LeakNarrationCue.AllAbsorbentPadsPlaced:
                    CompleteInteraction("LeakResponse_AbsorbentPadPlacement");
                    break;
                case P102LeakNarrationCue.ContainerLidStowed:
                    CompleteInteraction("LeakResponse_ContainerLidStow");
                    break;
                case P102LeakNarrationCue.RecoveryTongsPickedUp:
                    CompleteInteraction("LeakResponse_RecoveryTongsPickup");
                    session.StartCheckItem(
                        TrainingStage.LeakResponse, "leak.recovery", "집게로 오염 흡착재 수거");
                    break;
                case P102LeakNarrationCue.AllPadsRecovered:
                    CompleteInteraction("LeakResponse_AbsorbentPadRecovery");
                    break;
                case P102LeakNarrationCue.AllSocksRecovered:
                    CompleteInteraction("LeakResponse_AbsorbentSockRecovery");
                    break;
                case P102LeakNarrationCue.RecoveryTongsReturned:
                    CompleteInteraction("LeakResponse_RecoveryTongsReturn");
                    break;
                case P102LeakNarrationCue.ContainerClosedTaskCompleted:
                    CompleteInteraction("LeakResponse_ContainerClose");
                    break;
                case P102LeakNarrationCue.ContainerAndLidGhostActivated:
                    session.StartCheckItem(
                        TrainingStage.LeakResponse, "leak.waste_disposal", "오염 폐기물 용기 처리");
                    break;
                case P102LeakNarrationCue.DirectContactAttempted:
                    ReportRejectedInteraction(TelemetryRejectionReason.Other, "direct_contact_attempted");
                    session.RecordCheckItemIssue(TrainingStage.LeakResponse, "leak.recovery");
                    break;
                case P102LeakNarrationCue.SockRecoveryBeforePadsAttempted:
                    ReportRejectedInteraction(TelemetryRejectionReason.WrongOrder, "sock_recovery_before_pads");
                    session.RecordCheckItemIssue(TrainingStage.LeakResponse, "leak.recovery");
                    break;
                case P102LeakNarrationCue.ContaminatedAbsorbentDroppedOutside:
                    ReportRejectedInteraction(TelemetryRejectionReason.Other, "contaminated_absorbent_dropped_outside");
                    session.RecordCheckItemIssue(TrainingStage.LeakResponse, "leak.waste_disposal");
                    break;
            }
            SetStageActionsBlocked(true);
        }

        void OnLeakCueCompleted(P102LeakNarrationCue cue)
        {
            if (cue == P102LeakNarrationCue.LeakSceneArrival)
                leakArrivalCompleted = true;

            switch (cue)
            {
                case P102LeakNarrationCue.SuctionValveActivated:
                    QueueInteractionPrompt("LeakResponse_SuctionValveClose", "Either", "Grip");
                    break;
                case P102LeakNarrationCue.SuctionValveClosed:
                    session.CompleteCheckItem(
                        TrainingStage.LeakResponse, "leak.suction_valve", "흡입측 밸브 차단");
                    break;
                case P102LeakNarrationCue.DischargeValveActivated:
                    QueueInteractionPrompt("LeakResponse_DischargeValveClose", "Either", "Grip");
                    break;
                case P102LeakNarrationCue.BothValvesClosed:
                    session.CompleteCheckItem(
                        TrainingStage.LeakResponse, "leak.discharge_valve", "토출측 밸브 차단");
                    break;
                case P102LeakNarrationCue.LeakReductionStarted:
                    QueueInteractionPrompt("LeakResponse_LeakPointGaze", "None", "Gaze");
                    break;
                case P102LeakNarrationCue.AbsorbentSockPlacementActivated:
                    QueueInteractionPrompt("LeakResponse_AbsorbentSockPlacement", "Either", "Grip");
                    break;
                case P102LeakNarrationCue.AllAbsorbentSocksPlaced:
                    session.CompleteCheckItem(
                        TrainingStage.LeakResponse, "leak.absorbent_sock", "흡착삭 설치");
                    break;
                case P102LeakNarrationCue.AbsorbentPadPlacementActivated:
                    QueueInteractionPrompt("LeakResponse_AbsorbentPadPlacement", "Either", "Grip");
                    break;
                case P102LeakNarrationCue.AllAbsorbentPadsPlaced:
                    session.CompleteCheckItem(
                        TrainingStage.LeakResponse, "leak.absorbent_pad", "흡착패드 설치");
                    break;
                case P102LeakNarrationCue.ContainerAndLidGhostActivated:
                    QueueInteractionPrompt("LeakResponse_ContainerLidStow", "Either", "Grip");
                    break;
                case P102LeakNarrationCue.ContainerLidStowed:
                    QueueInteractionPrompt("LeakResponse_RecoveryTongsPickup", "Either", "Grip");
                    break;
                case P102LeakNarrationCue.RecoveryTongsPickedUp:
                    QueueInteractionPrompt("LeakResponse_AbsorbentPadRecovery", "Either", "Grip");
                    break;
                case P102LeakNarrationCue.SockRecoveryStarted:
                    QueueInteractionPrompt("LeakResponse_AbsorbentSockRecovery", "Either", "Grip");
                    break;
                case P102LeakNarrationCue.AllSocksRecovered:
                    QueueInteractionPrompt("LeakResponse_RecoveryTongsReturn", "Either", "Grip");
                    break;
                case P102LeakNarrationCue.RecoveryTongsReturned:
                    session.CompleteCheckItem(
                        TrainingStage.LeakResponse, "leak.recovery", "집게로 오염 흡착재 수거");
                    break;
                case P102LeakNarrationCue.ContainerClosureGhostActivated:
                    QueueInteractionPrompt("LeakResponse_ContainerClose", "Either", "Grip");
                    break;
                case P102LeakNarrationCue.ContainerClosedTaskCompleted:
                    session.CompleteCheckItem(
                        TrainingStage.LeakResponse, "leak.waste_disposal", "오염 폐기물 용기 처리");
                    break;
            }

            if (cue == P102LeakNarrationCue.ContainerClosedTaskCompleted)
                scenarioFlow.CompleteCurrentStage();
        }

        void QueueEntryInteractionPrompt(EntrySafetyStep step)
        {
            switch (step)
            {
                case EntrySafetyStep.EquipDetector:
                    QueueInteractionPrompt(EntryInteractionId(step), "Left", "Grip");
                    break;
                case EntrySafetyStep.MeasureInitialHcl:
                case EntrySafetyStep.MeasureReducedHcl:
                    QueueInteractionPrompt(EntryInteractionId(step), "Left", "Trigger");
                    break;
                case EntrySafetyStep.CheckInitialWind:
                case EntrySafetyStep.CheckChangedWind:
                    QueueInteractionPrompt(EntryInteractionId(step), "None", "Gaze");
                    break;
                case EntrySafetyStep.ConfirmInitialHcl:
                case EntrySafetyStep.ConfirmReducedHcl:
                    QueueInteractionPrompt(EntryInteractionId(step), "None", "Automatic");
                    break;
                case EntrySafetyStep.StoreDetector:
                    QueueInteractionPrompt(EntryInteractionId(step), "Left", "Grip");
                    break;
                case EntrySafetyStep.MoveToPT:
                case EntrySafetyStep.MoveToNextArea:
                    QueueInteractionPrompt(EntryInteractionId(step), "Right", "PrimaryButton");
                    break;
                case EntrySafetyStep.StopPump:
                    QueueInteractionPrompt(EntryInteractionId(step), "Right", "Trigger");
                    break;
            }
        }

        void QueueInteractionPrompt(string interactionId, string expectedHand, string expectedControl)
        {
            if (string.IsNullOrWhiteSpace(interactionId))
                return;
            if (string.Equals(pendingInteractionId, interactionId, System.StringComparison.Ordinal))
                return;

            pendingInteractionId = interactionId;
            pendingExpectedHand = expectedHand ?? string.Empty;
            pendingExpectedControl = expectedControl ?? string.Empty;
            pendingPromptSource = "VoiceAndUI";
            pendingExpectedOrder = ++nextInteractionOrder;
        }

        void TryFlushInteractionPrompt(bool force = false)
        {
            if (string.IsNullOrEmpty(pendingInteractionId) || !stageTimingStarted ||
                (!force && IsInteractionPromptBlocked()))
            {
                return;
            }

            TrainingTelemetry.Current.InteractionPrompted(
                boundStage,
                pendingInteractionId,
                pendingExpectedOrder,
                pendingExpectedHand,
                pendingExpectedControl,
                pendingPromptSource);
            pendingInteractionId = string.Empty;
            pendingExpectedHand = string.Empty;
            pendingExpectedControl = string.Empty;
            pendingPromptSource = string.Empty;
            pendingExpectedOrder = 0;
        }

        bool IsInteractionPromptBlocked()
        {
            if (menuPaused || stageActionsBlocked)
                return true;
            if (boundStage == TrainingStage.PPE)
                return PPETTSManager.Instance != null && PPETTSManager.Instance.IsBusy;
            if (boundStage == TrainingStage.PreEntry)
                return entryController != null && entryController.IsInteractionInputBlockedByNarration;
            if (boundStage == TrainingStage.LeakResponse)
                return leakNarration != null && (leakNarration.IsPlaying || leakNarration.PendingCount > 0);
            return false;
        }

        void CompleteInteraction(string interactionId)
        {
            if (string.IsNullOrWhiteSpace(interactionId))
                return;
            if (string.Equals(pendingInteractionId, interactionId, System.StringComparison.Ordinal))
                TryFlushInteractionPrompt(true);
            TrainingTelemetry.Current.InteractionCompleted(interactionId);
        }

        void ReportRejectedInteraction(TelemetryRejectionReason reason, string detail)
        {
            TrainingTelemetry.Current.InteractionAttempted(
                null,
                "Either",
                "Grip",
                TelemetryAttemptResult.Rejected,
                reason,
                detail);
        }

        void RegisterPpeCheckItems()
        {
            foreach (PPESequenceStep step in System.Enum.GetValues(typeof(PPESequenceStep)))
            {
                if (ppeSequence.ContainsStep(step))
                    session.RegisterCheckItem(TrainingStage.PPE, PpeItemId(step), PpeItemLabel(step));
            }
        }

        void RegisterPreEntryCheckItems()
        {
            var required = new[]
            {
                EntrySafetyStep.EquipDetector,
                EntrySafetyStep.MeasureInitialHcl,
                EntrySafetyStep.ConfirmInitialHcl,
                EntrySafetyStep.CheckInitialWind,
                EntrySafetyStep.CheckChangedWind,
                EntrySafetyStep.MeasureReducedHcl,
                EntrySafetyStep.ConfirmReducedHcl,
                EntrySafetyStep.StoreDetector,
                EntrySafetyStep.MoveToPT,
                EntrySafetyStep.StopPump,
                EntrySafetyStep.MoveToNextArea,
            };
            for (var i = 0; i < required.Length; i++)
            {
                session.RegisterCheckItem(
                    TrainingStage.PreEntry,
                    EntryItemId(required[i]),
                    EntryItemLabel(required[i]));
            }
        }

        void RegisterLeakResponseCheckItems()
        {
            session.RegisterCheckItem(TrainingStage.LeakResponse, "leak.situation", "누출 상황 확인");
            session.RegisterCheckItem(TrainingStage.LeakResponse, "leak.suction_valve", "흡입측 밸브 차단");
            session.RegisterCheckItem(TrainingStage.LeakResponse, "leak.discharge_valve", "토출측 밸브 차단");
            session.RegisterCheckItem(TrainingStage.LeakResponse, "leak.absorbent_sock", "흡착삭 설치");
            session.RegisterCheckItem(TrainingStage.LeakResponse, "leak.absorbent_pad", "흡착패드 설치");
            session.RegisterCheckItem(TrainingStage.LeakResponse, "leak.recovery", "집게로 오염 흡착재 수거");
            session.RegisterCheckItem(TrainingStage.LeakResponse, "leak.waste_disposal", "오염 폐기물 용기 처리");
        }

        void StartCurrentCheckItem()
        {
            if (boundStage == TrainingStage.PPE && ppeTrackedStep.HasValue)
            {
                session.StartCheckItem(
                    TrainingStage.PPE,
                    PpeItemId(ppeTrackedStep.Value),
                    PpeItemLabel(ppeTrackedStep.Value));
            }
            else if (boundStage == TrainingStage.PreEntry && IsTrackedEntryStep(entryTrackedStep))
            {
                session.StartCheckItem(
                    TrainingStage.PreEntry,
                    EntryItemId(entryTrackedStep),
                    EntryItemLabel(entryTrackedStep));
            }
            else if (boundStage == TrainingStage.LeakResponse)
            {
                session.StartCheckItem(
                    TrainingStage.LeakResponse,
                    "leak.situation",
                    "누출 상황 확인");
            }
        }

        static string PpeItemId(PPESequenceStep step) => "ppe." + step;
        static string PpeInteractionId(PPESequenceStep step) => "PPE_" + step;

        static string PpeItemLabel(PPESequenceStep step)
        {
            switch (step)
            {
                case PPESequenceStep.InnerGloves: return "속장갑 착용";
                case PPESequenceStep.ProtectiveSuit: return "보호복 착용";
                case PPESequenceStep.ChemicalBoots: return "내화학 장화 착용";
                case PPESequenceStep.Respirator: return "호흡보호구 착용";
                case PPESequenceStep.ResponseBag: return "대응 장비 가방 확인·착용";
                case PPESequenceStep.FaceProtection: return "안면 보호구 착용";
                case PPESequenceStep.ProtectiveGloves: return "내화학 장갑 착용";
                case PPESequenceStep.WasteContainer: return "폐기물 용기 준비";
                case PPESequenceStep.Helmet: return "안전모 착용";
                default: return step.ToString();
            }
        }

        static string EntryItemId(EntrySafetyStep step) => "preentry." + step;
        static string EntryInteractionId(EntrySafetyStep step) => "PreEntry_" + step;

        static bool IsTrackedEntryStep(EntrySafetyStep step)
        {
            switch (step)
            {
                case EntrySafetyStep.EquipDetector:
                case EntrySafetyStep.MeasureInitialHcl:
                case EntrySafetyStep.ConfirmInitialHcl:
                case EntrySafetyStep.CheckInitialWind:
                case EntrySafetyStep.CheckChangedWind:
                case EntrySafetyStep.MeasureReducedHcl:
                case EntrySafetyStep.ConfirmReducedHcl:
                case EntrySafetyStep.StoreDetector:
                case EntrySafetyStep.MoveToPT:
                case EntrySafetyStep.StopPump:
                case EntrySafetyStep.MoveToNextArea:
                    return true;
                default:
                    return false;
            }
        }

        static string EntryItemLabel(EntrySafetyStep step)
        {
            switch (step)
            {
                case EntrySafetyStep.EquipDetector: return "측정기 착용";
                case EntrySafetyStep.MeasureInitialHcl: return "초기 HCl 농도 측정";
                case EntrySafetyStep.ConfirmInitialHcl: return "초기 측정값 확인";
                case EntrySafetyStep.CheckInitialWind: return "초기 풍향 확인";
                case EntrySafetyStep.CheckChangedWind: return "변경 풍향 확인";
                case EntrySafetyStep.MeasureReducedHcl: return "재측정 HCl 농도 측정";
                case EntrySafetyStep.ConfirmReducedHcl: return "재측정값 확인";
                case EntrySafetyStep.StoreDetector: return "측정기 수납";
                case EntrySafetyStep.MoveToPT: return "현장 패널 위치 이동";
                case EntrySafetyStep.StopPump: return "펌프 정지 조작";
                case EntrySafetyStep.MoveToNextArea: return "누출 대응 구역 이동";
                default: return step.ToString();
            }
        }

        void StopStageNarration()
        {
            if (boundStage == TrainingStage.PPE)
                PPETTSManager.Instance?.StopAll();
            else if (boundStage == TrainingStage.PreEntry)
                entryController?.StopNarration();
            else if (boundStage == TrainingStage.LeakResponse)
                leakNarration?.StopAll();
        }

        void SetStageActionsBlocked(bool blocked)
        {
            if (stageActionsBlocked == blocked)
                return;

            stageActionsBlocked = blocked;
            if (blocked)
            {
                narrationBlockedInteractables.Clear();
                CaptureAndDisableStageInteractables();
                return;
            }

            for (var i = 0; i < narrationBlockedInteractables.Count; i++)
            {
                var interactable = narrationBlockedInteractables[i];
                if (interactable != null)
                    interactable.enabled = true;
            }
            narrationBlockedInteractables.Clear();
        }

        void CaptureAndDisableStageInteractables()
        {
            if (!boundScene.IsValid() || !boundScene.isLoaded)
                return;

            foreach (var root in boundScene.GetRootGameObjects())
            {
                var interactables = root.GetComponentsInChildren<XRBaseInteractable>(true);
                for (var i = 0; i < interactables.Length; i++)
                {
                    var interactable = interactables[i];
                    if (interactable == null || !interactable.enabled || !interactable.gameObject.activeInHierarchy ||
                        narrationBlockedInteractables.Contains(interactable))
                        continue;

                    // Narration may start as a direct result of a successful Grab
                    // (for example RecoveryTongsPickedUp). Disabling an interactable
                    // that is already selected cancels the hand attachment and leaves
                    // the tool frozen until TTS ends. Block new interactions during
                    // narration, but always preserve objects already held by a hand.
                    if (interactable is XRGrabInteractable heldGrab && heldGrab.isSelected)
                        continue;

                    narrationBlockedInteractables.Add(interactable);
                    interactable.enabled = false;
                }
            }
        }

        void ReportMissing(string role)
        {
            var message = $"{TrainingSessionState.GetStageLabel(session.CurrentStage)} 완료 Adapter에 필요한 {role} 참조가 없습니다.";
            Debug.LogError("[Stage Lifecycle] " + message, this);
            AppBootstrap.Instance?.ErrorUI.Show(message);
        }

        static T FindSceneComponent<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var component = root.GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }
            return null;
        }

        void OnDestroy()
        {
            // Stage Scene objects may already have been destroyed when the persistent
            // App root is torn down. Only detach delegates here; do not invoke Stage
            // presentation code on Unity fake-null component proxies.
            Unbind(deactivateBoundStage: false);
            ppeContinueAction?.Dispose();
            ppeContinueAction = null;
        }
    }
}
