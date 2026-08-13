using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRProject.Valves;

namespace VRProject.LeakResponse.Narration
{
    [DisallowMultipleComponent]
    public sealed class ValveProgressUI_Runtime : MonoBehaviour
    {
        ValveWheelClosureController controller;
        Transform fixedAnchor;
        CanvasGroup canvasGroup;
        TMP_Text titleText;
        TMP_Text percentText;
        Image barFill;
        RectTransform barFillRect;
        bool visible;

        const float PanelBottomClearance = 0.10f;
        const float ViewerClearance = 0.08f;
        const float CanvasScale = 0.002f;
        const float CanvasHeightUnits = 168f;
        const float BarInnerWidth = 462f;

        public ValveWheelClosureController Controller => controller;
        public Transform FixedAnchor => fixedAnchor;
        public bool IsVisible => visible;

        public void Configure(ValveWheelClosureController newController, string title,
            Transform fixedAnchorTransform, Transform cameraTransform)
        {
            if (controller != null)
                controller.OnClosurePercentChanged.RemoveListener(OnClosureChanged);

            controller = newController;
            fixedAnchor = fixedAnchorTransform;
            BuildVisual(title);
            CaptureFixedLocalAnchor(cameraTransform);

            if (controller != null)
            {
                controller.OnClosurePercentChanged.RemoveListener(OnClosureChanged);
                controller.OnClosurePercentChanged.AddListener(OnClosureChanged);
                OnClosureChanged(controller.ClosurePercent);
            }

            Hide();
        }

        void OnDestroy()
        {
            if (controller != null)
                controller.OnClosurePercentChanged.RemoveListener(OnClosureChanged);
        }

        void LateUpdate()
        {
            // The initial camera is used only to choose a readable fixed orientation.
            // Do not rotate this UI with later HMD movement: it must remain attached to
            // the valve root exactly like the physical equipment around it.
        }

        public void Show()
        {
            visible = true;
            gameObject.SetActive(true);
            if (canvasGroup != null)
                canvasGroup.alpha = 1f;
        }

        public void Hide()
        {
            visible = false;
            if (canvasGroup != null)
                canvasGroup.alpha = 0f;
            gameObject.SetActive(false);
        }

        void CaptureFixedLocalAnchor(Transform cameraTransform)
        {
            if (controller == null || fixedAnchor == null)
                return;

            Vector3 worldPosition;
            if (P102RuntimeUtility.TryGetRendererBounds(controller.gameObject, out Bounds bounds))
            {
                float panelHalfHeight = CanvasHeightUnits * CanvasScale * 0.5f;
                worldPosition = new Vector3(
                    bounds.center.x,
                    bounds.max.y + PanelBottomClearance + panelHalfHeight,
                    bounds.center.z);
            }
            else
                worldPosition = controller.transform.position + Vector3.up * 0.9f;

            Vector3 operatorSide = ResolveOperatorSideDirection(
                controller, worldPosition, cameraTransform);
            worldPosition += operatorSide * ViewerClearance;
            Quaternion worldRotation = P102RuntimeUtility.ResolveYawOnlyCanvasRotation(
                worldPosition, worldPosition + operatorSide, fixedAnchor.rotation);
            transform.SetParent(fixedAnchor, true);
            transform.SetPositionAndRotation(worldPosition, worldRotation);
            transform.localScale = Vector3.one * CanvasScale;

            Debug.Log(
                $"[P102 UI] {name} fixed anchor={P102RuntimeUtility.GetHierarchyPath(fixedAnchor)}, " +
                $"localPosition={transform.localPosition:F4}, localRotation={transform.localRotation.eulerAngles:F2}",
                this);
        }

        Vector3 ResolveOperatorSideDirection(ValveWheelClosureController valve,
            Vector3 worldPosition, Transform cameraTransform)
        {
            HingeJoint hinge = valve != null ? valve.GetComponent<HingeJoint>() : null;
            if (hinge != null)
            {
                Vector3 fromValveTowardOperator = Vector3.ProjectOnPlane(
                    -valve.transform.TransformDirection(hinge.axis), Vector3.up);
                if (fromValveTowardOperator.sqrMagnitude > 0.000001f)
                    return fromValveTowardOperator.normalized;
            }

            Vector3 towardCamera = cameraTransform != null
                ? Vector3.ProjectOnPlane(cameraTransform.position - worldPosition, Vector3.up)
                : Vector3.ProjectOnPlane(-fixedAnchor.forward, Vector3.up);
            return towardCamera.sqrMagnitude > 0.000001f
                ? towardCamera.normalized
                : Vector3.forward;
        }

        void OnClosureChanged(float percent)
        {
            float normalized = Mathf.Clamp01(percent * 0.01f);
            if (barFillRect != null)
                barFillRect.sizeDelta = new Vector2(BarInnerWidth * normalized, -8f);
            if (percentText != null)
                percentText.text = $"잠금 {Mathf.RoundToInt(normalized * 100f)}%";
        }

        void BuildVisual(string title)
        {
            if (canvasGroup != null)
                return;

            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 50;
            RectTransform rootRect = canvas.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(560f, CanvasHeightUnits);
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            TMP_FontAsset font = P102RuntimeUtility.LoadRuntimeFont();
            Image panel = CreateImage("Panel", transform, new Color(0.02f, 0.05f, 0.07f, 0.9f));
            Stretch(panel.rectTransform, Vector2.zero, Vector2.zero);

            titleText = CreateText("TitleText", transform, font, title, 34f);
            SetRect(titleText.rectTransform, new Vector2(0f, 43f), new Vector2(508f, 54f));

            Image background = CreateImage("BarBackground", transform, new Color(0.08f, 0.14f, 0.17f, 1f));
            SetRect(background.rectTransform, new Vector2(0f, -5f), new Vector2(470f, 38f));
            barFill = CreateImage("BarFill", background.transform, new Color(0.1f, 0.9f, 1f, 1f));
            barFillRect = barFill.rectTransform;
            barFillRect.anchorMin = new Vector2(0f, 0f);
            barFillRect.anchorMax = new Vector2(0f, 1f);
            barFillRect.pivot = new Vector2(0f, 0.5f);
            barFillRect.anchoredPosition = new Vector2(4f, 0f);
            barFillRect.sizeDelta = new Vector2(0f, -8f);

            percentText = CreateText("PercentText", transform, font, "0%", 34f);
            SetRect(percentText.rectTransform, new Vector2(0f, -52f), new Vector2(280f, 43f));

            P102RuntimeUtility.DisableGraphicRaycasts(gameObject);
            P102RuntimeUtility.SetIgnoreRaycastRecursively(gameObject);
        }

        static Image CreateImage(string objectName, Transform parent, Color color)
        {
            var gameObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            Image image = gameObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static TMP_Text CreateText(string objectName, Transform parent, TMP_FontAsset font, string value, float size)
        {
            var gameObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            gameObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = gameObject.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = Color.white;
            text.outlineColor = new Color32(0, 0, 0, 210);
            text.outlineWidth = 0.16f;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        static void SetRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }
    }
}
