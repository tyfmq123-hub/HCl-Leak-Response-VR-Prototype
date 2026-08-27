using System.Collections.Generic;

namespace JH.EntrySafety
{
    /// <summary>
    /// 각 단계에서 시선 완료가 필요한 대상 ID와 검증 메서드를 제공합니다.
    /// 7개 시선 단계만 매핑되며, 나머지 11개 단계는 None을 반환합니다.
    /// </summary>
    public static class EntrySafetyGazeTargetMap
    {
        // 명시적 매핑 테이블 — enum 정수 순서에 의존하지 않습니다.
        private static readonly Dictionary<EntrySafetyStep, EntrySafetyGazeTargetId> s_Map =
            new Dictionary<EntrySafetyStep, EntrySafetyGazeTargetId>
            {
                { EntrySafetyStep.CheckInitialWind,       EntrySafetyGazeTargetId.InitialWindsock },
                { EntrySafetyStep.CheckChangedWind,       EntrySafetyGazeTargetId.ChangedWindsock },
                { EntrySafetyStep.CheckPressure,          EntrySafetyGazeTargetId.PressureDisplay },
                { EntrySafetyStep.CheckTemperature,       EntrySafetyGazeTargetId.TemperatureDisplay },
                { EntrySafetyStep.CheckRunState,          EntrySafetyGazeTargetId.RunIndicator },
                { EntrySafetyStep.VerifyStopIndicator,    EntrySafetyGazeTargetId.StopIndicator },
                { EntrySafetyStep.VerifyPressureDecrease, EntrySafetyGazeTargetId.ReducedPressureDisplay },
            };

        /// <summary>
        /// 지정한 단계에서 시선 완료가 필요한 대상 ID를 반환합니다.
        /// 시선 단계가 아니면 <see cref="EntrySafetyGazeTargetId.None"/>을 반환합니다.
        /// </summary>
        public static EntrySafetyGazeTargetId GetRequiredTarget(EntrySafetyStep step)
        {
            return s_Map.TryGetValue(step, out var target) ? target : EntrySafetyGazeTargetId.None;
        }

        /// <summary>지정한 단계가 시선 완료 단계이면 true를 반환합니다.</summary>
        public static bool IsGazeStep(EntrySafetyStep step)
        {
            return s_Map.ContainsKey(step);
        }

        /// <summary>지정한 값이 EntrySafetyStep에 정의된 범위이면 true를 반환합니다.</summary>
        public static bool IsDefinedStep(EntrySafetyStep step)
        {
            return step >= EntrySafetyStep.SceneIntro && step <= EntrySafetyStep.ClearAccessRestriction;
        }

        /// <summary>지정한 값이 EntrySafetyGazeTargetId에 정의된 범위이면 true를 반환합니다.</summary>
        public static bool IsDefinedTargetId(EntrySafetyGazeTargetId targetId)
        {
            return targetId >= EntrySafetyGazeTargetId.None
                && targetId <= EntrySafetyGazeTargetId.ReducedPressureDisplay;
        }
    }
}
