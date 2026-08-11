using TMPro;
using UnityEngine;

namespace HClTraining.App
{
    [DisallowMultipleComponent]
    public sealed class StartMenuPresenter : MonoBehaviour
    {
        void Start()
        {
            var app = AppBootstrap.Instance;
            if (app == null)
            {
                Debug.LogError("[Start Menu] AppBootstrap이 없습니다. App Scene부터 실행하세요.", this);
                return;
            }

            var canvas = TrainingUIFactory.CreateWorldCanvas(transform, "StartTrainingCanvas", new Vector2(1500f, 1200f), false);
            TrainingUIFactory.PlaceFixedInView(canvas.transform, app.MainCamera);
            var panel = TrainingUIFactory.CreatePanel(canvas.transform, "SafetyTrainingPanel", new Vector2(1500f, 1200f), Vector2.zero);
            TrainingUIFactory.CreateText(panel.transform, "염산 누출 대응 VR 안전교육", new Vector2(1350f, 105f), new Vector2(0f, 500f), 66f);
            TrainingUIFactory.CreateText(panel.transform, "훈련할 과정을 선택하세요", new Vector2(1200f, 60f), new Vector2(0f, 420f), 34f,
                TextAlignmentOptions.Center, TrainingUIFactory.PaleText);

            CreateCard(panel.transform, app, TrainingMode.Full, "전체 훈련",
                "PPE 착용부터 누출 대응까지 전체 과정", 285f, true);
            CreateCard(panel.transform, app, TrainingMode.PPEOnly, "PPE 착용 교육",
                "개인보호구 확인 및 착용", 65f, false);
            CreateCard(panel.transform, app, TrainingMode.PreEntryOnly, "현장 진입 전 확인절차",
                "작업 전 안전 확인절차", -155f, false);
            CreateCard(panel.transform, app, TrainingMode.LeakResponseOnly, "P-102 염산 누출 대응",
                "밸브 차단과 흡착재를 이용한 누출 대응", -375f, false);
        }

        static void CreateCard(
            Transform parent, AppBootstrap app, TrainingMode mode,
            string title, string description, float y, bool primary)
        {
            var button = TrainingUIFactory.CreateButton(parent, title, new Vector2(1250f, 180f), new Vector2(0f, y),
                () => app.ScenarioFlow.StartTraining(mode), primary, primary ? 48f : 42f);
            var titleText = button.GetComponentInChildren<TMP_Text>();
            if (titleText != null)
                titleText.rectTransform.anchoredPosition = new Vector2(0f, 28f);
            TrainingUIFactory.CreateText(button.transform, description, new Vector2(1100f, 55f), new Vector2(0f, -42f), 27f,
                TextAlignmentOptions.Center, primary ? TrainingUIFactory.PanelColor : TrainingUIFactory.PaleText);
        }
    }
}
