using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JH.EntrySafety
{
    /// <summary>Displays TTS-only camera subtitles.</summary>
    public sealed class EntrySafetyGuidanceView : MonoBehaviour
    {
        private const float SubtitleMaxWidth = 960f;
        private const float SubtitleMaxHeight = 560f;
        private const float SubtitleMinWidth = 220f;
        private const float SubtitleMinHeight = 72f;
        private const float SubtitlePaddingX = 96f;
        private const float SubtitlePaddingY = 56f;
        private const float BackgroundInset = 4f;
        private const float ReadableFontSize = 41f;

        [SerializeField] private TMP_Text _instructionText;
        [SerializeField] private TMP_Text _taskText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private bool _followCameraAsSubtitle = true;
        [SerializeField] private Vector3 _subtitleLocalOffset = new Vector3(0f, -0.22f, 0.4f);
        [SerializeField, Min(0.00001f)] private float _subtitleWorldScale = 0.000288f;

        public Transform RootTransform => _instructionText == null ? null : _instructionText.transform.root;

        private void Awake()
        {
            // Movie-subtitle style: lower FOV, comfortable distance, readable scale.
            _subtitleLocalOffset = new Vector3(0f, -0.22f, 0.4f);
            _subtitleWorldScale = 0.000288f;

            if (!_followCameraAsSubtitle)
                return;

            // Keep Task/Status hidden, but leave the Background frame for subtitle readability.
            if (_taskText != null)
                _taskText.gameObject.SetActive(false);
            if (_statusText != null)
                _statusText.gameObject.SetActive(false);

            if (_instructionText != null)
            {
                ConfigureReadableText(_instructionText);
                ConfigureInstructionRect(_instructionText.rectTransform);
            }

            var root = RootTransform as RectTransform;
            if (root != null)
            {
                var background = root.Find("Background") as RectTransform;
                if (background != null)
                {
                    background.gameObject.SetActive(true);
                    ConfigureBackgroundRect(background);
                    var image = background.GetComponent<Image>();
                    if (image != null)
                        image.color = new Color(0f, 0f, 0f, 0.65f);
                }

                FitBackgroundToText();
            }
        }

        public void ShowInstruction(EntrySafetyInstruction instruction)
        {
            if (_instructionText == null)
                return;

            _instructionText.text = instruction == null
                ? string.Empty
                : instruction.HasTts
                    ? instruction.TtsText
                    : instruction.ScreenText;
            ConfigureReadableText(_instructionText);
            ConfigureInstructionRect(_instructionText.rectTransform);
            FitBackgroundToText();
        }

        public void SetVisible(bool visible)
        {
            var root = _instructionText == null ? null : _instructionText.transform.root;
            if (root != null)
                root.gameObject.SetActive(visible);
        }

        private void LateUpdate()
        {
            if (!_followCameraAsSubtitle)
                return;

            var root = RootTransform;
            var mainCamera = Camera.main;
            if (root == null || mainCamera == null)
                return;

            var cameraTransform = mainCamera.transform;
            root.position = cameraTransform.TransformPoint(_subtitleLocalOffset);
            root.rotation = Quaternion.LookRotation(root.position - cameraTransform.position, cameraTransform.up);
            root.localScale = Vector3.one * _subtitleWorldScale;
        }

        private void FitBackgroundToText()
        {
            if (_instructionText == null)
                return;

            var root = RootTransform as RectTransform;
            if (root == null)
                return;

            var content = string.IsNullOrWhiteSpace(_instructionText.text) ? " " : _instructionText.text;
            var textAreaWidth = Mathf.Max(100f, SubtitleMaxWidth - SubtitlePaddingX);

            // Measure with a known wrap width so long sentences expand height correctly.
            root.sizeDelta = new Vector2(SubtitleMaxWidth, SubtitleMinHeight);
            ConfigureInstructionRect(_instructionText.rectTransform);
            Canvas.ForceUpdateCanvases();
            _instructionText.ForceMeshUpdate(true);

            var preferred = _instructionText.GetPreferredValues(content, textAreaWidth, 0f);
            var renderedHeight = _instructionText.preferredHeight;
            var contentWidth = Mathf.Max(preferred.x, _instructionText.preferredWidth);
            var contentHeight = Mathf.Max(preferred.y, renderedHeight);

            var width = Mathf.Clamp(contentWidth + SubtitlePaddingX, SubtitleMinWidth, SubtitleMaxWidth);
            var height = Mathf.Clamp(contentHeight + SubtitlePaddingY, SubtitleMinHeight, SubtitleMaxHeight);
            root.sizeDelta = new Vector2(width, height);

            ConfigureInstructionRect(_instructionText.rectTransform);
            Canvas.ForceUpdateCanvases();
            _instructionText.ForceMeshUpdate(true);
        }

        private static void ConfigureReadableText(TMP_Text text)
        {
            if (text == null)
                return;

            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 1f, 1f, 0.98f);
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = false;
            text.fontSize = ReadableFontSize;
            text.lineSpacing = 12f;
            text.characterSpacing = 2f;
            text.wordSpacing = 4f;
            text.margin = new Vector4(10f, 8f, 10f, 8f);
        }

        private static void ConfigureInstructionRect(RectTransform rect)
        {
            if (rect == null)
                return;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(-SubtitlePaddingX, -SubtitlePaddingY);
        }

        private static void ConfigureBackgroundRect(RectTransform rect)
        {
            if (rect == null)
                return;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(-BackgroundInset * 2f, -BackgroundInset * 2f);
        }
    }
}
