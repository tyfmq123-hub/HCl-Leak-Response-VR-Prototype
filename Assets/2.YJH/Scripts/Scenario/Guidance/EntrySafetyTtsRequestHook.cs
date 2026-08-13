using System;
using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Provides the current instruction text to a future TTS integration without owning a speech SDK.</summary>
    public sealed class EntrySafetyTtsRequestHook : MonoBehaviour
    {
        public event Action<string> TtsRequested;

        public string LastRequestedText { get; private set; } = string.Empty;
        public int RequestCount { get; private set; }

        public void Request(EntrySafetyInstruction instruction)
        {
            if (instruction == null || !instruction.HasTts)
                return;

            LastRequestedText = instruction.TtsText;
            RequestCount++;
            TtsRequested?.Invoke(LastRequestedText);
        }
    }
}
