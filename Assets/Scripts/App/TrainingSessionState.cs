using System;
using System.Collections.Generic;
using HClTraining.SharedXR;
using UnityEngine;

namespace HClTraining.App
{
    public enum TrainingMode
    {
        None = 0,
        Full = 1,
        PPEOnly = 2,
        PreEntryOnly = 3,
        LeakResponseOnly = 4,
    }

    public enum TrainingStage
    {
        None = 0,
        PPE = 1,
        PreEntry = 2,
        LeakResponse = 3,
        Complete = 4,
    }

    [DisallowMultipleComponent]
    public sealed class TrainingSessionState : MonoBehaviour
    {
        static readonly TrainingStage[] FullSequence =
        {
            TrainingStage.PPE,
            TrainingStage.PreEntry,
            TrainingStage.LeakResponse,
        };

        readonly List<TrainingStage> stageSequence = new List<TrainingStage>();
        readonly List<TrainingStage> completedStages = new List<TrainingStage>();
        readonly HashSet<string> acquiredEquipmentIds = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> availableEquipmentIds = new HashSet<string>(StringComparer.Ordinal);

        public IncidentSessionState Incident { get; } = new IncidentSessionState();
        public TrainingMode SelectedMode { get; private set; }
        public TrainingStage CurrentStage { get; private set; }
        public TrainingStage RestartTarget { get; private set; }
        public IReadOnlyList<TrainingStage> StageSequence => stageSequence;
        public IReadOnlyList<TrainingStage> CompletedStages => completedStages;
        public IReadOnlyCollection<string> AcquiredEquipmentIds => acquiredEquipmentIds;
        public IReadOnlyCollection<string> AvailableEquipmentIds => availableEquipmentIds;
        public bool LeftChemicalGloveWorn { get; private set; }
        public bool RightChemicalGloveWorn { get; private set; }
        public bool HasPpeResult { get; private set; }

        public event Action Changed;

        public void Begin(TrainingMode mode)
        {
            if (mode == TrainingMode.None)
                throw new ArgumentOutOfRangeException(nameof(mode));

            SelectedMode = mode;
            CurrentStage = TrainingStage.None;
            RestartTarget = FirstStageFor(mode);
            completedStages.Clear();
            stageSequence.Clear();
            acquiredEquipmentIds.Clear();
            availableEquipmentIds.Clear();
            LeftChemicalGloveWorn = false;
            RightChemicalGloveWorn = false;
            HasPpeResult = false;
            Incident.Reset();

            if (mode == TrainingMode.Full)
                stageSequence.AddRange(FullSequence);
            else
                stageSequence.Add(FirstStageFor(mode));

            Changed?.Invoke();
        }

        public bool SetFacilityRunState(FacilityRunState state)
        {
            if (!Incident.SetFacilityRunState(state))
                return false;
            Changed?.Invoke();
            return true;
        }

        public bool SetLeakState(LeakState state)
        {
            if (!Incident.SetLeakState(state))
                return false;
            Changed?.Invoke();
            return true;
        }

        public void EnterStage(TrainingStage stage)
        {
            CurrentStage = stage;
            if (stage != TrainingStage.Complete && stage != TrainingStage.None)
                RestartTarget = stage;
            availableEquipmentIds.Clear();
            Changed?.Invoke();
        }

        public bool MarkCurrentStageComplete()
        {
            if (CurrentStage == TrainingStage.None || CurrentStage == TrainingStage.Complete)
                return false;

            if (!completedStages.Contains(CurrentStage))
                completedStages.Add(CurrentStage);
            Changed?.Invoke();
            return true;
        }

        public TrainingStage GetNextStage()
        {
            if (stageSequence.Count == 0)
                return TrainingStage.Complete;

            if (CurrentStage == TrainingStage.None)
                return stageSequence[0];

            var index = stageSequence.IndexOf(CurrentStage);
            return index >= 0 && index + 1 < stageSequence.Count
                ? stageSequence[index + 1]
                : TrainingStage.Complete;
        }

        public void SetPpeResult(bool leftGlove, bool rightGlove, IEnumerable<string> equipmentIds)
        {
            LeftChemicalGloveWorn = leftGlove;
            RightChemicalGloveWorn = rightGlove;
            HasPpeResult = true;
            acquiredEquipmentIds.Clear();
            if (equipmentIds != null)
            {
                foreach (var id in equipmentIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                        acquiredEquipmentIds.Add(id);
                }
            }
            availableEquipmentIds.RemoveWhere(id => !acquiredEquipmentIds.Contains(id));
            Changed?.Invoke();
        }

        public void SetEquipmentAvailable(string equipmentId, bool available)
        {
            if (string.IsNullOrWhiteSpace(equipmentId) || !acquiredEquipmentIds.Contains(equipmentId))
                return;

            var changed = available
                ? availableEquipmentIds.Add(equipmentId)
                : availableEquipmentIds.Remove(equipmentId);
            if (changed)
                Changed?.Invoke();
        }

        public bool IsEquipmentAcquired(string equipmentId) =>
            !string.IsNullOrWhiteSpace(equipmentId) && acquiredEquipmentIds.Contains(equipmentId);

        public bool IsEquipmentAvailable(string equipmentId) =>
            !string.IsNullOrWhiteSpace(equipmentId) && availableEquipmentIds.Contains(equipmentId);

        public void ClearScenarioAvailability()
        {
            if (availableEquipmentIds.Count == 0)
                return;
            availableEquipmentIds.Clear();
            Changed?.Invoke();
        }

        public void ReturnToStart()
        {
            CurrentStage = TrainingStage.None;
            availableEquipmentIds.Clear();
            Changed?.Invoke();
        }

        public static TrainingStage FirstStageFor(TrainingMode mode)
        {
            switch (mode)
            {
                case TrainingMode.Full: return TrainingStage.PPE;
                case TrainingMode.PPEOnly: return TrainingStage.PPE;
                case TrainingMode.PreEntryOnly: return TrainingStage.PreEntry;
                case TrainingMode.LeakResponseOnly: return TrainingStage.LeakResponse;
                default: return TrainingStage.None;
            }
        }

        public static string GetModeLabel(TrainingMode mode)
        {
            switch (mode)
            {
                case TrainingMode.Full: return "전체 훈련";
                case TrainingMode.PPEOnly: return "PPE 착용 교육";
                case TrainingMode.PreEntryOnly: return "현장 진입 전 확인절차";
                case TrainingMode.LeakResponseOnly: return "염산 누출 대응";
                default: return "선택 안 함";
            }
        }

        public static string GetStageLabel(TrainingStage stage)
        {
            switch (stage)
            {
                case TrainingStage.PPE: return "PPE 착용";
                case TrainingStage.PreEntry: return "현장 진입 전 확인";
                case TrainingStage.LeakResponse: return "누출 대응";
                case TrainingStage.Complete: return "훈련 완료";
                default: return "시작";
            }
        }
    }
}
