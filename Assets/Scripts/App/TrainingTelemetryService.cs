using System;
using System.IO;
using System.Text;
using HClTraining.SharedXR;
using UnityEngine;

namespace HClTraining.App
{
    public sealed class JsonLinesTrainingTelemetrySink : ITrainingTelemetrySink
    {
        readonly string directoryPath;
        string sessionFilePath;

        public JsonLinesTrainingTelemetrySink(string rootPath)
        {
            directoryPath = Path.Combine(rootPath, "TrainingTelemetry");
        }

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
            error = null;
            if (record == null || string.IsNullOrEmpty(sessionFilePath))
            {
                error = "Telemetry session file is not initialized.";
                return false;
            }

            try
            {
                File.AppendAllText(
                    sessionFilePath,
                    JsonUtility.ToJson(record) + Environment.NewLine,
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
        const int SchemaVersion = 1;
        const string DefaultContentVersion = "integration-v1";
        const string FixedFacilityId = "P102";

        [SerializeField] string contentVersion = DefaultContentVersion;

        TrainingSessionState session;
        ITrainingTelemetrySink sink;
        TrainingMode mode;
        int sequence;
        bool sinkFailureReported;

        public string SessionId { get; private set; } = string.Empty;
        public TrainingSessionStatus Status { get; private set; } = TrainingSessionStatus.None;
        public bool IsSessionActive => Status == TrainingSessionStatus.Running;

        public void Initialize(TrainingSessionState state, ITrainingTelemetrySink eventSink = null)
        {
            session = state;
            sink = eventSink ?? new JsonLinesTrainingTelemetrySink(Application.persistentDataPath);
            TrainingTelemetry.Register(this);
            SharedTrainingTelemetry.Register(this);
        }

        public void StartSession(TrainingMode trainingMode)
        {
            if (trainingMode == TrainingMode.None)
                return;

            if (IsSessionActive)
            {
                EndSession(
                    TrainingSessionStatus.Interrupted,
                    TrainingExitReason.NotProvided,
                    "session_restarted");
            }

            mode = trainingMode;
            SessionId = Guid.NewGuid().ToString("N");
            Status = TrainingSessionStatus.Running;
            sequence = 0;
            sinkFailureReported = false;

            string sinkError = null;
            if (sink == null || !sink.TryBeginSession(SessionId, out sinkError))
                ReportSinkFailure(sinkError);

            Emit(
                TrainingEventType.SessionStarted,
                targetId: trainingMode.ToString(),
                result: TelemetryResult.Success);
        }

        public void StageEntered(TrainingStage stage)
        {
            if (!IsSessionActive || stage == TrainingStage.None || stage == TrainingStage.Complete)
                return;
            Emit(TrainingEventType.StageEntered, stage: stage);
        }

        public void StageCompleted(TrainingStage stage)
        {
            if (!IsSessionActive || stage == TrainingStage.None || stage == TrainingStage.Complete)
                return;
            Emit(TrainingEventType.StageCompleted, stage: stage, result: TelemetryResult.Success);
        }

        public void StepEntered(TrainingStepId step)
        {
            if (IsSessionActive && step != TrainingStepId.None)
                Emit(TrainingEventType.StepEntered, step: step);
        }

        public void StepCompleted(TrainingStepId step, int attemptCount = 0, int rejectedCount = 0)
        {
            if (!IsSessionActive || step == TrainingStepId.None)
                return;
            Emit(
                TrainingEventType.StepCompleted,
                step: step,
                result: TelemetryResult.Success,
                attemptCount: Mathf.Max(0, attemptCount),
                rejectedCount: Mathf.Max(0, rejectedCount));
        }

        public void ContentViewed(string contentId)
        {
            if (IsSessionActive && !string.IsNullOrWhiteSpace(contentId))
                Emit(TrainingEventType.ContentViewed, targetId: contentId, result: TelemetryResult.Success);
        }

        public void BagItemRequested(string equipmentId, bool success)
        {
            if (IsSessionActive && !string.IsNullOrWhiteSpace(equipmentId))
            {
                Emit(
                    TrainingEventType.BagItemRequested,
                    targetId: equipmentId,
                    result: success ? TelemetryResult.Success : TelemetryResult.Rejected);
            }
        }

        public void EquipmentUsed(string equipmentId)
        {
            if (IsSessionActive && !string.IsNullOrWhiteSpace(equipmentId))
                Emit(TrainingEventType.EquipmentUsed, targetId: equipmentId, result: TelemetryResult.Success);
        }

        public void EndSession(
            TrainingSessionStatus status,
            TrainingExitReason reason = TrainingExitReason.NotProvided,
            string detail = null)
        {
            if (!IsSessionActive || status == TrainingSessionStatus.None || status == TrainingSessionStatus.Running)
                return;

            var eventType = status == TrainingSessionStatus.Completed
                ? TrainingEventType.SessionCompleted
                : status == TrainingSessionStatus.Abandoned
                    ? TrainingEventType.SessionAbandoned
                    : TrainingEventType.SessionInterrupted;
            Emit(
                eventType,
                result: status == TrainingSessionStatus.Completed
                    ? TelemetryResult.Success
                    : TelemetryResult.Failed,
                exitReason: reason,
                detail: detail);
            Status = status;
        }

        void Emit(
            TrainingEventType eventType,
            TrainingStage stage = TrainingStage.None,
            TrainingStepId step = TrainingStepId.None,
            string targetId = null,
            TelemetryResult result = TelemetryResult.None,
            int attemptCount = 0,
            int rejectedCount = 0,
            TrainingExitReason exitReason = TrainingExitReason.None,
            string detail = null)
        {
            if (sink == null || string.IsNullOrEmpty(SessionId))
                return;

            var effectiveStage = stage != TrainingStage.None
                ? stage
                : (session != null ? session.CurrentStage : TrainingStage.None);
            var record = new TrainingTelemetryRecord
            {
                schema_version = SchemaVersion,
                session_id = SessionId,
                sequence = ++sequence,
                occurred_at_utc = DateTime.UtcNow.ToString("O"),
                event_type = eventType.ToString(),
                mode = mode.ToString(),
                stage_id = effectiveStage == TrainingStage.None ? string.Empty : effectiveStage.ToString(),
                step_id = step == TrainingStepId.None ? string.Empty : step.ToString(),
                facility_id = FixedFacilityId,
                target_id = targetId ?? string.Empty,
                result = result == TelemetryResult.None ? string.Empty : result.ToString(),
                attempt_count = attemptCount,
                rejected_count = rejectedCount,
                exit_reason = exitReason == TrainingExitReason.None ? string.Empty : exitReason.ToString(),
                detail = detail ?? string.Empty,
                app_version = string.IsNullOrWhiteSpace(Application.version) ? "unknown" : Application.version,
                content_version = string.IsNullOrWhiteSpace(contentVersion)
                    ? DefaultContentVersion
                    : contentVersion,
            };

            if (!sink.TryWrite(record, out var error))
                ReportSinkFailure(error);
        }

        void ReportSinkFailure(string error)
        {
            if (sinkFailureReported)
                return;
            sinkFailureReported = true;
            Debug.LogWarning(
                "[Training Telemetry] Local event recording is unavailable. " +
                "Training will continue normally. " + (error ?? string.Empty),
                this);
        }

        void OnApplicationQuit()
        {
            EndSession(
                TrainingSessionStatus.Interrupted,
                TrainingExitReason.NotProvided,
                "application_quit");
        }

        void OnDestroy()
        {
            SharedTrainingTelemetry.Unregister(this);
            TrainingTelemetry.Unregister(this);
        }
    }
}
