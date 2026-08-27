namespace JH.EntrySafety
{
    /// <summary>현장 진입 안전확인 시나리오의 단계를 정의합니다.</summary>
    public enum EntrySafetyStep
    {
        SceneIntro,
        CheckInitialWind,
        EquipDetector,
        MeasureInitialHcl,
        ConfirmInitialHcl,
        WaitWindChange,
        CheckChangedWind,
        MeasureReducedHcl,
        ConfirmReducedHcl,
        StoreDetector,
        MoveToPT,
        CheckPressure,
        CheckTemperature,
        CheckRunState,
        StopPump,
        VerifyStopIndicator,
        VerifyPressureDecrease,
        MoveToNextArea,
        // Appended to preserve the serialized numeric values of existing steps.
        WaitPressureDecrease,
        ClearAccessRestriction,
    }
}
