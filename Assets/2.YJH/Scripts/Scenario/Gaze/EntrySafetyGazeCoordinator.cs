using System;

namespace JH.EntrySafety
{
    /// <summary>
    /// 현재 단계에 맞는 시선 대상만 타이머를 진행하고, 완료 시 <see cref="EntrySafetyStateMachine"/>을 Normal로 완료합니다.
    /// Update 호출마다 StateMachine.CurrentStep을 비교하여 단계 변경(전이·Reset 포함)을 감지합니다.
    /// </summary>
    public sealed class EntrySafetyGazeCoordinator
    {
        private readonly EntrySafetyStateMachine _sm;
        private readonly EntrySafetyGazeHoldTimer _timer;
        private readonly EntrySafetyGazeHoldTimer _windsockTimer;
        private EntrySafetyGazeHoldTimer ActiveTimer =>
            RequiredTarget == EntrySafetyGazeTargetId.InitialWindsock ||
            RequiredTarget == EntrySafetyGazeTargetId.ChangedWindsock ? _windsockTimer : _timer;
        private EntrySafetyStep _lastKnownStep;
        private bool _hasCompletedCurrentStep;

        /// <summary>현재 단계에서 시선이 필요한 대상 ID입니다. 비시선 단계이면 None입니다.</summary>
        public EntrySafetyGazeTargetId RequiredTarget =>
            EntrySafetyGazeTargetMap.GetRequiredTarget(_sm.CurrentStep);

        /// <summary>현재 단계가 시선 완료 단계이면 true입니다.</summary>
        public bool IsGazeStep => EntrySafetyGazeTargetMap.IsGazeStep(_sm.CurrentStep);

        /// <summary>시선 완료 진행률(0~1)입니다. 비시선 단계이면 0입니다.</summary>
        public float Progress01 => IsGazeStep ? ActiveTimer.Progress01 : 0f;

        /// <summary>현재 프레임에서 유효 대상을 응시 중이면 true입니다.</summary>
        public bool IsTracking => IsGazeStep && ActiveTimer.IsTracking;

        /// <param name="stateMachine">연결할 상태 머신입니다.</param>
        /// <param name="requiredHoldSeconds">완료에 필요한 연속 유지 시간(초)입니다.</param>
        /// <param name="exitGraceSeconds">Hold를 유지하는 이탈 허용 시간(초)입니다.</param>
        public EntrySafetyGazeCoordinator(
            EntrySafetyStateMachine stateMachine,
            float requiredHoldSeconds = 3f,
            float exitGraceSeconds = 0.2f,
            float? windsockExitGraceSeconds = null)
        {
            _sm = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _timer = new EntrySafetyGazeHoldTimer(requiredHoldSeconds, exitGraceSeconds);
            _windsockTimer = new EntrySafetyGazeHoldTimer(requiredHoldSeconds,
                windsockExitGraceSeconds ?? exitGraceSeconds);
            _lastKnownStep = _sm.CurrentStep;
        }

        /// <summary>
        /// 한 프레임의 시선 상태를 갱신합니다.
        /// 현재 단계의 요구 대상과 일치하고 isVisible이 true일 때만 타이머를 진행합니다.
        /// </summary>
        /// <param name="observedTarget">외부 어댑터가 전달하는 현재 관측 대상 ID입니다.</param>
        /// <param name="isVisible">대상이 유효하게 보이면 true입니다.</param>
        /// <param name="deltaTime">이번 프레임 경과 시간(초)입니다.</param>
        public void Update(EntrySafetyGazeTargetId observedTarget, bool isVisible, float deltaTime,
            bool allowCompletion = true)
        {
            // 단계 변경 감지 — 정상 전이와 Reset 모두 처리합니다.
            if (_sm.CurrentStep != _lastKnownStep)
            {
                _lastKnownStep = _sm.CurrentStep;
                _hasCompletedCurrentStep = false;
                _timer.Reset();
                _windsockTimer.Reset();
            }

            if (_sm.IsSectionCompleted)
                return;

            if (!IsGazeStep || _hasCompletedCurrentStep)
                return;

            bool isValidTarget = isVisible
                && observedTarget != EntrySafetyGazeTargetId.None
                && observedTarget == RequiredTarget;

            ActiveTimer.Update(isValidTarget, deltaTime);

            if (ActiveTimer.IsCompleted && allowCompletion)
            {
                _hasCompletedCurrentStep = true;
                // TryCompleteStep 성공 시 _sm.CurrentStep이 즉시 변경됩니다.
                // 다음 Update에서 단계 변경을 감지하여 타이머를 초기화합니다.
                _sm.TryCompleteStep(_sm.CurrentStep, EntrySafetyCompletionMode.Normal);
            }
        }

        /// <summary>현재 단계의 시선 진행을 초기화합니다. 상태 머신 상태는 변경하지 않습니다.</summary>
        public void ResetProgress()
        {
            _timer.Reset();
            _windsockTimer.Reset();
            _hasCompletedCurrentStep = false;
        }
    }
}
