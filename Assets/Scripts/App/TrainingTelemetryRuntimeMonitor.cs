using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using HClTraining.SharedXR;
using UnityEngine;
using UnityEngine.XR;

namespace HClTraining.App
{
    /// <summary>
    /// Bridges process/XR lifecycle and the project-owned input contract into the
    /// append-only telemetry service. Every callback is best-effort and must never
    /// block or change training progression.
    /// </summary>
    [DefaultExecutionOrder(-9650)]
    [DisallowMultipleComponent]
    public sealed class TrainingTelemetryRuntimeMonitor : MonoBehaviour
    {
        const int ApplicationPauseInactiveBit = 1;
        const int UserPresenceInactiveBit = 4;
        const float PresencePollSeconds = 1f;
        const int MaximumQueuedErrors = 64;
        const int MaximumErrorsPerFrame = 8;

        readonly ConcurrentQueue<PendingLog> pendingLogs = new ConcurrentQueue<PendingLog>();
        readonly HashSet<(SharedXRHand Hand, SharedXRControl Control)> pressedTelemetryControls =
            new HashSet<(SharedXRHand Hand, SharedXRControl Control)>();

        TrainingTelemetryService telemetry;
        SharedXRInputContract sharedInput;
        bool initialized;
        int queuedLogCount;
        float nextPresencePollAt;
        bool hasPresence;
        bool lastPresence;
        string presenceSessionId = string.Empty;
        string watchedInteractionId = string.Empty;
        bool stallReported;

        public void Initialize(TrainingTelemetryService service, SharedXRInputContract input)
        {
            if (initialized)
                return;

            initialized = true;
            telemetry = service;
            sharedInput = input;
            if (sharedInput != null)
            {
                sharedInput.Performed += OnSharedInputPerformed;
                sharedInput.Canceled += OnSharedInputCanceled;
            }
            SharedTrainingPause.Changed += OnTrainingPauseChanged;
            Application.logMessageReceivedThreaded += OnLogMessageReceivedThreaded;
            nextPresencePollAt = Time.unscaledTime;
        }

        void Update()
        {
            DrainErrors();
            PollUserPresence();
            CheckForStalledInteraction();
        }

        void OnSharedInputPerformed(SharedXRHand hand, SharedXRControl control)
        {
            // XRI Grip/Trigger value actions can enter the performed phase more than
            // once while a single physical press is held and its analog value moves.
            // Count one telemetry input per press, then wait for canceled/release.
            if (TracksPressState(control) && !pressedTelemetryControls.Add((hand, control)))
                return;
            telemetry?.ObserveInput(hand, control);
        }

        void OnSharedInputCanceled(SharedXRHand hand, SharedXRControl control)
        {
            pressedTelemetryControls.Remove((hand, control));
        }

        static bool TracksPressState(SharedXRControl control)
        {
            return control == SharedXRControl.Trigger ||
                   control == SharedXRControl.Grip ||
                   control == SharedXRControl.PrimaryButton ||
                   control == SharedXRControl.SecondaryButton ||
                   control == SharedXRControl.ThumbstickClick;
        }

        void OnTrainingPauseChanged(bool paused)
        {
            pressedTelemetryControls.Clear();
            telemetry?.TrainingPauseChanged(paused);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                pressedTelemetryControls.Clear();
            SharedTrainingPause.SetExternalInactive(ApplicationPauseInactiveBit, paused);
            telemetry?.AppStateChanged("ApplicationPause", !paused, "UnityApplication");
        }

        void OnApplicationFocus(bool focused)
        {
            // Focus can be lost by the Unity Editor, Quest Link, or the desktop mirror
            // while the headset is still worn and XR interaction is continuing. Keep
            // recording the lifecycle event, but do not subtract that interval from
            // active training time. ApplicationPause and XR user presence remain the
            // authoritative inactivity signals.
            telemetry?.AppStateChanged("ApplicationFocus", focused, "UnityApplication");
        }

        void PollUserPresence()
        {
            if (Time.unscaledTime < nextPresencePollAt)
                return;
            nextPresencePollAt = Time.unscaledTime + PresencePollSeconds;

            var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.userPresence, out var present))
                return;

            var sessionId = telemetry != null && telemetry.IsSessionActive ? telemetry.SessionId : string.Empty;
            var mustReportForSession = !string.IsNullOrEmpty(sessionId) &&
                                       !string.Equals(presenceSessionId, sessionId, StringComparison.Ordinal);
            if (!hasPresence || present != lastPresence || mustReportForSession)
            {
                hasPresence = true;
                lastPresence = present;
                presenceSessionId = sessionId;
                SharedTrainingPause.SetExternalInactive(UserPresenceInactiveBit, !present);
                telemetry?.AppStateChanged("UserPresence", present, "XRRuntime", false);
            }
        }

        void CheckForStalledInteraction()
        {
            if (telemetry == null || !telemetry.IsSessionActive)
            {
                watchedInteractionId = string.Empty;
                stallReported = false;
                return;
            }

            var interactionId = telemetry.ActiveInteractionId;
            if (!string.Equals(watchedInteractionId, interactionId, StringComparison.Ordinal))
            {
                watchedInteractionId = interactionId;
                stallReported = false;
            }
            if (stallReported || string.IsNullOrEmpty(interactionId))
                return;

            var thresholdMs = ResolveStallThresholdMs(interactionId);
            if (telemetry.ActiveInteractionElapsedMs < thresholdMs)
                return;

            stallReported = true;
            telemetry.FlowAnomaly(
                "ProgressStalledSuspected",
                "InteractionCompleted",
                "WaitingForInput",
                telemetry.ActiveInteractionElapsedMs,
                false);
        }

        static long ResolveStallThresholdMs(string interactionId)
        {
            if (interactionId.IndexOf("Gaze", StringComparison.OrdinalIgnoreCase) >= 0 ||
                interactionId.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0)
                return 60000L;
            if (interactionId.IndexOf("Placement", StringComparison.OrdinalIgnoreCase) >= 0 ||
                interactionId.IndexOf("Recovery", StringComparison.OrdinalIgnoreCase) >= 0)
                return 180000L;
            if (interactionId.StartsWith("PPE_", StringComparison.Ordinal))
                return 120000L;
            return 90000L;
        }

        void OnLogMessageReceivedThreaded(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;
            if (Interlocked.Increment(ref queuedLogCount) > MaximumQueuedErrors)
            {
                Interlocked.Decrement(ref queuedLogCount);
                return;
            }
            pendingLogs.Enqueue(new PendingLog(type, condition, stackTrace));
        }

        void DrainErrors()
        {
            for (var i = 0; i < MaximumErrorsPerFrame && pendingLogs.TryDequeue(out var item); i++)
            {
                Interlocked.Decrement(ref queuedLogCount);
                telemetry?.ClientError(
                    NormalizeLogType(item.Type),
                    Sanitize(item.Condition, 4000),
                    Sanitize(item.StackTrace, 16000));
            }
        }

        static string NormalizeLogType(LogType type)
        {
            switch (type)
            {
                case LogType.Exception: return "Exception";
                case LogType.Assert: return "Assert";
                default: return "Error";
            }
        }

        static string Sanitize(string value, int maximumLength)
        {
            var sanitized = value ?? string.Empty;
            sanitized = ReplacePath(sanitized, Application.dataPath, "<APP_DATA>");
            sanitized = ReplacePath(sanitized, Application.persistentDataPath, "<PERSISTENT_DATA>");
            sanitized = ReplacePath(
                sanitized,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "<USER_HOME>");
            if (sanitized.Length > maximumLength)
                sanitized = sanitized.Substring(0, maximumLength);
            return sanitized;
        }

        static string ReplacePath(string value, string path, string replacement)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(path))
                return value;
            return value.Replace(path, replacement);
        }

        void OnDestroy()
        {
            if (!initialized)
                return;
            initialized = false;
            if (sharedInput != null)
            {
                sharedInput.Performed -= OnSharedInputPerformed;
                sharedInput.Canceled -= OnSharedInputCanceled;
            }
            pressedTelemetryControls.Clear();
            SharedTrainingPause.Changed -= OnTrainingPauseChanged;
            Application.logMessageReceivedThreaded -= OnLogMessageReceivedThreaded;
            SharedTrainingPause.SetExternalInactive(ApplicationPauseInactiveBit, false);
            SharedTrainingPause.SetExternalInactive(UserPresenceInactiveBit, false);
            sharedInput = null;
            telemetry = null;
        }

        readonly struct PendingLog
        {
            public PendingLog(LogType type, string condition, string stackTrace)
            {
                Type = type;
                Condition = condition;
                StackTrace = stackTrace;
            }

            public LogType Type { get; }
            public string Condition { get; }
            public string StackTrace { get; }
        }
    }
}
