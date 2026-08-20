using System;
using System.Collections;
using System.Collections.Generic;
using HClTraining.SharedXR;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;

namespace JH.EntrySafety
{
    /// <summary>Connects the Unit 1 and Unit 2 core to the initial-wind and initial-HCl Scene flow.</summary>
    public sealed class EntrySafetyScenarioController : MonoBehaviour
    {
        private const float DefaultGazeHoldSeconds = 3f;
        private const float PtPanelGazeHoldSeconds = 2f;
        private const string Goal2SafetyCheckCompleteText =
            "염화수소 농도와 풍향, {0} 정지와 압력 감소까지 확인했습니다.";
        private const string Goal2CheckAccessRestrictionText =
            "빨간 접근 금지 표시가 해제되면 오른손 A를 눌러 누출 대응 구역으로 이동하십시오.";

        [SerializeField] private EntrySafetyCameraGazeAdapter _gazeAdapter;
        [SerializeField] private EntrySafetyInputAdapter _inputAdapter;
        [SerializeField] private EntrySafetyGuidanceView _guidanceView;
        [SerializeField] private EntrySafetyStepVisuals _stepVisuals;
        [SerializeField] private EntrySafetyEquipmentTransferZone _equipmentTransferZone;
        [SerializeField] private EntrySafetyHclDetectorController _detectorController;
        [SerializeField] private EntrySafetyWindChangeController _windChangeController;
        [SerializeField] private EntrySafetyGazeTargetBehaviour _windsockGazeTarget;
        [SerializeField] private EntrySafetyGazeProgressVisual _windsockGazeProgress;
        [SerializeField] private EntrySafetyTtsRequestHook _ttsRequestHook;
        [SerializeField] private EntrySafetyGoal01TtsPlayer _goal01TtsPlayer;
        [SerializeField] private EntrySafetyGoal02TtsPlayer _goal02TtsPlayer;
        [SerializeField] private EntrySafetyGoal2PtPanelController _goal2PtPanel;
        [SerializeField] private EntrySafetyNoAccessOverlayController _noAccessOverlay;
        [SerializeField, FormerlySerializedAs("_initialResultSeconds"), Min(1f)]
        private float _resultConfirmationSeconds = 1f;
        [SerializeField, Min(0f)]
        private float _introStartDelaySeconds = 1.25f;

        private EntrySafetyStateMachine _stateMachine;
        private EntrySafetyGazeCoordinator _gazeCoordinator;
        private EntrySafetyGuidanceCoordinator _guidanceCoordinator;
        private EntrySafetyStep _lastStep;
        private bool _isConfigured;
        private EntrySafetyStep? _resultDelayStep;
        private bool _reportedConfigurationError;
        private bool _isMovingToPtPanel;
        private bool _goal2OutroPlaying;
        private bool _goal2OutroStarted;
        private bool _allowLeakResponseHandoff;
        private bool _inputHandlersSubscribed;
        private bool _preEntryTelemetryStarted;
        private int _panelAttemptCount;
        private int _panelRejectedCount;

        public EntrySafetyStateMachine StateMachine => _stateMachine;
        public bool LeakResponseHandoffAllowed => _allowLeakResponseHandoff;
        public bool InputHandlersSubscribed => _inputHandlersSubscribed;

        private void Awake()
        {
            EnsureLocalSupportComponents();
            _isConfigured = _gazeAdapter != null && _inputAdapter != null && _guidanceView != null &&
                            _equipmentTransferZone != null && _detectorController != null &&
                            _windChangeController != null && _windsockGazeTarget != null;
            if (!_isConfigured)
            {
                _stateMachine = null;
                ReportConfigurationErrorOnce();
                enabled = false;
                return;
            }

            _stateMachine = new EntrySafetyStateMachine();
            _gazeCoordinator = new EntrySafetyGazeCoordinator(_stateMachine);
            _guidanceCoordinator = new EntrySafetyGuidanceCoordinator(_stateMachine);
            _stateMachine.SectionCompleted += OnSectionCompleted;
            _stateMachine.StepChanged += OnStepChanged;
            SubscribeInputHandlers();
            _lastStep = _stateMachine.CurrentStep;
            _detectorController.ResetDetector();
            _windChangeController.ResetWind();
            _windsockGazeTarget.SetTargetId(EntrySafetyGazeTargetId.InitialWindsock);
            _goal2PtPanel?.ResetPresentation();
            _noAccessOverlay?.RestoreImmediate();
        }

        private void OnDestroy()
        {
            StopNarration();
            if (_stateMachine != null)
            {
                _stateMachine.SectionCompleted -= OnSectionCompleted;
                _stateMachine.StepChanged -= OnStepChanged;
            }
            UnsubscribeInputHandlers();
        }

        /// <summary>
        /// Integration supplies execution context; this Stage keeps ownership of its
        /// barrier and A-button handoff meaning.
        /// </summary>
        public void ConfigureLeakResponseHandoff(bool allowed)
        {
            _allowLeakResponseHandoff = allowed;
            if (!allowed)
                _noAccessOverlay?.RestoreImmediate();
        }

        public void DeactivateForLeakResponseHandoff()
        {
            StopNarration();
            UnsubscribeInputHandlers();
            // LeakResponse keeps the PreEntry environment loaded, so restore the
            // authored visible collider as the physical return boundary.
            _noAccessOverlay?.RestoreImmediate();
            if (_inputAdapter != null)
                _inputAdapter.enabled = false;
            enabled = false;
        }

        public void SetStageActivated(bool active)
        {
            if (!active)
            {
                DeactivateForLeakResponseHandoff();
                return;
            }

            enabled = true;
            if (_inputAdapter != null)
                _inputAdapter.enabled = true;
            SubscribeInputHandlers();
        }

        private void SubscribeInputHandlers()
        {
            if (_inputHandlersSubscribed || _inputAdapter == null)
                return;

            _inputAdapter.EquipDetectorPressed += TryEquipDetector;
            _inputAdapter.StoreDetectorPressed += TryStoreDetector;
            _inputAdapter.SimulationGazeConfirmPressed += TryCompleteGazeForSimulation;
            _inputAdapter.MoveToPtPressed += TryMoveToPtPanel;
            _inputAdapter.RightTriggerPressed += TryStopPump;
            _inputHandlersSubscribed = true;
        }

        private void UnsubscribeInputHandlers()
        {
            if (!_inputHandlersSubscribed || _inputAdapter == null)
                return;

            _inputAdapter.EquipDetectorPressed -= TryEquipDetector;
            _inputAdapter.StoreDetectorPressed -= TryStoreDetector;
            _inputAdapter.SimulationGazeConfirmPressed -= TryCompleteGazeForSimulation;
            _inputAdapter.MoveToPtPressed -= TryMoveToPtPanel;
            _inputAdapter.RightTriggerPressed -= TryStopPump;
            _inputHandlersSubscribed = false;
        }

        private IEnumerator Start()
        {
            if (!_isConfigured)
                yield break;

            // Keep intro TTS/subtitle off until scene loading and first frames settle.
            _guidanceView.SetVisible(false);
            _goal01TtsPlayer?.Stop();
            _goal02TtsPlayer?.Stop();
            yield return null;
            yield return new WaitForEndOfFrame();
            if (_introStartDelaySeconds > 0f)
                yield return new WaitForSeconds(_introStartDelaySeconds);

            RefreshGuidance();
            ReportPreEntryEntered();
            var introSeconds = _goal01TtsPlayer != null
                ? _goal01TtsPlayer.GetClipLength(EntrySafetyStep.SceneIntro)
                : 0f;
            if (introSeconds > 0f)
                yield return new WaitForSeconds(introSeconds);
            else
                yield return null;
            Advance(EntrySafetyStep.SceneIntro);
        }

        private void Update()
        {
            if (!_isConfigured || _stateMachine == null || _stateMachine.IsSectionCompleted)
                return;

            // Every step becomes actionable only after its matching guidance TTS has finished.
            if (IsGuidanceTtsBlocking())
            {
                HideWindsockGazeProgress();
                _gazeAdapter.SetGuideRayVisible(false);
                _equipmentTransferZone.UpdateZone(false, Time.deltaTime);
                return;
            }

            // Match the other stages: guidance text is a TTS subtitle, not a persistent
            // objective panel. Once narration has finished, remove both text and backdrop.
            _guidanceView.SetVisible(false);

            var step = _stateMachine.CurrentStep;
            if (step == EntrySafetyStep.CheckInitialWind || step == EntrySafetyStep.CheckChangedWind)
            {
                UpdateWindsockGaze();
            }
            else if (step == EntrySafetyStep.EquipDetector)
            {
                HideWindsockGazeProgress();
                _gazeAdapter.SetGuideRayVisible(false);
                _equipmentTransferZone.UpdateZone(true, Time.deltaTime);
            }
            else if (step == EntrySafetyStep.MeasureInitialHcl)
            {
                HideWindsockGazeProgress();
                _gazeAdapter.SetGuideRayVisible(false);
                var isLookingAtDetector = _detectorController.IsLookingAtDisplay;
                if (_detectorController.UpdateInitialMeasurement(
                        _inputAdapter.IsLeftTriggerPressed,
                        isLookingAtDetector,
                        Time.deltaTime))
                    Advance(EntrySafetyStep.MeasureInitialHcl);
            }
            else if (step == EntrySafetyStep.MeasureReducedHcl)
            {
                HideWindsockGazeProgress();
                _gazeAdapter.SetGuideRayVisible(false);
                var isLookingAtDetector = _detectorController.IsLookingAtDisplay;
                if (_detectorController.UpdateReducedMeasurement(
                        _inputAdapter.IsLeftTriggerPressed,
                        isLookingAtDetector,
                        Time.deltaTime))
                    Advance(EntrySafetyStep.MeasureReducedHcl);
            }
            else if ((step == EntrySafetyStep.ConfirmInitialHcl || step == EntrySafetyStep.ConfirmReducedHcl) &&
                     _resultDelayStep != step)
            {
                HideWindsockGazeProgress();
                _resultDelayStep = step;
                _gazeAdapter.SetGuideRayVisible(false);
                StartCoroutine(CompleteResultAfterDelay(step));
            }
            else if (step == EntrySafetyStep.WaitWindChange)
            {
                HideWindsockGazeProgress();
                _gazeAdapter.SetGuideRayVisible(true);
                _windChangeController.Begin();
                if (_windChangeController.UpdateRotation(Time.deltaTime))
                {
                    _windsockGazeTarget.SetTargetId(EntrySafetyGazeTargetId.ChangedWindsock);
                    Advance(EntrySafetyStep.WaitWindChange);
                }
            }
            else if (IsGoal2Step(step))
            {
                HideWindsockGazeProgress();
                _gazeAdapter.SetGuideRayVisible(false);
                UpdateGoal2(step);
            }
            else
            {
                HideWindsockGazeProgress();
                _gazeAdapter.SetGuideRayVisible(false);
                _equipmentTransferZone.UpdateZone(false, Time.deltaTime);
            }

            // Gaze/Goal2 may change the step mid-Update. Start the new step TTS and stop here so
            // WaitWindChange cannot begin rotating in the same frame as TTS_06 starts.
            if (_lastStep != _stateMachine.CurrentStep)
            {
                _lastStep = _stateMachine.CurrentStep;
                if (_lastStep != EntrySafetyStep.CheckInitialWind &&
                    _lastStep != EntrySafetyStep.CheckChangedWind)
                    _gazeAdapter.SetGuideRayVisible(false);
                RefreshGuidance();
            }
        }

        private void TryEquipDetector()
        {
            if (_stateMachine == null || _stateMachine.CurrentStep != EntrySafetyStep.EquipDetector)
                return;
            if (IsGuidanceTtsBlocking())
                return;
            if (_equipmentTransferZone == null || !_equipmentTransferZone.EvaluateEquipAttempt())
            {
                SharedTrainingTelemetry.Current.BagItemRequested(SharedEquipmentIds.GasDetector, false);
                return;
            }

            if (_detectorController.TryEquip())
            {
                SharedTrainingTelemetry.Current.BagItemRequested(SharedEquipmentIds.GasDetector, true);
                Advance(EntrySafetyStep.EquipDetector);
            }
        }

        private void TryStoreDetector()
        {
            if (_stateMachine == null || _stateMachine.CurrentStep != EntrySafetyStep.StoreDetector)
                return;
            if (IsGuidanceTtsBlocking())
                return;

            if (_detectorController.TryStore())
            {
                SharedTrainingTelemetry.Current.EquipmentUsed(SharedEquipmentIds.GasDetector);
                Advance(EntrySafetyStep.StoreDetector);
            }
        }

        private void TryMoveToPtPanel()
        {
            if (_stateMachine == null)
                return;
            if (IsGuidanceTtsBlocking())
                return;

            if (_stateMachine.CurrentStep == EntrySafetyStep.MoveToNextArea)
            {
                if (!_allowLeakResponseHandoff)
                    return;

                // The next area's anchor belongs to the later integrated scenario. Completing this
                // terminal step exposes the existing SectionCompleted event for that handoff.
                Advance(EntrySafetyStep.MoveToNextArea);
                if (_stateMachine.IsSectionCompleted)
                    _goal2PtPanel?.ShowNextAreaReady();
                return;
            }

            if (_stateMachine.CurrentStep != EntrySafetyStep.MoveToPT ||
                !_stateMachine.IsGoal1Completed || !_stateMachine.IsDetectorStored || _goal2PtPanel == null)
                return;

            if (_isMovingToPtPanel)
                return;

            StartCoroutine(MoveToPtPanelRoutine());
        }

        private IEnumerator MoveToPtPanelRoutine()
        {
            _isMovingToPtPanel = true;
            _guidanceView.SetVisible(false);
            _goal01TtsPlayer?.Stop();
            _goal02TtsPlayer?.Stop();
            yield return _goal2PtPanel.MoveToPanelWithFade();
            if (_stateMachine != null && _stateMachine.CurrentStep == EntrySafetyStep.MoveToPT)
                Advance(EntrySafetyStep.MoveToPT);
            _isMovingToPtPanel = false;
        }

        private void TryStopPump()
        {
            if (_stateMachine == null || _stateMachine.CurrentStep != EntrySafetyStep.StopPump || _goal2PtPanel == null)
                return;
            if (IsGuidanceTtsBlocking())
                return;

            var result = _goal2PtPanel.TryStopPump(_inputAdapter.RightHandPosition, _inputAdapter.HasRightHandPose);
            if (result == EntrySafetyStopAttemptResult.Stopped)
            {
                _panelAttemptCount++;
                SharedTrainingTelemetry.Current.ContentViewed(
                    "PreEntry.FacilityStop.P-102");
                Advance(EntrySafetyStep.StopPump);
                return;
            }

            if (result == EntrySafetyStopAttemptResult.Missed)
            {
                _panelRejectedCount++;
                SharedTrainingTelemetry.Current.ContentViewed("PreEntry.Panel.StopFailed");
            }
        }

        private void TryCompleteGazeForSimulation()
        {
#if UNITY_EDITOR
            if (_stateMachine == null ||
                !EntrySafetyGazeTargetMap.IsGazeStep(_stateMachine.CurrentStep))
                return;

            if (IsGuidanceTtsBlocking())
                return;

            _gazeCoordinator.Update(_gazeCoordinator.RequiredTarget, true, 3f);
#endif
        }

        private IEnumerator CompleteResultAfterDelay(EntrySafetyStep expectedStep)
        {
            // Advance as soon as the result TTS finishes (with a short minimum display time).
            var elapsed = 0f;
            while (elapsed < _resultConfirmationSeconds || IsGuidanceTtsBlocking())
            {
                elapsed += Time.deltaTime;
                yield return null;
                if (_stateMachine == null || _stateMachine.CurrentStep != expectedStep)
                    yield break;
            }

            if (_stateMachine != null && _stateMachine.CurrentStep == expectedStep)
                Advance(expectedStep);
        }

        private void Advance(EntrySafetyStep expectedStep)
        {
            if (_stateMachine == null)
                return;

            if (_stateMachine.TryCompleteStep(expectedStep, EntrySafetyCompletionMode.Normal))
            {
                if (expectedStep == EntrySafetyStep.MeasureInitialHcl ||
                    expectedStep == EntrySafetyStep.MeasureReducedHcl)
                    SharedTrainingTelemetry.Current.EquipmentUsed(SharedEquipmentIds.GasDetector);

                SkipAutoCompletePanelSteps();
                _lastStep = _stateMachine.CurrentStep;
                RefreshGuidance();
            }
        }

        private void RefreshGuidance()
        {
            if (_stateMachine == null || _guidanceCoordinator == null)
                return;

            // TryCompleteStep raises SectionCompleted synchronously. The App handoff listener
            // disables this controller during that callback, then control returns to Advance.
            // Never rebuild/play the terminal MoveToNextArea instruction after completion.
            if (_stateMachine.IsSectionCompleted)
            {
                StopAllTts();
                _guidanceView.SetVisible(false);
                return;
            }

            var step = _stateMachine.CurrentStep;
            if (step == EntrySafetyStep.MoveToNextArea && !_goal2OutroStarted && _goal02TtsPlayer != null)
            {
                _goal2OutroStarted = true;
                StartCoroutine(PlayGoal2CompletionOutro());
                return;
            }

            var instruction = _guidanceCoordinator.CurrentInstruction;

            if (step == EntrySafetyStep.WaitPressureDecrease && _goal2PtPanel != null)
            {
                var duration = _goal02TtsPlayer != null
                    ? _goal02TtsPlayer.GetClipLength(EntrySafetyStep.WaitPressureDecrease)
                    : 0f;
                _goal2PtPanel.BeginPressureDecrease(duration);
            }

            ApplyInstruction(instruction);
            _stepVisuals?.SetStep(step);
            _ttsRequestHook?.Request(instruction);
            if (instruction != null && instruction.HasTts)
                PlayStepTts(step);
            else
                StopAllTts();
        }

        private IEnumerator PlayGoal2CompletionOutro()
        {
            _goal2OutroPlaying = true;
            _stepVisuals?.SetStep(EntrySafetyStep.MoveToNextArea);

            yield return PlayTemporaryGoal2Line(
                string.Format(
                    Goal2SafetyCheckCompleteText,
                    "P-102"),
                () => _goal02TtsPlayer != null ? _goal02TtsPlayer.PlaySafetyCheckComplete() : 0f);

            if (!_allowLeakResponseHandoff)
            {
                _noAccessOverlay?.RestoreImmediate();
                _goal2OutroPlaying = false;
                Advance(EntrySafetyStep.MoveToNextArea);
                yield break;
            }

            if (_noAccessOverlay != null)
                yield return _noAccessOverlay.FadeOutAndClear();
            yield return PlayTemporaryGoal2Line(
                Goal2CheckAccessRestrictionText,
                () => _goal02TtsPlayer != null ? _goal02TtsPlayer.PlayCheckAccessRestriction() : 0f);

            _goal2OutroPlaying = false;
        }

        private IEnumerator PlayTemporaryGoal2Line(string text, Func<float> playClip)
        {
            var instruction = new EntrySafetyInstruction(text, text);
            ApplyInstruction(instruction);
            _ttsRequestHook?.Request(instruction);
            var seconds = playClip != null ? playClip() : 0f;
            if (seconds > 0f)
                yield return new WaitForSeconds(seconds);
            else
                yield return null;
        }

        private void ApplyInstruction(EntrySafetyInstruction instruction)
        {
            if (instruction != null && instruction.HasTts)
            {
                _guidanceView.SetVisible(true);
                _guidanceView.ShowInstruction(instruction);
            }
            else
            {
                _guidanceView.ShowInstruction(instruction);
                _guidanceView.SetVisible(false);
            }
        }

        private void PlayStepTts(EntrySafetyStep step)
        {
            if (UsesGoal02Tts(step))
            {
                _goal01TtsPlayer?.Stop();
                _goal02TtsPlayer?.Play(step);
                return;
            }

            _goal02TtsPlayer?.Stop();
            _goal01TtsPlayer?.Play(step);
        }

        private void StopAllTts()
        {
            _goal01TtsPlayer?.Stop();
            _goal02TtsPlayer?.Stop();
        }

        public bool IsNarrationPlaying => IsGuidanceTtsBlocking();

        public void StopNarration()
        {
            StopAllTts();
            _goal2OutroPlaying = false;
            if (_guidanceView != null)
                _guidanceView.SetVisible(false);
        }

        private bool IsGuidanceTtsBlocking()
        {
            if (_goal2OutroPlaying)
                return true;
            if (_goal01TtsPlayer != null && _goal01TtsPlayer.IsPlaying)
                return true;
            if (_goal02TtsPlayer != null && _goal02TtsPlayer.IsPlaying)
                return true;
            return false;
        }

        private static bool UsesGoal02Tts(EntrySafetyStep step)
        {
            return step == EntrySafetyStep.CheckPressure ||
                   step == EntrySafetyStep.CheckTemperature ||
                   step == EntrySafetyStep.CheckRunState ||
                   step == EntrySafetyStep.StopPump ||
                   step == EntrySafetyStep.VerifyStopIndicator ||
                   step == EntrySafetyStep.WaitPressureDecrease ||
                   step == EntrySafetyStep.VerifyPressureDecrease ||
                   step == EntrySafetyStep.MoveToNextArea;
        }

        private void UpdateGoal2(EntrySafetyStep step)
        {
            if (_goal2PtPanel == null)
                return;

            var isGazeStep = EntrySafetyGazeTargetMap.IsGazeStep(step);
            if (isGazeStep)
            {
                var hasTarget = _gazeAdapter.TryGetTarget(out var targetId);
                // Only the five P/T panel frames use the shorter two-second hold.
                // Windsock gaze remains at the coordinator's three-second duration.
                var panelGazeDelta = Time.deltaTime *
                                     (DefaultGazeHoldSeconds / PtPanelGazeHoldSeconds);
                _gazeCoordinator.Update(targetId, hasTarget, panelGazeDelta);
                if (_stateMachine.CurrentStep != step)
                {
                    // A completed gaze can change the state immediately. Clear the
                    // old frame before the next step's TTS blocks panel updates.
                    _goal2PtPanel.ClearGazeFocus();
                    return;
                }
            }

            _goal2PtPanel.UpdateForStep(step, Time.deltaTime, _gazeCoordinator.Progress01, _gazeCoordinator.IsTracking);
            var wasInsideStopZone = _goal2PtPanel.IsRightHandInsideStopZone;
            _goal2PtPanel.UpdateStopProximity(
                step == EntrySafetyStep.StopPump,
                _inputAdapter.RightHandPosition,
                _inputAdapter.HasRightHandPose);
            if (!wasInsideStopZone && _goal2PtPanel.IsRightHandInsideStopZone)
            {
                _inputAdapter.PulseRightHand(0.45f, 0.08f);
                Debug.Log("P-102 STOP zone entered; right-hand haptic requested.");
            }

            if (step == EntrySafetyStep.WaitPressureDecrease)
            {
                if (_goal2PtPanel.IsPressureDecreaseComplete && !IsGuidanceTtsBlocking())
                    Advance(EntrySafetyStep.WaitPressureDecrease);
            }
        }

        private static bool IsGoal2Step(EntrySafetyStep step)
        {
            return step == EntrySafetyStep.MoveToPT || step == EntrySafetyStep.CheckPressure ||
                   step == EntrySafetyStep.CheckTemperature || step == EntrySafetyStep.CheckRunState ||
                   step == EntrySafetyStep.StopPump || step == EntrySafetyStep.VerifyStopIndicator ||
                   step == EntrySafetyStep.WaitPressureDecrease || step == EntrySafetyStep.VerifyPressureDecrease ||
                   step == EntrySafetyStep.MoveToNextArea;
        }

        private void UpdateWindsockGaze()
        {
            // Keep aiming assist for windsock gaze; text guidance UI stays removed.
            _gazeAdapter.SetGuideRayVisible(true);
            var hasTarget = _gazeAdapter.TryGetTarget(out var targetId);
            _gazeCoordinator.Update(targetId, hasTarget, Time.deltaTime);
            // Keep the bar visible for the whole windsock gaze step; fill only advances while tracking.
            UpdateWindsockGazeProgress(true);
        }

        private void UpdateWindsockGazeProgress(bool visible)
        {
            if (_windsockGazeProgress == null)
                return;

            if (!visible || _windsockGazeTarget == null)
            {
                _windsockGazeProgress.SetVisible(false);
                return;
            }

            if (_gazeAdapter != null)
                _windsockGazeProgress.SetBillboardCamera(_gazeAdapter.GazeCamera);

            _windsockGazeProgress.SetVisible(true);
            var progress01 = _gazeCoordinator != null ? _gazeCoordinator.Progress01 : 0f;
            _windsockGazeProgress.SetProgress(progress01, progress01 >= 0.999f);
        }

        private void HideWindsockGazeProgress()
        {
            if (_windsockGazeProgress == null)
                return;

            _windsockGazeProgress.SetVisible(false);
        }

        private void EnsureLocalSupportComponents()
        {
            if (GetComponent<EntrySafetyLeakAmbientPlayer>() == null)
                gameObject.AddComponent<EntrySafetyLeakAmbientPlayer>();
            if (GetComponent<EntrySafetyMovementBounds>() == null)
                gameObject.AddComponent<EntrySafetyMovementBounds>();
        }

        private void SkipAutoCompletePanelSteps()
        {
            if (_stateMachine == null)
                return;

            while (IsAutoCompletePanelStep(_stateMachine.CurrentStep) && !_stateMachine.IsSectionCompleted)
            {
                if (!_stateMachine.TryCompleteStep(_stateMachine.CurrentStep, EntrySafetyCompletionMode.Normal))
                    break;
            }
        }

        private static bool IsAutoCompletePanelStep(EntrySafetyStep step)
        {
            return step == EntrySafetyStep.CheckPressure ||
                   step == EntrySafetyStep.CheckTemperature ||
                   step == EntrySafetyStep.CheckRunState ||
                   step == EntrySafetyStep.VerifyStopIndicator ||
                   step == EntrySafetyStep.VerifyPressureDecrease;
        }

        private void ReportPreEntryEntered()
        {
            if (_preEntryTelemetryStarted)
                return;

            _preEntryTelemetryStarted = true;
            SharedTrainingTelemetry.Current.StepEntered(TrainingStepId.PreEntry);
        }

        private void OnStepChanged(EntrySafetyStep previous, EntrySafetyStep next)
        {
            if (next != EntrySafetyStep.StopPump)
                return;

            SharedTrainingTelemetry.Current.ContentViewed("PreEntry.Panel.Start");
            SharedTrainingTelemetry.Current.ContentViewed("PreEntry.Facility.P-102");
        }

        private void OnSectionCompleted()
        {
            SharedTrainingTelemetry.Current.ContentViewed("PreEntry.Panel.Complete");
            SharedTrainingTelemetry.Current.StepCompleted(
                TrainingStepId.PreEntry,
                Mathf.Max(1, _panelAttemptCount),
                _panelRejectedCount);
        }

        private void ReportConfigurationErrorOnce()
        {
            if (_reportedConfigurationError)
                return;

            _reportedConfigurationError = true;
            Debug.LogError("EntrySafetyScenarioController requires gaze, input, guidance, zone, and detector references.", this);
        }
    }

    /// <summary>
    /// PreEntry-owned additive activation contract. A dormant dependency keeps its
    /// authored environment loaded without starting pose anchoring, input, scenario,
    /// completion waits, subtitles, or TTS.
    /// </summary>
    public static class EntrySafetyStageActivation
    {
        private static readonly Dictionary<string, bool> SceneActivation =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetActivationState()
        {
            SceneActivation.Clear();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.IsValid() &&
                SceneActivation.TryGetValue(Normalize(scene.path), out var activate) &&
                !activate)
            {
                Apply(scene, false);
            }
        }

        public static void PrepareSceneLoad(string scenePath, bool activate)
        {
            if (!string.IsNullOrWhiteSpace(scenePath))
                SceneActivation[Normalize(scenePath)] = activate;
        }

        public static void Apply(Scene scene, bool activate)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            SceneActivation[Normalize(scene.path)] = activate;
            foreach (var root in scene.GetRootGameObjects())
            {
                var poseAnchors = root.GetComponentsInChildren<EntrySafetyInitialPoseAnchor>(true);
                for (var i = 0; i < poseAnchors.Length; i++)
                    poseAnchors[i].enabled = activate;

                var controllers = root.GetComponentsInChildren<EntrySafetyScenarioController>(true);
                for (var i = 0; i < controllers.Length; i++)
                    controllers[i].SetStageActivated(activate);
            }
        }

        private static string Normalize(string path) =>
            string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').Trim();
    }
}
