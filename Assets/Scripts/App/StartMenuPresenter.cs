using TMPro;
using HClTraining.SharedXR;
using UnityEngine;

namespace HClTraining.App
{
    [DisallowMultipleComponent]
    public sealed class StartMenuPresenter : MonoBehaviour
    {
        static readonly Vector2 MenuSize = new Vector2(1200f, 720f);
        static readonly Color WarmPanel = new Color32(42, 40, 35, 242);
        static readonly Color WarmPrimary = new Color32(205, 151, 69, 255);
        static readonly Color WarmSecondary = new Color32(76, 73, 65, 250);
        static readonly Color WarmText = new Color32(244, 239, 226, 255);

        void Start()
        {
            var app = AppBootstrap.Instance;
            if (app == null)
            {
                Debug.LogError("[Start Menu] AppBootstrap이 없습니다. App Scene부터 실행하세요.", this);
                return;
            }

            var canvas = TrainingUIFactory.CreateWorldCanvas(transform, "StartTrainingCanvas", MenuSize, false);
            if (!WorldBoardUiPlacement.TryPlace(canvas.transform, app.MainCamera, "Board", MenuSize))
                TrainingUIFactory.PlaceFixedInView(canvas.transform, app.MainCamera, 3.2f);
            var panel = TrainingUIFactory.CreatePanel(canvas.transform, "SafetyTrainingPanel", MenuSize, Vector2.zero, WarmPanel);
            TrainingUIFactory.CreateText(panel.transform, "염산 누출 대응 VR 안전교육", new Vector2(1080f, 72f), new Vector2(0f, 285f), 48f,
                TextAlignmentOptions.Center, WarmText);
            TrainingUIFactory.CreateText(panel.transform, "훈련할 과정을 선택하세요", new Vector2(1000f, 45f), new Vector2(0f, 230f), 27f,
                TextAlignmentOptions.Center, new Color32(205, 198, 181, 255));

            CreateCard(panel.transform, app, TrainingMode.Full, "전체 훈련",
                "PPE 착용부터 누출 대응까지 전체 과정", 145f, true);
            CreateCard(panel.transform, app, TrainingMode.PPEOnly, "PPE 착용 교육",
                "개인보호구 확인 및 착용", 20f, false);
            CreateCard(panel.transform, app, TrainingMode.PreEntryOnly, "현장 진입 전 확인절차",
                "작업 전 안전 확인절차", -105f, false);
            CreateCard(panel.transform, app, TrainingMode.LeakResponseOnly, "P-102 염산 누출 대응",
                "밸브 차단과 흡착재를 이용한 누출 대응", -230f, false);
        }

        static void CreateCard(
            Transform parent, AppBootstrap app, TrainingMode mode,
            string title, string description, float y, bool primary)
        {
            var button = TrainingUIFactory.CreateButton(parent, title, new Vector2(1030f, 105f), new Vector2(0f, y),
                () => app.ScenarioFlow.StartTraining(mode), primary, primary ? 34f : 31f,
                primary ? WarmPrimary : WarmSecondary);
            var titleText = button.GetComponentInChildren<TMP_Text>();
            if (titleText != null)
                titleText.rectTransform.anchoredPosition = new Vector2(0f, 18f);
            TrainingUIFactory.CreateText(button.transform, description, new Vector2(930f, 34f), new Vector2(0f, -26f), 21f,
                TextAlignmentOptions.Center, primary ? new Color32(55, 45, 31, 255) : new Color32(218, 211, 194, 255));
        }
    }
}
