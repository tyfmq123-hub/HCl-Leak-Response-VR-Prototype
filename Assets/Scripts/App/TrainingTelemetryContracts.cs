using System;
using HClTraining.SharedXR;

namespace HClTraining.App
{
    public enum TrainingEventType
    {
        SessionStarted = 0,
        StageEntered = 1,
        StageCompleted = 2,
        StepEntered = 3,
        StepCompleted = 4,
        ContentViewed = 5,
        BagItemRequested = 6,
        EquipmentUsed = 7,
        SessionCompleted = 8,
        SessionAbandoned = 9,
        SessionInterrupted = 10,
        CompletionScreenShown = 11,
        CompletionRestartRequested = 12,
        CompletionExitRequested = 13,
        StageEnded = 14,
        QuickFeedbackSubmitted = 15,
        VoiceFeedbackSubmitted = 16,
        FeedbackSkipped = 17,
        RecoveredIncompleteSession = 18,
        InteractionPrompted = 19,
        InteractionFirstInput = 20,
        InteractionAttempted = 21,
        InteractionCompleted = 22,
        AppStateChanged = 23,
        Heartbeat = 24,
        ClientError = 25,
        FlowAnomaly = 26,
        TrainingPauseChanged = 27,
    }

    public enum TrainingSessionStatus
    {
        None = 0,
        Running = 1,
        Completed = 2,
        Abandoned = 3,
        Interrupted = 4,
    }

    public enum TrainingExitReason
    {
        None = 0,
        NotProvided = 1,
        UserRequested = 2,
        InteractionDifficulty = 3,
        TrainingTooLong = 4,
        ExplanationUnclear = 5,
        VrDiscomfort = 6,
        SystemError = 7,
        Other = 8,
    }

    public enum TelemetryResult
    {
        None = 0,
        Success = 1,
        Rejected = 2,
        Failed = 3,
    }

    public enum TelemetryAttemptResult
    {
        Accepted = 0,
        Rejected = 1,
        Ignored = 2,
    }

    public enum TelemetryRejectionReason
    {
        None = 0,
        WrongControl = 1,
        WrongHand = 2,
        WrongOrder = 3,
        PrerequisiteMissing = 4,
        InteractionInactive = 5,
        Other = 6,
    }

    [Serializable]
    public sealed class TrainingTelemetryRecord
    {
        public string schema_version;
        public string event_id;
        public string session_id;
        public int sequence;
        public string occurred_at_utc;
        public string event_type;
        public string interaction_id;
        public string unity_scene_name;
        public string build_id;
        public string platform;
        public string device_model;
        public string payload_json;
        public string mode;
        public string stage_id;
        public string step_id;
        public string facility_id;
        public string target_id;
        public string result;
        public int attempt_count;
        public int rejected_count;
        public string exit_reason;
        public string detail;
        public string app_version;
        public string content_version;
        public double ppe_duration_seconds;
        public double preentry_duration_seconds;
        public double leakresponse_duration_seconds;
        public double total_duration_seconds;
        public bool training_completed;
        public string ended_at_utc;
        public string completed_at_utc;
        public string execution_mode;
        public string selected_stage;
        public bool stage_ended;
        public bool stage_completed;
        public double stage_duration_seconds;
        public int retry_count;
        public string stage_result_json;
        public string training_result_json;
        public string feedback_target;
        public string feedback_json;
    }

    public interface ITrainingTelemetrySink
    {
        bool TryBeginSession(string sessionId, out string error);
        bool TryWrite(TrainingTelemetryRecord record, out string error);
    }

    public interface ITrainingTelemetry : IStageTrainingTelemetry
    {
        string SessionId { get; }
        TrainingSessionStatus Status { get; }
        bool IsSessionActive { get; }

        void StartSession(TrainingMode mode);
        void StageEntered(TrainingStage stage);
        void StageEnded(TrainingStage stage, bool completed, StageTrainingResult result);
        void StageCompleted(TrainingStage stage);
        void CompletionScreenShown();
        void CompletionRestartRequested();
        void CompletionExitRequested();
        void QuickFeedbackSubmitted(TrainingStage targetStage, TrainingFeedbackRecord feedback);
        void VoiceFeedbackSubmitted(TrainingStage targetStage, TrainingFeedbackRecord feedback);
        void FeedbackSkipped(TrainingStage targetStage, TrainingFeedbackRecord feedback);
        void InteractionPrompted(
            TrainingStage stage,
            string interactionId,
            int expectedOrderIndex,
            string expectedHand,
            string expectedControl,
            string promptSource);
        void InteractionAttempted(
            string interactionId,
            string actualHand,
            string actualControl,
            TelemetryAttemptResult result,
            TelemetryRejectionReason rejectionReason = TelemetryRejectionReason.None,
            string detail = null);
        void InteractionCompleted(
            string interactionId,
            string actualHand = null,
            string actualControl = null);
        void ObserveInput(SharedXRHand hand, SharedXRControl control);
        void AppStateChanged(string stateType, bool active, string source, bool exactPhysicalRemovalConfirmed = false);
        void TrainingPauseChanged(bool paused);
        void ClientError(string logType, string message, string stackTrace);
        void FlowAnomaly(
            string anomalyType,
            string expectedState,
            string actualState,
            long elapsedMs,
            bool confirmedBug = false);
        void EndSession(
            TrainingSessionStatus status,
            TrainingExitReason reason = TrainingExitReason.NotProvided,
            string detail = null);
    }

    /// <summary>
    /// Stage code can always call Current. Missing App/DB infrastructure becomes a
    /// no-op instead of a NullReferenceException or a gameplay dependency.
    /// </summary>
    public static class TrainingTelemetry
    {
        static readonly ITrainingTelemetry Null = new NullTrainingTelemetry();
        static ITrainingTelemetry current = Null;

        public static ITrainingTelemetry Current => current;

        internal static void Register(ITrainingTelemetry telemetry)
        {
            current = telemetry ?? Null;
        }

        internal static void Unregister(ITrainingTelemetry telemetry)
        {
            if (ReferenceEquals(current, telemetry))
                current = Null;
        }

        sealed class NullTrainingTelemetry : ITrainingTelemetry
        {
            public string SessionId => string.Empty;
            public TrainingSessionStatus Status => TrainingSessionStatus.None;
            public bool IsSessionActive => false;

            public void StartSession(TrainingMode mode) { }
            public void StageEntered(TrainingStage stage) { }
            public void StageEnded(TrainingStage stage, bool completed, StageTrainingResult result) { }
            public void StageCompleted(TrainingStage stage) { }
            public void CompletionScreenShown() { }
            public void CompletionRestartRequested() { }
            public void CompletionExitRequested() { }
            public void QuickFeedbackSubmitted(TrainingStage targetStage, TrainingFeedbackRecord feedback) { }
            public void VoiceFeedbackSubmitted(TrainingStage targetStage, TrainingFeedbackRecord feedback) { }
            public void FeedbackSkipped(TrainingStage targetStage, TrainingFeedbackRecord feedback) { }
            public void InteractionPrompted(TrainingStage stage, string interactionId, int expectedOrderIndex, string expectedHand, string expectedControl, string promptSource) { }
            public void InteractionAttempted(string interactionId, string actualHand, string actualControl, TelemetryAttemptResult result, TelemetryRejectionReason rejectionReason = TelemetryRejectionReason.None, string detail = null) { }
            public void InteractionCompleted(string interactionId, string actualHand = null, string actualControl = null) { }
            public void ObserveInput(SharedXRHand hand, SharedXRControl control) { }
            public void AppStateChanged(string stateType, bool active, string source, bool exactPhysicalRemovalConfirmed = false) { }
            public void TrainingPauseChanged(bool paused) { }
            public void ClientError(string logType, string message, string stackTrace) { }
            public void FlowAnomaly(string anomalyType, string expectedState, string actualState, long elapsedMs, bool confirmedBug = false) { }
            public void StepEntered(TrainingStepId step) { }
            public void StepCompleted(TrainingStepId step, int attemptCount = 0, int rejectedCount = 0) { }
            public void ContentViewed(string contentId) { }
            public void BagItemRequested(string equipmentId, bool success) { }
            public void EquipmentUsed(string equipmentId) { }
            public void EndSession(
                TrainingSessionStatus status,
                TrainingExitReason reason = TrainingExitReason.NotProvided,
                string detail = null) { }
        }
    }
}
