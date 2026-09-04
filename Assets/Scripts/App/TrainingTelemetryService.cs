using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HClTraining.SharedXR;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HClTraining.App
{
    public sealed class TrainingCompletionSummary
    {
        readonly string[] reviewItems;

        internal TrainingCompletionSummary(
            double ppeDurationSeconds,
            double preEntryDurationSeconds,
            double leakResponseDurationSeconds,
            double totalDurationSeconds,
            bool trainingCompleted,
            string completedAtUtc,
            bool hasProcedureErrorCount,
            int procedureErrorCount,
            string[] reviewItems)
        {
            PpeDurationSeconds = Math.Max(0d, ppeDurationSeconds);
            PreEntryDurationSeconds = Math.Max(0d, preEntryDurationSeconds);
            LeakResponseDurationSeconds = Math.Max(0d, leakResponseDurationSeconds);
            TotalDurationSeconds = Math.Max(0d, totalDurationSeconds);
            TrainingCompleted = trainingCompleted;
            CompletedAtUtc = completedAtUtc ?? string.Empty;
            HasProcedureErrorCount = hasProcedureErrorCount;
            ProcedureErrorCount = Math.Max(0, procedureErrorCount);
            this.reviewItems = reviewItems ?? Array.Empty<string>();
        }

        public bool HasTotalDuration => true;
        public double PpeDurationSeconds { get; }
        public double PreEntryDurationSeconds { get; }
        public double LeakResponseDurationSeconds { get; }
        public double TotalDurationSeconds { get; }
        public bool TrainingCompleted { get; }
        public string CompletedAtUtc { get; }
        public bool HasProcedureErrorCount { get; }
        public int ProcedureErrorCount { get; }
        public IReadOnlyList<string> ReviewItems => reviewItems;
    }

    public sealed class JsonLinesTrainingTelemetrySink : ITrainingTelemetrySink
    {
        readonly string directoryPath;
        string sessionFilePath;

        public JsonLinesTrainingTelemetrySink(string rootPath)
        {
            directoryPath = Path.Combine(rootPath, "TrainingTelemetry");
        }

        public string DirectoryPath => directoryPath;

        public bool TryBeginSession(string sessionId, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(directoryPath);
                sessionFilePath = Path.Combine(directoryPath, sessionId + ".jsonl");
                return true;
            }
            catch (Exception exception)
            {
                sessionFilePath = null;
                error = exception.Message;
                return false;
            }
        }

        public bool TryWrite(TrainingTelemetryRecord record, out string error)
        {
            if (record == null || string.IsNullOrEmpty(sessionFilePath))
            {
                error = "Telemetry session file is not initialized.";
                return false;
            }
            return TryAppend(sessionFilePath, record, out error);
        }

        internal static bool TryAppend(string path, TrainingTelemetryRecord record, out string error)
        {
            error = null;
            try
            {
                File.AppendAllText(
                    path,
                    TrainingTelemetryJson.SerializeRecord(record) + Environment.NewLine,
                    new UTF8Encoding(false));
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }

    [DefaultExecutionOrder(-9700)]
    [DisallowMultipleComponent]
    public sealed class TrainingTelemetryService : MonoBehaviour, ITrainingTelemetry
    {
        public const string ContractSchemaVersion = "1.0";
        const string DefaultContentVersion = "integration-v1";
        const float HeartbeatSeconds = 10f;
        const int RecentContextLimit = 10;
        const int ErrorEventsPerFingerprintLimit = 10;
        const int MaximumRecordedWrongInputsPerInteraction = 8;
        const int RapidInputPressThreshold = 5;
        const double RapidInputWindowSeconds = 2d;
        const int ContextualInputAnalyticsVersion = 2;

        [SerializeField] string contentVersion = DefaultContentVersion;

        readonly Dictionary<TrainingStepId, int> rejectedByStep = new Dictionary<TrainingStepId, int>();
        readonly Dictionary<string, int> errorCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<TrainingTelemetryRecord> recentContext = new List<TrainingTelemetryRecord>();
        readonly HashSet<string> activeDistinctWrongInputIds = new HashSet<string>(StringComparer.Ordinal);
        readonly List<WrongInputObservation> activeDistinctWrongInputs = new List<WrongInputObservation>();
        readonly List<double> activeRecentWrongInputTimes = new List<double>();

        TrainingSessionState session;
        ITrainingTelemetrySink sink;
        TrainingMode mode;
        int sequence;
        bool sinkFailureReported;
        bool hasProcedureErrorMetric;
        int procedureErrorCount;
        TrainingCompletionSummary completionSummary;
        DateTime stageEnteredAtUtc;
        TrainingStage enteredStage;
        bool enteredStageEnded;
        float nextHeartbeatAt;

        string activeInteractionId = string.Empty;
        TrainingStage activeInteractionStage;
        int activeExpectedOrderIndex;
        string activeExpectedHand = string.Empty;
        string activeExpectedControl = string.Empty;
        string activePromptSource = string.Empty;
        double activeInteractionStartedAt;
        bool activeFirstInputRecorded;
        bool activeAcceptedAttemptRecorded;
        int activeAttemptCount;
        int activeRejectedCount;
        int actualCompletionOrder;
        string lastObservedHand = string.Empty;
        string lastObservedControl = string.Empty;
        string lastCompletedInteractionId = string.Empty;
        string lastCompletedStageId = string.Empty;
        string previousCompletedInteractionId = string.Empty;
        double previousInteractionCompletedAt;
        int activeWrongInputPressCount;
        int activeWrongInputEventsEmitted;
        bool activeRapidInputDetected;
        string activeFirstWrongHand = string.Empty;
        string activeFirstWrongControl = string.Empty;
        string activeFirstWrongKey = string.Empty;
        long activeFirstWrongLatencyMs;
        string activeLastWrongHand = string.Empty;
        string activeLastWrongControl = string.Empty;
        string activeLastWrongKey = string.Empty;

        public string SessionId { get; private set; } = string.Empty;
        public TrainingSessionStatus Status { get; private set; } = TrainingSessionStatus.None;
        public bool IsSessionActive => Status == TrainingSessionStatus.Running;
        public int LastEventSequence => sequence;
        public string ActiveInteractionId => activeInteractionId;
        public string LastCompletedInteractionId => lastCompletedInteractionId;
        public long ActiveInteractionElapsedMs => CurrentInteractionElapsedMs();
        public static string CurrentBuildId => ResolveBuildId();
        public event Action<TrainingTelemetryRecord, bool> RecordPersisted;

        public bool TryGetCompletionSummary(out TrainingCompletionSummary summary)
        {
            summary = completionSummary;
            return summary != null;
        }

        public void Initialize(TrainingSessionState state, ITrainingTelemetrySink eventSink = null)
        {
            session = state;
            sink = eventSink ?? new JsonLinesTrainingTelemetrySink(Application.persistentDataPath);
            if (eventSink == null)
                RecoverIncompleteSessions(Path.Combine(Application.persistentDataPath, "TrainingTelemetry"));
            TrainingTelemetry.Register(this);
            SharedTrainingTelemetry.Register(this);
        }

        public void StartSession(TrainingMode trainingMode)
        {
            if (trainingMode == TrainingMode.None)
                return;

            if (IsSessionActive)
                EndSession(TrainingSessionStatus.Interrupted, TrainingExitReason.NotProvided, "session_restarted");

            mode = trainingMode;
            SessionId = session != null && !string.IsNullOrWhiteSpace(session.SessionId)
                ? session.SessionId
                : Guid.NewGuid().ToString("N");
            Status = TrainingSessionStatus.Running;
            sequence = 0;
            sinkFailureReported = false;
            hasProcedureErrorMetric = false;
            procedureErrorCount = 0;
            rejectedByStep.Clear();
            errorCounts.Clear();
            recentContext.Clear();
            completionSummary = null;
            lastCompletedInteractionId = string.Empty;
            lastCompletedStageId = string.Empty;
            previousCompletedInteractionId = string.Empty;
            previousInteractionCompletedAt = 0d;
            enteredStage = TrainingStage.None;
            enteredStageEnded = false;
            ClearActiveInteraction();
            nextHeartbeatAt = Time.unscaledTime + HeartbeatSeconds;

            string sinkError = null;
            if (sink == null || !sink.TryBeginSession(SessionId, out sinkError))
                ReportSinkFailure(sinkError);

            var executionMode = trainingMode == TrainingMode.Full ? "FullTraining" : "SingleStage";
            var selectedStage = trainingMode == TrainingMode.Full
                ? string.Empty
                : TrainingSessionState.FirstStageFor(trainingMode).ToString();
            var payload = new TelemetryPayloadBuilder()
                .Add("mode", trainingMode.ToString())
                .Add("execution_mode", executionMode);
            if (string.IsNullOrEmpty(selectedStage)) payload.AddNull("selected_stage");
            else payload.Add("selected_stage", selectedStage);

            Emit(
                TrainingEventType.SessionStarted,
                targetId: trainingMode.ToString(),
                result: TelemetryResult.Success,
                payloadJson: payload.ToString());
        }

        public void StageEntered(TrainingStage stage)
        {
            if (!IsSessionActive || stage == TrainingStage.None || stage == TrainingStage.Complete)
                return;
            stageEnteredAtUtc = DateTime.UtcNow;
            enteredStage = stage;
            enteredStageEnded = false;
            actualCompletionOrder = 0;
            previousCompletedInteractionId = string.Empty;
            previousInteractionCompletedAt = 0d;
            ClearActiveInteraction();
            Emit(TrainingEventType.StageEntered, stage: stage, payloadJson: "{}");
        }

        public void StageCompleted(TrainingStage stage)
        {
            if (!IsSessionActive || stage == TrainingStage.None || stage == TrainingStage.Complete)
                return;
            lastCompletedStageId = stage.ToString();
            var payload = new TelemetryPayloadBuilder()
                .Add("last_completed_interaction_id", lastCompletedInteractionId);
            Emit(TrainingEventType.StageCompleted, stage: stage, result: TelemetryResult.Success, payloadJson: payload.ToString());
        }

        public void StageEnded(TrainingStage stage, bool completed, StageTrainingResult stageResult)
        {
            if (!IsSessionActive || stage == TrainingStage.None || stage == TrainingStage.Complete)
                return;

            var elapsedMs = stageEnteredAtUtc == default
                ? ToMilliseconds(stageResult != null ? stageResult.StageDurationSeconds : 0d)
                : Math.Max(0L, (long)(DateTime.UtcNow - stageEnteredAtUtc).TotalMilliseconds);
            var activeSeconds = stageResult != null && stageResult.StageEnded
                ? stageResult.StageDurationSeconds
                : session != null && session.CurrentStage == stage
                    ? session.GetCurrentStageActiveDurationSeconds()
                    : stageResult != null ? stageResult.StageDurationSeconds : 0d;
            var activeMs = ToMilliseconds(activeSeconds);
            var payload = new TelemetryPayloadBuilder()
                .Add("completed", completed)
                .Add("stage_elapsed_duration_ms", elapsedMs)
                .Add("stage_inactive_duration_ms", Math.Max(0L, elapsedMs - activeMs))
                .Add("stage_active_duration_ms", activeMs)
                .Add("last_completed_interaction_id", lastCompletedInteractionId);
            Emit(
                TrainingEventType.StageEnded,
                stage: stage,
                result: completed ? TelemetryResult.Success : TelemetryResult.Failed,
                stageResult: stageResult,
                payloadJson: payload.ToString(),
                immediateFlush: true);
            if (enteredStage == stage)
                enteredStageEnded = true;
        }

        public void StepEntered(TrainingStepId step)
        {
            if (IsSessionActive && step != TrainingStepId.None)
                Emit(TrainingEventType.StepEntered, step: step, payloadJson: "{}");
        }

        public void StepCompleted(TrainingStepId step, int attemptCount = 0, int rejectedCount = 0)
        {
            if (!IsSessionActive || step == TrainingStepId.None)
                return;

            var safeAttemptCount = Mathf.Max(0, attemptCount);
            var safeRejectedCount = Mathf.Max(0, rejectedCount);
            if (safeAttemptCount > 0 || safeRejectedCount > 0)
                hasProcedureErrorMetric = true;
            procedureErrorCount += safeRejectedCount;
            if (session != null)
            {
                if (step == TrainingStepId.PreEntry)
                {
                    session.RecordCheckItemIssue(
                        TrainingStage.PreEntry,
                        "preentry.StopPump",
                        safeRejectedCount,
                        Mathf.Max(0, safeAttemptCount - 1));
                }
                else
                {
                    session.RecordStageMetrics(
                        session.CurrentStage,
                        safeRejectedCount,
                        Mathf.Max(0, safeAttemptCount - 1));
                }
            }
            if (safeRejectedCount > 0)
            {
                if (!rejectedByStep.TryGetValue(step, out var currentRejectedCount)) currentRejectedCount = 0;
                rejectedByStep[step] = currentRejectedCount + safeRejectedCount;
            }

            var payload = new TelemetryPayloadBuilder()
                .Add("attempt_count", safeAttemptCount)
                .Add("rejected_count", safeRejectedCount);
            Emit(
                TrainingEventType.StepCompleted,
                step: step,
                result: TelemetryResult.Success,
                attemptCount: safeAttemptCount,
                rejectedCount: safeRejectedCount,
                payloadJson: payload.ToString());
        }

        public void ContentViewed(string contentId)
        {
            if (!IsSessionActive || string.IsNullOrWhiteSpace(contentId))
                return;

            const string ppeBlockedPrefix = "PPE.EquipBlocked.";
            if (contentId.StartsWith(ppeBlockedPrefix, StringComparison.Ordinal))
            {
                var stepId = contentId.Substring(ppeBlockedPrefix.Length);
                session?.RecordCheckItemIssue(TrainingStage.PPE, "ppe." + stepId);
                InteractionAttempted(activeInteractionId, "Either", "Grip", TelemetryAttemptResult.Rejected,
                    TelemetryRejectionReason.WrongOrder, "attempted_target=" + stepId);
            }
            else if (string.Equals(contentId, "PreEntry.Panel.StopFailed", StringComparison.Ordinal))
            {
                InteractionAttempted(activeInteractionId, "Right", "Trigger", TelemetryAttemptResult.Rejected,
                    TelemetryRejectionReason.Other, "panel_target_missed");
            }

            var payload = new TelemetryPayloadBuilder().Add("content_id", contentId);
            Emit(TrainingEventType.ContentViewed, targetId: contentId, result: TelemetryResult.Success,
                payloadJson: payload.ToString());
        }

        public void BagItemRequested(string equipmentId, bool success)
        {
            if (!IsSessionActive || string.IsNullOrWhiteSpace(equipmentId))
                return;
            if (!success)
            {
                InteractionAttempted(activeInteractionId, "Left", "Grip", TelemetryAttemptResult.Rejected,
                    TelemetryRejectionReason.PrerequisiteMissing, "equipment_id=" + equipmentId);
            }
            var payload = new TelemetryPayloadBuilder()
                .Add("equipment_id", equipmentId)
                .Add("result", success ? "Success" : "Rejected");
            Emit(TrainingEventType.BagItemRequested, targetId: equipmentId,
                result: success ? TelemetryResult.Success : TelemetryResult.Rejected,
                payloadJson: payload.ToString());
        }

        public void EquipmentUsed(string equipmentId)
        {
            if (!IsSessionActive || string.IsNullOrWhiteSpace(equipmentId))
                return;
            var payload = new TelemetryPayloadBuilder().Add("equipment_id", equipmentId);
            Emit(TrainingEventType.EquipmentUsed, targetId: equipmentId, result: TelemetryResult.Success,
                payloadJson: payload.ToString());
        }

        public void InteractionPrompted(
            TrainingStage stage,
            string interactionId,
            int expectedOrderIndex,
            string expectedHand,
            string expectedControl,
            string promptSource)
        {
            if (!IsSessionActive || string.IsNullOrWhiteSpace(interactionId))
                return;

            if (!string.IsNullOrEmpty(activeInteractionId))
            {
                if (string.Equals(activeInteractionId, interactionId, StringComparison.Ordinal))
                    return;
                FlowAnomaly("DuplicateInteractionStart", activeInteractionId, interactionId, 0L);
            }

            activeInteractionId = interactionId;
            activeInteractionStage = stage;
            activeExpectedOrderIndex = Math.Max(0, expectedOrderIndex);
            activeExpectedHand = expectedHand ?? string.Empty;
            activeExpectedControl = expectedControl ?? string.Empty;
            activePromptSource = string.IsNullOrWhiteSpace(promptSource) ? "Unknown" : promptSource;
            activeInteractionStartedAt = SharedTrainingPause.UnscaledTime;
            activeFirstInputRecorded = false;
            activeAcceptedAttemptRecorded = false;
            activeAttemptCount = 0;
            activeRejectedCount = 0;
            lastObservedHand = string.Empty;
            lastObservedControl = string.Empty;
            ResetWrongInputSummary();

            var payload = new TelemetryPayloadBuilder()
                .Add("input_analytics_version", ContextualInputAnalyticsVersion)
                .Add("expected_order_index", activeExpectedOrderIndex)
                .Add("expected_hand", activeExpectedHand)
                .Add("expected_control", activeExpectedControl)
                .Add("expected_key", ResolveExpectedKey(activeExpectedHand, activeExpectedControl))
                .Add("prompt_source", activePromptSource);
            if (previousInteractionCompletedAt > 0d)
            {
                payload
                    .Add("previous_interaction_id", previousCompletedInteractionId)
                    .Add("transition_active_duration_ms", Math.Max(
                        0L,
                        (long)((SharedTrainingPause.UnscaledTime - previousInteractionCompletedAt) * 1000d)));
            }
            Emit(TrainingEventType.InteractionPrompted, stage: stage, interactionId: interactionId,
                payloadJson: payload.ToString());
        }

        public void ObserveInput(SharedXRHand hand, SharedXRControl control)
        {
            if (!IsSessionActive || string.IsNullOrEmpty(activeInteractionId) || SharedTrainingPause.IsClockInactive)
                return;
            if (control == SharedXRControl.Position || control == SharedXRControl.Rotation ||
                control == SharedXRControl.Thumbstick || control == SharedXRControl.ThumbstickClick ||
                control == SharedXRControl.Select ||
                control == SharedXRControl.Activate || control == SharedXRControl.UIPress ||
                control == SharedXRControl.Menu)
                return;
            if (string.Equals(activeExpectedControl, "Gaze", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(activeExpectedControl, "Movement", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(activeExpectedControl, "Automatic", StringComparison.OrdinalIgnoreCase))
                return;

            var actualHand = hand.ToString();
            var actualControl = control.ToString();
            var actualKey = ResolvePhysicalKey(actualHand, actualControl);
            RecordFirstInput(actualHand, actualControl);
            lastObservedHand = actualHand;
            lastObservedControl = actualControl;
            var handMatches = string.IsNullOrEmpty(activeExpectedHand) ||
                              string.Equals(activeExpectedHand, "Either", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(activeExpectedHand, actualHand, StringComparison.OrdinalIgnoreCase);
            var controlMatches = ControlMatchesExpected(actualHand, actualControl, actualKey);
            if (!handMatches || !controlMatches)
            {
                RecordObservedWrongInput(
                    actualHand,
                    actualControl,
                    actualKey,
                    !handMatches ? TelemetryRejectionReason.WrongHand : TelemetryRejectionReason.WrongControl);
            }
        }

        public void InteractionAttempted(
            string interactionId,
            string actualHand,
            string actualControl,
            TelemetryAttemptResult result,
            TelemetryRejectionReason rejectionReason = TelemetryRejectionReason.None,
            string detail = null)
        {
            if (!IsSessionActive || string.IsNullOrEmpty(activeInteractionId))
                return;
            if (!string.IsNullOrEmpty(interactionId) &&
                !string.Equals(interactionId, activeInteractionId, StringComparison.Ordinal))
                return;

            RecordFirstInput(actualHand, actualControl);
            activeAttemptCount++;
            if (result == TelemetryAttemptResult.Rejected) activeRejectedCount++;
            else if (result == TelemetryAttemptResult.Accepted) activeAcceptedAttemptRecorded = true;
            if (!string.IsNullOrWhiteSpace(actualHand)) lastObservedHand = actualHand;
            if (!string.IsNullOrWhiteSpace(actualControl)) lastObservedControl = actualControl;

            var payload = new TelemetryPayloadBuilder()
                .Add("input_analytics_version", ContextualInputAnalyticsVersion)
                .Add("attempt_number", activeAttemptCount)
                .Add("expected_hand", activeExpectedHand)
                .Add("expected_control", activeExpectedControl)
                .Add("expected_key", ResolveExpectedKey(activeExpectedHand, activeExpectedControl))
                .Add("actual_hand", actualHand ?? string.Empty)
                .Add("actual_control", actualControl ?? string.Empty)
                .Add("actual_key", ResolvePhysicalKey(actualHand, actualControl))
                .Add("result", result.ToString())
                .Add("expected_order_index", activeExpectedOrderIndex);
            if (result == TelemetryAttemptResult.Rejected)
                payload.Add("rejection_reason", rejectionReason == TelemetryRejectionReason.None ? "Other" : rejectionReason.ToString());
            if (!string.IsNullOrWhiteSpace(detail)) payload.Add("detail", detail);
            payload.AddNull("actual_completion_order_index");

            Emit(TrainingEventType.InteractionAttempted, stage: activeInteractionStage,
                interactionId: activeInteractionId,
                result: result == TelemetryAttemptResult.Accepted ? TelemetryResult.Success :
                    result == TelemetryAttemptResult.Rejected ? TelemetryResult.Rejected : TelemetryResult.None,
                attemptCount: activeAttemptCount, rejectedCount: activeRejectedCount,
                payloadJson: payload.ToString());
        }

        public void InteractionCompleted(string interactionId, string actualHand = null, string actualControl = null)
        {
            if (!IsSessionActive || string.IsNullOrWhiteSpace(interactionId) || string.IsNullOrEmpty(activeInteractionId))
                return;
            if (!string.Equals(interactionId, activeInteractionId, StringComparison.Ordinal))
            {
                FlowAnomaly("UnexpectedStepTransition", activeInteractionId, interactionId, CurrentInteractionElapsedMs());
                return;
            }

            var lastObservedKey = ResolvePhysicalKey(lastObservedHand, lastObservedControl);
            var lastObservedMatches = HandMatchesExpected(lastObservedHand) &&
                                      ControlMatchesExpected(lastObservedHand, lastObservedControl, lastObservedKey);
            var resolvedHand = !string.IsNullOrWhiteSpace(actualHand) ? actualHand :
                lastObservedMatches ? lastObservedHand : activeExpectedHand;
            var resolvedControl = !string.IsNullOrWhiteSpace(actualControl) ? actualControl :
                lastObservedMatches ? lastObservedControl : activeExpectedControl;
            RecordFirstInput(resolvedHand, resolvedControl);
            if (!activeAcceptedAttemptRecorded)
                InteractionAttempted(activeInteractionId, resolvedHand, resolvedControl, TelemetryAttemptResult.Accepted);

            actualCompletionOrder++;
            var sequenceResult = actualCompletionOrder != activeExpectedOrderIndex ? "OutOfOrder" :
                activeAttemptCount > 1 || activeRejectedCount > 0 ? "Repeated" : "Correct";
            var payload = new TelemetryPayloadBuilder()
                .Add("input_analytics_version", ContextualInputAnalyticsVersion)
                .Add("expected_order_index", activeExpectedOrderIndex)
                .Add("actual_completion_order_index", actualCompletionOrder)
                .Add("attempt_count", activeAttemptCount)
                .Add("rejected_count", activeRejectedCount)
                .Add("completion_time_ms", CurrentInteractionElapsedMs())
                .Add("sequence_result", sequenceResult)
                .Add("expected_hand", activeExpectedHand)
                .Add("expected_control", activeExpectedControl)
                .Add("expected_key", ResolveExpectedKey(activeExpectedHand, activeExpectedControl))
                .Add("actual_hand", resolvedHand ?? string.Empty)
                .Add("actual_control", resolvedControl ?? string.Empty)
                .Add("actual_key", ResolvePhysicalKey(resolvedHand, resolvedControl))
                .Add("had_wrong_input", activeWrongInputPressCount > 0)
                .Add("wrong_input_press_count", activeWrongInputPressCount)
                .Add("wrong_input_distinct_count", activeDistinctWrongInputs.Count)
                .Add("wrong_input_repeat_count", Math.Max(0, activeWrongInputPressCount - activeDistinctWrongInputs.Count))
                .Add("wrong_input_events_emitted", activeWrongInputEventsEmitted)
                .Add("wrong_input_unemitted_distinct_count", Math.Max(0, activeDistinctWrongInputs.Count - activeWrongInputEventsEmitted))
                .Add("wrong_input_pattern", ResolveWrongInputPattern())
                .Add("wrong_input_signal_quality", HasRapidMultiButtonInput() ? "Low" : "Normal")
                .AddRaw("wrong_inputs", BuildWrongInputsJson());
            if (activeWrongInputPressCount > 0)
            {
                payload
                    .Add("first_wrong_hand", activeFirstWrongHand)
                    .Add("first_wrong_control", activeFirstWrongControl)
                    .Add("first_wrong_key", activeFirstWrongKey)
                    .Add("first_wrong_latency_ms", activeFirstWrongLatencyMs)
                    .Add("last_wrong_hand", activeLastWrongHand)
                    .Add("last_wrong_control", activeLastWrongControl)
                    .Add("last_wrong_key", activeLastWrongKey);
            }
            Emit(TrainingEventType.InteractionCompleted, stage: activeInteractionStage,
                interactionId: activeInteractionId, result: TelemetryResult.Success,
                attemptCount: activeAttemptCount, rejectedCount: activeRejectedCount,
                payloadJson: payload.ToString(), immediateFlush: true);
            lastCompletedInteractionId = activeInteractionId;
            previousCompletedInteractionId = activeInteractionId;
            previousInteractionCompletedAt = SharedTrainingPause.UnscaledTime;
            ClearActiveInteraction();
        }

        public void AppStateChanged(string stateType, bool active, string source, bool exactPhysicalRemovalConfirmed = false)
        {
            if (!IsSessionActive)
                return;
            var payload = new TelemetryPayloadBuilder()
                .Add("state_type", stateType)
                .Add("state", active ? "Active" : "Inactive")
                .Add("source", source)
                .Add("exact_physical_removal_confirmed", exactPhysicalRemovalConfirmed);
            Emit(TrainingEventType.AppStateChanged, interactionId: activeInteractionId,
                payloadJson: payload.ToString(), immediateFlush: !active);
        }

        public void TrainingPauseChanged(bool paused)
        {
            if (!IsSessionActive)
                return;
            var payload = new TelemetryPayloadBuilder()
                .Add("state", paused ? "Inactive" : "Active")
                .Add("source", "InAppMenu");
            Emit(TrainingEventType.TrainingPauseChanged, interactionId: activeInteractionId,
                payloadJson: payload.ToString(), immediateFlush: paused);
        }

        public void ClientError(string logType, string message, string stackTrace)
        {
            if (!IsSessionActive || string.IsNullOrWhiteSpace(message))
                return;
            message = Truncate(message, 4000);
            stackTrace = Truncate(stackTrace ?? string.Empty, 16000);
            var fingerprint = ComputeSha256(logType + "\n" + message + "\n" + stackTrace);
            if (!errorCounts.TryGetValue(fingerprint, out var count)) count = 0;
            if (count >= ErrorEventsPerFingerprintLimit) return;
            errorCounts[fingerprint] = count + 1;

            var payload = new TelemetryPayloadBuilder()
                .Add("log_type", logType)
                .Add("message", message)
                .Add("stack_trace", stackTrace ?? string.Empty, true)
                .Add("error_fingerprint", fingerprint)
                .Add("last_action_sequence", LastMajorActionSequence())
                .AddRaw("recent_context", BuildRecentContextJson());
            Emit(TrainingEventType.ClientError, interactionId: activeInteractionId,
                result: TelemetryResult.Failed, payloadJson: payload.ToString(), immediateFlush: true);
        }

        public void FlowAnomaly(string anomalyType, string expectedState, string actualState,
            long elapsedMs, bool confirmedBug = false)
        {
            if (!IsSessionActive)
                return;
            var payload = new TelemetryPayloadBuilder()
                .Add("anomaly_type", anomalyType)
                .Add("expected_state", expectedState)
                .Add("actual_state", actualState)
                .Add("elapsed_ms", Math.Max(0L, elapsedMs))
                .Add("confirmed_bug", confirmedBug);
            Emit(TrainingEventType.FlowAnomaly, interactionId: activeInteractionId,
                result: confirmedBug ? TelemetryResult.Failed : TelemetryResult.None,
                payloadJson: payload.ToString());
        }

        public void CompletionScreenShown()
        {
            if (IsSessionActive) Emit(TrainingEventType.CompletionScreenShown, result: TelemetryResult.Success, payloadJson: "{}");
        }

        public void CompletionRestartRequested()
        {
            if (Status == TrainingSessionStatus.Completed && !string.IsNullOrEmpty(SessionId))
                Emit(TrainingEventType.CompletionRestartRequested, result: TelemetryResult.Success, payloadJson: "{}");
        }

        public void CompletionExitRequested()
        {
            if (Status == TrainingSessionStatus.Completed && !string.IsNullOrEmpty(SessionId))
                Emit(TrainingEventType.CompletionExitRequested, result: TelemetryResult.Success, payloadJson: "{}");
        }

        public void QuickFeedbackSubmitted(TrainingStage targetStage, TrainingFeedbackRecord feedback) =>
            EmitFeedback(TrainingEventType.QuickFeedbackSubmitted, targetStage, feedback);
        public void VoiceFeedbackSubmitted(TrainingStage targetStage, TrainingFeedbackRecord feedback) =>
            EmitFeedback(TrainingEventType.VoiceFeedbackSubmitted, targetStage, feedback);
        public void FeedbackSkipped(TrainingStage targetStage, TrainingFeedbackRecord feedback) =>
            EmitFeedback(TrainingEventType.FeedbackSkipped, targetStage, feedback);

        void EmitFeedback(TrainingEventType eventType, TrainingStage targetStage, TrainingFeedbackRecord feedback)
        {
            if (string.IsNullOrEmpty(SessionId) || feedback == null)
                return;
            var feedbackJson = JsonUtility.ToJson(feedback);
            var payload = new TelemetryPayloadBuilder()
                .Add("feedback_target", feedback.TargetStage)
                .AddRaw("feedback", feedbackJson);
            Emit(eventType, stage: targetStage, interactionId: activeInteractionId,
                result: TelemetryResult.Success, feedback: feedback, payloadJson: payload.ToString());
        }

        public void EndSession(TrainingSessionStatus status,
            TrainingExitReason reason = TrainingExitReason.NotProvided, string detail = null)
        {
            if (!IsSessionActive || status == TrainingSessionStatus.None || status == TrainingSessionStatus.Running)
                return;

            if (status == TrainingSessionStatus.Completed)
            {
                if (session == null || !session.HasEndedSelectedTraining || session.CompletionResult == null)
                {
                    Debug.LogWarning("[Training Telemetry] 선택한 Stage가 종료되지 않아 결과 세션을 기록하지 않습니다.", this);
                    return;
                }
                CaptureCompletionSummary();
            }

            if (status != TrainingSessionStatus.Completed && enteredStage != TrainingStage.None && !enteredStageEnded)
            {
                StageEnded(
                    enteredStage,
                    completed: false,
                    session != null ? session.GetStageResult(enteredStage) : null);
            }

            var eventType = status == TrainingSessionStatus.Completed ? TrainingEventType.SessionCompleted :
                status == TrainingSessionStatus.Abandoned ? TrainingEventType.SessionAbandoned : TrainingEventType.SessionInterrupted;
            var totalActiveMs = ToMilliseconds(
                status == TrainingSessionStatus.Completed && session != null && session.CompletionResult != null
                    ? session.CompletionResult.TotalDurationSeconds
                    : session != null ? session.TotalActiveDurationSecondsIncludingCurrent : 0d);
            var payload = new TelemetryPayloadBuilder()
                .Add("training_completed", status == TrainingSessionStatus.Completed && session != null &&
                                           session.CompletionResult != null && session.CompletionResult.TrainingCompleted)
                .Add("total_active_duration_ms", totalActiveMs)
                .Add("completed_stage_count", session != null ? session.CompletedStages.Count : 0)
                .Add("last_completed_stage_id", lastCompletedStageId)
                .Add("last_completed_interaction_id", lastCompletedInteractionId)
                .Add("exit_reason", reason == TrainingExitReason.None ? string.Empty : reason.ToString())
                .Add("detail", detail ?? string.Empty);
            Emit(eventType,
                result: status == TrainingSessionStatus.Completed ? TelemetryResult.Success : TelemetryResult.Failed,
                exitReason: reason, detail: detail, payloadJson: payload.ToString(), immediateFlush: true);
            Status = status;
        }

        void Update()
        {
            if (!IsSessionActive || Time.unscaledTime < nextHeartbeatAt)
                return;
            nextHeartbeatAt = Time.unscaledTime + HeartbeatSeconds;
            var payload = new TelemetryPayloadBuilder()
                .Add("activity_state", SharedTrainingPause.IsClockInactive ? "Inactive" : "Active")
                .Add("current_interaction_elapsed_ms", CurrentInteractionElapsedMs());
            Emit(TrainingEventType.Heartbeat, interactionId: activeInteractionId, payloadJson: payload.ToString());
        }

        void RecordFirstInput(string actualHand, string actualControl)
        {
            if (activeFirstInputRecorded || string.IsNullOrEmpty(activeInteractionId))
                return;
            activeFirstInputRecorded = true;
            var payload = new TelemetryPayloadBuilder()
                .Add("input_analytics_version", ContextualInputAnalyticsVersion)
                .Add("expected_hand", activeExpectedHand)
                .Add("expected_control", activeExpectedControl)
                .Add("expected_key", ResolveExpectedKey(activeExpectedHand, activeExpectedControl))
                .Add("actual_hand", actualHand ?? string.Empty)
                .Add("actual_control", actualControl ?? string.Empty)
                .Add("actual_key", ResolvePhysicalKey(actualHand, actualControl))
                .Add("response_time_ms", CurrentInteractionElapsedMs());
            Emit(TrainingEventType.InteractionFirstInput, stage: activeInteractionStage,
                interactionId: activeInteractionId, payloadJson: payload.ToString());
        }

        long CurrentInteractionElapsedMs()
        {
            if (string.IsNullOrEmpty(activeInteractionId)) return 0L;
            return Math.Max(0L, (long)((SharedTrainingPause.UnscaledTime - activeInteractionStartedAt) * 1000d));
        }

        void ClearActiveInteraction()
        {
            activeInteractionId = string.Empty;
            activeInteractionStage = TrainingStage.None;
            activeExpectedOrderIndex = 0;
            activeExpectedHand = string.Empty;
            activeExpectedControl = string.Empty;
            activePromptSource = string.Empty;
            activeInteractionStartedAt = 0d;
            activeFirstInputRecorded = false;
            activeAcceptedAttemptRecorded = false;
            activeAttemptCount = 0;
            activeRejectedCount = 0;
            lastObservedHand = string.Empty;
            lastObservedControl = string.Empty;
            ResetWrongInputSummary();
        }

        void RecordObservedWrongInput(
            string actualHand,
            string actualControl,
            string actualKey,
            TelemetryRejectionReason rejectionReason)
        {
            var elapsedMs = CurrentInteractionElapsedMs();
            var elapsedSeconds = elapsedMs / 1000d;
            activeWrongInputPressCount++;
            if (activeWrongInputPressCount == 1)
            {
                activeFirstWrongHand = actualHand ?? string.Empty;
                activeFirstWrongControl = actualControl ?? string.Empty;
                activeFirstWrongKey = actualKey ?? string.Empty;
                activeFirstWrongLatencyMs = elapsedMs;
            }

            activeLastWrongHand = actualHand ?? string.Empty;
            activeLastWrongControl = actualControl ?? string.Empty;
            activeLastWrongKey = actualKey ?? string.Empty;
            activeRecentWrongInputTimes.Add(elapsedSeconds);
            for (var i = activeRecentWrongInputTimes.Count - 1; i >= 0; i--)
            {
                if (elapsedSeconds - activeRecentWrongInputTimes[i] <= RapidInputWindowSeconds)
                    continue;
                activeRecentWrongInputTimes.RemoveAt(i);
            }
            if (activeRecentWrongInputTimes.Count >= RapidInputPressThreshold)
                activeRapidInputDetected = true;

            var identity = (actualHand ?? string.Empty) + "|" + (actualKey ?? actualControl ?? string.Empty);
            if (!activeDistinctWrongInputIds.Add(identity))
                return;

            activeDistinctWrongInputs.Add(new WrongInputObservation(
                actualHand,
                actualControl,
                actualKey,
                elapsedMs));
            if (activeWrongInputEventsEmitted >= MaximumRecordedWrongInputsPerInteraction)
                return;

            activeWrongInputEventsEmitted++;
            InteractionAttempted(
                activeInteractionId,
                actualHand,
                actualControl,
                TelemetryAttemptResult.Rejected,
                rejectionReason,
                "input_observation=unique_per_interaction");
        }

        bool ControlMatchesExpected(string actualHand, string actualControl, string actualKey)
        {
            return string.IsNullOrEmpty(activeExpectedControl) ||
                   string.Equals(activeExpectedControl, actualControl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(activeExpectedControl, actualKey, StringComparison.OrdinalIgnoreCase);
        }

        bool HandMatchesExpected(string actualHand)
        {
            return !string.IsNullOrWhiteSpace(actualHand) &&
                   (string.IsNullOrEmpty(activeExpectedHand) ||
                    string.Equals(activeExpectedHand, "Either", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(activeExpectedHand, actualHand, StringComparison.OrdinalIgnoreCase));
        }

        string ResolveWrongInputPattern()
        {
            if (activeWrongInputPressCount <= 0)
                return "None";
            if (HasRapidMultiButtonInput())
                return "RapidMultiButtonInput";
            if (activeRapidInputDetected)
                return "RapidRepeatedInput";
            if (activeDistinctWrongInputs.Count == 1 && activeWrongInputPressCount > 1)
                return "RepeatedSameInput";
            if (activeDistinctWrongInputs.Count > 1)
                return "ExploratoryInput";
            return "SingleMistake";
        }

        bool HasRapidMultiButtonInput() =>
            activeRapidInputDetected && activeDistinctWrongInputs.Count >= 3;

        string BuildWrongInputsJson()
        {
            var builder = new StringBuilder(128);
            builder.Append('[');
            for (var i = 0; i < activeDistinctWrongInputs.Count; i++)
            {
                if (i > 0) builder.Append(',');
                var observation = activeDistinctWrongInputs[i];
                builder.Append('{');
                TrainingTelemetryJson.AppendName(builder, "hand");
                TrainingTelemetryJson.AppendQuoted(builder, observation.Hand);
                builder.Append(',');
                TrainingTelemetryJson.AppendName(builder, "control");
                TrainingTelemetryJson.AppendQuoted(builder, observation.Control);
                builder.Append(',');
                TrainingTelemetryJson.AppendName(builder, "key");
                TrainingTelemetryJson.AppendQuoted(builder, observation.Key);
                builder.Append(',');
                TrainingTelemetryJson.AppendName(builder, "first_latency_ms");
                builder.Append(observation.FirstLatencyMs);
                builder.Append('}');
            }
            builder.Append(']');
            return builder.ToString();
        }

        void ResetWrongInputSummary()
        {
            activeDistinctWrongInputIds.Clear();
            activeDistinctWrongInputs.Clear();
            activeRecentWrongInputTimes.Clear();
            activeWrongInputPressCount = 0;
            activeWrongInputEventsEmitted = 0;
            activeRapidInputDetected = false;
            activeFirstWrongHand = string.Empty;
            activeFirstWrongControl = string.Empty;
            activeFirstWrongKey = string.Empty;
            activeFirstWrongLatencyMs = 0L;
            activeLastWrongHand = string.Empty;
            activeLastWrongControl = string.Empty;
            activeLastWrongKey = string.Empty;
        }

        static string ResolveExpectedKey(string expectedHand, string expectedControl) =>
            ResolvePhysicalKey(expectedHand, expectedControl);

        static string ResolvePhysicalKey(string hand, string control)
        {
            if (string.Equals(control, "PrimaryButton", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(hand, "Right", StringComparison.OrdinalIgnoreCase)) return "A";
                if (string.Equals(hand, "Left", StringComparison.OrdinalIgnoreCase)) return "X";
            }
            else if (string.Equals(control, "SecondaryButton", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(hand, "Right", StringComparison.OrdinalIgnoreCase)) return "B";
                if (string.Equals(hand, "Left", StringComparison.OrdinalIgnoreCase)) return "Y";
            }
            return control ?? string.Empty;
        }

        readonly struct WrongInputObservation
        {
            public WrongInputObservation(string hand, string control, string key, long firstLatencyMs)
            {
                Hand = hand ?? string.Empty;
                Control = control ?? string.Empty;
                Key = key ?? string.Empty;
                FirstLatencyMs = Math.Max(0L, firstLatencyMs);
            }

            public string Hand { get; }
            public string Control { get; }
            public string Key { get; }
            public long FirstLatencyMs { get; }
        }

        void CaptureCompletionSummary()
        {
            var reviewItems = new List<string>();
            foreach (var pair in rejectedByStep)
                if (pair.Value > 0) reviewItems.Add(GetStepLabel(pair.Key));
            var result = session.CompletionResult;
            completionSummary = new TrainingCompletionSummary(
                result.PpeDurationSeconds, result.PreEntryDurationSeconds, result.LeakResponseDurationSeconds,
                result.TotalDurationSeconds, result.TrainingCompleted, result.CompletedAtUtc,
                hasProcedureErrorMetric, procedureErrorCount, reviewItems.ToArray());
        }

        static string GetStepLabel(TrainingStepId step)
        {
            switch (step)
            {
                case TrainingStepId.PPE: return "보호구 착용";
                case TrainingStepId.EquipmentPreparation: return "대응장비 준비";
                case TrainingStepId.PreEntry: return "현장 진입 전 확인";
                case TrainingStepId.SuctionValve: return "흡입측 밸브 조작";
                case TrainingStepId.DischargeValve: return "토출측 밸브 조작";
                case TrainingStepId.AbsorbentSock: return "흡착삭 설치";
                case TrainingStepId.AbsorbentPad: return "흡착패드 설치";
                case TrainingStepId.Recovery: return "오염 흡착재 수거";
                case TrainingStepId.WasteDisposal: return "오염 폐기물 처리";
                default: return step.ToString();
            }
        }

        TrainingTelemetryRecord Emit(
            TrainingEventType eventType, TrainingStage stage = TrainingStage.None,
            TrainingStepId step = TrainingStepId.None, string interactionId = null,
            string targetId = null, TelemetryResult result = TelemetryResult.None,
            int attemptCount = 0, int rejectedCount = 0,
            TrainingExitReason exitReason = TrainingExitReason.None, string detail = null,
            StageTrainingResult stageResult = null, TrainingFeedbackRecord feedback = null,
            string payloadJson = null, bool immediateFlush = false)
        {
            if (sink == null || string.IsNullOrEmpty(SessionId)) return null;
            var effectiveStage = stage != TrainingStage.None ? stage :
                session != null ? session.CurrentStage : TrainingStage.None;
            var completionResult = eventType == TrainingEventType.SessionCompleted && session != null
                ? session.CompletionResult : null;
            var executionMode = mode == TrainingMode.Full ? "FullTraining" : "SingleStage";
            var selectedStage = mode == TrainingMode.Full ? string.Empty : TrainingSessionState.FirstStageFor(mode).ToString();
            var record = new TrainingTelemetryRecord
            {
                schema_version = ContractSchemaVersion,
                event_id = Guid.NewGuid().ToString("N"),
                session_id = SessionId,
                sequence = ++sequence,
                occurred_at_utc = UtcNow(),
                event_type = eventType.ToString(),
                interaction_id = interactionId ?? string.Empty,
                unity_scene_name = SceneNameFor(effectiveStage),
                app_version = string.IsNullOrWhiteSpace(Application.version) ? "unknown" : Application.version,
                build_id = ResolveBuildId(),
                platform = Application.platform.ToString(),
                device_model = SystemInfo.deviceModel ?? string.Empty,
                payload_json = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
                mode = mode.ToString(),
                stage_id = effectiveStage == TrainingStage.None ? string.Empty : effectiveStage.ToString(),
                step_id = step == TrainingStepId.None ? string.Empty : step.ToString(),
                facility_id = "P102",
                target_id = targetId ?? string.Empty,
                result = result == TelemetryResult.None ? string.Empty : result.ToString(),
                attempt_count = attemptCount,
                rejected_count = stageResult != null ? stageResult.ErrorCount : rejectedCount,
                exit_reason = exitReason == TrainingExitReason.None ? string.Empty : exitReason.ToString(),
                detail = detail ?? string.Empty,
                content_version = string.IsNullOrWhiteSpace(contentVersion) ? DefaultContentVersion : contentVersion,
                ppe_duration_seconds = completionResult != null ? completionResult.PpeDurationSeconds : 0d,
                preentry_duration_seconds = completionResult != null ? completionResult.PreEntryDurationSeconds : 0d,
                leakresponse_duration_seconds = completionResult != null ? completionResult.LeakResponseDurationSeconds : 0d,
                total_duration_seconds = completionResult != null ? completionResult.TotalDurationSeconds : 0d,
                training_completed = completionResult != null && completionResult.TrainingCompleted,
                ended_at_utc = completionResult != null ? completionResult.EndedAtUtc : string.Empty,
                completed_at_utc = completionResult != null ? completionResult.CompletedAtUtc : string.Empty,
                execution_mode = executionMode,
                selected_stage = selectedStage,
                stage_ended = stageResult != null && stageResult.StageEnded,
                stage_completed = stageResult != null && stageResult.StageCompleted,
                stage_duration_seconds = stageResult != null ? stageResult.StageDurationSeconds : 0d,
                retry_count = stageResult != null ? stageResult.RetryCount : 0,
                stage_result_json = stageResult != null ? JsonUtility.ToJson(stageResult) : string.Empty,
                training_result_json = completionResult != null ? JsonUtility.ToJson(completionResult) : string.Empty,
                feedback_target = feedback != null ? feedback.TargetStage : string.Empty,
                feedback_json = feedback != null ? JsonUtility.ToJson(feedback) : string.Empty,
            };

            if (!sink.TryWrite(record, out var error))
            {
                ReportSinkFailure(error);
                return null;
            }
            TrackRecentContext(record);
            try { RecordPersisted?.Invoke(record, immediateFlush); }
            catch (Exception exception)
            {
                Debug.LogWarning("[Training Telemetry] Upload notification failed; local record is safe. " + exception.Message, this);
            }
            return record;
        }

        void TrackRecentContext(TrainingTelemetryRecord record)
        {
            if (record == null || record.event_type == TrainingEventType.Heartbeat.ToString() ||
                record.event_type == TrainingEventType.ClientError.ToString()) return;
            recentContext.Add(record);
            if (recentContext.Count > RecentContextLimit) recentContext.RemoveAt(0);
        }

        int LastMajorActionSequence() => recentContext.Count == 0 ? 0 : recentContext[recentContext.Count - 1].sequence;

        string BuildRecentContextJson()
        {
            var builder = new StringBuilder(256);
            builder.Append('[');
            for (var i = 0; i < recentContext.Count; i++)
            {
                if (i > 0) builder.Append(',');
                var item = recentContext[i];
                builder.Append('{');
                TrainingTelemetryJson.AppendName(builder, "sequence");
                builder.Append(item.sequence);
                builder.Append(',');
                TrainingTelemetryJson.AppendName(builder, "event_type");
                TrainingTelemetryJson.AppendQuoted(builder, item.event_type);
                if (!string.IsNullOrEmpty(item.interaction_id))
                {
                    builder.Append(',');
                    TrainingTelemetryJson.AppendName(builder, "interaction_id");
                    TrainingTelemetryJson.AppendQuoted(builder, item.interaction_id);
                }
                builder.Append('}');
            }
            builder.Append(']');
            return builder.ToString();
        }

        void RecoverIncompleteSessions(string telemetryDirectory)
        {
            if (!Directory.Exists(telemetryDirectory)) return;
            string[] files;
            try { files = Directory.GetFiles(telemetryDirectory, "*.jsonl", SearchOption.TopDirectoryOnly); }
            catch { return; }

            for (var i = 0; i < files.Length; i++)
            {
                TrainingTelemetryRecord first = null;
                TrainingTelemetryRecord last = null;
                var terminal = false;
                try
                {
                    foreach (var line in File.ReadLines(files[i], Encoding.UTF8))
                    {
                        if (!TrainingTelemetryJson.TryDeserializeRecord(line, out var record)) continue;
                        if (first == null) first = record;
                        last = record;
                        if (IsTerminalEvent(record.event_type)) terminal = true;
                    }
                }
                catch { continue; }
                if (first == null || last == null || terminal ||
                    !string.Equals(first.event_type, TrainingEventType.SessionStarted.ToString(), StringComparison.Ordinal)) continue;

                var payload = new TelemetryPayloadBuilder()
                    .Add("inferred_status", "AbnormalTerminationSuspected")
                    .Add("last_event_sequence", last.sequence)
                    .Add("last_event_at_utc", last.occurred_at_utc)
                    .Add("last_stage_id", last.stage_id)
                    .Add("last_interaction_id", last.interaction_id)
                    .Add("exact_cause_known", false);
                var recovered = new TrainingTelemetryRecord
                {
                    schema_version = ContractSchemaVersion,
                    event_id = Guid.NewGuid().ToString("N"),
                    session_id = last.session_id,
                    sequence = last.sequence + 1,
                    occurred_at_utc = UtcNow(),
                    event_type = TrainingEventType.RecoveredIncompleteSession.ToString(),
                    stage_id = last.stage_id ?? string.Empty,
                    step_id = last.step_id ?? string.Empty,
                    interaction_id = last.interaction_id ?? string.Empty,
                    unity_scene_name = last.unity_scene_name ?? string.Empty,
                    app_version = string.IsNullOrWhiteSpace(Application.version) ? "unknown" : Application.version,
                    build_id = ResolveBuildId(),
                    platform = Application.platform.ToString(),
                    device_model = SystemInfo.deviceModel ?? string.Empty,
                    payload_json = payload.ToString(),
                    mode = last.mode ?? string.Empty,
                    content_version = string.IsNullOrWhiteSpace(contentVersion) ? DefaultContentVersion : contentVersion,
                    result = TelemetryResult.Failed.ToString(),
                };
                JsonLinesTrainingTelemetrySink.TryAppend(files[i], recovered, out _);
            }
        }

        static bool IsTerminalEvent(string eventType) =>
            string.Equals(eventType, TrainingEventType.SessionCompleted.ToString(), StringComparison.Ordinal) ||
            string.Equals(eventType, TrainingEventType.SessionAbandoned.ToString(), StringComparison.Ordinal) ||
            string.Equals(eventType, TrainingEventType.SessionInterrupted.ToString(), StringComparison.Ordinal) ||
            string.Equals(eventType, TrainingEventType.RecoveredIncompleteSession.ToString(), StringComparison.Ordinal);

        static string SceneNameFor(TrainingStage stage)
        {
            switch (stage)
            {
                case TrainingStage.PPE: return "04_PPE";
                case TrainingStage.PreEntry: return "05_PreEntry";
                case TrainingStage.LeakResponse: return "06_LeakResponse";
                case TrainingStage.Complete: return "07_Complete";
                default:
                    var active = SceneManager.GetActiveScene();
                    return active.IsValid() ? active.name : string.Empty;
            }
        }

        static string ResolveBuildId()
        {
            var overrideId = Environment.GetEnvironmentVariable("HCL_VR_BUILD_ID");
            if (!string.IsNullOrWhiteSpace(overrideId)) return overrideId.Trim();
            if (!string.IsNullOrWhiteSpace(Application.buildGUID) &&
                !string.Equals(Application.buildGUID, "00000000000000000000000000000000", StringComparison.Ordinal))
                return Application.buildGUID;
            return Application.isEditor ? "editor-" + Application.version : string.Empty;
        }

        static long ToMilliseconds(double seconds) => Math.Max(0L, (long)Math.Round(seconds * 1000d));
        static string UtcNow() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

        static string Truncate(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maximum)
                return value ?? string.Empty;
            return value.Substring(0, maximum);
        }

        static string ComputeSha256(string value)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
            var builder = new StringBuilder(bytes.Length * 2);
            for (var i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }

        void ReportSinkFailure(string error)
        {
            if (sinkFailureReported) return;
            sinkFailureReported = true;
            Debug.LogWarning("[Training Telemetry] Local event recording is unavailable. Training will continue normally. " +
                             (error ?? string.Empty), this);
        }

        void OnApplicationQuit()
        {
            EndSession(TrainingSessionStatus.Interrupted, TrainingExitReason.NotProvided, "application_quit");
        }

        void OnDestroy()
        {
            SharedTrainingTelemetry.Unregister(this);
            TrainingTelemetry.Unregister(this);
        }
    }
}
