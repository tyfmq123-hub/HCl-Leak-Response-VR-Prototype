using System.Collections.Generic;

namespace JH.EntrySafety
{
    /// <summary>
    /// 단계별 화면 안내와 TTS 문구 카탈로그입니다.
    /// Goal 1 자막은 TTS 문구와 동일하게 표시합니다.
    /// </summary>
    public static class EntrySafetyInstructionCatalog
    {
        private static readonly Dictionary<EntrySafetyStep, EntrySafetyInstruction> s_Catalog =
            new Dictionary<EntrySafetyStep, EntrySafetyInstruction>
            {
                {
                    EntrySafetyStep.SceneIntro,
                    new EntrySafetyInstruction(
                        "현장에 접근하기 전에 풍향과 염화 수소 농도를 확인 해야합니다. 먼저 염화수소측정기를 꺼내십시오.",
                        "현장에 접근하기 전에 풍향과 염화 수소 농도를 확인 해야합니다. 먼저 염화수소측정기를 꺼내십시오.")
                },
                {
                    EntrySafetyStep.EquipDetector,
                    new EntrySafetyInstruction(
                        "왼손을 왼쪽 아래 가방으로 이동하여 Y버튼을 눌러 염화수소측정기를 꺼내십시오.",
                        "왼손을 왼쪽 아래 가방으로 이동하여 Y버튼을 눌러 염화수소측정기를 꺼내십시오.")
                },
                {
                    EntrySafetyStep.MeasureInitialHcl,
                    new EntrySafetyInstruction(
                        "왼손의 염화수소측정기 화면을 확인하면서 왼쪽 트리거 버튼을 3초간 유지하여 농도를 측정 하십시오.",
                        "왼손의 염화수소측정기 화면을 확인하면서 왼쪽 트리거 버튼을 3초간 유지하여 농도를 측정 하십시오.")
                },
                {
                    EntrySafetyStep.ConfirmInitialHcl,
                    new EntrySafetyInstruction(
                        "고농도 감지. 현장 접근을 보류하십시오.",
                        "현재 염화수소농도는 3.0ppm입니다. 농도가 높으므로 현장 접근을 보류 하십시오.")
                },
                {
                    EntrySafetyStep.CheckInitialWind,
                    new EntrySafetyInstruction(
                        "오른쪽 위에 있는 풍향계를 3초간 확인하여 가스의 이동 방향과 안전한 접근 방향을 판단하십시오.",
                        "오른쪽 위에 있는 풍향계를 3초간 확인하여 가스의 이동 방향과 안전한 접근 방향을 판단하십시오.")
                },
                {
                    EntrySafetyStep.WaitWindChange,
                    new EntrySafetyInstruction(
                        "현재 풍향에서는 염화수소가 포함된 유해 증기가 접근 위치로 유입될 수 있습니다. 풍향이 바뀔 때까지 풍향계를 계속 확인하십시오.",
                        "현재 풍향에서는 염화수소가 포함된 유해 증기가 접근 위치로 유입될 수 있습니다. 풍향이 바뀔 때까지 풍향계를 계속 확인하십시오.")
                },
                {
                    EntrySafetyStep.CheckChangedWind,
                    new EntrySafetyInstruction(
                        "풍향이 바뀌었습니다. 변경된 풍향계를 3초간 확인하십시오.",
                        "풍향이 바뀌었습니다. 변경된 풍향계를 3초간 확인하십시오.")
                },
                {
                    EntrySafetyStep.MeasureReducedHcl,
                    new EntrySafetyInstruction(
                        "풍향이 안전한 방향으로 바뀌었습니다. 왼손의 염화수소측정기 화면을 확인하면서 왼쪽 트리거 버튼을 3초간 유지하여 농도를 다시 측정하십시오.",
                        "풍향이 안전한 방향으로 바뀌었습니다. 왼손의 염화수소측정기 화면을 확인하면서 왼쪽 트리거 버튼을 3초간 유지하여 농도를 다시 측정하십시오.")
                },
                {
                    EntrySafetyStep.ConfirmReducedHcl,
                    new EntrySafetyInstruction(
                        "농도 감소 확인. 다음 안전 확인 절차를 진행할 수 있습니다.",
                        "현재 염화수소 농도는 0.8 ppm입니다. 농도가 낮아져 다음 안전 확인 절차를 진행할 수 있습니다.")
                },
                {
                    EntrySafetyStep.StoreDetector,
                    new EntrySafetyInstruction(
                        "B 버튼을 눌러 염화수소 측정기를 수납하십시오.",
                        "B 버튼을 눌러 염화수소 측정기를 수납하십시오.")
                },
                {
                    EntrySafetyStep.MoveToPT,
                    new EntrySafetyInstruction(
                        "A 버튼을 눌러 압력 및 온도 패널로 이동하십시오.",
                        "A 버튼을 눌러 압력 및 온도 패널로 이동하십시오.")
                },
                {
                    EntrySafetyStep.CheckPressure,
                    new EntrySafetyInstruction(
                        "현장에 접근하기 전에 P-102의 상태를 확인해야 합니다. 압력과 온도, 운전 상태를 확인하고, P-102가 운전 중이라면 안전을 위해 정지해야 합니다. 먼저 압력 상태를 확인하십시오.",
                        "현장에 접근하기 전에 P-102의 상태를 확인해야 합니다. 압력과 온도, 운전 상태를 확인하고, P-102가 운전 중이라면 안전을 위해 정지해야 합니다. 먼저 압력 상태를 확인하십시오.")
                },
                {
                    EntrySafetyStep.CheckTemperature,
                    new EntrySafetyInstruction(
                        "압력 상태를 확인했습니다. 다음으로 P-102의 온도 상태를 3초간 확인하십시오",
                        "압력 상태를 확인했습니다. 다음으로 P-102의 온도 상태를 3초간 확인하십시오")
                },
                {
                    EntrySafetyStep.CheckRunState,
                    new EntrySafetyInstruction(
                        "온도 상태를 확인했습니다. 다음으로 P-102가 현재 RUN 상태인지 3초간 확인하십시오.",
                        "온도 상태를 확인했습니다. 다음으로 P-102가 현재 RUN 상태인지 3초간 확인하십시오.")
                },
                {
                    EntrySafetyStep.StopPump,
                    new EntrySafetyInstruction(
                        "P-102가 RUN 상태로 운전 중입니다. 오른손을 파란색 영역에 위치시킨 뒤, 오른쪽 트리거 버튼을 눌러 P-102를 정지하십시오.",
                        "P-102가 RUN 상태로 운전 중입니다. 오른손을 파란색 영역에 위치시킨 뒤, 오른쪽 트리거 버튼을 눌러 P-102를 정지하십시오.")
                },
                {
                    EntrySafetyStep.VerifyStopIndicator,
                    new EntrySafetyInstruction(
                        "P-102가 정지되었습니다. STOP 상태가 정상적으로 표시되는지 3초간 확인하십시오.",
                        "P-102가 정지되었습니다. STOP 상태가 정상적으로 표시되는지 3초간 확인하십시오.")
                },
                {
                    EntrySafetyStep.WaitPressureDecrease,
                    new EntrySafetyInstruction(
                        "P-102 정지 후 배관 압력이 감소하고 있습니다. 압력계에서 압력이 충분히 낮아졌는지 3초간 확인하십시오.",
                        "P-102 정지 후 배관 압력이 감소하고 있습니다. 압력계에서 압력이 충분히 낮아졌는지 3초간 확인하십시오.")
                },
                {
                    EntrySafetyStep.VerifyPressureDecrease,
                    new EntrySafetyInstruction(
                        "압력계에서 압력이 충분히 낮아졌는지 3초간 확인하십시오.",
                        "압력계에서 압력이 충분히 낮아졌는지 3초간 확인하십시오.")
                },
                {
                    EntrySafetyStep.MoveToNextArea,
                    new EntrySafetyInstruction(
                        "A 버튼을 눌러 다음 누출 대응 구역으로 이동하십시오.",
                        "A 버튼을 눌러 다음 누출 대응 구역으로 이동하십시오.")
                },
            };

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
