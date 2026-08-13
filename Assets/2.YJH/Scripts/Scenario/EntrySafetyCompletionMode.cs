namespace JH.EntrySafety
{
    /// <summary>단계 완료 방식을 정의합니다.</summary>
    public enum EntrySafetyCompletionMode
    {
        /// <summary>도움 없이 완료.</summary>
        Normal,

        /// <summary>힌트를 사용해 완료.</summary>
        WithHint,

        /// <summary>보조(자동) 완료.</summary>
        Assisted,
    }
}
