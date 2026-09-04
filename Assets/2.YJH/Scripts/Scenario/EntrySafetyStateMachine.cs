using System;
using System.Collections.Generic;

namespace JH.EntrySafety
{
    /// <summary>
    /// 현장 진입 안전확인 시나리오의 순수 C# 상태 머신입니다.
    /// UnityEngine API 없이 동작하며 Scene 연결과 무관하게 테스트할 수 있습니다.
    /// </summary>
    public sealed class EntrySafetyStateMachine
    {
        // 명시적 전이표 — enum 정수 순서(+1)로 다음 단계를 계산하지 않습니다.
        private static readonly Dictionary<EntrySafetyStep, EntrySafetyStep> s_NextStep =
            new Dictionary<EntrySafetyStep, EntrySafetyStep>
            {
                { EntrySafetyStep.SceneIntro,             EntrySafetyStep.EquipDetector },
                { EntrySafetyStep.EquipDetector,          EntrySafetyStep.MeasureInitialHcl },
                { EntrySafetyStep.MeasureInitialHcl,      EntrySafetyStep.ConfirmInitialHcl },
                { EntrySafetyStep.ConfirmInitialHcl,      EntrySafetyStep.CheckInitialWind },
                { EntrySafetyStep.CheckInitialWind,       EntrySafetyStep.WaitWindChange },
                { EntrySafetyStep.WaitWindChange,         EntrySafetyStep.CheckChangedWind },
                { EntrySafetyStep.CheckChangedWind,       EntrySafetyStep.MeasureReducedHcl },
                { EntrySafetyStep.MeasureReducedHcl,      EntrySafetyStep.ConfirmReducedHcl },
                { EntrySafetyStep.ConfirmReducedHcl,      EntrySafetyStep.StoreDetector },
                { EntrySafetyStep.StoreDetector,          EntrySafetyStep.MoveToPT },
                { EntrySafetyStep.MoveToPT,               EntrySafetyStep.CheckPressure },
                { EntrySafetyStep.CheckPressure,          EntrySafetyStep.CheckTemperature },
                { EntrySafetyStep.CheckTemperature,       EntrySafetyStep.CheckRunState },
                { EntrySafetyStep.CheckRunState,          EntrySafetyStep.StopPump },
                { EntrySafetyStep.StopPump,               EntrySafetyStep.VerifyStopIndicator },
                { EntrySafetyStep.VerifyStopIndicator,    EntrySafetyStep.WaitPressureDecrease },
                { EntrySafetyStep.WaitPressureDecrease,   EntrySafetyStep.VerifyPressureDecrease },
                { EntrySafetyStep.VerifyPressureDecrease, EntrySafetyStep.ClearAccessRestriction },
                { EntrySafetyStep.ClearAccessRestriction, EntrySafetyStep.MoveToNextArea },
                // MoveToNextArea는 전이표에 없는 터미널 단계입니다.
            };

        private EntrySafetyStep _previousStepValue;

        // ── 공개 상태 ──────────────────────────────────────────────────────────

        /// <summary>현재 진행 단계입니다.</summary>
        public EntrySafetyStep CurrentStep { get; private set; }

        /// <summary>
        /// 직전에 완료된 단계입니다.
        /// <see cref="HasPreviousStep"/>이 false인 경우 접근하면
        /// <see cref="InvalidOperationException"/>이 발생합니다.
        /// </summary>
        public EntrySafetyStep PreviousStep
        {
            get
            {
                if (!HasPreviousStep)
                    throw new InvalidOperationException(
                        "PreviousStep is not available until at least one step has been completed.");
                return _previousStepValue;
            }
        }

        /// <summary>하나 이상의 단계가 완료되었으면 true입니다.</summary>
        public bool HasPreviousStep { get; private set; }

        /// <summary>가장 최근 성공한 완료의 방식입니다.</summary>
        public EntrySafetyCompletionMode LastCompletionMode { get; private set; }

        /// <summary>
        /// 목표 1 완료 여부입니다.
        /// ConfirmReducedHcl 완료 후 StoreDetector에 진입한 시점부터 true입니다.
        /// </summary>
        public bool IsGoal1Completed { get; private set; }

        /// <summary>
        /// 측정기 수납 완료 여부입니다.
        /// StoreDetector 완료 후 MoveToPT에 진입한 시점부터 true입니다.
        /// IsGoal1Completed = true이면서 IsDetectorStored = false인 상태가 가능합니다.
        /// </summary>
        public bool IsDetectorStored { get; private set; }

        /// <summary>
        /// 목표 2 완료 여부입니다.
        /// VerifyPressureDecrease 이후 접근 금지를 해제한 다음 MoveToNextArea에 진입한 시점부터 true입니다.
        /// </summary>
        public bool IsGoal2Completed { get; private set; }

        /// <summary>
        /// 담당 구간 전체 완료 여부입니다.
        /// MoveToNextArea 단계 자체가 완료된 시점부터 true입니다.
        /// 완료 후 CurrentStep은 MoveToNextArea에 유지됩니다.
        /// </summary>
        public bool IsSectionCompleted { get; private set; }

        /// <summary>WithHint 방식으로 완료된 횟수입니다.</summary>
        public int WithHintCompletionCount { get; private set; }

        /// <summary>Assisted 방식으로 완료된 횟수입니다.</summary>
        public int AssistedCompletionCount { get; private set; }

        // ── 이벤트 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 비터미널 단계가 성공적으로 전이될 때 발생합니다. 인수: (이전 단계, 새 현재 단계).
        /// 터미널 단계(MoveToNextArea) 완료 시에는 발생하지 않으며 대신 <see cref="SectionCompleted"/>가 발생합니다.
        /// 실패한 <see cref="TryCompleteStep"/>에서는 발생하지 않습니다.
        /// <see cref="Reset"/>에서는 발생하지 않습니다.
        /// </summary>
        public event Action<EntrySafetyStep, EntrySafetyStep> StepChanged;

        /// <summary>
        /// 담당 구간 전체가 완료될 때 정확히 한 번 발생합니다.
        /// 실패한 완료 요청이나 <see cref="Reset"/>에서는 발생하지 않습니다.
        /// </summary>
        public event Action SectionCompleted;

        // ── 공개 API ──────────────────────────────────────────────────────────

        /// <summary>
        /// 현재 단계의 완료를 시도합니다. 성공하면 상태를 전이하고 true를 반환합니다.
        /// </summary>
        /// <remarks>
        /// 다음 조건 중 하나라도 해당하면 상태를 변경하지 않고 false를 반환합니다.
        /// <list type="bullet">
        /// <item><paramref name="completedStep"/>이 <see cref="CurrentStep"/>과 다름</item>
        /// <item>정의되지 않은 <see cref="EntrySafetyStep"/> 값</item>
        /// <item>정의되지 않은 <see cref="EntrySafetyCompletionMode"/> 값</item>
        /// <item>담당 구간이 이미 완료됨(<see cref="IsSectionCompleted"/> == true)</item>
        /// </list>
        /// </remarks>
        public bool TryCompleteStep(EntrySafetyStep completedStep, EntrySafetyCompletionMode completionMode)
        {
            if (!IsDefinedStep(completedStep) || !IsDefinedCompletionMode(completionMode))
                return false;

            if (IsSectionCompleted)
                return false;

            if (completedStep != CurrentStep)
                return false;

            // 이 시점에서 completedStep == CurrentStep, 입력 유효성 검사 통과.
            bool isTerminal = !s_NextStep.ContainsKey(CurrentStep);

            EntrySafetyStep previous = CurrentStep;
            _previousStepValue = previous;
            HasPreviousStep = true;
            LastCompletionMode = completionMode;

            if (completionMode == EntrySafetyCompletionMode.WithHint)
                WithHintCompletionCount++;
            else if (completionMode == EntrySafetyCompletionMode.Assisted)
                AssistedCompletionCount++;

            if (isTerminal)
            {
                // MoveToNextArea 완료: CurrentStep을 유지하고 구간 완료 처리.
                IsSectionCompleted = true;
                SectionCompleted?.Invoke();
            }
            else
            {
                EntrySafetyStep next = s_NextStep[CurrentStep];
                CurrentStep = next;
                ApplyMilestoneFlags(next);
                StepChanged?.Invoke(previous, next);
            }

            return true;
        }

        /// <summary>
        /// 상태 머신을 초기 상태로 되돌립니다.
        /// <see cref="StepChanged"/>와 <see cref="SectionCompleted"/>를 발생시키지 않습니다.
        /// </summary>
        public void Reset()
        {
            CurrentStep = EntrySafetyStep.SceneIntro;
            HasPreviousStep = false;
            _previousStepValue = EntrySafetyStep.SceneIntro;
            LastCompletionMode = EntrySafetyCompletionMode.Normal;
            WithHintCompletionCount = 0;
            AssistedCompletionCount = 0;
            IsGoal1Completed = false;
            IsDetectorStored = false;
            IsGoal2Completed = false;
            IsSectionCompleted = false;
        }

        // ── 내부 헬퍼 ────────────────────────────────────────────────────────

        private void ApplyMilestoneFlags(EntrySafetyStep enteredStep)
        {
            if (enteredStep == EntrySafetyStep.StoreDetector)
                IsGoal1Completed = true;

            if (enteredStep == EntrySafetyStep.MoveToPT)
                IsDetectorStored = true;

            if (enteredStep == EntrySafetyStep.MoveToNextArea)
                IsGoal2Completed = true;
        }

        private static bool IsDefinedStep(EntrySafetyStep step)
        {
            return step >= EntrySafetyStep.SceneIntro && step <= EntrySafetyStep.ClearAccessRestriction;
        }

        private static bool IsDefinedCompletionMode(EntrySafetyCompletionMode mode)
        {
            return mode >= EntrySafetyCompletionMode.Normal && mode <= EntrySafetyCompletionMode.Assisted;
        }
    }
}
