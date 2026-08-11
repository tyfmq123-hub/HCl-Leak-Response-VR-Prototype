using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace HClTraining.App
{
    [DisallowMultipleComponent]
    public sealed class CompleteMenuPresenter : MonoBehaviour
    {
        void Start()
        {
            var app = AppBootstrap.Instance;
            if (app == null)
            {
                Debug.LogError("[Complete Menu] AppBootstrap이 없습니다.", this);
                return;
            }

            var canvas = TrainingUIFactory.CreateWorldCanvas(transform, "CompleteTrainingCanvas", new Vector2(1400f, 1050f), false);
            TrainingUIFactory.PlaceFixedInView(canvas.transform, app.MainCamera);
            var panel = TrainingUIFactory.CreatePanel(canvas.transform, "CompletePanel", new Vector2(1400f, 1050f), Vector2.zero);
            TrainingUIFactory.CreateText(panel.transform, "훈련을 완료했습니다", new Vector2(1250f, 100f), new Vector2(0f, 410f), 64f);
            TrainingUIFactory.CreateText(panel.transform,
                "실행 모드: " + TrainingSessionState.GetModeLabel(app.Session.SelectedMode),
                new Vector2(1150f, 60f), new Vector2(0f, 325f), 34f, TextAlignmentOptions.Center, TrainingUIFactory.PaleText);

            var completed = new List<string>();
            foreach (var stage in app.Session.CompletedStages)
                completed.Add("✓ " + TrainingSessionState.GetStageLabel(stage));
            TrainingUIFactory.CreateText(panel.transform,
                completed.Count > 0 ? string.Join("     ", completed) : "완료한 단계 정보가 없습니다.",
                new Vector2(1200f, 150f), new Vector2(0f, 210f), 31f);

            TrainingUIFactory.CreateButton(panel.transform, "동일 훈련 다시 하기", new Vector2(1050f, 130f), new Vector2(0f, 55f),
                () => app.ScenarioFlow.RepeatSelectedTraining(), true, 39f);
            TrainingUIFactory.CreateButton(panel.transform, "다른 훈련 선택하기", new Vector2(1050f, 130f), new Vector2(0f, -115f),
                () => app.ScenarioFlow.ReturnToStart(), false, 39f);
            TrainingUIFactory.CreateButton(panel.transform, "시작 화면으로", new Vector2(1050f, 130f), new Vector2(0f, -285f),
                () => app.ScenarioFlow.ReturnToStart(), false, 39f);
        }
    }
}
