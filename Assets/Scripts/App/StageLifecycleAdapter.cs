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
        EntrySafetyScenarioController entryController;
        EntrySafetyStateMachine entryState;
        P102LeakNarrationController leakNarration;
        Scene boundScene;
        TrainingStage boundStage;

        readonly List<XRBaseInteractable> narrationBlockedInteractables = new List<XRBaseInteractable>();
        bool stageActionsBlocked;
        bool menuPaused;
        bool leakArrivalCompleted;
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
                    ppeSequence.OnPPESequenceCompleted += OnPpeCompleted;
                    ppeState.BindPpeStage();
                    SetStageActionsBlocked(PPETTSManager.Instance != null && PPETTSManager.Instance.IsBusy);
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
                    inventory.SetAvailable(PersistentEquipmentInventory.GasDetector, true);
                    entryState.SectionCompleted += OnEntryCompleted;
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
        }

        public void Unbind()
        {
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
                ppeSequence.OnPPESequenceCompleted -= OnPpeCompleted;
            if (entryState != null)
            {
                entryState.SectionCompleted -= OnEntryCompleted;
            }
            if (entryController != null && boundStage == TrainingStage.PreEntry)
                entryController.DeactivateForLeakResponseHandoff();
            if (leakNarration != null)
            {
                leakNarration.CueStarted -= OnLeakCueStarted;
                leakNarration.CueCompleted -= OnLeakCueCompleted;
            }
            ppeState?.UnbindPpeStage();
            ppeSequence = null;
            entryController = null;
            entryState = null;
            leakNarration = null;
            boundScene = default;
            boundStage = TrainingStage.None;
            leakArrivalCompleted = false;
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

        void OnLeakCueStarted(P102LeakNarrationCue cue)
        {
            SetStageActionsBlocked(true);
        }

        void OnLeakCueCompleted(P102LeakNarrationCue cue)
        {
            if (cue == P102LeakNarrationCue.LeakSceneArrival)
                leakArrivalCompleted = true;

            if (cue == P102LeakNarrationCue.ContainerClosedTaskCompleted)
                scenarioFlow.CompleteCurrentStage();
        }

        void StopStageNarration()
        {
            if (boundStage == TrainingStage.PPE)
                PPETTSManager.Instance?.StopAll();
            else if (boundStage == TrainingStage.PreEntry)
                entryController?.StopNarration();
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
            Unbind();
            ppeContinueAction?.Dispose();
            ppeContinueAction = null;
        }
    }
}
