using System;

namespace JH.EntrySafety
{
    /// <summary>
    /// 순수 C# 시선 유지 타이머입니다. UnityEngine 없이 동작합니다.
    /// <para>유효 대상 응시 3초 연속 시 완료, 0.2초 미만 이탈 시 진행 유지, 0.2초 이상 이탈 시 초기화합니다.</para>
    /// </summary>
    public sealed class EntrySafetyGazeHoldTimer
    {
        private readonly float _requiredHoldSeconds;
        private readonly float _exitGraceSeconds;

        /// <summary>시선 유지 누적 시간(초)입니다.</summary>
        public float ElapsedHoldSeconds { get; private set; }

        /// <summary>시선 이탈 누적 시간(초)입니다.</summary>
        public float ElapsedExitSeconds { get; private set; }

        /// <summary>완료 진행률(0~1)입니다. 완료 시 1로 고정됩니다.</summary>
        public float Progress01
        {
            get
            {
                if (IsCompleted) return 1f;
                float raw = ElapsedHoldSeconds / _requiredHoldSeconds;
                return raw < 0f ? 0f : raw > 1f ? 1f : raw;
            }
        }

        /// <summary>필요 유지 시간에 도달하여 완료되었으면 true입니다.</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>현재 프레임에서 유효 대상이 응시 중이면 true입니다.</summary>
        public bool IsTracking { get; private set; }

        /// <param name="requiredHoldSeconds">완료에 필요한 연속 유지 시간(초)입니다. 0 초과 유한 값이어야 합니다.</param>
        /// <param name="exitGraceSeconds">Hold를 유지하는 최대 이탈 허용 시간(초)입니다. 0 이상 유한 값이어야 합니다.</param>
        public EntrySafetyGazeHoldTimer(float requiredHoldSeconds = 3f, float exitGraceSeconds = 0.2f)
        {
            if (float.IsNaN(requiredHoldSeconds) || float.IsInfinity(requiredHoldSeconds) || requiredHoldSeconds <= 0f)
                throw new ArgumentException("requiredHoldSeconds must be a positive finite value.", nameof(requiredHoldSeconds));
            if (float.IsNaN(exitGraceSeconds) || float.IsInfinity(exitGraceSeconds) || exitGraceSeconds < 0f)
                throw new ArgumentException("exitGraceSeconds must be a non-negative finite value.", nameof(exitGraceSeconds));

            _requiredHoldSeconds = requiredHoldSeconds;
            _exitGraceSeconds = exitGraceSeconds;
        }

        /// <summary>
        /// 한 프레임의 시선 상태를 갱신합니다.
        /// 음수·NaN·Infinity deltaTime은 상태 변경 없이 거부합니다. 0은 허용하지만 진행하지 않습니다.
        /// </summary>
        /// <param name="isValidTargetVisible">이 프레임에서 유효 대상이 시야에 있으면 true입니다.</param>
        /// <param name="deltaTime">이번 프레임 경과 시간(초)입니다.</param>
        public void Update(bool isValidTargetVisible, float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                return;

            if (IsCompleted)
                return;

            if (isValidTargetVisible)
            {
                IsTracking = true;
                ElapsedExitSeconds = 0f;
                ElapsedHoldSeconds += deltaTime;

                if (ElapsedHoldSeconds >= _requiredHoldSeconds)
                {
                    ElapsedHoldSeconds = _requiredHoldSeconds;
                    IsCompleted = true;
                }
            }
            else
            {
                IsTracking = false;
                ElapsedExitSeconds += deltaTime;

                if (ElapsedExitSeconds >= _exitGraceSeconds)
                {
                    ElapsedHoldSeconds = 0f;
                    ElapsedExitSeconds = 0f;
                }
            }
        }

        /// <summary>타이머를 초기 상태로 되돌립니다.</summary>
        public void Reset()
        {
            ElapsedHoldSeconds = 0f;
            ElapsedExitSeconds = 0f;
            IsCompleted = false;
            IsTracking = false;
        }
    }
}
