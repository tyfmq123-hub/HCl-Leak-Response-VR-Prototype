using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace HClTraining.App
{
    static class GlobalPretendardFont
    {
        const string ResourcePath = "P102/Pretendard-SemiBold SDF";
        static TMP_FontAsset cachedFont;

        public static TMP_FontAsset GetFont()
        {
            if (cachedFont == null)
                cachedFont = Resources.Load<TMP_FontAsset>(ResourcePath);
            return cachedFont;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRuntimeState()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            cachedFont = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            for (var i = 0; i < SceneManager.sceneCount; i++)
                ApplyToScene(SceneManager.GetSceneAt(i));
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyToScene(scene);

        static void ApplyToScene(Scene scene)
        {
            var font = GetFont();
            if (font == null || !scene.IsValid() || !scene.isLoaded)
                return;

            foreach (var root in scene.GetRootGameObjects())
            {
                var texts = root.GetComponentsInChildren<TMP_Text>(true);
                for (var i = 0; i < texts.Length; i++)
                {
                    var text = texts[i];
                    if (text != null && text.font != font)
                        text.font = font;
                }
            }
        }
    }

    /// <summary>
    /// Shared camera-relative subtitle layout used by every training stage.
    /// The HUD remains owned by its stage scene and follows the active XR camera in world space,
    /// so unloading a stage cannot leave UI children under the persistent App camera.
    /// </summary>
    public static class XRSubtitleHudLayout
    {
        public const float CanvasWidth = 1200f;
        public const float CanvasHeight = 170f;
        public const float FontSize = 48f;
        // Keep the subtitle inside the central binocular comfort zone. These values are
        // physical world-space measurements relative to the tracked head, not Game View
        // pixels or a percentage of either eye's viewport.
        public const float CameraDistance = 1.2f;
        public const float CameraVerticalOffset = -0.16f;
        public const float PanelWorldWidth = 0.72f;
        public const int SortingOrder = 32000;

        public static readonly Vector2 CanvasSize = new Vector2(CanvasWidth, CanvasHeight);

        public static Camera ResolveActiveCamera(Camera cachedCamera = null)
        {
            var main = Camera.main;
            if (IsUsable(main))
                return main;
            if (IsUsable(cachedCamera))
                return cachedCamera;

            var cameras = Camera.allCameras;
            for (var i = 0; i < cameras.Length; i++)
                if (IsUsable(cameras[i])) return cameras[i];
            return null;
        }

        public static void Apply(Canvas canvas, RectTransform root, Camera camera)
        {
            if (canvas == null || root == null)
                return;

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;
            root.sizeDelta = CanvasSize;
            root.pivot = new Vector2(0.5f, 0.5f);

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
            scaler.dynamicPixelsPerUnit = 10f;

            if (!IsUsable(camera))
                return;

            var headRotation = camera.transform.rotation;
            var headOffset = new Vector3(0f, CameraVerticalOffset, CameraDistance);
            var worldScale = PanelWorldWidth / CanvasWidth;

            root.position = camera.transform.position + headRotation * headOffset;
            root.rotation = camera.transform.rotation;
            SetWorldScale(root, worldScale);
        }

        public static void ConfigurePanel(RectTransform panel)
        {
            if (panel == null)
                return;
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
        }

        public static void ConfigureText(TMP_Text text)
        {
            if (text == null)
                return;

            var rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = new Vector2(36f, 14f);
            rect.offsetMax = new Vector2(-36f, -14f);
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = false;
            text.fontSize = FontSize;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
        }

        static bool IsUsable(Camera camera)
        {
            return camera != null && camera.isActiveAndEnabled && camera.gameObject.activeInHierarchy;
        }

        static void SetWorldScale(Transform target, float worldScale)
        {
            var parent = target.parent;
            if (parent == null)
            {
                target.localScale = Vector3.one * worldScale;
                return;
            }

            var parentScale = parent.lossyScale;
            target.localScale = new Vector3(
                worldScale / Mathf.Max(0.00001f, Mathf.Abs(parentScale.x)),
                worldScale / Mathf.Max(0.00001f, Mathf.Abs(parentScale.y)),
                worldScale / Mathf.Max(0.00001f, Mathf.Abs(parentScale.z)));
        }
    }

    /// <summary>
    /// Conservative binocular-safe viewport used by persistent XR HUDs. The full
    /// rectangular Game View is wider than the area comfortably shared by both
    /// Quest eyes, so important controls stay inside this inset rectangle.
    /// </summary>
    public static class XRUiSafeAreaLayout
    {
        public static readonly Vector2 AnchorMin = new Vector2(0.22f, 0.18f);
        public static readonly Vector2 AnchorMax = new Vector2(0.78f, 0.82f);

        public static RectTransform Create(Transform canvas)
        {
            var go = new GameObject("XR_UI_SafeArea", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(canvas, false);
            rect.anchorMin = AnchorMin;
            rect.anchorMax = AnchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }
    }

    static class TrainingUIFactory
    {
        static TMP_FontAsset cachedFont;
        static readonly Color Navy = new Color32(12, 35, 64, 255);
        static readonly Color Blue = new Color32(25, 92, 155, 255);
        static readonly Color Yellow = new Color32(255, 196, 37, 255);
        static readonly Color Pale = new Color32(232, 241, 249, 255);

        public static Canvas CreateWorldCanvas(Transform owner, string name, Vector2 size, bool cameraAttached)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(owner, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 20f;
            scaler.referencePixelsPerUnit = 100f;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * (cameraAttached ? 0.00075f : 0.00125f);
            // XR controllers use the tracked-device raycaster. GraphicRaycaster
            // keeps the same world-space menu clickable with the mouse in Editor
            // PlayMode, which is also required for direct integration testing.
            var trackedRaycaster = go.AddComponent<TrackedDeviceGraphicRaycaster>();
            trackedRaycaster.ignoreReversedGraphics = false;
            var mouseRaycaster = go.AddComponent<GraphicRaycaster>();
            mouseRaycaster.ignoreReversedGraphics = false;
            return canvas;
        }

        public static void PlaceFixedInView(Transform root, Camera camera, float distance = 2.2f)
        {
            if (camera == null)
                return;
            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f)
                forward = camera.transform.forward;
            root.position = camera.transform.position + forward * distance;
            root.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public static bool PlaceOnNamedSurface(
            Transform root, Camera camera, string objectName, Vector2 canvasSize, float inset = 0.9f)
        {
            if (root == null || camera == null || string.IsNullOrWhiteSpace(objectName))
                return false;

            Transform surface = null;
            var transforms = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && transforms[i].name == objectName)
                {
                    surface = transforms[i];
                    break;
                }
            }
            if (surface == null)
                return false;

            var renderers = surface.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return false;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            var cameraForward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (cameraForward.sqrMagnitude < 0.01f)
                cameraForward = camera.transform.forward;
            var towardCamera = (camera.transform.position - bounds.center).normalized;
            // Keep the UI clearly in front of the slightly angled board mesh.
            // A tiny offset caused half of the Canvas to intersect the white face.
            root.position = bounds.center + towardCamera * 0.12f;
            root.rotation = Quaternion.LookRotation(cameraForward, Vector3.up);

            var widthScale = bounds.size.x / Mathf.Max(1f, canvasSize.x);
            var heightScale = bounds.size.y / Mathf.Max(1f, canvasSize.y);
            root.localScale = Vector3.one * Mathf.Min(widthScale, heightScale) * inset;
            return true;
        }

        public static Image CreatePanel(Transform parent, string name, Vector2 size, Vector2 position, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            // World-space parent/child surfaces must not share the same depth.
            // At the standard canvas scale this is 2.5 mm, not a layout change.
            rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, -2f);
            var image = go.GetComponent<Image>();
            image.color = color ?? new Color(Navy.r, Navy.g, Navy.b, 0.97f);
            return image;
        }

        public static TMP_Text CreateText(
            Transform parent, string text, Vector2 size, Vector2 position, float fontSize,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center, Color? color = null)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, -1f);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.font = GetFont();
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = color ?? Color.white;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static Button CreateButton(
            Transform parent, string label, Vector2 size, Vector2 position,
            Action onClick, bool primary = false, float fontSize = 38f, Color? backgroundColor = null)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, -2f);
            var image = go.GetComponent<Image>();
            var normalColor = backgroundColor ?? (primary ? Yellow : Blue);
            image.color = normalColor;
            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = normalColor;
            colors.highlightedColor = backgroundColor.HasValue
                ? new Color(normalColor.r, normalColor.g, normalColor.b, Mathf.Min(1f, normalColor.a + 0.18f))
                : primary ? new Color(1f, 220f / 255f, 100f / 255f, 1f) : new Color(45f / 255f, 135f / 255f, 215f / 255f, 1f);
            colors.pressedColor = backgroundColor.HasValue
                ? new Color(normalColor.r, normalColor.g, normalColor.b, Mathf.Min(1f, normalColor.a + 0.28f))
                : new Color(8f / 255f, 52f / 255f, 96f / 255f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color32(80, 91, 101, 180);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.targetGraphic = image;
            if (onClick != null)
                button.onClick.AddListener(() => onClick());
            CreateText(go.transform, label, size - new Vector2(28f, 16f), Vector2.zero, fontSize,
                TextAlignmentOptions.Center, primary ? Navy : Color.white);
            return button;
        }

        public static TMP_FontAsset GetFont()
        {
            if (cachedFont == null)
                cachedFont = GlobalPretendardFont.GetFont();
            return cachedFont;
        }

        public static Color PanelColor => Navy;
        public static Color PaleText => Pale;
    }

    [DisallowMultipleComponent]
    public sealed class CommonErrorUI : MonoBehaviour
    {
        GameObject root;
        TMP_Text message;

        public void Initialize(Camera camera)
        {
            var canvas = TrainingUIFactory.CreateWorldCanvas(transform, "CommonErrorCanvas", new Vector2(1000f, 380f), true);
            canvas.transform.SetParent(camera != null ? camera.transform : transform, false);
            canvas.transform.localPosition = new Vector3(0f, 0.05f, 1.2f);
            canvas.transform.localRotation = Quaternion.identity;
            var panel = TrainingUIFactory.CreatePanel(canvas.transform, "ErrorPanel", new Vector2(1000f, 380f), Vector2.zero,
                new Color32(92, 20, 25, 245));
            TrainingUIFactory.CreateText(panel.transform, "오류 안내", new Vector2(900f, 70f), new Vector2(0f, 115f), 45f);
            message = TrainingUIFactory.CreateText(panel.transform, string.Empty, new Vector2(900f, 170f), Vector2.zero, 30f);
            TrainingUIFactory.CreateButton(panel.transform, "확인", new Vector2(300f, 75f), new Vector2(0f, -125f), Hide, false, 30f);
            root = canvas.gameObject;
            root.SetActive(false);
        }

        public void Show(string text)
        {
            if (root == null)
                return;
            message.text = string.IsNullOrWhiteSpace(text) ? "알 수 없는 오류가 발생했습니다." : text;
            root.SetActive(true);
        }

        public void Hide()
        {
            if (root != null)
                root.SetActive(false);
        }
    }

}
