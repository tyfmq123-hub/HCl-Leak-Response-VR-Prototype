using TMPro;
using HClTraining.SharedXR;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace JH.EntrySafety
{
    /// <summary>
    /// Builds Title menu World Space UI to match the SafeMeta mock.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TitleMenuController))]
    public sealed class TitleMenuUiBuilder : MonoBehaviour
    {
        const string FontPretendardGuid = "36c35605027e2434aa02e7ab933b2793";
        const string FontNotoGuid = "4a8e8112fda0153498c569efd55c8d93";

        static readonly Color BgColor = new Color(0.13f, 0.13f, 0.13f, 0.94f);
        static readonly Color HoverOrange = new Color(1f, 0.48f, 0.12f, 1f);
        static readonly Color IdleBorder = Color.white;

        [SerializeField] TMP_FontAsset titleFont;
        [SerializeField] TMP_FontAsset bodyFont;
        [SerializeField] Sprite buttonFrameSprite;
        [SerializeField] Sprite backgroundPanelSprite;
        [SerializeField] bool buildOnAwake = true;

        TitleMenuController _controller;

        void Awake()
        {
            _controller = GetComponent<TitleMenuController>();
            if (!buildOnAwake)
                return;

            // Always rebuild so domain-reload / script edits don't keep a stale orange canvas.
            RebuildNow();
        }

        [ContextMenu("Rebuild Title Menu UI")]
        public void RebuildNow()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child != null && child.name == "TitleMenuCanvas")
                {
                    if (Application.isPlaying)
                        Destroy(child.gameObject);
                    else
                        DestroyImmediate(child.gameObject);
                }
            }

            // Next frame build if Destroy was deferred.
            if (Application.isPlaying)
                StartCoroutine(BuildNextFrame());
            else
            {
                ResolveAssets();
                Build();
            }
        }

        System.Collections.IEnumerator BuildNextFrame()
        {
            yield return null;
            if (transform.Find("TitleMenuCanvas") != null)
                yield break;
            ResolveAssets();
            Build();
        }

        void ResolveAssets()
        {
#if UNITY_EDITOR
            if (titleFont == null)
                titleFont = LoadAsset<TMP_FontAsset>(FontPretendardGuid);
            if (bodyFont == null)
                bodyFont = LoadAsset<TMP_FontAsset>(FontNotoGuid);
            if (buttonFrameSprite == null)
                buttonFrameSprite = LoadSprite("Assets/3.JH/UI/TitleMenu/T_UI_TitleMenu_BtnFrame_01.png");
            if (backgroundPanelSprite == null)
                backgroundPanelSprite = LoadSprite("Assets/3.JH/UI/TitleMenu/T_UI_TitleMenu_BgPanel_01.png");
#endif
            // Prefer a single Hangul-capable font to avoid stacked fallback glyphs.
            if (bodyFont == null)
                bodyFont = titleFont;
            if (titleFont == null)
                titleFont = bodyFont;
        }

#if UNITY_EDITOR
        static T LoadAsset<T>(string guid) where T : Object
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }

        static Sprite LoadSprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);
#endif

        void Build()
        {
            // Closer to mock card proportions (less overly-tall empty space).
            var canvasGo = Create("TitleMenuCanvas", transform);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 1f;
            scaler.referencePixelsPerUnit = 100f;
            // Quest controller rays require the XR tracked-device raycaster.
            // Keep GraphicRaycaster as well so the menu remains clickable with
            // a mouse while testing the Title scene in the Editor.
            var trackedRaycaster = canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();
            trackedRaycaster.ignoreReversedGraphics = false;
            var mouseRaycaster = canvasGo.AddComponent<GraphicRaycaster>();
            mouseRaycaster.ignoreReversedGraphics = false;

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(1000f, 900f);
            canvasGo.transform.localPosition = Vector3.zero;
            canvasGo.transform.localRotation = Quaternion.identity;
            canvasGo.transform.localScale = Vector3.one * 0.00125f;

            var background = Create("Background", canvasGo.transform);
            StretchFull(background.GetComponent<RectTransform>());
            var bg = background.AddComponent<Image>();
            bg.sprite = backgroundPanelSprite != null ? backgroundPanelSprite : GetUiSprite();
            bg.type = Image.Type.Sliced;
            bg.color = BgColor;
            bg.raycastTarget = true;

            // Primary hangul font: Noto if available (more complete), else Pretendard.
            var hangul = bodyFont != null ? bodyFont : titleFont;
            var display = titleFont != null ? titleFont : hangul;

            CreateText(
                canvasGo.transform,
                "Title",
                "산업 안전 VR",
                new Vector2(0f, 300f),
                new Vector2(860f, 96f),
                80f,
                FontStyles.Bold,
                display,
                TextAlignmentOptions.Center);

            CreateText(
                canvasGo.transform,
                "Subtitle",
                "HCl 누출 비상대응 교육",
                new Vector2(0f, 230f),
                new Vector2(860f, 52f),
                40f,
                FontStyles.Normal,
                hangul,
                TextAlignmentOptions.Center);

            var eduFrame = BuildModeButton(
                canvasGo.transform,
                "StartEducationButton",
                new Vector2(0f, 20f),
                "교육 시작",
                "전체 교육 또는 단계별 교육을 선택합니다",
                hangul,
                display,
                () => _controller.SelectEducation());

            CreateExitButton(canvasGo.transform, hangul);
            CreateText(
                canvasGo.transform,
                "SafeMeta",
                "SafeMeta",
                new Vector2(340f, -370f),
                new Vector2(220f, 36f),
                24f,
                FontStyles.Bold,
                display,
                TextAlignmentOptions.Right);

            _controller.BindVisuals(eduFrame, null, null, null);
            WorldBoardUiPlacement.TryPlace(
                canvasGo.transform, Camera.main, "Board", new Vector2(1000f, 900f));
        }

        Image BuildModeButton(
            Transform canvas,
            string name,
            Vector2 anchoredPos,
            string title,
            string desc,
            TMP_FontAsset body,
            TMP_FontAsset display,
            UnityEngine.Events.UnityAction onClick)
        {
            var buttonGo = Create(name, canvas);
            var buttonRt = buttonGo.GetComponent<RectTransform>();
            buttonRt.anchorMin = buttonRt.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRt.pivot = new Vector2(0.5f, 0.5f);
            buttonRt.sizeDelta = new Vector2(780f, 128f);
            buttonRt.anchoredPosition = anchoredPos;

            var hit = buttonGo.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.01f);
            hit.raycastTarget = true;

            var frameGo = Create("Frame", buttonGo.transform);
            StretchFull(frameGo.GetComponent<RectTransform>());
            var frame = frameGo.AddComponent<Image>();
            frame.sprite = buttonFrameSprite != null ? buttonFrameSprite : GetUiSprite();
            frame.type = Image.Type.Sliced;
            frame.color = IdleBorder;
            frame.raycastTarget = false;

            var button = buttonGo.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            button.onClick.AddListener(onClick);

            var hover = buttonGo.AddComponent<TitleMenuFrameHover>();
            hover.Configure(frame, IdleBorder, HoverOrange);

            CreateText(
                buttonGo.transform,
                "Label",
                title,
                new Vector2(0f, 22f),
                new Vector2(700f, 50f),
                38f,
                FontStyles.Bold,
                display,
                TextAlignmentOptions.Center);

            CreateText(
                buttonGo.transform,
                "Desc",
                desc,
                new Vector2(0f, -24f),
                new Vector2(700f, 36f),
                24f,
                FontStyles.Normal,
                body,
                TextAlignmentOptions.Center);

            return frame;
        }

        void CreateExitButton(Transform canvas, TMP_FontAsset font)
        {
            var go = Create("ExitButton", canvas);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(180f, 52f);
            rt.anchoredPosition = new Vector2(0f, -300f);

            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.001f);
            img.raycastTarget = true;
            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => _controller.ClickExit());

            CreateText(
                go.transform,
                "ExitLabel",
                "종료",
                Vector2.zero,
                new Vector2(180f, 52f),
                30f,
                FontStyles.Normal,
                font,
                TextAlignmentOptions.Center);
        }

        static TMP_Text CreateText(
            Transform parent,
            string name,
            string text,
            Vector2 anchoredPos,
            Vector2 size,
            float fontSize,
            FontStyles style,
            TMP_FontAsset font,
            TextAlignmentOptions align,
            Vector2? anchor = null,
            Vector2? pivot = null)
        {
            var go = Create(name, parent);
            var rt = go.GetComponent<RectTransform>();
            var a = anchor ?? new Vector2(0.5f, 0.5f);
            var p = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchorMin = rt.anchorMax = a;
            rt.pivot = p;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = align;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.richText = false;
            tmp.characterSpacing = 0f;
            tmp.lineSpacing = 0f;
            if (font != null)
            {
                tmp.font = font;
                // Avoid invisible dual-draw from fallback stacking on missing glyphs.
                tmp.fontSharedMaterial = font.material;
            }

            return tmp;
        }

        static GameObject Create(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return go;
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        static Sprite GetUiSprite()
        {
            return Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        }
    }
}
