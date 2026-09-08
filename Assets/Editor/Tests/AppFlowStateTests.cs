#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HClTraining.App;
using HClTraining.SharedXR;
using JH.EntrySafety;
using NUnit.Framework;
using UnityEngine;

public sealed class AppFlowStateTests
{
    GameObject root;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("AppFlowStateTests");
        var existing = PPELoadoutData.Instance;
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);
    }

    [TearDown]
    public void TearDown()
    {
        if (root != null)
            Object.DestroyImmediate(root);
        var existing = PPELoadoutData.Instance;
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);
    }

    [Test]
    public void FullTraining_UsesAuthoredThreeStageOrder()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.Full);

        CollectionAssert.AreEqual(
            new[] { TrainingStage.PPE, TrainingStage.PreEntry, TrainingStage.LeakResponse },
            state.StageSequence);
        Assert.AreEqual(TrainingStage.PPE, state.GetNextStage());

        state.EnterStage(TrainingStage.PPE);
        Assert.IsTrue(state.MarkCurrentStageComplete());
        Assert.AreEqual(TrainingStage.PreEntry, state.GetNextStage());
        state.EnterStage(TrainingStage.PreEntry);
        Assert.IsTrue(state.MarkCurrentStageComplete());
        Assert.AreEqual(TrainingStage.LeakResponse, state.GetNextStage());
        state.EnterStage(TrainingStage.LeakResponse);
        Assert.IsTrue(state.MarkCurrentStageComplete());
        Assert.AreEqual(TrainingStage.Complete, state.GetNextStage());
        Assert.IsTrue(state.HasCompletedRequiredTraining);
        Assert.IsNotNull(state.CompletionResult);
        Assert.AreEqual(
            state.PpeDurationSeconds + state.PreEntryDurationSeconds + state.LeakResponseDurationSeconds,
            state.CompletionResult.TotalDurationSeconds);
    }

    [TestCase(TrainingMode.PPEOnly, TrainingStage.PPE)]
    [TestCase(TrainingMode.PreEntryOnly, TrainingStage.PreEntry)]
    [TestCase(TrainingMode.LeakResponseOnly, TrainingStage.LeakResponse)]
    public void IndividualTraining_CompletesAfterSelectedStage(TrainingMode mode, TrainingStage expected)
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(mode);
        Assert.AreEqual(expected, state.GetNextStage());
        state.EnterStage(expected);
        state.MarkCurrentStageComplete();
        Assert.AreEqual(TrainingStage.Complete, state.GetNextStage());
        Assert.IsTrue(state.HasEndedSelectedTraining);
        Assert.IsTrue(state.HasCompletedRequiredTraining);
        Assert.IsNotNull(state.CompletionResult);
        Assert.AreEqual("SingleStage", state.CompletionResult.ExecutionMode);
        Assert.AreEqual(expected.ToString(), state.CompletionResult.SelectedStage);
        Assert.AreEqual(1, state.CompletionResult.StageResults.Count);
    }

    [Test]
    public void EquipmentAvailability_RequiresAcquisition()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.Full);
        state.SetPpeResult(true, true, new[] { PersistentEquipmentInventory.AbsorbentSock });

        state.SetEquipmentAvailable(PersistentEquipmentInventory.AbsorbentPad, true);
        Assert.IsFalse(state.IsEquipmentAvailable(PersistentEquipmentInventory.AbsorbentPad));

        state.SetEquipmentAvailable(PersistentEquipmentInventory.AbsorbentSock, true);
        Assert.IsTrue(state.IsEquipmentAvailable(PersistentEquipmentInventory.AbsorbentSock));
        state.ClearScenarioAvailability();
        Assert.IsFalse(state.IsEquipmentAvailable(PersistentEquipmentInventory.AbsorbentSock));
    }

    [Test]
    public void IncidentState_ResetsFixedP102RunAndLeakFacts()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.Full);

        Assert.AreEqual(FacilityRunState.Running, state.Incident.FacilityRunState);
        Assert.AreEqual(LeakState.Active, state.Incident.LeakState);

        Assert.IsTrue(state.SetFacilityRunState(FacilityRunState.Stopped));
        Assert.AreEqual(FacilityRunState.Stopped, state.Incident.FacilityRunState);
        state.Begin(TrainingMode.Full);
        Assert.AreEqual(FacilityRunState.Running, state.Incident.FacilityRunState);
        Assert.AreEqual(LeakState.Active, state.Incident.LeakState);
    }

    [Test]
    public void Telemetry_RecordsMeaningfulEventsWithoutServerDependency()
    {
        var state = root.AddComponent<TrainingSessionState>();
        var sink = new RecordingTelemetrySink();
        var telemetry = root.AddComponent<TrainingTelemetryService>();
        telemetry.Initialize(state, sink);
        state.Begin(TrainingMode.Full);

        telemetry.StartSession(TrainingMode.Full);
        state.EnterStage(TrainingStage.PPE);
        state.BeginCurrentStageTiming();
        state.MarkCurrentStageComplete();
        state.EnterStage(TrainingStage.PreEntry);
        state.BeginCurrentStageTiming();
        telemetry.StageEntered(TrainingStage.PreEntry);
        telemetry.StepEntered(TrainingStepId.PreEntry);
        telemetry.StepCompleted(TrainingStepId.PreEntry, attemptCount: 2, rejectedCount: 1);
        state.MarkCurrentStageComplete();
        state.EnterStage(TrainingStage.LeakResponse);
        state.BeginCurrentStageTiming();
        state.MarkCurrentStageComplete();
        telemetry.CompletionScreenShown();
        telemetry.EndSession(TrainingSessionStatus.Completed, TrainingExitReason.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(sink.SessionId));
        Assert.AreEqual(6, sink.Records.Count);
        Assert.AreEqual("SessionStarted", sink.Records[0].event_type);
        Assert.AreEqual("P102", sink.Records[0].facility_id);
        Assert.AreEqual("StepCompleted", sink.Records[3].event_type);
        Assert.AreEqual(2, sink.Records[3].attempt_count);
        Assert.AreEqual(1, sink.Records[3].rejected_count);
        Assert.AreEqual("CompletionScreenShown", sink.Records[4].event_type);
        Assert.AreEqual("SessionCompleted", sink.Records[5].event_type);
        Assert.AreEqual("P102", sink.Records[5].facility_id);
        Assert.IsTrue(sink.Records[5].training_completed);
        Assert.AreEqual(
            sink.Records[5].ppe_duration_seconds +
            sink.Records[5].preentry_duration_seconds +
            sink.Records[5].leakresponse_duration_seconds,
            sink.Records[5].total_duration_seconds);
        Assert.IsFalse(string.IsNullOrWhiteSpace(sink.Records[5].completed_at_utc));
        Assert.IsTrue(telemetry.TryGetCompletionSummary(out var summary));
        Assert.IsTrue(summary.HasTotalDuration);
        Assert.IsTrue(summary.TrainingCompleted);
        Assert.AreEqual(
            summary.PpeDurationSeconds + summary.PreEntryDurationSeconds + summary.LeakResponseDurationSeconds,
            summary.TotalDurationSeconds);
        Assert.IsTrue(summary.HasProcedureErrorCount);
        Assert.AreEqual(1, summary.ProcedureErrorCount);
        CollectionAssert.AreEqual(new[] { "현장 진입 전 확인" }, summary.ReviewItems);
    }

    [Test]
    public void TelemetryContractV1_RecordsInteractionPayloadAndBuildsBatch()
    {
        var state = root.AddComponent<TrainingSessionState>();
        var sink = new RecordingTelemetrySink();
        var telemetry = root.AddComponent<TrainingTelemetryService>();
        telemetry.Initialize(state, sink);
        state.Begin(TrainingMode.PreEntryOnly);
        telemetry.StartSession(TrainingMode.PreEntryOnly);
        state.EnterStage(TrainingStage.PreEntry);
        telemetry.StageEntered(TrainingStage.PreEntry);

        telemetry.InteractionPrompted(
            TrainingStage.PreEntry,
            "PreEntry_StopPump",
            1,
            "Right",
            "Trigger",
            "VoiceAndUI");
        telemetry.ObserveInput(SharedXRHand.Left, SharedXRControl.SecondaryButton);
        telemetry.InteractionCompleted("PreEntry_StopPump", "Right", "Trigger");

        Assert.IsTrue(sink.Records.All(record => record.schema_version == "1.0"));
        Assert.AreEqual(
            sink.Records.Count,
            sink.Records.Select(record => record.event_id).Distinct().Count());
        var prompted = sink.Records.Single(record => record.event_type == "InteractionPrompted");
        StringAssert.Contains("\"expected_order_index\":1", prompted.payload_json);
        StringAssert.Contains("\"expected_control\":\"Trigger\"", prompted.payload_json);
        var rejected = sink.Records.Single(record =>
            record.event_type == "InteractionAttempted" && record.result == "Rejected");
        StringAssert.Contains("\"rejection_reason\":\"WrongHand\"", rejected.payload_json);
        var completed = sink.Records.Single(record => record.event_type == "InteractionCompleted");
        StringAssert.Contains("\"actual_completion_order_index\":1", completed.payload_json);
        StringAssert.Contains("\"sequence_result\":\"Repeated\"", completed.payload_json);

        var eventJson = TrainingTelemetryJson.SerializeRecord(prompted);
        StringAssert.Contains("\"payload\":{", eventJson);
        var batchJson = TrainingTelemetryJson.SerializeBatch(
            "12345678batch",
            sink.SessionId,
            "2026-09-03T00:00:00.000Z",
            sink.Records);
        StringAssert.StartsWith("{\"schema_version\":\"1.0\"", batchJson);
        StringAssert.Contains("\"events\":[", batchJson);
    }

    [Test]
    public void ApplicationFocusLoss_IsRecordedWithoutStoppingVrTrainingClock()
    {
        var state = root.AddComponent<TrainingSessionState>();
        var sink = new RecordingTelemetrySink();
        var telemetry = root.AddComponent<TrainingTelemetryService>();
        telemetry.Initialize(state, sink);
        state.Begin(TrainingMode.LeakResponseOnly);
        telemetry.StartSession(TrainingMode.LeakResponseOnly);

        var monitor = root.AddComponent<TrainingTelemetryRuntimeMonitor>();
        monitor.Initialize(telemetry, null);
        var callback = typeof(TrainingTelemetryRuntimeMonitor).GetMethod(
            "OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(callback, Is.Not.Null);

        callback.Invoke(monitor, new object[] { false });

        Assert.That(SharedTrainingPause.IsClockInactive, Is.False,
            "Desktop mirror/Editor focus loss must not subtract time while XR is active.");
        Assert.That(sink.Records.Any(record =>
            record.event_type == "AppStateChanged" &&
            record.payload_json.Contains("ApplicationFocus")), Is.True);
    }

    [Test]
    public void TelemetryRuntimeMonitor_CountsAnalogPerformedChangesOncePerPhysicalPress()
    {
        var state = root.AddComponent<TrainingSessionState>();
        var sink = new RecordingTelemetrySink();
        var telemetry = root.AddComponent<TrainingTelemetryService>();
        telemetry.Initialize(state, sink);
        state.Begin(TrainingMode.PreEntryOnly);
        telemetry.StartSession(TrainingMode.PreEntryOnly);
        state.EnterStage(TrainingStage.PreEntry);
        telemetry.StageEntered(TrainingStage.PreEntry);
        telemetry.InteractionPrompted(
            TrainingStage.PreEntry,
            "PreEntry_StopPump",
            1,
            "Right",
            "Trigger",
            "VoiceAndUI");

        var monitor = root.AddComponent<TrainingTelemetryRuntimeMonitor>();
        monitor.Initialize(telemetry, null);
        var performed = typeof(TrainingTelemetryRuntimeMonitor).GetMethod(
            "OnSharedInputPerformed", BindingFlags.Instance | BindingFlags.NonPublic);
        var canceled = typeof(TrainingTelemetryRuntimeMonitor).GetMethod(
            "OnSharedInputCanceled", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(performed, Is.Not.Null);
        Assert.That(canceled, Is.Not.Null);

        performed.Invoke(monitor, new object[] { SharedXRHand.Left, SharedXRControl.Grip });
        performed.Invoke(monitor, new object[] { SharedXRHand.Left, SharedXRControl.Grip });

        Assert.AreEqual(1, sink.Records.Count(record =>
            record.event_type == "InteractionAttempted" && record.result == "Rejected"),
            "Analog value changes during one held Grip must not inflate wrong-input totals.");

        canceled.Invoke(monitor, new object[] { SharedXRHand.Left, SharedXRControl.Grip });
        performed.Invoke(monitor, new object[] { SharedXRHand.Left, SharedXRControl.Grip });

        Assert.AreEqual(1, sink.Records.Count(record =>
            record.event_type == "InteractionAttempted" && record.result == "Rejected"),
            "Repeated use of the same wrong key in one interaction must not inflate dashboard rows.");

        telemetry.InteractionCompleted("PreEntry_StopPump", "Right", "Trigger");
        var completed = sink.Records.Single(record => record.event_type == "InteractionCompleted");
        StringAssert.Contains("\"wrong_input_press_count\":2", completed.payload_json);
        StringAssert.Contains("\"wrong_input_distinct_count\":1", completed.payload_json);
        StringAssert.Contains("\"wrong_input_repeat_count\":1", completed.payload_json);
        StringAssert.Contains("\"wrong_input_pattern\":\"RepeatedSameInput\"", completed.payload_json);
    }

    [Test]
    public void InteractionWrongInputs_AreContextualBoundedAndUseQuestKeyLabels()
    {
        var state = root.AddComponent<TrainingSessionState>();
        var sink = new RecordingTelemetrySink();
        var telemetry = root.AddComponent<TrainingTelemetryService>();
        telemetry.Initialize(state, sink);
        state.Begin(TrainingMode.PreEntryOnly);
        telemetry.StartSession(TrainingMode.PreEntryOnly);
        state.EnterStage(TrainingStage.PreEntry);
        telemetry.StageEntered(TrainingStage.PreEntry);
        telemetry.InteractionPrompted(
            TrainingStage.PreEntry,
            "PreEntry_MoveToPT",
            1,
            "Right",
            "PrimaryButton",
            "VoiceAndUI");

        telemetry.ObserveInput(SharedXRHand.Left, SharedXRControl.Grip);
        telemetry.ObserveInput(SharedXRHand.Left, SharedXRControl.Grip);
        telemetry.ObserveInput(SharedXRHand.Right, SharedXRControl.SecondaryButton);
        telemetry.ObserveInput(SharedXRHand.Left, SharedXRControl.PrimaryButton);
        telemetry.ObserveInput(SharedXRHand.Left, SharedXRControl.SecondaryButton);
        telemetry.ObserveInput(SharedXRHand.Right, SharedXRControl.Grip);
        telemetry.ObserveInput(SharedXRHand.Left, SharedXRControl.Trigger);
        telemetry.ObserveInput(SharedXRHand.Right, SharedXRControl.Trigger);
        telemetry.ObserveInput(SharedXRHand.Left, SharedXRControl.ThumbstickClick);
        telemetry.InteractionCompleted("PreEntry_MoveToPT", "Right", "PrimaryButton");

        var rejected = sink.Records.Where(record =>
            record.event_type == "InteractionAttempted" && record.result == "Rejected").ToArray();
        Assert.AreEqual(7, rejected.Length,
            "The duplicate Grip and excluded stick click must not create extra attempt rows.");
        Assert.IsTrue(rejected.Any(record => record.payload_json.Contains("\"actual_key\":\"B\"")));
        Assert.IsTrue(rejected.Any(record => record.payload_json.Contains("\"actual_key\":\"X\"")));
        Assert.IsTrue(rejected.Any(record => record.payload_json.Contains("\"actual_key\":\"Y\"")));

        var completed = sink.Records.Single(record => record.event_type == "InteractionCompleted");
        StringAssert.Contains("\"input_analytics_version\":2", completed.payload_json);
        StringAssert.Contains("\"expected_key\":\"A\"", completed.payload_json);
        StringAssert.Contains("\"had_wrong_input\":true", completed.payload_json);
        StringAssert.Contains("\"wrong_input_press_count\":8", completed.payload_json);
        StringAssert.Contains("\"wrong_input_distinct_count\":7", completed.payload_json);
        StringAssert.Contains("\"wrong_input_repeat_count\":1", completed.payload_json);
        StringAssert.Contains("\"wrong_input_pattern\":\"RapidMultiButtonInput\"", completed.payload_json);
        StringAssert.Contains("\"wrong_input_signal_quality\":\"Low\"", completed.payload_json);
        StringAssert.Contains("\"first_wrong_key\":\"Grip\"", completed.payload_json);
        StringAssert.Contains("\"key\":\"B\"", completed.payload_json);
        StringAssert.Contains("\"key\":\"X\"", completed.payload_json);
        StringAssert.Contains("\"key\":\"Y\"", completed.payload_json);
    }

    [Test]
    public void InteractionCompletion_DoesNotReportLastWrongKeyAsSuccessfulInput()
    {
        var state = root.AddComponent<TrainingSessionState>();
        var sink = new RecordingTelemetrySink();
        var telemetry = root.AddComponent<TrainingTelemetryService>();
        telemetry.Initialize(state, sink);
        state.Begin(TrainingMode.PPEOnly);
        telemetry.StartSession(TrainingMode.PPEOnly);
        state.EnterStage(TrainingStage.PPE);
        telemetry.StageEntered(TrainingStage.PPE);
        telemetry.InteractionPrompted(
            TrainingStage.PPE,
            "PPE_Respirator",
            1,
            "Either",
            "Grip",
            "VoiceAndUI");

        telemetry.ObserveInput(SharedXRHand.Right, SharedXRControl.SecondaryButton);
        telemetry.InteractionCompleted("PPE_Respirator");

        var completed = sink.Records.Single(record => record.event_type == "InteractionCompleted");
        StringAssert.Contains("\"actual_hand\":\"Either\"", completed.payload_json);
        StringAssert.Contains("\"actual_control\":\"Grip\"", completed.payload_json);
        StringAssert.Contains("\"actual_key\":\"Grip\"", completed.payload_json);
        StringAssert.Contains("\"first_wrong_key\":\"B\"", completed.payload_json);
    }

    [Test]
    public void PreEntryDeliberateLeftGrip_DrivesPickupAndStoreCommands()
    {
        var input = root.AddComponent<EntrySafetyInputAdapter>();
        var pickupCount = 0;
        var storeCount = 0;
        input.EquipDetectorPressed += () => pickupCount++;
        input.StoreDetectorPressed += () => storeCount++;

        Assert.That(input.SimulateLeftGripForIntegrationSmoke(0f, 0.13f), Is.False);
        Assert.That(input.SimulateLeftGripForIntegrationSmoke(1f, 0f), Is.True);
        Assert.That(pickupCount, Is.EqualTo(1));
        Assert.That(storeCount, Is.EqualTo(1));
    }

    [Test]
    public void CompletionSummary_HidesMetricsThatWereNotRecorded()
    {
        var state = root.AddComponent<TrainingSessionState>();
        var sink = new RecordingTelemetrySink();
        var telemetry = root.AddComponent<TrainingTelemetryService>();
        telemetry.Initialize(state, sink);
        state.Begin(TrainingMode.Full);

        telemetry.StartSession(TrainingMode.Full);
        telemetry.StepCompleted(TrainingStepId.PPE);
        CompleteRequiredStages(state);
        telemetry.EndSession(TrainingSessionStatus.Completed, TrainingExitReason.None);

        Assert.IsTrue(telemetry.TryGetCompletionSummary(out var summary));
        Assert.IsTrue(summary.HasTotalDuration);
        Assert.IsFalse(summary.HasProcedureErrorCount);
        Assert.IsEmpty(summary.ReviewItems);
    }

    [Test]
    public void SingleStageTelemetry_CompletesWithoutUnselectedStages()
    {
        var state = root.AddComponent<TrainingSessionState>();
        var sink = new RecordingTelemetrySink();
        var telemetry = root.AddComponent<TrainingTelemetryService>();
        telemetry.Initialize(state, sink);
        state.Begin(TrainingMode.LeakResponseOnly);

        telemetry.StartSession(TrainingMode.LeakResponseOnly);
        state.EnterStage(TrainingStage.LeakResponse);
        state.BeginCurrentStageTiming();
        state.MarkCurrentStageComplete();
        telemetry.EndSession(TrainingSessionStatus.Completed, TrainingExitReason.None);

        Assert.AreEqual(TrainingSessionStatus.Completed, telemetry.Status);
        Assert.IsTrue(telemetry.TryGetCompletionSummary(out var summary));
        Assert.IsTrue(summary.TrainingCompleted);
        Assert.IsTrue(sink.Records.Any(record => record.event_type == "SessionCompleted"));
    }

    [Test]
    public void SingleStage_EndAndCompletionAreSeparatedByRequiredItems()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.PPEOnly);
        state.EnterStage(TrainingStage.PPE);
        state.RegisterCheckItem(TrainingStage.PPE, "ppe.gloves", "장갑 착용");
        state.RegisterCheckItem(TrainingStage.PPE, "ppe.suit", "보호복 착용");
        state.StartCheckItem(TrainingStage.PPE, "ppe.gloves", "장갑 착용");
        state.CompleteCheckItem(TrainingStage.PPE, "ppe.gloves", "장갑 착용");

        Assert.IsTrue(state.EndCurrentStage());
        Assert.IsTrue(state.HasEndedSelectedTraining);
        Assert.IsFalse(state.HasCompletedRequiredTraining);
        Assert.IsNotNull(state.CompletionResult);
        Assert.IsFalse(state.CompletionResult.TrainingCompleted);
        Assert.AreEqual(string.Empty, state.CompletionResult.CompletedAtUtc);

        var result = state.GetStageResult(TrainingStage.PPE);
        Assert.IsTrue(result.StageEnded);
        Assert.IsFalse(result.StageCompleted);
        CollectionAssert.AreEqual(new[] { "보호복 착용" }, result.MissingRequiredItems);
    }

    [Test]
    public void QuickFeedback_NoProblemIsExclusiveAndUsesSelectedStage()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.PreEntryOnly);
        state.EnterStage(TrainingStage.PreEntry);
        state.EndCurrentStage();

        var feedback = state.SaveQuickFeedback(
            TrainingStage.None,
            new[] { "interaction_difficult", "no_problem", "runtime_error" });

        Assert.IsNotNull(feedback);
        Assert.AreEqual("PreEntry", feedback.TargetStage);
        CollectionAssert.AreEqual(new[] { "no_problem" }, feedback.QuickFeedbackIds);
        Assert.AreEqual(state.SessionId, feedback.SessionId);
    }

    [Test]
    public void StageEndedTelemetry_StoresIncompleteStageWithoutBlockingResultSession()
    {
        var state = root.AddComponent<TrainingSessionState>();
        var sink = new RecordingTelemetrySink();
        var telemetry = root.AddComponent<TrainingTelemetryService>();
        telemetry.Initialize(state, sink);
        state.Begin(TrainingMode.PPEOnly);
        telemetry.StartSession(TrainingMode.PPEOnly);
        state.EnterStage(TrainingStage.PPE);
        state.RegisterCheckItem(TrainingStage.PPE, "ppe.required", "필수 PPE");
        state.EndCurrentStage();

        var stageResult = state.GetStageResult(TrainingStage.PPE);
        telemetry.StageEnded(TrainingStage.PPE, stageResult.StageCompleted, stageResult);
        telemetry.EndSession(TrainingSessionStatus.Completed, TrainingExitReason.None);

        var ended = sink.Records.Single(record => record.event_type == "StageEnded");
        Assert.IsTrue(ended.stage_ended);
        Assert.IsFalse(ended.stage_completed);
        Assert.AreEqual("SingleStage", ended.execution_mode);
        Assert.AreEqual("PPE", ended.selected_stage);
        Assert.IsFalse(string.IsNullOrWhiteSpace(ended.stage_result_json));

        var sessionResult = sink.Records.Single(record => record.event_type == "SessionCompleted");
        Assert.IsFalse(sessionResult.training_completed);
        Assert.IsFalse(string.IsNullOrWhiteSpace(sessionResult.ended_at_utc));
        Assert.IsFalse(string.IsNullOrWhiteSpace(sessionResult.training_result_json));
    }

    [Test]
    public void InterruptedPpeTelemetry_EmitsPartialPpeStageBeforeSessionTerminalEvent()
    {
        var state = root.AddComponent<TrainingSessionState>();
        var sink = new RecordingTelemetrySink();
        var telemetry = root.AddComponent<TrainingTelemetryService>();
        telemetry.Initialize(state, sink);
        state.Begin(TrainingMode.PPEOnly);
        telemetry.StartSession(TrainingMode.PPEOnly);
        state.EnterStage(TrainingStage.PPE);
        state.BeginCurrentStageTiming();
        telemetry.StageEntered(TrainingStage.PPE);

        telemetry.EndSession(
            TrainingSessionStatus.Interrupted,
            TrainingExitReason.UserRequested,
            "test_interrupt");

        var stageEndedIndex = sink.Records.FindIndex(record => record.event_type == "StageEnded");
        var terminalIndex = sink.Records.FindIndex(record => record.event_type == "SessionInterrupted");
        Assert.GreaterOrEqual(stageEndedIndex, 0);
        Assert.Greater(terminalIndex, stageEndedIndex);
        Assert.AreEqual("PPE", sink.Records[stageEndedIndex].stage_id);
        StringAssert.Contains("\"completed\":false", sink.Records[stageEndedIndex].payload_json);
        StringAssert.Contains("\"stage_active_duration_ms\":", sink.Records[stageEndedIndex].payload_json);
    }

    [Test]
    public void InteractionPrompted_StoresPauseAwareTransitionDuration()
    {
        var state = root.AddComponent<TrainingSessionState>();
        var sink = new RecordingTelemetrySink();
        var telemetry = root.AddComponent<TrainingTelemetryService>();
        telemetry.Initialize(state, sink);
        state.Begin(TrainingMode.PPEOnly);
        telemetry.StartSession(TrainingMode.PPEOnly);
        state.EnterStage(TrainingStage.PPE);
        telemetry.StageEntered(TrainingStage.PPE);

        telemetry.InteractionPrompted(
            TrainingStage.PPE, "PPE_Respirator", 1, "Either", "Grip", "VoiceAndUI");
        telemetry.InteractionCompleted("PPE_Respirator", "Right", "Grip");
        telemetry.InteractionPrompted(
            TrainingStage.PPE, "PPE_FaceProtection", 2, "Either", "Grip", "VoiceAndUI");

        var prompted = sink.Records.Last(record => record.event_type == "InteractionPrompted");
        StringAssert.Contains("\"previous_interaction_id\":\"PPE_Respirator\"", prompted.payload_json);
        StringAssert.Contains("\"transition_active_duration_ms\":", prompted.payload_json);
    }

    [Test]
    public void NewTrainingSession_ResetsStageTimesCompletionAndResult()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.Full);
        CompleteRequiredStages(state);

        Assert.IsTrue(state.HasCompletedRequiredTraining);
        Assert.IsNotNull(state.CompletionResult);

        state.Begin(TrainingMode.Full);

        Assert.IsFalse(state.HasCompletedRequiredTraining);
        Assert.IsNull(state.CompletionResult);
        Assert.IsEmpty(state.CompletedStages);
        Assert.AreEqual(0d, state.PpeDurationSeconds);
        Assert.AreEqual(0d, state.PreEntryDurationSeconds);
        Assert.AreEqual(0d, state.LeakResponseDurationSeconds);
        Assert.AreEqual(0d, state.TotalDurationSeconds);
    }

    [Test]
    public void SharedBag_UsesFixedStageAnchorAndRestoresLegacyPlacement()
    {
        var xrRoot = CreatePath(root.transform, "Common XR Origin");
        var bodyAnchor = CreatePath(xrRoot, "PlayerBodyAnchor");
        var bag = CreatePath(bodyAnchor, "ResponseBag_Wearable");
        CreatePath(xrRoot, "Camera Offset/Left Controller");
        bag.localPosition = new Vector3(0.1f, 0.2f, 0.3f);

        var stageAnchor = CreatePath(root.transform, SharedPlayerEquipment.StageBagAnchorName);
        stageAnchor.SetPositionAndRotation(new Vector3(4f, 1f, -2f), Quaternion.Euler(0f, 90f, 0f));

        var equipment = root.AddComponent<SharedPlayerEquipment>();
        equipment.Initialize(xrRoot);

        Assert.IsTrue(equipment.TryPlaceAtStageAnchor(root.scene));
        Assert.IsTrue(equipment.IsUsingStageAnchor);
        Assert.Less(Vector3.Distance(stageAnchor.position, bag.position), 0.001f);
        Assert.Less(Quaternion.Angle(stageAnchor.rotation, bag.rotation), 0.01f);

        equipment.RestoreBodyRelativePlacement();
        Assert.IsFalse(equipment.IsUsingStageAnchor);
        Assert.AreSame(bodyAnchor, bag.parent);
        Assert.AreEqual(new Vector3(0.1f, 0.2f, 0.3f), bag.localPosition);
    }

    [Test]
    public void StandaloneFieldPreset_SeedsGlovesAndResponseEquipment()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.LeakResponseOnly);
        var loadout = new GameObject(nameof(PPELoadoutData)).AddComponent<PPELoadoutData>();
        typeof(PPELoadoutData).GetProperty(
            nameof(PPELoadoutData.Instance),
            BindingFlags.Static | BindingFlags.Public)?.SetValue(null, loadout);
        PPELoadoutPresets.BeginFieldStandaloneWithOfficialPreset();

        var inventory = root.AddComponent<PersistentEquipmentInventory>();
        inventory.Initialize(state, null);
        inventory.SyncFromLoadout();

        Assert.IsTrue(state.LeftChemicalGloveWorn);
        Assert.IsTrue(state.RightChemicalGloveWorn);
        Assert.IsTrue(state.IsEquipmentAcquired(PersistentEquipmentInventory.AbsorbentSock));
        Assert.IsTrue(state.IsEquipmentAcquired(PersistentEquipmentInventory.AbsorbentPad));
        Assert.IsTrue(state.IsEquipmentAcquired(PersistentEquipmentInventory.WasteContainer));
        Assert.IsTrue(state.IsEquipmentAcquired(PersistentEquipmentInventory.RecoveryTongs));
    }

    [Test]
    public void Inventory_DeployRecallAndRecover_ReusesOneAuthoredInstance()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.Full);
        state.SetPpeResult(
            true,
            true,
            new[] { PersistentEquipmentInventory.AbsorbentSock });

        var inventory = root.AddComponent<PersistentEquipmentInventory>();
        inventory.Initialize(state, root.transform);

        var authoredInstance = new GameObject("INT_AbsorbentSock_Straight_01");
        authoredInstance.transform.SetParent(root.transform, false);
        authoredInstance.transform.localPosition = new Vector3(1f, 2f, 3f);
        var entityId = authoredInstance.GetEntityId();

        inventory.BeginStage(root.scene, TrainingStage.LeakResponse);
        Assert.IsFalse(authoredInstance.activeSelf);
        Assert.IsTrue(inventory.SetAvailable(PersistentEquipmentInventory.AbsorbentSock, true));

        Assert.IsTrue(inventory.DeployOrRecall(PersistentEquipmentInventory.AbsorbentSock));
        Assert.IsTrue(inventory.IsDeployed(PersistentEquipmentInventory.AbsorbentSock));
        Assert.AreEqual(entityId, authoredInstance.GetEntityId());

        Assert.IsTrue(inventory.DeployOrRecall(PersistentEquipmentInventory.AbsorbentSock));
        Assert.IsFalse(inventory.IsDeployed(PersistentEquipmentInventory.AbsorbentSock));
        Assert.IsFalse(authoredInstance.activeSelf);

        authoredInstance.transform.localPosition = Vector3.zero;
        Assert.IsTrue(inventory.Recover(PersistentEquipmentInventory.AbsorbentSock));
        Assert.IsTrue(authoredInstance.activeSelf);
        Assert.AreEqual(new Vector3(1f, 2f, 3f), authoredInstance.transform.localPosition);
        Assert.AreEqual(
            1,
            root.GetComponentsInChildren<Transform>(true)
                .Count(item => item.name == "INT_AbsorbentSock_Straight_01"));
    }

    [Test]
    public void TransitionInputLock_RejectsDoubleLock()
    {
        var inputLock = root.AddComponent<TransitionInputLock>();
        inputLock.Initialize(root.transform);
        Assert.IsTrue(inputLock.TryLock());
        Assert.IsFalse(inputLock.TryLock());
        inputLock.Unlock();
        Assert.IsFalse(inputLock.IsLocked);
    }

    [Test]
    public void ScenePaths_KeepOfficialStageAssets()
    {
        Assert.AreEqual("Assets/Scenes/04_PPE.unity", SceneFlowController.PpeScenePath);
        Assert.AreEqual("Assets/Scenes/05_PreEntry.unity", SceneFlowController.PreEntryScenePath);
        Assert.AreEqual("Assets/Scenes/06_LeakResponse.unity", SceneFlowController.LeakResponseScenePath);
        Assert.IsFalse(new[]
        {
            SceneFlowController.AppScenePath,
            SceneFlowController.StartScenePath,
            SceneFlowController.CompleteScenePath,
        }.Any(string.IsNullOrWhiteSpace));
    }

    [Test]
    public void CommonXrPolicy_UsesPpeBothHandsWithoutRaysAndKeepsOtherStageControllers()
    {
        var xrRoot = CreatePath(root.transform, "Common XR Origin");
        var leftVisuals = CreatePath(xrRoot, "Camera Offset/Left Controller/Visuals");
        var leftModel = CreatePath(leftVisuals, "XR Controller Left");
        var leftHand = CreatePath(leftVisuals, "PPE_DefaultHand_Left");
        var leftGlove = CreatePath(leftVisuals, "PPE_VrHand_Left_Wearable");
        var leftLegacyVisual = CreatePath(xrRoot, "Camera Offset/Left Controller/Visual");
        var leftRay = CreatePath(xrRoot, "Camera Offset/Left Controller/Ray Interactor");
        var leftNearFar = CreatePath(xrRoot, "Camera Offset/Left Controller/Left_NearFarInteractor");
        CreatePath(leftNearFar, "LineVisual");

        var rightVisuals = CreatePath(xrRoot, "Camera Offset/Right Controller/Visuals");
        var rightModel = CreatePath(rightVisuals, "XR Controller Right");
        var rightHand = CreatePath(rightVisuals, "PPE_DefaultHand_Right");
        var rightGlove = CreatePath(rightVisuals, "PPE_VrHand_Right_Wearable");
        var rightLegacyVisual = CreatePath(xrRoot, "Camera Offset/Right Controller/Visual");
        var rightRay = CreatePath(xrRoot, "Camera Offset/Right Controller/Ray Interactor");
        var rightNearFar = CreatePath(xrRoot, "Camera Offset/Right Controller/Right_NearFarInteractor");
        CreatePath(rightNearFar, "LineVisual");

        leftVisuals.gameObject.SetActive(false);
        leftModel.gameObject.SetActive(false);
        rightVisuals.gameObject.SetActive(false);
        rightModel.gameObject.SetActive(false);
        leftLegacyVisual.gameObject.SetActive(true);
        rightLegacyVisual.gameObject.SetActive(true);
        leftRay.localRotation = Quaternion.Euler(70f, 0f, 0f);
        rightRay.localRotation = Quaternion.Euler(70f, 0f, 0f);

        var policy = typeof(StageXRProfileController).GetMethod(
            "ApplyCommonControllerPolicy",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(policy);

        var ppeRequirements = CreateRequirements(
            far: false,
            near: true,
            ui: true,
            grab: true,
            handVisualMode: StageXRHandVisualMode.BothHands);
        var preEntryRequirements = CreateRequirements(far: false, near: false, ui: false, grab: false);
        var leakRequirements = CreateRequirements(far: true, near: true, ui: true, grab: true);

        policy.Invoke(null, new object[] { xrRoot, ppeRequirements });
        AssertCommonControllerState(
            leftVisuals, leftModel, leftHand, leftLegacyVisual, leftRay, leftNearFar,
            expectedModel: false, expectedHand: true, expectedRay: false, expectedNearFar: true);
        AssertCommonControllerState(
            rightVisuals, rightModel, rightHand, rightLegacyVisual, rightRay, rightNearFar,
            expectedModel: false, expectedHand: true, expectedRay: false, expectedNearFar: true);
        Assert.IsFalse(leftGlove.gameObject.activeSelf);
        Assert.IsFalse(rightGlove.gameObject.activeSelf);

        policy.Invoke(null, new object[] { xrRoot, preEntryRequirements });
        AssertCommonControllerState(leftVisuals, leftModel, leftHand, leftLegacyVisual, leftRay, leftNearFar, true, false, false, false);
        AssertCommonControllerState(rightVisuals, rightModel, rightHand, rightLegacyVisual, rightRay, rightNearFar, true, false, false, false);
        Assert.IsFalse(leftGlove.gameObject.activeSelf);
        Assert.IsFalse(rightGlove.gameObject.activeSelf);

        policy.Invoke(null, new object[] { xrRoot, leakRequirements });
        AssertCommonControllerState(leftVisuals, leftModel, leftHand, leftLegacyVisual, leftRay, leftNearFar, true, false, false, true);
        AssertCommonControllerState(rightVisuals, rightModel, rightHand, rightLegacyVisual, rightRay, rightNearFar, true, false, false, true);
        Assert.IsFalse(leftGlove.gameObject.activeSelf);
        Assert.IsFalse(rightGlove.gameObject.activeSelf);

        Object.DestroyImmediate(ppeRequirements);
        Object.DestroyImmediate(preEntryRequirements);
        Object.DestroyImmediate(leakRequirements);
    }

    [Test]
    public void PpeStatePostProcessing_KeepsBothHandsVisibleAndControllerModelsHidden()
    {
        var session = root.AddComponent<TrainingSessionState>();
        session.Begin(TrainingMode.PPEOnly);
        session.EnterStage(TrainingStage.PPE);

        var xrRoot = CreatePath(root.transform, "Common XR Origin");
        var leftVisuals = CreatePath(xrRoot, "Camera Offset/Left Controller/Visuals");
        var leftModel = CreatePath(leftVisuals, "XR Controller Left").gameObject;
        var leftHand = CreatePath(leftVisuals, "PPE_DefaultHand_Left").gameObject;
        var leftGlove = CreatePath(leftVisuals, "PPE_VrHand_Left_Wearable").gameObject;
        var rightVisuals = CreatePath(xrRoot, "Camera Offset/Right Controller/Visuals");
        var rightModel = CreatePath(rightVisuals, "XR Controller Right").gameObject;
        var rightHand = CreatePath(rightVisuals, "PPE_DefaultHand_Right").gameObject;
        var rightGlove = CreatePath(rightVisuals, "PPE_VrHand_Right_Wearable").gameObject;

        var wearableHost = CreatePath(xrRoot, "ChemicalGloves_WearableHost").gameObject;
        var wearable = wearableHost.AddComponent<ChemicalGlovesWearableVisual>();
        wearable.EditorWire(
            leftHand, rightHand, leftGlove, rightGlove, new[] { leftModel, rightModel });

        var ppeState = root.AddComponent<PPEStateController>();
        ppeState.Initialize(session, null, xrRoot);
        ppeState.ApplyPersistentState();

        Assert.IsTrue(leftHand.activeSelf);
        Assert.IsTrue(rightHand.activeSelf);
        Assert.IsFalse(leftGlove.activeSelf);
        Assert.IsFalse(rightGlove.activeSelf);
        Assert.IsFalse(leftModel.activeSelf);
        Assert.IsFalse(rightModel.activeSelf);

        wearable.ApplyPpeRoomState(true, true);
        Assert.IsTrue(leftHand.activeSelf,
            "The tracked hand roots must stay active while their renderers are replaced by gloves.");
        Assert.IsTrue(rightHand.activeSelf,
            "The tracked hand roots must stay active while their renderers are replaced by gloves.");
        Assert.IsTrue(leftGlove.activeSelf);
        Assert.IsTrue(rightGlove.activeSelf);
    }

    [TestCase("<XRController>{RightHand}/gripPressed", "Right")]
    [TestCase("<OculusTouchController>{LeftHand}/gripPressed", "Left")]
    [TestCase("unassigned-device", "Unknown")]
    public void ConfirmRouter_ResolvesTheHandThatPressedGrip(string bindingPath, string expectedHand)
    {
        var resolver = typeof(XRInteractionConfirmRouter).GetMethod(
            "ResolveHandText",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(resolver);
        var resolved = resolver.Invoke(null, new object[] { bindingPath });
        Assert.AreEqual(expectedHand, resolved.ToString());
    }

    static void CompleteRequiredStages(TrainingSessionState state)
    {
        var stages = new[]
        {
            TrainingStage.PPE,
            TrainingStage.PreEntry,
            TrainingStage.LeakResponse,
        };

        for (var i = 0; i < stages.Length; i++)
        {
            state.EnterStage(stages[i]);
            state.BeginCurrentStageTiming();
            Assert.IsTrue(state.MarkCurrentStageComplete());
        }
    }

    static Transform CreatePath(Transform parent, string path)
    {
        var current = parent;
        foreach (var segment in path.Split('/'))
        {
            var child = current.Find(segment);
            if (child == null)
            {
                child = new GameObject(segment).transform;
                child.SetParent(current, false);
            }
            current = child;
        }
        return current;
    }

    static StageXRRequirements CreateRequirements(
        bool far,
        bool near,
        bool ui,
        bool grab,
        StageXRHandVisualMode handVisualMode = StageXRHandVisualMode.Controllers)
    {
        var requirements = ScriptableObject.CreateInstance<StageXRRequirements>();
        requirements.EditorConfigure(string.Empty, Vector3.zero, Quaternion.identity,
            far, near, ui, grab, useHaptics: true, visualMode: handVisualMode);
        return requirements;
    }

    static void AssertCommonControllerState(
        Transform visuals,
        Transform model,
        Transform hand,
        Transform legacyVisual,
        Transform ray,
        Transform nearFar,
        bool expectedModel,
        bool expectedHand,
        bool expectedRay,
        bool expectedNearFar)
    {
        Assert.IsTrue(visuals.gameObject.activeSelf);
        Assert.AreEqual(expectedModel, model.gameObject.activeSelf);
        Assert.AreEqual(expectedHand, hand.gameObject.activeSelf);
        Assert.IsFalse(legacyVisual.gameObject.activeSelf);
        Assert.AreEqual(expectedRay, ray.gameObject.activeSelf);
        Assert.Less(Quaternion.Angle(Quaternion.Euler(70f, 0f, 0f), ray.localRotation), 0.01f);
        Assert.AreEqual(expectedNearFar, nearFar.gameObject.activeSelf);
    }

    sealed class RecordingTelemetrySink : ITrainingTelemetrySink
    {
        public string SessionId { get; private set; }
        public List<TrainingTelemetryRecord> Records { get; } =
            new List<TrainingTelemetryRecord>();

        public bool TryBeginSession(string sessionId, out string error)
        {
            SessionId = sessionId;
            error = null;
            return true;
        }

        public bool TryWrite(TrainingTelemetryRecord record, out string error)
        {
            Records.Add(record);
            error = null;
            return true;
        }
    }
}
#endif
