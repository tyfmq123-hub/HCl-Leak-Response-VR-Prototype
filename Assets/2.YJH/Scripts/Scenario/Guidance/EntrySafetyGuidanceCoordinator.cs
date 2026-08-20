using System;

namespace JH.EntrySafety
{
    /// <summary>
    /// 현재 단계의 안내를 제공하고 반복 요청을 처리합니다.
    /// <para>
    /// CurrentInstruction은 StateMachine.CurrentStep에서 즉시 파생되는 계산 프로퍼티입니다.
    /// 따라서 TryCompleteStep·Reset·SectionCompleted 모두 추가 구독 없이 자동으로 반영됩니다.
    /// </para>
    /// </summary>
    public sealed class EntrySafetyGuidanceCoordinator
    {
        private readonly EntrySafetyStateMachine _sm;

        /// <summary>
        /// 현재 단계의 안내입니다.
        /// StateMachine.CurrentStep이 변경될 때마다 자동으로 갱신됩니다.
        /// </summary>
        public EntrySafetyInstruction CurrentInstruction
        {
            get
            {
                return EntrySafetyInstructionCatalog.GetInstruction(_sm.CurrentStep);
            }
        }

        /// <summary>
        /// 현재 단계에서 TTS 반복이 가능하면 true입니다.
        /// CheckChangedWind처럼 HasTts=false인 단계는 false를 반환합니다.
        /// </summary>
        public bool CanRepeatTts => CurrentInstruction?.HasTts ?? false;

        /// <param name="stateMachine">연결할 상태 머신입니다.</param>
        public EntrySafetyGuidanceCoordinator(EntrySafetyStateMachine stateMachine)
        {
            _sm = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        }

        /// <summary>
        /// 현재 단계의 안내를 반환합니다.
        /// HasTts=false인 단계에서는 이전 단계 TTS를 재생하지 않습니다.
        /// </summary>
        public EntrySafetyInstruction RequestCurrentInstructionRepeat()
        {
            return CurrentInstruction;
        }
    }
}
