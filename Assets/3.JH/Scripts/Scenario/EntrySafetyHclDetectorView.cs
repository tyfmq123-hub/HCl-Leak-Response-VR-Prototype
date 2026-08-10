using TMPro;
using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Minimal display for the temporary HCl detector prototype.</summary>
    public sealed class EntrySafetyHclDetectorView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _displayText;

        public Transform DisplayTransform => _displayText == null ? null : _displayText.transform;

        public void ShowReady(float? lastConfirmedPpm = null)
        {
            SetText(lastConfirmedPpm.HasValue
                ? $"HCl {lastConfirmedPpm.Value:0.0} ppm\nREADY"
                : "READY\nHCl --.- ppm");
        }

        public void ShowMeasuring(float progress01)
        {
            SetText($"MEASURING...\n{Mathf.Clamp01(progress01) * 3f:0.0} / 3.0 s");
        }

        public void ShowInitialResult(float ppm)
        {
            SetText($"HCl {ppm:0.0} ppm\nHIGH");
        }

        public void ShowReducedMeasuring(float progress01, float initialPpm, float reducedPpm)
        {
            var currentPpm = Mathf.Lerp(initialPpm, reducedPpm, Mathf.Clamp01(progress01));
            SetText($"MEASURING...\n{currentPpm:0.0} ppm");
        }

        public void ShowReducedResult(float ppm)
        {
            SetText($"HCl {ppm:0.0} ppm\nLOWER");
        }

        private void SetText(string value)
        {
            if (_displayText != null)
                _displayText.text = value;
        }
    }
}
