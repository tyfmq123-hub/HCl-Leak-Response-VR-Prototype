using System.Collections;
using JH.EntrySafety;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        EntrySafetyStateMachine entryState;
        P102LeakNarrationController leakNarration;

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
                    break;

                case TrainingStage.PreEntry:
                    var entryController = FindSceneComponent<EntrySafetyScenarioController>(scene);
                    entryState = entryController != null ? entryController.StateMachine : null;
                    if (entryState == null)
                    {
                        ReportMissing("EntrySafetyScenarioController.StateMachine");
                        yield break;
                    }
                    entryState.StepChanged += OnEntryStepChanged;
                    entryState.SectionCompleted += OnEntryCompleted;
                    ApplyEntryEquipmentStep(entryState.CurrentStep);
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
                    break;
            }
        }

        public void Unbind()
        {
            if (ppeSequence != null)
                ppeSequence.OnPPESequenceCompleted -= OnPpeCompleted;
            if (entryState != null)
            {
                entryState.StepChanged -= OnEntryStepChanged;
                entryState.SectionCompleted -= OnEntryCompleted;
            }
            if (leakNarration != null)
            {
                leakNarration.CueStarted -= OnLeakCueStarted;
                leakNarration.CueCompleted -= OnLeakCueCompleted;
            }
            ppeState?.UnbindPpeStage();
            ppeSequence = null;
            entryState = null;
            leakNarration = null;
        }

        void OnPpeCompleted()
        {
            ppeState.CommitPpeResult();
            scenarioFlow.CompleteCurrentStage();
        }

        void OnEntryStepChanged(EntrySafetyStep previous, EntrySafetyStep current) =>
            ApplyEntryEquipmentStep(current);

        void ApplyEntryEquipmentStep(EntrySafetyStep step)
        {
            inventory.SetAvailable(PersistentEquipmentInventory.GasDetector, step == EntrySafetyStep.EquipDetector);
        }

        void OnEntryCompleted() => scenarioFlow.CompleteCurrentStage();

        void OnLeakCueStarted(P102LeakNarrationCue cue)
        {
            switch (cue)
            {
                case P102LeakNarrationCue.AbsorbentSockPlacementActivated:
                    inventory.SetAvailable(PersistentEquipmentInventory.AbsorbentSock, true);
                    break;
                case P102LeakNarrationCue.AllAbsorbentSocksPlaced:
                    inventory.SetAvailable(PersistentEquipmentInventory.AbsorbentSock, false);
                    break;
                case P102LeakNarrationCue.AbsorbentPadPlacementActivated:
                    inventory.SetAvailable(PersistentEquipmentInventory.AbsorbentPad, true);
                    break;
                case P102LeakNarrationCue.AllAbsorbentPadsPlaced:
                    inventory.SetAvailable(PersistentEquipmentInventory.AbsorbentPad, false);
                    break;
                case P102LeakNarrationCue.ContainerAndLidGhostActivated:
                    inventory.SetAvailable(PersistentEquipmentInventory.WasteContainer, true);
                    break;
            }
        }

        void OnLeakCueCompleted(P102LeakNarrationCue cue)
        {
            if (cue == P102LeakNarrationCue.ContainerLidStowed)
                inventory.SetAvailable(PersistentEquipmentInventory.RecoveryTongs, true);
            else if (cue == P102LeakNarrationCue.RecoveryTongsReturned)
                inventory.SetAvailable(PersistentEquipmentInventory.RecoveryTongs, false);
            else if (cue == P102LeakNarrationCue.ContainerClosedTaskCompleted)
                scenarioFlow.CompleteCurrentStage();
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

        void OnDestroy() => Unbind();
    }
}
