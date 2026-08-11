using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace HClTraining.App
{
    static class TrainingUIFactory
    {
        static TMP_FontAsset cachedFont;
        static readonly Color Navy = new Color32(12, 35, 64, 255);
        static readonly Color Blue = new Color32(25, 92, 155, 255);
        static readonly Color Yellow = new Color32(255, 196, 37, 255);
        static readonly Color Pale = new Color32(232, 241, 249, 255);

        public static Canvas CreateWorldCanvas(Transform owner, string name, Vector2 size, bool cameraAttached)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(owner, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * (cameraAttached ? 0.00075f : 0.00125f);
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
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

        public static Image CreatePanel(Transform parent, string name, Vector2 size, Vector2 position, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
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
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.font = GetFont();
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = color ?? Color.white;
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static Button CreateButton(
            Transform parent, string label, Vector2 size, Vector2 position,
            Action onClick, bool primary = false, float fontSize = 38f)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = go.GetComponent<Image>();
            image.color = primary ? Yellow : Blue;
            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = primary ? Yellow : Blue;
            colors.highlightedColor = primary ? new Color32(255, 220, 100, 255) : new Color32(45, 135, 215, 255);
            colors.pressedColor = new Color32(8, 52, 96, 255);
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
                cachedFont = Resources.Load<TMP_FontAsset>("PPE/PPE_HangulFont");
            if (cachedFont == null)
                cachedFont = Resources.Load<TMP_FontAsset>("P102/Pretendard-SemiBold SDF");
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

    [DisallowMultipleComponent]
    public sealed class CommonTrainingMenu : MonoBehaviour
    {
        AppBootstrap app;
        GameObject root;
        GameObject menuPanel;
        GameObject confirmPanel;
        Action pendingConfirmedAction;

        public void Initialize(AppBootstrap bootstrap)
        {
            app = bootstrap;
            var canvas = TrainingUIFactory.CreateWorldCanvas(transform, "CommonTrainingMenuCanvas", new Vector2(900f, 850f), true);
            canvas.transform.SetParent(app.MainCamera.transform, false);
            canvas.transform.localPosition = new Vector3(0.55f, -0.22f, 1.05f);
            canvas.transform.localRotation = Quaternion.identity;
            root = canvas.gameObject;

            TrainingUIFactory.CreateButton(canvas.transform, "훈련 메뉴", new Vector2(300f, 90f), new Vector2(260f, 350f), ToggleMenu, false, 30f);
            menuPanel = TrainingUIFactory.CreatePanel(canvas.transform, "MenuPanel", new Vector2(720f, 600f), new Vector2(0f, 5f)).gameObject;
            TrainingUIFactory.CreateText(menuPanel.transform, "훈련 메뉴", new Vector2(650f, 70f), new Vector2(0f, 230f), 44f);
            TrainingUIFactory.CreateButton(menuPanel.transform, "계속하기", new Vector2(580f, 85f), new Vector2(0f, 125f), ToggleMenu, true, 30f);
            TrainingUIFactory.CreateButton(menuPanel.transform, "현재 단계 다시 시작", new Vector2(580f, 85f), new Vector2(0f, 25f),
                () => Confirm("현재 단계 진행 상태를 초기화하고 다시 시작할까요?", () => app.ScenarioFlow.RestartCurrentStage()), false, 28f);
            TrainingUIFactory.CreateButton(menuPanel.transform, "처음 화면으로", new Vector2(580f, 85f), new Vector2(0f, -75f),
                () => Confirm("현재 진행 상태를 종료하고 시작 화면으로 돌아갈까요?", () => app.ScenarioFlow.ReturnToStart()), false, 28f);
            TrainingUIFactory.CreateButton(menuPanel.transform, "취소", new Vector2(580f, 85f), new Vector2(0f, -175f), ToggleMenu, false, 28f);
            menuPanel.SetActive(false);

            confirmPanel = TrainingUIFactory.CreatePanel(canvas.transform, "ConfirmPanel", new Vector2(760f, 390f), Vector2.zero,
                new Color32(22, 42, 67, 255)).gameObject;
            var confirmText = TrainingUIFactory.CreateText(confirmPanel.transform, string.Empty, new Vector2(670f, 150f), new Vector2(0f, 75f), 29f);
            confirmText.name = "ConfirmMessage";
            TrainingUIFactory.CreateButton(confirmPanel.transform, "확인", new Vector2(280f, 85f), new Vector2(-165f, -105f), ExecuteConfirmed, true, 29f);
            TrainingUIFactory.CreateButton(confirmPanel.transform, "취소", new Vector2(280f, 85f), new Vector2(165f, -105f), CancelConfirm, false, 29f);
            confirmPanel.SetActive(false);

            app.Session.Changed += RefreshVisibility;
            RefreshVisibility();
        }

        void ToggleMenu() => menuPanel.SetActive(!menuPanel.activeSelf);

        void Confirm(string text, Action action)
        {
            pendingConfirmedAction = action;
            var label = confirmPanel.transform.Find("ConfirmMessage")?.GetComponent<TMP_Text>();
            if (label != null)
                label.text = text;
            menuPanel.SetActive(false);
            confirmPanel.SetActive(true);
        }

        void ExecuteConfirmed()
        {
            var action = pendingConfirmedAction;
            CancelConfirm();
            action?.Invoke();
        }

        void CancelConfirm()
        {
            pendingConfirmedAction = null;
            confirmPanel.SetActive(false);
        }

        void RefreshVisibility()
        {
            var visible = app.Session.CurrentStage != TrainingStage.None && app.Session.CurrentStage != TrainingStage.Complete;
            root.SetActive(visible);
            if (!visible)
            {
                menuPanel.SetActive(false);
                confirmPanel.SetActive(false);
            }
        }

        void OnDestroy()
        {
            if (app != null && app.Session != null)
                app.Session.Changed -= RefreshVisibility;
        }
    }

    [DisallowMultipleComponent]
    public sealed class EquipmentMenuPresenter : MonoBehaviour
    {
        sealed class ButtonPair
        {
            public GameObject row;
            public Button deploy;
            public TMP_Text label;
        }

        readonly Dictionary<string, ButtonPair> rows = new Dictionary<string, ButtonPair>(StringComparer.Ordinal);
        AppBootstrap app;
        GameObject root;

        public void Initialize(AppBootstrap bootstrap)
        {
            app = bootstrap;
            var canvas = TrainingUIFactory.CreateWorldCanvas(transform, "EquipmentMenuCanvas", new Vector2(950f, 850f), true);
            canvas.transform.SetParent(app.MainCamera.transform, false);
            canvas.transform.localPosition = new Vector3(-0.62f, -0.12f, 1.1f);
            canvas.transform.localRotation = Quaternion.identity;
            root = canvas.gameObject;
            var panel = TrainingUIFactory.CreatePanel(canvas.transform, "EquipmentPanel", new Vector2(900f, 790f), Vector2.zero);
            TrainingUIFactory.CreateText(panel.transform, "사용 가능 장비", new Vector2(800f, 70f), new Vector2(0f, 325f), 42f);
            AddRow(panel.transform, PersistentEquipmentInventory.GasDetector, "염화수소 농도 측정기", 220f);
            AddRow(panel.transform, PersistentEquipmentInventory.AbsorbentSock, "흡착 Sock", 95f);
            AddRow(panel.transform, PersistentEquipmentInventory.AbsorbentPad, "흡착 Pad", -30f);
            AddRow(panel.transform, PersistentEquipmentInventory.WasteContainer, "폐기물 수거용기", -155f);
            AddRow(panel.transform, PersistentEquipmentInventory.RecoveryTongs, "회수용 집게", -280f);

            app.Session.Changed += Refresh;
            app.Inventory.Changed += Refresh;
            Refresh();
        }

        void AddRow(Transform parent, string id, string label, float y)
        {
            var row = TrainingUIFactory.CreatePanel(parent, id, new Vector2(800f, 105f), new Vector2(0f, y), new Color32(20, 58, 94, 255));
            TrainingUIFactory.CreateText(row.transform, label, new Vector2(360f, 80f), new Vector2(-190f, 0f), 27f, TextAlignmentOptions.MidlineLeft);
            var deploy = TrainingUIFactory.CreateButton(row.transform, "꺼내기", new Vector2(225f, 72f), new Vector2(135f, 0f),
                () => app.Inventory.DeployOrRecall(id), true, 26f);
            TrainingUIFactory.CreateButton(row.transform, "복구", new Vector2(150f, 72f), new Vector2(325f, 0f),
                () => app.Inventory.Recover(id), false, 25f);
            rows[id] = new ButtonPair
            {
                row = row.gameObject,
                deploy = deploy,
                label = deploy.GetComponentInChildren<TMP_Text>(),
            };
        }

        void Refresh()
        {
            var any = false;
            foreach (var pair in rows)
            {
                var visible = app.Session.IsEquipmentAvailable(pair.Key);
                pair.Value.row.SetActive(visible);
                if (!visible)
                    continue;
                any = true;
                pair.Value.label.text = app.Inventory.IsDeployed(pair.Key) ? "보관하기" : "꺼내기";
            }
            root.SetActive(any);
        }

        void OnDestroy()
        {
            if (app == null)
                return;
            app.Session.Changed -= Refresh;
            app.Inventory.Changed -= Refresh;
        }
    }
}
