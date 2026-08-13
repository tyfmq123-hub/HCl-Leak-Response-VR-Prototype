using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JH.EntrySafety
{
    /// <summary>Displays TTS-only camera subtitles.</summary>
    public sealed class EntrySafetyGuidanceView : MonoBehaviour
    {
        // Keep these values identical to HClTraining.App.XRSubtitleHudLayout without
        // adding an App -> Stage assembly dependency.
        private const float SubtitleMaxWidth = 1200f;
        private const float SubtitleMaxHeight = 170f;
        private const float SubtitlePaddingX = 48f;
        private const float SubtitlePaddingY = 32f;
        private const float BackgroundInset = 4f;
        private const float ReadableFontSize = 48f;
        private const float SubtitleDistance = 1.2f;
        private const float SubtitleVerticalOffset = -0.16f;
        private const float SubtitlePanelWorldWidth = 0.72f;

        [SerializeField] private TMP_Text _instructionText;
        [SerializeField] private TMP_Text _taskText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private bool _followCameraAsSubtitle = true;
        [SerializeField] private Vector3 _subtitleLocalOffset = new Vector3(0f, -0.16f, 0.4f);
        [SerializeField, Min(0.00001f)] private float _subtitleWorldScale = 0.00042f;

        public Transform RootTransform => _instructionText == null ? null : _instructionText.transform.root;

        private void Awake()
        {
            // Physical head-relative pose. Do not derive this from the editor Game View
            // or an eye viewport edge: it must stay in the central binocular comfort zone.
            _subtitleLocalOffset = new Vector3(0f, SubtitleVerticalOffset, SubtitleDistance);
            _subtitleWorldScale = SubtitlePanelWorldWidth / SubtitleMaxWidth;

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
                        image.color = new Color(0f, 0f, 0f, 0.72f);
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

            var rootRect = root as RectTransform;
            var canvas = root.GetComponent<Canvas>();
            ApplyCameraLayout(canvas, rootRect, mainCamera);
        }

        private void FitBackgroundToText()
        {
            if (_instructionText == null)
                return;

            var root = RootTransform as RectTransform;
            if (root == null)
                return;

            root.sizeDelta = new Vector2(SubtitleMaxWidth, SubtitleMaxHeight);

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

        private void ApplyCameraLayout(Canvas canvas, RectTransform root, Camera camera)
        {
            if (canvas == null || root == null || camera == null)
                return;

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32000;
            root.sizeDelta = new Vector2(SubtitleMaxWidth, SubtitleMaxHeight);

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
            scaler.dynamicPixelsPerUnit = 10f;

            var headRotation = camera.transform.rotation;
            root.position = camera.transform.position + headRotation * _subtitleLocalOffset;
            root.rotation = headRotation;

            var parentScale = root.parent != null ? root.parent.lossyScale : Vector3.one;
            root.localScale = new Vector3(
                _subtitleWorldScale / Mathf.Max(0.00001f, Mathf.Abs(parentScale.x)),
                _subtitleWorldScale / Mathf.Max(0.00001f, Mathf.Abs(parentScale.y)),
                _subtitleWorldScale / Mathf.Max(0.00001f, Mathf.Abs(parentScale.z)));
        }

    }
}
