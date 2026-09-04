using System.Collections.Generic;
using HClTraining.SharedXR;
using UnityEngine;

namespace VRProject.LeakResponse.Narration
{
    /// <summary>
    /// Reports stage facts through the shared no-op-safe telemetry contract.
    /// It stores no user score and owns no dashboard or persistence logic.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LeakResponseTelemetryReporter : MonoBehaviour
    {
        readonly HashSet<TrainingStepId> enteredSteps = new HashSet<TrainingStepId>();
        readonly HashSet<TrainingStepId> completedSteps = new HashSet<TrainingStepId>();
        P102LeakNarrationController narration;

        public void Configure(P102LeakNarrationController newNarration)
        {
            Unsubscribe();
            narration = newNarration;
            if (narration == null)
                return;
            narration.CueStarted += OnCueStarted;
            narration.CueCompleted += OnCueCompleted;
        }

        void OnDestroy() => Unsubscribe();

        void OnCueStarted(P102LeakNarrationCue cue)
        {
            switch (cue)
            {
                case P102LeakNarrationCue.SuctionValveActivated:
                    Enter(TrainingStepId.SuctionValve);
                    break;
                case P102LeakNarrationCue.DischargeValveActivated:
                    Enter(TrainingStepId.DischargeValve);
                    break;
                case P102LeakNarrationCue.AbsorbentSockPlacementActivated:
                    Enter(TrainingStepId.AbsorbentSock);
                    break;
                case P102LeakNarrationCue.AbsorbentPadPlacementActivated:
                    Enter(TrainingStepId.AbsorbentPad);
                    break;
                case P102LeakNarrationCue.ContainerAndLidGhostActivated:
                    Enter(TrainingStepId.WasteDisposal);
                    break;
                case P102LeakNarrationCue.RecoveryTongsPickedUp:
                    Enter(TrainingStepId.Recovery);
                    break;
            }
        }

        void OnCueCompleted(P102LeakNarrationCue cue)
        {
            switch (cue)
            {
                case P102LeakNarrationCue.SuctionValveClosed:
                    Complete(TrainingStepId.SuctionValve);
                    break;
                case P102LeakNarrationCue.BothValvesClosed:
                    Complete(TrainingStepId.DischargeValve);
                    SharedIncidentState.Current.SetLeakState(LeakState.Isolated);
                    break;
                case P102LeakNarrationCue.AllAbsorbentSocksPlaced:
                    Complete(TrainingStepId.AbsorbentSock);
                    break;
                case P102LeakNarrationCue.AllAbsorbentPadsPlaced:
                    Complete(TrainingStepId.AbsorbentPad);
                    break;
                case P102LeakNarrationCue.RecoveryTongsReturned:
                    Complete(TrainingStepId.Recovery);
                    break;
                case P102LeakNarrationCue.ContainerClosedTaskCompleted:
                    Complete(TrainingStepId.WasteDisposal);
                    break;
            }
        }

        void Enter(TrainingStepId step)
        {
            if (enteredSteps.Add(step))
                SharedTrainingTelemetry.Current.StepEntered(step);
        }

        void Complete(TrainingStepId step)
        {
            Enter(step);
            if (completedSteps.Add(step))
                SharedTrainingTelemetry.Current.StepCompleted(step);
        }

        void Unsubscribe()
        {
            if (narration != null)
            {
                narration.CueStarted -= OnCueStarted;
                narration.CueCompleted -= OnCueCompleted;
            }
            narration = null;
        }
    }
}
