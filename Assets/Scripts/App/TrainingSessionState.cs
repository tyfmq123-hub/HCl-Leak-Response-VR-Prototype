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

    [Serializable]
    public sealed class StageCheckItemResult
    {
        public string ItemId;
        public string DisplayName;
        public bool IsRequired;
        public bool WasPerformed;
        public int ErrorCount;
        public int RetryCount;
        public double DurationSeconds;

        [NonSerialized] internal bool TimingActive;
        [NonSerialized] internal double TimingStartedAt;
    }

    [Serializable]
    public sealed class TrainingFeedbackRecord
    {
        public string SessionId;
        public string TargetStage;
        public List<string> QuickFeedbackIds = new List<string>();
        public double VoiceDurationSeconds;
        public string VoiceTranscript;
        public string SubmittedAtUtc;
        public bool Skipped;
    }

    [Serializable]
    public sealed class StageTrainingResult
    {
        public string SessionId;
        public string ExecutionMode;
        public string SelectedStage;
        public string StageId;
        public string StageName;
        public bool StageEnded;
        public bool StageCompleted;
        public List<StageCheckItemResult> CheckItems = new List<StageCheckItemResult>();
        public List<string> MissingRequiredItems = new List<string>();
        public double StageDurationSeconds;
        public int ErrorCount;
        public int RetryCount;
        public List<TrainingFeedbackRecord> Feedback = new List<TrainingFeedbackRecord>();

        [NonSerialized] internal int UnassignedErrorCount;
        [NonSerialized] internal int UnassignedRetryCount;
    }

    [Serializable]
    public sealed class TrainingResultData
    {
        public string SessionId;
        public string ExecutionMode;
        public string SelectedStage;
        public List<StageTrainingResult> StageResults = new List<StageTrainingResult>();
        public List<TrainingFeedbackRecord> CourseFeedback = new List<TrainingFeedbackRecord>();
        public double PpeDurationSeconds;
        public double PreEntryDurationSeconds;
        public double LeakResponseDurationSeconds;
        public double TotalDurationSeconds;
        public bool TrainingCompleted;
        public string EndedAtUtc;
        public string CompletedAtUtc;
    }

    [DisallowMultipleComponent]
    public sealed class TrainingSessionState : MonoBehaviour
    {
        static double TrainingClockNow => SharedTrainingPause.UnscaledTime;
        static readonly TrainingStage[] FullSequence =
        {
            TrainingStage.PPE,
            TrainingStage.PreEntry,
            TrainingStage.LeakResponse,
        };

        readonly List<TrainingStage> stageSequence = new List<TrainingStage>();
        readonly List<TrainingStage> endedStages = new List<TrainingStage>();
        readonly List<TrainingStage> completedStages = new List<TrainingStage>();
        readonly List<StageTrainingResult> stageResults = new List<StageTrainingResult>();
        readonly HashSet<string> acquiredEquipmentIds = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> availableEquipmentIds = new HashSet<string>(StringComparer.Ordinal);

        TrainingStage timedStage;
        double timedStageStartedAt;
        double ppeDurationSeconds;
        double preEntryDurationSeconds;
        double leakResponseDurationSeconds;

        public IncidentSessionState Incident { get; } = new IncidentSessionState();
        public string SessionId { get; private set; } = string.Empty;
        public TrainingMode SelectedMode { get; private set; }
        public TrainingStage CurrentStage { get; private set; }
        public TrainingStage RestartTarget { get; private set; }
        public IReadOnlyList<TrainingStage> StageSequence => stageSequence;
        public IReadOnlyList<TrainingStage> EndedStages => endedStages;
        public IReadOnlyList<TrainingStage> CompletedStages => completedStages;
        public IReadOnlyList<StageTrainingResult> StageResults => stageResults;
        public IReadOnlyCollection<string> AcquiredEquipmentIds => acquiredEquipmentIds;
        public IReadOnlyCollection<string> AvailableEquipmentIds => availableEquipmentIds;
        public bool LeftChemicalGloveWorn { get; private set; }
        public bool RightChemicalGloveWorn { get; private set; }
        public bool HasPpeResult { get; private set; }
        public double PpeDurationSeconds => ppeDurationSeconds;
        public double PreEntryDurationSeconds => preEntryDurationSeconds;
        public double LeakResponseDurationSeconds => leakResponseDurationSeconds;
        public double TotalDurationSeconds =>
            ppeDurationSeconds + preEntryDurationSeconds + leakResponseDurationSeconds;
        public double TotalActiveDurationSecondsIncludingCurrent =>
            TotalDurationSeconds + GetCurrentStageActiveDurationSeconds();
        public bool HasEndedSelectedTraining => ContainsEveryStage(endedStages);
        public bool HasCompletedRequiredTraining => ContainsEveryStage(completedStages);
        public TrainingResultData CompletionResult { get; private set; }

        public event Action Changed;

        public void Begin(TrainingMode mode)
        {
            if (mode == TrainingMode.None)
                throw new ArgumentOutOfRangeException(nameof(mode));

            SelectedMode = mode;
            SessionId = Guid.NewGuid().ToString("N");
            CurrentStage = TrainingStage.None;
            RestartTarget = FirstStageFor(mode);
            endedStages.Clear();
            completedStages.Clear();
            stageSequence.Clear();
            stageResults.Clear();
            acquiredEquipmentIds.Clear();
            availableEquipmentIds.Clear();
            LeftChemicalGloveWorn = false;
            RightChemicalGloveWorn = false;
            HasPpeResult = false;
            timedStage = TrainingStage.None;
            timedStageStartedAt = 0d;
            ppeDurationSeconds = 0d;
            preEntryDurationSeconds = 0d;
            leakResponseDurationSeconds = 0d;
            CompletionResult = null;
            Incident.Reset();

            if (mode == TrainingMode.Full)
                stageSequence.AddRange(FullSequence);
            else
                stageSequence.Add(FirstStageFor(mode));

            for (var i = 0; i < stageSequence.Count; i++)
                stageResults.Add(CreateStageResult(stageSequence[i]));

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
            if (timedStage != TrainingStage.None && timedStage != stage)
            {
                timedStage = TrainingStage.None;
                timedStageStartedAt = 0d;
            }
            CurrentStage = stage;
            if (stage != TrainingStage.Complete && stage != TrainingStage.None)
                RestartTarget = stage;
            availableEquipmentIds.Clear();
            Changed?.Invoke();
        }

        public bool BeginCurrentStageTiming()
        {
            var stage = CurrentStage;
            if (stage == TrainingStage.None || stage == TrainingStage.Complete ||
                endedStages.Contains(stage) || timedStage == stage)
            {
                return false;
            }

            timedStage = stage;
            timedStageStartedAt = TrainingClockNow;
            SetStageDuration(stage, 0d);
            return true;
        }

        public bool EndCurrentStage()
        {
            if (CurrentStage == TrainingStage.None || CurrentStage == TrainingStage.Complete)
                return false;

            if (endedStages.Contains(CurrentStage))
                return false;

            StopCurrentStageTiming();
            var result = GetStageResult(CurrentStage);
            if (result == null)
                return false;

            StopOpenCheckItemTimings(result);
            result.StageDurationSeconds = GetStageDurationSeconds(CurrentStage);
            result.StageEnded = true;
            result.MissingRequiredItems.Clear();
            for (var i = 0; i < result.CheckItems.Count; i++)
            {
                var item = result.CheckItems[i];
                if (item.IsRequired && !item.WasPerformed)
                    result.MissingRequiredItems.Add(item.DisplayName);
            }

            result.StageCompleted = result.MissingRequiredItems.Count == 0;
            RefreshStageMetrics(result);
            endedStages.Add(CurrentStage);
            if (result.StageCompleted)
                completedStages.Add(CurrentStage);

            if (HasEndedSelectedTraining && CompletionResult == null)
                CompletionResult = CreateCompletionResult();
            Changed?.Invoke();
            return true;
        }

        // Kept for existing integration callers. Reaching the Stage endpoint now
        // ends the Stage; StageCompleted is evaluated from its required check items.
        public bool MarkCurrentStageComplete() => EndCurrentStage();

        public StageTrainingResult GetStageResult(TrainingStage stage)
        {
            for (var i = 0; i < stageResults.Count; i++)
            {
                if (string.Equals(stageResults[i].StageId, stage.ToString(), StringComparison.Ordinal))
                    return stageResults[i];
            }
            return null;
        }

        public StageCheckItemResult RegisterCheckItem(
            TrainingStage stage,
            string itemId,
            string displayName,
            bool required = true)
        {
            if (stage == TrainingStage.None || stage == TrainingStage.Complete ||
                string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            var result = GetStageResult(stage);
            if (result == null || result.StageEnded)
                return null;

            var existing = FindCheckItem(result, itemId);
            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(displayName))
                    existing.DisplayName = displayName;
                existing.IsRequired |= required;
                return existing;
            }

            var item = new StageCheckItemResult
            {
                ItemId = itemId,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? itemId : displayName,
                IsRequired = required,
            };
            result.CheckItems.Add(item);
            return item;
        }

        public bool StartCheckItem(
            TrainingStage stage,
            string itemId,
            string displayName,
            bool required = true)
        {
            var item = RegisterCheckItem(stage, itemId, displayName, required);
            if (item == null || item.WasPerformed || item.TimingActive)
                return false;
            item.TimingActive = true;
            item.TimingStartedAt = TrainingClockNow;
            return true;
        }

        public bool CompleteCheckItem(
            TrainingStage stage,
            string itemId,
            string displayName,
            bool required = true,
            int errorCount = 0,
            int retryCount = 0)
        {
            var result = GetStageResult(stage);
            var item = RegisterCheckItem(stage, itemId, displayName, required);
            if (result == null || item == null || item.WasPerformed)
                return false;

            if (item.TimingActive)
            {
                item.DurationSeconds = RoundDuration(
                    TrainingClockNow - item.TimingStartedAt);
            }
            item.TimingActive = false;
            item.TimingStartedAt = 0d;
            item.WasPerformed = true;
            item.ErrorCount += Math.Max(0, errorCount);
            item.RetryCount += Math.Max(0, retryCount);
            RefreshStageMetrics(result);
            Changed?.Invoke();
            return true;
        }

        public void RecordCheckItemIssue(
            TrainingStage stage,
            string itemId,
            int errorCount = 1,
            int retryCount = 1)
        {
            var result = GetStageResult(stage);
            var item = result != null ? FindCheckItem(result, itemId) : null;
            if (item == null || result.StageEnded)
                return;
            item.ErrorCount += Math.Max(0, errorCount);
            item.RetryCount += Math.Max(0, retryCount);
            RefreshStageMetrics(result);
            Changed?.Invoke();
        }

        public void RecordStageMetrics(TrainingStage stage, int errorCount, int retryCount)
        {
            var result = GetStageResult(stage);
            if (result == null || result.StageEnded)
                return;
            result.UnassignedErrorCount += Math.Max(0, errorCount);
            result.UnassignedRetryCount += Math.Max(0, retryCount);
            RefreshStageMetrics(result);
            Changed?.Invoke();
        }

        public double GetStageDurationSeconds(TrainingStage stage)
        {
            switch (stage)
            {
                case TrainingStage.PPE: return ppeDurationSeconds;
                case TrainingStage.PreEntry: return preEntryDurationSeconds;
                case TrainingStage.LeakResponse: return leakResponseDurationSeconds;
                default: return 0d;
            }
        }

        public double GetCurrentStageActiveDurationSeconds()
        {
            if (timedStage == TrainingStage.None || timedStage != CurrentStage)
                return 0d;
            return Math.Max(0d, TrainingClockNow - timedStageStartedAt);
        }

        void StopCurrentStageTiming()
        {
            if (timedStage != CurrentStage)
                return;

            var elapsed = Math.Max(0d, TrainingClockNow - timedStageStartedAt);
            // Store whole seconds once at the Stage boundary. The displayed total is
            // therefore exactly the sum of the three displayed Stage values.
            SetStageDuration(CurrentStage, RoundDuration(elapsed));
            timedStage = TrainingStage.None;
            timedStageStartedAt = 0d;
        }

        void SetStageDuration(TrainingStage stage, double seconds)
        {
            var safeSeconds = Math.Max(0d, seconds);
            switch (stage)
            {
                case TrainingStage.PPE:
                    ppeDurationSeconds = safeSeconds;
                    break;
                case TrainingStage.PreEntry:
                    preEntryDurationSeconds = safeSeconds;
                    break;
                case TrainingStage.LeakResponse:
                    leakResponseDurationSeconds = safeSeconds;
                    break;
            }
        }

        TrainingResultData CreateCompletionResult()
        {
            var endedAtUtc = DateTime.UtcNow.ToString("O");
            var result = new TrainingResultData
            {
                SessionId = SessionId,
                ExecutionMode = SelectedMode == TrainingMode.Full ? "FullCourse" : "SingleStage",
                SelectedStage = SelectedMode == TrainingMode.Full
                    ? string.Empty
                    : FirstStageFor(SelectedMode).ToString(),
                PpeDurationSeconds = ppeDurationSeconds,
                PreEntryDurationSeconds = preEntryDurationSeconds,
                LeakResponseDurationSeconds = leakResponseDurationSeconds,
                TotalDurationSeconds = TotalDurationSeconds,
                TrainingCompleted = HasCompletedRequiredTraining,
                EndedAtUtc = endedAtUtc,
                CompletedAtUtc = HasCompletedRequiredTraining ? endedAtUtc : string.Empty,
            };
            result.StageResults.AddRange(stageResults);
            return result;
        }

        StageTrainingResult CreateStageResult(TrainingStage stage)
        {
            return new StageTrainingResult
            {
                SessionId = SessionId,
                ExecutionMode = SelectedMode == TrainingMode.Full ? "FullCourse" : "SingleStage",
                SelectedStage = SelectedMode == TrainingMode.Full
                    ? string.Empty
                    : FirstStageFor(SelectedMode).ToString(),
                StageId = stage.ToString(),
                StageName = GetStageLabel(stage),
            };
        }

        bool ContainsEveryStage(List<TrainingStage> states)
        {
            if (stageSequence.Count == 0)
                return false;
            for (var i = 0; i < stageSequence.Count; i++)
            {
                if (!states.Contains(stageSequence[i]))
                    return false;
            }
            return true;
        }

        static StageCheckItemResult FindCheckItem(StageTrainingResult result, string itemId)
        {
            for (var i = 0; i < result.CheckItems.Count; i++)
            {
                if (string.Equals(result.CheckItems[i].ItemId, itemId, StringComparison.Ordinal))
                    return result.CheckItems[i];
            }
            return null;
        }

        static void StopOpenCheckItemTimings(StageTrainingResult result)
        {
            var now = TrainingClockNow;
            for (var i = 0; i < result.CheckItems.Count; i++)
            {
                var item = result.CheckItems[i];
                if (!item.TimingActive)
                    continue;
                item.DurationSeconds = RoundDuration(now - item.TimingStartedAt);
                item.TimingActive = false;
                item.TimingStartedAt = 0d;
            }
        }

        static void RefreshStageMetrics(StageTrainingResult result)
        {
            var errors = result.UnassignedErrorCount;
            var retries = result.UnassignedRetryCount;
            for (var i = 0; i < result.CheckItems.Count; i++)
            {
                errors += result.CheckItems[i].ErrorCount;
                retries += result.CheckItems[i].RetryCount;
            }
            result.ErrorCount = errors;
            result.RetryCount = retries;
        }

        static double RoundDuration(double seconds) =>
            Math.Round(Math.Max(0d, seconds), MidpointRounding.AwayFromZero);

        public TrainingFeedbackRecord SaveQuickFeedback(
            TrainingStage targetStage,
            IEnumerable<string> optionIds)
        {
            var feedback = ResolveFeedbackRecord(targetStage);
            if (feedback == null)
                return null;

            feedback.QuickFeedbackIds.Clear();
            if (optionIds != null)
            {
                foreach (var id in optionIds)
                {
                    if (string.IsNullOrWhiteSpace(id) || feedback.QuickFeedbackIds.Contains(id))
                        continue;
                    if (string.Equals(id, "no_problem", StringComparison.Ordinal))
                    {
                        feedback.QuickFeedbackIds.Clear();
                        feedback.QuickFeedbackIds.Add(id);
                        break;
                    }
                    feedback.QuickFeedbackIds.Add(id);
                }
            }
            feedback.Skipped = false;
            feedback.SubmittedAtUtc = DateTime.UtcNow.ToString("O");
            Changed?.Invoke();
            return feedback;
        }

        public TrainingFeedbackRecord SaveVoiceFeedback(
            TrainingStage targetStage,
            double durationSeconds,
            string transcript,
            string submittedAtUtc)
        {
            var feedback = ResolveFeedbackRecord(targetStage);
            if (feedback == null)
                return null;
            feedback.VoiceDurationSeconds = Math.Min(30d, Math.Max(0d, durationSeconds));
            feedback.VoiceTranscript = transcript ?? string.Empty;
            feedback.SubmittedAtUtc = string.IsNullOrWhiteSpace(submittedAtUtc)
                ? DateTime.UtcNow.ToString("O")
                : submittedAtUtc;
            feedback.Skipped = false;
            Changed?.Invoke();
            return feedback;
        }

        public TrainingFeedbackRecord SkipFeedback(TrainingStage targetStage)
        {
            var feedback = ResolveFeedbackRecord(targetStage);
            if (feedback == null)
                return null;
            feedback.Skipped = true;
            feedback.SubmittedAtUtc = DateTime.UtcNow.ToString("O");
            Changed?.Invoke();
            return feedback;
        }

        TrainingFeedbackRecord ResolveFeedbackRecord(TrainingStage requestedStage)
        {
            if (CompletionResult == null)
                return null;

            var targetStage = SelectedMode == TrainingMode.Full
                ? requestedStage
                : FirstStageFor(SelectedMode);
            List<TrainingFeedbackRecord> records;
            if (targetStage == TrainingStage.None && SelectedMode == TrainingMode.Full)
            {
                records = CompletionResult.CourseFeedback;
            }
            else
            {
                var stageResult = GetStageResult(targetStage);
                if (stageResult == null)
                    return null;
                records = stageResult.Feedback;
            }

            var targetKey = targetStage == TrainingStage.None ? "FullCourse" : targetStage.ToString();
            for (var i = 0; i < records.Count; i++)
            {
                if (string.Equals(records[i].TargetStage, targetKey, StringComparison.Ordinal))
                    return records[i];
            }

            var feedback = new TrainingFeedbackRecord
            {
                SessionId = SessionId,
                TargetStage = targetKey,
            };
            records.Add(feedback);
            return feedback;
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
            timedStage = TrainingStage.None;
            timedStageStartedAt = 0d;
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
                case TrainingMode.Full: return "전체 교육";
                case TrainingMode.PPEOnly: return "PPE 착용";
                case TrainingMode.PreEntryOnly: return "현장 진입 전 확인";
                case TrainingMode.LeakResponseOnly: return "누출 대응";
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
                case TrainingStage.Complete: return "교육 완료";
                default: return "시작";
            }
        }
    }
}
