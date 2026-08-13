namespace JH.EntrySafety
{
    /// <summary>
    /// 시선 판정 대상의 식별자입니다.
    /// None은 대상 없음(비시선 단계)을 나타냅니다.
    /// </summary>
    public enum EntrySafetyGazeTargetId
    {
        None,
        InitialWindsock,
        ChangedWindsock,
        PressureDisplay,
        TemperatureDisplay,
        RunIndicator,
        StopIndicator,
        ReducedPressureDisplay,
    }
}
