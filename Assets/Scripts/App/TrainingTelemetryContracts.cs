using System;
using HClTraining.SharedXR;

namespace HClTraining.App
{
    public enum TrainingEventType
    {
        SessionStarted = 0,
        FacilitySelected = 1,
        StageEntered = 2,
        StageCompleted = 3,
        StepEntered = 4,
        StepCompleted = 5,
        ContentViewed = 6,
        BagItemRequested = 7,
        EquipmentUsed = 8,
        SessionCompleted = 9,
        SessionAbandoned = 10,
        SessionInterrupted = 11,
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

    [Serializable]
    public sealed class TrainingTelemetryRecord
    {
        public int schema_version;
        public string session_id;
        public int sequence;
        public string occurred_at_utc;
        public string event_type;
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

        void StartSession(TrainingMode mode, FacilityId facility);
        void StageEntered(TrainingStage stage);
        void StageCompleted(TrainingStage stage);
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

            public void StartSession(TrainingMode mode, FacilityId facility) { }
            public void StageEntered(TrainingStage stage) { }
            public void StageCompleted(TrainingStage stage) { }
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
