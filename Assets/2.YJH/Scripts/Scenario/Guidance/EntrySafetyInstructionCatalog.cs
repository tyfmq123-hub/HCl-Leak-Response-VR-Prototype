using System.Collections.Generic;

namespace JH.EntrySafety
{
    /// <summary>
    /// 단계별 화면 안내와 TTS 문구 카탈로그입니다.
    /// 자막과 TTS는 동일한 단계 의미와 순서를 유지합니다.
    /// </summary>
    public static class EntrySafetyInstructionCatalog
    {
        private static readonly Dictionary<EntrySafetyStep, EntrySafetyInstruction> s_Catalog =
            new Dictionary<EntrySafetyStep, EntrySafetyInstruction>
            {
                {
                    EntrySafetyStep.SceneIntro,
                    Same("염산 누출 현장에 접근하기 전에는 풍향과 염화수소 농도를 확인하여 접근 가능 여부를 판단해야 합니다.")
                },
                {
                    EntrySafetyStep.EquipDetector,
                    Same("왼손을 대응가방에 대고 왼손 Y를 눌러 염화수소 측정기를 꺼내십시오.")
                },
                {
                    EntrySafetyStep.MeasureInitialHcl,
                    Same("측정기 화면을 보며 왼쪽 Trigger를 3초간 유지하여 현재 염화수소 농도를 측정하십시오.")
                },
                {
                    EntrySafetyStep.ConfirmInitialHcl,
                    Same("염화수소 농도는 3.0 ppm으로 높게 측정되었습니다.")
                },
                {
                    EntrySafetyStep.CheckInitialWind,
                    Same("현장 접근을 보류하고 풍향계를 3초간 확인하여 유해 증기의 이동 방향을 확인하십시오.")
                },
                {
                    EntrySafetyStep.WaitWindChange,
                    Pair(
                        "현재 풍향에서는 염화수소가 포함된 유해 증기가 접근 위치로 유입될 수 있습니다.\n풍향이 바뀔 때까지 대기하십시오.",
                        "현재 풍향에서는 염화수소가 포함된 유해 증기가 접근 위치로 유입될 수 있습니다. 풍향이 바뀔 때까지 대기하십시오.")
                },
                {
                    EntrySafetyStep.CheckChangedWind,
                    Pair(
                        "풍향이 바뀌었습니다.\n변경된 풍향계를 3초간 확인하십시오.",
                        "풍향이 바뀌었습니다. 변경된 풍향계를 3초간 확인하십시오.")
                },
                {
                    EntrySafetyStep.MeasureReducedHcl,
                    Pair(
                        "풍향이 바뀌었더라도 현장에 접근하기 전 농도를 다시 확인해야 합니다.\n측정기 화면을 보며 왼쪽 트리거(Trigger)를 3초간 유지하여 염화수소 농도를 다시 측정하십시오.",
                        "풍향이 바뀌었더라도 현장에 접근하기 전 농도를 다시 확인해야 합니다. 측정기 화면을 보며 왼쪽 트리거(Trigger)를 3초간 유지하여 염화수소 농도를 다시 측정하십시오.")
                },
                {
                    EntrySafetyStep.ConfirmReducedHcl,
                    Same("염화수소 농도는 0.8 ppm으로 낮아졌습니다.")
                },
                {
                    EntrySafetyStep.StoreDetector,
                    Same("오른손 B를 눌러 측정기를 수납하십시오.")
                },
                {
                    EntrySafetyStep.MoveToPT,
                    Pair(
                        "농도와 풍향 확인을 마쳤습니다.\n현장에 접근하기 전에 P-102의 설비 상태를 확인하려면 오른손 A를 눌러 상태 확인 패널로 이동하십시오.",
                        "농도와 풍향 확인을 마쳤습니다. 현장에 접근하기 전에 P-102의 설비 상태를 확인하려면 오른손 A를 눌러 상태 확인 패널로 이동하십시오.")
                },
                {
                    EntrySafetyStep.CheckPressure,
                    Pair(
                        "P-102의 압력과 온도, 운전 상태를 순서대로 확인합니다.\n먼저 압력 표시를 2초간 확인하십시오.",
                        "P-102의 압력과 온도, 운전 상태를 순서대로 확인합니다. 먼저 압력 표시를 2초간 확인하십시오.")
                },
                {
                    EntrySafetyStep.CheckTemperature,
                    Same("온도 표시를 2초간 확인하십시오.")
                },
                {
                    EntrySafetyStep.CheckRunState,
                    Pair(
                        "P-102가 현재 운전 중인지 확인합니다.\nRUN 표시를 2초간 확인하십시오.",
                        "P-102가 현재 운전 중인지 확인합니다. RUN 표시를 2초간 확인하십시오.")
                },
                {
                    EntrySafetyStep.StopPump,
                    Pair(
                        "P-102가 운전 중입니다.\n안전한 누출 대응을 위해 오른손을 파란 영역에 넣고 오른쪽 Trigger를 눌러 펌프를 정지하십시오.",
                        "P-102가 운전 중입니다. 안전한 누출 대응을 위해 오른손을 파란 영역에 넣고 오른쪽 Trigger를 눌러 펌프를 정지하십시오.")
                },
                {
                    EntrySafetyStep.VerifyStopIndicator,
                    Same("펌프 정지 상태가 정상적으로 표시되는지 STOP 표시를 2초간 확인하십시오.")
                },
                {
                    EntrySafetyStep.WaitPressureDecrease,
                    Pair(
                        "펌프를 정지한 뒤에도 배관의 압력은 즉시 사라지지 않습니다.\n배관 압력이 감소할 때까지 기다리십시오.",
                        "펌프를 정지한 뒤에도 배관의 압력은 즉시 사라지지 않습니다. 배관 압력이 감소할 때까지 기다리십시오.")
                },
                {
                    EntrySafetyStep.VerifyPressureDecrease,
                    Pair(
                        "압력 감소가 끝났습니다.\n감소한 압력 표시를 2초간 확인하십시오.",
                        "압력 감소가 끝났습니다. 감소한 압력 표시를 2초간 확인하십시오.")
                },
                {
                    EntrySafetyStep.MoveToNextArea,
                    Same("빨간 접근 금지 표시가 해제되면 오른손 A를 눌러 누출 대응 구역으로 이동하십시오.")
                },
            };

        private static EntrySafetyInstruction Same(string text)
        {
            return new EntrySafetyInstruction(text, text);
        }

        private static EntrySafetyInstruction Pair(string screenText, string ttsText)
        {
            return new EntrySafetyInstruction(screenText, ttsText);
        }

        /// <summary>
        /// 지정한 단계의 안내를 반환합니다.
        /// 정의되지 않은 Step 값이면 null을 반환합니다.
        /// </summary>
        public static EntrySafetyInstruction GetInstruction(EntrySafetyStep step)
        {
            return s_Catalog.TryGetValue(step, out var instruction) ? instruction : null;
        }

        /// <summary>지정한 단계가 카탈로그에 존재하면 true를 반환합니다.</summary>
        public static bool Contains(EntrySafetyStep step)
        {
            return s_Catalog.ContainsKey(step);
        }

        /// <summary>카탈로그에 등록된 총 단계 수입니다.</summary>
        public static int Count => s_Catalog.Count;
    }
}
