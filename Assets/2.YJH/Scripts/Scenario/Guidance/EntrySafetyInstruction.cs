namespace JH.EntrySafety
{
    /// <summary>
    /// 단계별 화면 안내와 TTS 문구를 담는 읽기 전용 데이터입니다.
    /// WaitWindChange처럼 TTS가 없는 단계는 <see cref="EntrySafetyInstruction.NoTts"/>로 생성합니다.
    /// </summary>
    public sealed class EntrySafetyInstruction
    {
        /// <summary>화면에 표시할 안내 문자열입니다.</summary>
        public string ScreenText { get; }

        /// <summary>
        /// TTS로 재생할 문자열입니다.
        /// <see cref="HasTts"/>가 false인 단계에서는 이 값을 사용하지 않습니다.
        /// </summary>
        public string TtsText { get; }

        /// <summary>TTS 재생이 필요한 단계이면 true입니다. WaitWindChange처럼 TTS 없는 단계는 false입니다.</summary>
        public bool HasTts { get; }

        /// <summary>화면 안내와 TTS 문구를 모두 가진 안내를 생성합니다.</summary>
        public EntrySafetyInstruction(string screenText, string ttsText)
        {
            ScreenText = screenText ?? string.Empty;
            TtsText = ttsText ?? string.Empty;
            HasTts = true;
        }

        /// <summary>TTS가 없는 단계의 안내를 생성합니다(예: WaitWindChange).</summary>
        public static EntrySafetyInstruction NoTts(string screenText)
        {
            return new EntrySafetyInstruction(screenText);
        }

        private EntrySafetyInstruction(string screenText)
        {
            ScreenText = screenText ?? string.Empty;
            TtsText = string.Empty;
            HasTts = false;
        }

    }
}
