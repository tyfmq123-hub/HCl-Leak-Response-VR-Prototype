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
                    Same("현장에 접근하기 전에 염화수소 농도와 풍향을 확인하고, 누출 의심 설비의 운전 상태와 잔류 압력을 점검해야 합니다.")
                },
                {
                    EntrySafetyStep.EquipDetector,
                    Same("가방 위에서 Grip을 눌렀다 놓아 측정기를 올린 뒤, 측정기를 다시 Grip으로 잡으십시오.")
                },
                {
                    EntrySafetyStep.MeasureInitialHcl,
                    Same("측정기 화면을 보며 왼쪽 Trigger를 3초간 유지하여 현재 염화수소 농도를 측정하십시오.")
                },
                {
                    EntrySafetyStep.ConfirmInitialHcl,
                    Same("염화수소 농도가 높게 측정되었습니다. 지금은 현장에 접근하지 마십시오.")
                },
                {
                    EntrySafetyStep.CheckInitialWind,
                    Same("풍향계를 3초간 확인하여 유해 증기가 이동하는 방향을 확인하십시오.")
                },
                {
                    EntrySafetyStep.WaitWindChange,
                    Pair(
                        "현재 풍향에서는 유해 증기가 접근 위치로 유입될 수 있습니다.\n안전한 풍향으로 바뀔 때까지 대기하십시오.",
                        "현재 풍향에서는 유해 증기가 접근 위치로 유입될 수 있습니다. 안전한 풍향으로 바뀔 때까지 대기하십시오.")
                },
                {
                    EntrySafetyStep.CheckChangedWind,
                    Pair(
                        "풍향이 바뀌었습니다.\n변경된 풍향을 3초간 다시 확인하십시오.",
                        "풍향이 바뀌었습니다. 변경된 풍향을 3초간 다시 확인하십시오.")
                },
                {
                    EntrySafetyStep.MeasureReducedHcl,
                    Pair(
                        "풍향이 바뀌었더라도 진입 전 농도를 다시 확인해야 합니다.\n왼쪽 Trigger를 3초간 유지하여 염화수소 농도를 재측정하십시오.",
                        "풍향이 바뀌었더라도 진입 전 농도를 다시 확인해야 합니다. 왼쪽 Trigger를 3초간 유지하여 염화수소 농도를 재측정하십시오.")
                },
                {
                    EntrySafetyStep.ConfirmReducedHcl,
                    Same("염화수소 농도가 낮아졌습니다. 먼저 측정기를 가방에 수납하십시오.")
                },
                {
                    EntrySafetyStep.StoreDetector,
                    Same("왼손 Grip을 눌러 측정기를 가방에 수납하십시오.")
                },
                {
                    EntrySafetyStep.MoveToPT,
                    Pair(
                        "측정기 수납을 완료했습니다.\n오른손 A를 눌러 설비 상태 확인 패널로 이동한 뒤 패널을 확인하십시오.",
                        "측정기 수납을 완료했습니다. 오른손 A를 눌러 설비 상태 확인 패널로 이동한 뒤 패널을 확인하십시오.")
                },
                {
                    EntrySafetyStep.CheckPressure,
                    Pair(
                        "누출 의심 설비의 압력과 온도, 운전 상태를 순서대로 확인합니다.\n먼저 압력 표시를 2초간 확인하십시오.",
                        "누출 의심 설비의 압력과 온도, 운전 상태를 순서대로 확인합니다. 먼저 압력 표시를 2초간 확인하십시오.")
                },
                {
                    EntrySafetyStep.CheckTemperature,
                    Same("온도 표시를 2초간 확인하십시오.")
                },
                {
                    EntrySafetyStep.CheckRunState,
                    Pair(
                        "설비가 현재 운전 중인지 확인합니다.\nRUN 표시를 2초간 확인하십시오.",
                        "설비가 현재 운전 중인지 확인합니다. RUN 표시를 2초간 확인하십시오.")
                },
                {
                    EntrySafetyStep.StopPump,
                    Pair(
                        "설비가 운전 중입니다.\n해당 열의 STOP 버튼에 오른손을 넣고 오른쪽 Trigger를 눌러 펌프를 정지하십시오.",
                        "설비가 운전 중입니다. 해당 열의 STOP 버튼에 오른손을 넣고 오른쪽 Trigger를 눌러 펌프를 정지하십시오.")
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
                    EntrySafetyStep.ClearAccessRestriction,
                    EntrySafetyInstruction.NoTts("방호벽은 유지됩니다. 지정된 이동 지점으로 이동하십시오.")
                },
                {
                    EntrySafetyStep.MoveToNextArea,
                    Same("방호벽은 유지됩니다. 지정된 이동 지점에서 오른손 A를 눌러 누출 대응 구역으로 이동하십시오.")
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
