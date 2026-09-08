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
            "염화수소 농도와 풍향, 설비 정지와 압력 감소까지 확인했습니다. 현장 진입 전 안전 확인이 완료되었습니다.";
        private const string Goal2CheckAccessRestrictionText =
            "방호벽은 유지됩니다. 지정된 이동 지점에서 오른손 A를 눌러 누출 대응 구역으로 이동하십시오.";
        [SerializeField] private EntrySafetyCameraGazeAdapter _gazeAdapter;
        [SerializeField] private EntrySafetyInputAdapter _inputAdapter;
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
        [SerializeField] private GameObject _barricade;
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
        private SessionEquipmentInventory _sessionInventory;
        private bool _detectorCheckedOut;
        private bool _clearAccessStarted;
        public EntrySafetyStateMachine StateMachine => _stateMachine;
        public bool LeakResponseHandoffAllowed => _allowLeakResponseHandoff;
        public bool InputHandlersSubscribed => _inputHandlersSubscribed;

        private void Awake()
        {
            _sessionInventory = SessionEquipmentInventory.Ensure();
            _sessionInventory?.SeedStandalone(SessionEquipmentPresets.PreEntryStandaloneEquipment);
            EnsureLocalSupportComponents();
            _isConfigured = _gazeAdapter != null && _inputAdapter != null &&
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
            _gazeCoordinator = new EntrySafetyGazeCoordinator(_stateMachine,
                windsockExitGraceSeconds: 0.45f);
            _guidanceCoordinator = new EntrySafetyGuidanceCoordinator(_stateMachine);
            _stateMachine.SectionCompleted += OnSectionCompleted;
            _stateMachine.StepChanged += OnStepChanged;
            _detectorController.DetectorGrabbed += OnDetectorGrabbed;
            SubscribeInputHandlers();
            _lastStep = _stateMachine.CurrentStep;
            _detectorController.ResetDetector();
            _windChangeController.ResetWind();
            _windsockGazeTarget.SetTargetId(EntrySafetyGazeTargetId.InitialWindsock);
            if (_windsockGazeProgress != null)
                _windsockGazeProgress.Follow(GetWindsockFollowVisual());
            _goal2PtPanel?.ResetPresentation();
            if (_noAccessOverlay != null)
                _noAccessOverlay.RestoreImmediate();
            SetBarricadeActive(true);
            _clearAccessStarted = false;
        }

        private void OnDestroy()
        {
            if (_detectorCheckedOut)
                _sessionInventory?.ReturnCheckedOut(SharedEquipmentIds.GasDetector);
            StopNarration();
            if (_stateMachine != null)
            {
                _stateMachine.SectionCompleted -= OnSectionCompleted;
                _stateMachine.StepChanged -= OnStepChanged;
            }
            if (_detectorController != null)
                _detectorController.DetectorGrabbed -= OnDetectorGrabbed;
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
            {
                if (_noAccessOverlay != null)
                    _noAccessOverlay.RestoreImmediate();
                SetBarricadeActive(true);
            }
        }

        public void DeactivateForLeakResponseHandoff()
        {
            StopNarration();
            UnsubscribeInputHandlers();
            var preEntryAmbient = GetComponent<EntrySafetyLeakAmbientPlayer>();
            if (preEntryAmbient != null)
                preEntryAmbient.enabled = false;
            // LeakResponse keeps the PreEntry environment loaded, so restore the
            // authored visible collider as the physical return boundary.
            if (_noAccessOverlay != null)
                _noAccessOverlay.RestoreImmediate();
            SetBarricadeActive(true);
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
            var preEntryAmbient = GetComponent<EntrySafetyLeakAmbientPlayer>();
            if (preEntryAmbient != null)
                preEntryAmbient.enabled = true;
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

            // Keep the intro TTS off until scene loading and the first frames settle.
            _goal01TtsPlayer?.Stop();
            _goal02TtsPlayer?.Stop();
            yield return null;
            // WaitForEndOfFrame is never resumed by Unity's headless batch runner.
            // Keep the authored rendered-frame delay on Quest/Editor while allowing
            // clean-checkout PlayMode validation to exercise the same startup flow.
            if (Application.isBatchMode)
                yield return null;
            else
                yield return new WaitForEndOfFrame();
            if (_introStartDelaySeconds > 0f)
                yield return new WaitForSeconds(_introStartDelaySeconds);

            RefreshGuidance();
            ReportPreEntryEntered();
            yield return null;
            while (_goal01TtsPlayer != null && _goal01TtsPlayer.IsPlaying)
                yield return null;
            Advance(EntrySafetyStep.SceneIntro);
        }

        private void Update()
        {
            if (!_isConfigured || _stateMachine == null || _stateMachine.IsSectionCompleted)
                return;

            var step = _stateMachine.CurrentStep;
            // Start the result's minimum display time together with its narration,
            // not one second after that narration has already ended.
            if ((step == EntrySafetyStep.ConfirmInitialHcl || step == EntrySafetyStep.ConfirmReducedHcl) &&
                _resultDelayStep != step)
            {
                _resultDelayStep = step;
                StartCoroutine(CompleteResultAfterDelay(step));
            }

            // Windsock acquisition should respond while the learner follows the
            // spoken instruction. Completion still waits for that cue below.
            bool windsockStep = step == EntrySafetyStep.CheckInitialWind ||
                                step == EntrySafetyStep.CheckChangedWind;
            if (IsGuidanceTtsBlocking() && !windsockStep)
            {
                HideWindsockGazeProgress();
                _gazeAdapter.SetGuideRayVisible(false);
                _equipmentTransferZone.UpdateZone(false, Time.deltaTime);
                return;
            }

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
            if (_detectorController.IsEquipped)
                return;
            if (_detectorController.IsAwaitingPickup)
            {
                // The input adapter only emits another Grip after the first press has
                // been fully released. Use that second deliberate press to seat the
                // detector onto the authored Ghost; XRI selection is intentionally not
                // involved, so a stale select state cannot skip this visible step.
                _detectorController.TryCompletePickup(_equipmentTransferZone.TrackedLeftHand);
                return;
            }
            if (_equipmentTransferZone == null || !_equipmentTransferZone.EvaluateEquipAttempt())
            {
                SharedTrainingTelemetry.Current.BagItemRequested(SharedEquipmentIds.GasDetector, false);
                return;
            }

            var checkedOutNow = false;
            if (!_detectorCheckedOut)
            {
                checkedOutNow = _sessionInventory != null &&
                                _sessionInventory.TryCheckOut(SharedEquipmentIds.GasDetector);
                _detectorCheckedOut = checkedOutNow;
            }

            // The detector is mandatory for this Stage. A stale/missing cross-scene
            // inventory entry must not make the authored detector disappear or block
            // progression; checkout remains opportunistic bookkeeping.
            if (!_detectorController.TryPresentForPickup() && checkedOutNow)
            {
                _sessionInventory.ReturnCheckedOut(SharedEquipmentIds.GasDetector);
                _detectorCheckedOut = false;
            }
        }

        private void OnDetectorGrabbed()
        {
            if (_stateMachine == null || _stateMachine.CurrentStep != EntrySafetyStep.EquipDetector)
                return;

            SharedTrainingTelemetry.Current.BagItemRequested(SharedEquipmentIds.GasDetector, true);
            Advance(EntrySafetyStep.EquipDetector);
        }

        private void TryStoreDetector()
        {
            if (_stateMachine == null || _stateMachine.CurrentStep != EntrySafetyStep.StoreDetector)
                return;
            if (_detectorController.TryStore())
            {
                if (_detectorCheckedOut)
                {
                    _sessionInventory?.ReturnCheckedOut(SharedEquipmentIds.GasDetector);
                    _detectorCheckedOut = false;
                }
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
            _goal01TtsPlayer?.Stop();
            _goal02TtsPlayer?.Stop();
            yield return _goal2PtPanel.MoveToPanelWithFade();
            if (_goal2PtPanel != null)
                yield return _goal2PtPanel.ShowArrivalContext();
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

            if (step == EntrySafetyStep.ClearAccessRestriction && !_clearAccessStarted)
            {
                _clearAccessStarted = true;
                StartCoroutine(ClearAccessRestrictionRoutine());
            }

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
                Goal2SafetyCheckCompleteText,
                () => _goal02TtsPlayer != null ? _goal02TtsPlayer.PlaySafetyCheckComplete() : 0f);

            if (!_allowLeakResponseHandoff)
            {
                _goal2OutroPlaying = false;
                Advance(EntrySafetyStep.MoveToNextArea);
                yield break;
            }

            yield return PlayTemporaryGoal2Line(
                Goal2CheckAccessRestrictionText,
                () => _goal02TtsPlayer != null ? _goal02TtsPlayer.PlayCheckAccessRestriction() : 0f);

            _goal2OutroPlaying = false;
        }

        private IEnumerator PlayTemporaryGoal2Line(string text, Func<float> playClip)
        {
            var instruction = new EntrySafetyInstruction(text, text);
            _ttsRequestHook?.Request(instruction);
            playClip?.Invoke();
            yield return null;
            while (_goal02TtsPlayer != null && _goal02TtsPlayer.IsPlaying)
                yield return null;
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

        // Store/gaze are already usable during their spoken instruction. Keep the
        // telemetry input window aligned with those actual interaction rules.
        public bool IsInteractionInputBlockedByNarration => IsGuidanceTtsBlocking() &&
            (_stateMachine == null || (_stateMachine.CurrentStep != EntrySafetyStep.StoreDetector &&
             _stateMachine.CurrentStep != EntrySafetyStep.CheckInitialWind &&
             _stateMachine.CurrentStep != EntrySafetyStep.CheckChangedWind));

        public void StopNarration()
        {
            StopAllTts();
            _goal2OutroPlaying = false;
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
                   step == EntrySafetyStep.ClearAccessRestriction ||
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
                   step == EntrySafetyStep.ClearAccessRestriction ||
                   step == EntrySafetyStep.MoveToNextArea;
        }

        private void UpdateWindsockGaze()
        {
            // Keep aiming assist for windsock gaze; text guidance UI stays removed.
            _gazeAdapter.SetGuideRayVisible(true);
            var hasTarget = _gazeAdapter.TryGetWindsockTarget(_windsockGazeTarget, out var targetId);
            _gazeCoordinator.Update(targetId, hasTarget, Time.deltaTime,
                allowCompletion: !IsGuidanceTtsBlocking());
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

            _windsockGazeProgress.Follow(GetWindsockFollowVisual());
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

        private Transform GetWindsockFollowVisual()
        {
            if (_windsockGazeTarget == null)
                return null;

            var current = _windsockGazeTarget.transform;
            while (current != null)
            {
                if (current.name == "Windsock_UnityReady")
                    return current;
                current = current.parent;
            }

            return _windsockGazeTarget.transform;
        }

        private void EnsureLocalSupportComponents()
        {
            if (GetComponent<EntrySafetyLeakAmbientPlayer>() == null)
                gameObject.AddComponent<EntrySafetyLeakAmbientPlayer>();
            if (GetComponent<EntrySafetyMovementBounds>() == null)
                gameObject.AddComponent<EntrySafetyMovementBounds>();
        }

        private IEnumerator ClearAccessRestrictionRoutine()
        {
            // The A-button handoff teleports through the controlled transition. Keep
            // the authored barricade visible as the physical no-walk boundary.
            SetBarricadeActive(true);
            if (_noAccessOverlay != null)
                yield return _noAccessOverlay.FadeOutAndClear();
            else
                yield return null;
            SetBarricadeActive(true);

            if (_stateMachine != null &&
                _stateMachine.CurrentStep == EntrySafetyStep.ClearAccessRestriction)
                Advance(EntrySafetyStep.ClearAccessRestriction);
        }

        private void SetBarricadeActive(bool active)
        {
            if (_barricade == null)
            {
                var named = GameObject.Find("Barricade");
                if (named != null)
                    _barricade = named;
            }

            if (_barricade != null)
                _barricade.SetActive(active);
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
        private const string ResponseBagRootName = "PreEntry_ResponseBag";

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
                if (string.Equals(root.name, ResponseBagRootName, StringComparison.Ordinal))
                    root.SetActive(activate);

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
