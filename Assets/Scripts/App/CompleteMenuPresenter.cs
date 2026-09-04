using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HClTraining.App
{
    [DisallowMultipleComponent]
    public sealed class CompleteMenuPresenter : MonoBehaviour
    {
        const string CompletionVoiceResourcePath = "App/TTS/CompletionTrainingComplete";

        static readonly Color Navy = new Color32(15, 42, 70, 255);
        static readonly Color LightBackground = new Color32(239, 243, 245, 255);
        static readonly Color White = new Color32(252, 253, 253, 255);
        static readonly Color MutedText = new Color32(70, 86, 101, 255);
        static readonly Color Teal = new Color32(0, 151, 145, 255);
        static readonly Color Orange = new Color32(224, 126, 42, 255);
        static readonly Color Divider = new Color32(210, 218, 224, 255);
        static readonly Color Disabled = new Color32(116, 126, 135, 255);

        static readonly (string Id, string Label)[] QuickFeedbackOptions =
        {
            ("interaction_difficult", "조작이 어려웠어요"),
            ("guidance_insufficient", "안내가 부족했어요"),
            ("equipment_confusing", "장비 사용이 헷갈렸어요"),
            ("movement_uncomfortable", "이동이 불편했어요"),
            ("sequence_unclear", "진행 순서를 모르겠어요"),
            ("runtime_error", "오류가 발생했어요"),
            ("no_problem", "특별한 문제 없었어요"),
        };

        AppBootstrap app;
        GameObject completionPanel;
        GameObject confirmationRoot;
        GameObject detailOverlay;
        Transform detailContent;
        GameObject feedbackOverlay;
        Transform feedbackContent;
        TMP_Text feedbackStatus;
        readonly HashSet<string> selectedQuickFeedback = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, Button> quickFeedbackButtons = new Dictionary<string, Button>();
        readonly Dictionary<string, string> quickFeedbackLabels = new Dictionary<string, string>();
        readonly Dictionary<TrainingStage, Button> feedbackTargetButtons =
            new Dictionary<TrainingStage, Button>();
        readonly Dictionary<TrainingStage, string> feedbackTargetLabels =
            new Dictionary<TrainingStage, string>();
        TrainingStage feedbackTarget;
        bool feedbackTargetSelected;
        AudioSource completionVoiceSource;
        bool built;
        bool completionVoicePlayed;
        bool actionPending;

        void Start()
        {
            StartCoroutine(InitializeAfterCompletionTransition());
        }

        IEnumerator InitializeAfterCompletionTransition()
        {
            // SceneFlow records and saves completion immediately after the additive
            // Complete scene activates. Wait one frame so the result UI reads that
            // finalized snapshot regardless of Unity's scene Start ordering.
            yield return null;

            if (built)
                yield break;

            app = AppBootstrap.Instance;
            if (app == null)
            {
                Debug.LogError("[Complete Menu] AppBootstrap이 없습니다.", this);
                yield break;
            }

            if (!app.Session.HasEndedSelectedTraining || app.Session.CompletionResult == null)
            {
                Debug.LogWarning(
                    "[Complete Menu] 선택한 Stage가 종료되지 않아 결과 화면을 표시하지 않습니다.",
                    this);
                yield break;
            }

            built = true;
            BuildCompletionScreen();
            if (app.Session.CompletionResult.TrainingCompleted)
                StartCoroutine(PlayCompletionVoiceOnce());
        }

        void BuildCompletionScreen()
        {
            var canvasSize = new Vector2(1500f, 1150f);
            var canvas = TrainingUIFactory.CreateWorldCanvas(
                transform,
                "CompleteTrainingCanvas",
                canvasSize,
                false);
            TrainingUIFactory.PlaceFixedInView(canvas.transform, app.MainCamera, 2.05f);

            TrainingUIFactory.CreatePanel(
                canvas.transform,
                "CompletionFrame",
                canvasSize,
                Vector2.zero,
                Navy);
            var panel = TrainingUIFactory.CreatePanel(
                canvas.transform,
                "CompletionPanel",
                new Vector2(1450f, 1100f),
                Vector2.zero,
                LightBackground);
            completionPanel = panel.gameObject;

            TrainingUIFactory.CreateText(
                panel.transform,
                GetResultTitle(),
                new Vector2(1280f, 90f),
                new Vector2(0f, 470f),
                62f,
                TextAlignmentOptions.Center,
                Navy);
            TrainingUIFactory.CreateText(
                panel.transform,
                app.Session.CompletionResult.TrainingCompleted
                    ? "필수 교육 절차를 모두 수행했습니다."
                    : "다시 확인할 항목이 있습니다.",
                new Vector2(1200f, 64f),
                new Vector2(0f, 400f),
                34f,
                TextAlignmentOptions.Center,
                app.Session.CompletionResult.TrainingCompleted ? Teal : Orange);

            var checklist = BuildChecklist(panel.transform);
            var resultPanel = BuildResultPanel(panel.transform);
            if (!resultPanel.activeSelf)
                checklist.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 48f);

            TrainingUIFactory.CreateButton(
                panel.transform,
                "처음부터 다시하기",
                new Vector2(570f, 118f),
                new Vector2(-265f, -482f),
                ShowRestartConfirmation,
                false,
                32f,
                Teal);
            TrainingUIFactory.CreateButton(
                panel.transform,
                "교육 종료",
                new Vector2(570f, 118f),
                new Vector2(265f, -482f),
                ExitTraining,
                false,
                32f,
                Navy);

            BuildRestartConfirmation(canvas.transform);
            BuildFeedbackActions(panel.transform);
            BuildStageDetailOverlay(canvas.transform);
            BuildQuickFeedbackOverlay(canvas.transform);
        }

        string GetResultTitle()
        {
            var completed = app.Session.CompletionResult.TrainingCompleted;
            if (app.Session.SelectedMode == TrainingMode.Full)
                return completed ? "HCl 누출 대응 교육 완료" : "HCl 누출 대응 교육 결과";

            var stage = TrainingSessionState.FirstStageFor(app.Session.SelectedMode);
            var label = stage == TrainingStage.PPE
                ? "PPE 교육"
                : stage == TrainingStage.PreEntry ? "현장 진입 교육" : "누출 대응 교육";
            return label + (completed ? " 완료" : " 결과");
        }

        GameObject BuildChecklist(Transform parent)
        {
            var checklist = TrainingUIFactory.CreatePanel(
                parent,
                "CompletionChecklist",
                new Vector2(670f, 500f),
                new Vector2(-350f, 82f),
                White);
            var singleStage = app.Session.SelectedMode != TrainingMode.Full;
            var selectedStage = singleStage
                ? TrainingSessionState.FirstStageFor(app.Session.SelectedMode)
                : TrainingStage.None;
            var selectedResult = singleStage ? app.Session.GetStageResult(selectedStage) : null;
            TrainingUIFactory.CreateText(
                checklist.transform,
                singleStage && selectedResult != null
                    ? selectedResult.StageName + " 세부 검사"
                    : "Stage별 교육 결과",
                new Vector2(580f, 60f),
                new Vector2(0f, 200f),
                34f,
                TextAlignmentOptions.Left,
                Navy);
            TrainingUIFactory.CreatePanel(
                checklist.transform,
                "ChecklistDivider",
                new Vector2(580f, 3f),
                new Vector2(0f, 163f),
                Divider);

            if (!singleStage)
            {
                CreateStageSummaryRow(checklist.transform, TrainingStage.PPE, 102f);
                CreateStageSummaryRow(checklist.transform, TrainingStage.PreEntry, 8f);
                CreateStageSummaryRow(checklist.transform, TrainingStage.LeakResponse, -86f);
            }
            else if (selectedResult != null)
            {
                var count = Mathf.Max(1, selectedResult.CheckItems.Count);
                var spacing = Mathf.Min(42f, 330f / count);
                var startY = 138f - spacing * 0.5f;
                for (var i = 0; i < selectedResult.CheckItems.Count; i++)
                    CreateCheckItemRow(checklist.transform, selectedResult.CheckItems[i], startY - i * spacing, spacing);
            }
            return checklist.gameObject;
        }

        GameObject BuildResultPanel(Transform parent)
        {
            var result = TrainingUIFactory.CreatePanel(
                parent,
                "TrainingResult",
                new Vector2(670f, 500f),
                new Vector2(350f, 82f),
                White);
            TrainingUIFactory.CreateText(
                result.transform,
                "교육 결과",
                new Vector2(580f, 60f),
                new Vector2(0f, 200f),
                37f,
                TextAlignmentOptions.Left,
                Navy);
            TrainingUIFactory.CreatePanel(
                result.transform,
                "ResultDivider",
                new Vector2(580f, 3f),
                new Vector2(0f, 163f),
                Divider);

            var trainingResult = app.Session.CompletionResult;
            if (trainingResult == null)
            {
                result.gameObject.SetActive(false);
                return result.gameObject;
            }

            if (app.Session.SelectedMode == TrainingMode.Full)
            {
                CreateResultRow(result.transform, "PPE 교육", FormatDuration(trainingResult.PpeDurationSeconds), 112f, Navy);
                CreateResultRow(result.transform, "현장 진입", FormatDuration(trainingResult.PreEntryDurationSeconds), 48f, Navy);
                CreateResultRow(result.transform, "누출 대응", FormatDuration(trainingResult.LeakResponseDurationSeconds), -16f, Navy);
                TrainingUIFactory.CreatePanel(result.transform, "TotalDurationDivider", new Vector2(580f, 3f),
                    new Vector2(0f, -60f), Divider);
                CreateResultRow(result.transform, "총 소요 시간", FormatDuration(trainingResult.TotalDurationSeconds), -112f, Teal);
            }
            else
            {
                var stage = TrainingSessionState.FirstStageFor(app.Session.SelectedMode);
                var stageResult = app.Session.GetStageResult(stage);
                CreateResultRow(result.transform, "Stage 소요 시간", FormatDuration(stageResult.StageDurationSeconds), 112f, Teal);
                CreateResultRow(result.transform, "오류 횟수", stageResult.ErrorCount.ToString(), 35f, Navy);
                TrainingUIFactory.CreatePanel(result.transform, "MissingDivider", new Vector2(580f, 3f),
                    new Vector2(0f, -32f), Divider);
                CreateResultRow(result.transform, "확인 필요 항목", stageResult.MissingRequiredItems.Count.ToString(), -95f,
                    stageResult.MissingRequiredItems.Count == 0 ? Teal : Orange);
            }

            return result.gameObject;
        }

        void BuildRestartConfirmation(Transform canvas)
        {
            var fullCourse = app.Session.SelectedMode == TrainingMode.Full;
            var overlay = TrainingUIFactory.CreatePanel(
                canvas,
                "RestartConfirmationOverlay",
                new Vector2(1500f, 1050f),
                Vector2.zero,
                new Color(5f / 255f, 18f / 255f, 31f / 255f, 0.82f));
            confirmationRoot = overlay.gameObject;

            var dialog = TrainingUIFactory.CreatePanel(
                overlay.transform,
                "RestartConfirmationDialog",
                new Vector2(980f, 500f),
                Vector2.zero,
                LightBackground);
            TrainingUIFactory.CreateText(
                dialog.transform,
                fullCourse ? "처음부터 다시 시작할까요?" : "선택한 Stage를 다시 시작할까요?",
                new Vector2(850f, 82f),
                new Vector2(0f, 150f),
                49f,
                TextAlignmentOptions.Center,
                Navy);
            TrainingUIFactory.CreateText(
                dialog.transform,
                fullCourse
                    ? "시간, Stage, 장비와 세션 상태를 초기화하고\nPPE 단계부터 새 교육 세션을 시작합니다."
                    : "현재 결과를 종료하고 선택한 Stage의\n새 교육 세션을 시작합니다.",
                new Vector2(820f, 125f),
                new Vector2(0f, 40f),
                31f,
                TextAlignmentOptions.Center,
                MutedText);
            TrainingUIFactory.CreateButton(
                dialog.transform,
                "취소",
                new Vector2(360f, 105f),
                new Vector2(-205f, -145f),
                HideRestartConfirmation,
                false,
                34f,
                Navy);
            TrainingUIFactory.CreateButton(
                dialog.transform,
                "다시 시작",
                new Vector2(360f, 105f),
                new Vector2(205f, -145f),
                ConfirmRestart,
                false,
                34f,
                Teal);

            confirmationRoot.SetActive(false);
        }

        void CreateChecklistRow(Transform parent, string label, bool complete, float y)
        {
            TrainingUIFactory.CreateText(
                parent,
                label,
                new Vector2(390f, 52f),
                new Vector2(-90f, y),
                31f,
                TextAlignmentOptions.Left,
                Navy);
            TrainingUIFactory.CreateText(
                parent,
                complete ? "✓  완료" : "확인 필요",
                new Vector2(190f, 52f),
                new Vector2(205f, y),
                30f,
                TextAlignmentOptions.Right,
                complete ? Teal : Orange);
        }

        void CreateStageSummaryRow(Transform parent, TrainingStage stage, float y)
        {
            var result = app.Session.GetStageResult(stage);
            var complete = result != null && result.StageCompleted;
            TrainingUIFactory.CreateButton(
                parent,
                TrainingSessionState.GetStageLabel(stage) + " 세부 결과",
                new Vector2(380f, 70f),
                new Vector2(-90f, y),
                () => ShowStageDetails(stage),
                false,
                25f,
                Navy);
            TrainingUIFactory.CreateText(
                parent,
                complete ? "✓ 완료" : "확인 필요",
                new Vector2(180f, 60f),
                new Vector2(205f, y),
                26f,
                TextAlignmentOptions.Right,
                complete ? Teal : Orange);
        }

        static void CreateCheckItemRow(
            Transform parent,
            StageCheckItemResult item,
            float y,
            float rowHeight)
        {
            TrainingUIFactory.CreateText(
                parent,
                item.DisplayName,
                new Vector2(350f, rowHeight),
                new Vector2(-105f, y),
                21f,
                TextAlignmentOptions.Left,
                Navy);
            TrainingUIFactory.CreateText(
                parent,
                item.WasPerformed ? "✓ 완료" : "확인 필요",
                new Vector2(170f, rowHeight),
                new Vector2(205f, y),
                21f,
                TextAlignmentOptions.Right,
                item.WasPerformed ? Teal : Orange);
        }

        static void CreateResultRow(Transform parent, string label, string value, float y, Color valueColor)
        {
            TrainingUIFactory.CreateText(
                parent,
                label,
                new Vector2(320f, 54f),
                new Vector2(-125f, y),
                30f,
                TextAlignmentOptions.Left,
                Navy);
            TrainingUIFactory.CreateText(
                parent,
                value,
                new Vector2(250f, 54f),
                new Vector2(165f, y),
                31f,
                TextAlignmentOptions.Right,
                valueColor);
        }

        bool HasCompletedStage(TrainingStage stage)
        {
            var completed = app.Session.CompletedStages;
            for (var i = 0; i < completed.Count; i++)
            {
                if (completed[i] == stage)
                    return true;
            }
            return false;
        }

        static string FormatDuration(double seconds)
        {
            var duration = TimeSpan.FromSeconds(Math.Max(0d, seconds));
            return duration.TotalHours >= 1d
                ? string.Format("{0}:{1:00}:{2:00}", (int)duration.TotalHours, duration.Minutes, duration.Seconds)
                : string.Format("{0:00}:{1:00}", (int)duration.TotalMinutes, duration.Seconds);
        }

        void BuildFeedbackActions(Transform parent)
        {
            var voiceButton = TrainingUIFactory.CreateButton(
                parent,
                "음성 녹음 시작",
                new Vector2(360f, 76f),
                new Vector2(-410f, -310f),
                null,
                false,
                27f,
                Disabled);
            voiceButton.interactable = false;
            TrainingUIFactory.CreateButton(
                parent,
                "빠른 피드백",
                new Vector2(360f, 76f),
                new Vector2(0f, -310f),
                ShowQuickFeedback,
                false,
                27f,
                Teal);
            TrainingUIFactory.CreateButton(
                parent,
                "건너뛰기",
                new Vector2(360f, 76f),
                new Vector2(410f, -310f),
                SkipFeedback,
                false,
                27f,
                Navy);
            feedbackStatus = TrainingUIFactory.CreateText(
                parent,
                "음성 피드백은 승인된 서버 연결 후 사용할 수 있습니다.",
                new Vector2(1300f, 38f),
                new Vector2(0f, -365f),
                21f,
                TextAlignmentOptions.Center,
                MutedText);
        }

        void BuildStageDetailOverlay(Transform canvas)
        {
            detailOverlay = TrainingUIFactory.CreatePanel(
                canvas,
                "StageDetailOverlay",
                new Vector2(1500f, 1150f),
                Vector2.zero,
                new Color(5f / 255f, 18f / 255f, 31f / 255f, 0.86f)).gameObject;
            detailContent = TrainingUIFactory.CreatePanel(
                detailOverlay.transform,
                "StageDetailContent",
                new Vector2(1320f, 900f),
                new Vector2(0f, 45f),
                LightBackground).transform;
            TrainingUIFactory.CreateButton(
                detailOverlay.transform,
                "닫기",
                new Vector2(340f, 86f),
                new Vector2(0f, -505f),
                () => detailOverlay.SetActive(false),
                false,
                30f,
                Navy);
            detailOverlay.SetActive(false);
        }

        void ShowStageDetails(TrainingStage stage)
        {
            if (detailContent == null)
                return;
            for (var i = detailContent.childCount - 1; i >= 0; i--)
                Destroy(detailContent.GetChild(i).gameObject);

            var result = app.Session.GetStageResult(stage);
            if (result == null)
                return;
            TrainingUIFactory.CreateText(
                detailContent,
                result.StageName + " 세부 검사 결과",
                new Vector2(1180f, 70f),
                new Vector2(0f, 380f),
                44f,
                TextAlignmentOptions.Center,
                Navy);
            TrainingUIFactory.CreateText(
                detailContent,
                $"소요 {FormatDuration(result.StageDurationSeconds)}  ·  오류 {result.ErrorCount}",
                new Vector2(1100f, 48f),
                new Vector2(0f, 325f),
                27f,
                TextAlignmentOptions.Center,
                MutedText);
            TrainingUIFactory.CreatePanel(detailContent, "DetailDivider", new Vector2(1160f, 3f),
                new Vector2(0f, 288f), Divider);

            var count = Mathf.Max(1, result.CheckItems.Count);
            var spacing = Mathf.Min(54f, 560f / count);
            var startY = 255f - spacing * 0.5f;
            for (var i = 0; i < result.CheckItems.Count; i++)
            {
                var item = result.CheckItems[i];
                var y = startY - i * spacing;
                TrainingUIFactory.CreateText(detailContent, item.DisplayName, new Vector2(520f, spacing),
                    new Vector2(-300f, y), 25f, TextAlignmentOptions.Left, Navy);
                TrainingUIFactory.CreateText(detailContent, item.WasPerformed ? "✓ 완료" : "확인 필요",
                    new Vector2(210f, spacing), new Vector2(125f, y), 24f,
                    TextAlignmentOptions.Center, item.WasPerformed ? Teal : Orange);
                TrainingUIFactory.CreateText(detailContent,
                    $"오류 {item.ErrorCount} · {FormatDuration(item.DurationSeconds)}",
                    new Vector2(360f, spacing), new Vector2(390f, y), 22f,
                    TextAlignmentOptions.Right, MutedText);
            }
            detailOverlay.SetActive(true);
        }

        void BuildQuickFeedbackOverlay(Transform canvas)
        {
            feedbackOverlay = TrainingUIFactory.CreatePanel(
                canvas,
                "QuickFeedbackOverlay",
                new Vector2(1500f, 1150f),
                Vector2.zero,
                new Color(5f / 255f, 18f / 255f, 31f / 255f, 1f)).gameObject;
            var feedbackOverlayRect = feedbackOverlay.GetComponent<RectTransform>();
            if (feedbackOverlayRect != null)
                feedbackOverlayRect.localPosition = new Vector3(0f, 0f, -12f);
            feedbackContent = TrainingUIFactory.CreatePanel(
                feedbackOverlay.transform,
                "QuickFeedbackContent",
                new Vector2(1320f, 920f),
                Vector2.zero,
                White).transform;
            feedbackOverlay.SetActive(false);
        }

        void ShowQuickFeedback()
        {
            selectedQuickFeedback.Clear();
            quickFeedbackButtons.Clear();
            quickFeedbackLabels.Clear();
            feedbackTargetButtons.Clear();
            feedbackTargetLabels.Clear();
            // Full-course feedback opens on "전체 교육" so the issue buttons are
            // immediately usable. Stage tabs only change the target; they never gate or
            // disable the lower buttons, which caused XR presses to look selected but do
            // nothing on the following click.
            feedbackTargetSelected = true;
            feedbackTarget = app.Session.SelectedMode == TrainingMode.Full
                ? TrainingStage.None
                : TrainingSessionState.FirstStageFor(app.Session.SelectedMode);
            for (var i = feedbackContent.childCount - 1; i >= 0; i--)
                Destroy(feedbackContent.GetChild(i).gameObject);

            TrainingUIFactory.CreateText(feedbackContent, "빠른 피드백", new Vector2(1160f, 70f),
                new Vector2(0f, 390f), 46f, TextAlignmentOptions.Center, Navy);
            if (app.Session.SelectedMode == TrainingMode.Full)
            {
                TrainingUIFactory.CreateText(feedbackContent, "피드백 대상을 선택하세요.",
                    new Vector2(1100f, 45f), new Vector2(0f, 337f), 25f,
                    TextAlignmentOptions.Center, MutedText);
                CreateFeedbackTargetButton(TrainingStage.PPE, "PPE", -435f);
                CreateFeedbackTargetButton(TrainingStage.PreEntry, "현장 진입", -145f);
                CreateFeedbackTargetButton(TrainingStage.LeakResponse, "누출 대응", 145f);
                CreateFeedbackTargetButton(TrainingStage.None, "전체 교육", 435f);
            }

            for (var i = 0; i < QuickFeedbackOptions.Length; i++)
            {
                var option = QuickFeedbackOptions[i];
                var column = i % 2;
                var row = i / 2;
                var button = TrainingUIFactory.CreateButton(
                    feedbackContent,
                    option.Label,
                    new Vector2(540f, 72f),
                    new Vector2(column == 0 ? -295f : 295f, 175f - row * 88f),
                    () => ToggleQuickFeedback(option.Id),
                    false,
                    24f,
                    Navy);
                button.interactable = true;
                button.transition = Selectable.Transition.None;
                quickFeedbackButtons[option.Id] = button;
                quickFeedbackLabels[option.Id] = option.Label;
            }

            RefreshFeedbackTargetButtons();
            RefreshQuickFeedbackButtons();

            TrainingUIFactory.CreateButton(feedbackContent, "취소", new Vector2(340f, 82f),
                new Vector2(-195f, -365f), CloseQuickFeedback, false, 29f, Navy);
            TrainingUIFactory.CreateButton(feedbackContent, "피드백 저장", new Vector2(340f, 82f),
                new Vector2(195f, -365f), SubmitQuickFeedback, false, 29f, Teal);
            if (completionPanel != null)
                completionPanel.SetActive(false);
            feedbackOverlay.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void CreateFeedbackTargetButton(TrainingStage stage, string label, float x)
        {
            var button = TrainingUIFactory.CreateButton(
                feedbackContent,
                label,
                new Vector2(260f, 64f),
                new Vector2(x, 280f),
                () => SelectFeedbackTarget(stage),
                false,
                23f,
                Navy);
            button.transition = Selectable.Transition.None;
            feedbackTargetButtons[stage] = button;
            feedbackTargetLabels[stage] = label;
        }

        void SelectFeedbackTarget(TrainingStage stage)
        {
            feedbackTarget = stage;
            feedbackTargetSelected = true;
            foreach (var pair in quickFeedbackButtons)
                pair.Value.interactable = true;
            RefreshFeedbackTargetButtons();
            RefreshQuickFeedbackButtons();
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void ToggleQuickFeedback(string id)
        {
            if (!feedbackTargetSelected)
                return;
            if (string.Equals(id, "no_problem", StringComparison.Ordinal))
            {
                selectedQuickFeedback.Clear();
                selectedQuickFeedback.Add(id);
            }
            else
            {
                selectedQuickFeedback.Remove("no_problem");
                if (!selectedQuickFeedback.Add(id))
                    selectedQuickFeedback.Remove(id);
            }

            RefreshQuickFeedbackButtons();
        }

        void RefreshFeedbackTargetButtons()
        {
            foreach (var pair in feedbackTargetButtons)
            {
                var selected = feedbackTargetSelected && pair.Key == feedbackTarget;
                var label = feedbackTargetLabels.TryGetValue(pair.Key, out var value)
                    ? value
                    : pair.Key.ToString();
                ApplyFeedbackButtonVisual(pair.Value, selected, true, label);
            }
        }

        void RefreshQuickFeedbackButtons()
        {
            foreach (var pair in quickFeedbackButtons)
            {
                var selected = selectedQuickFeedback.Contains(pair.Key);
                var label = quickFeedbackLabels.TryGetValue(pair.Key, out var value)
                    ? value
                    : pair.Key;
                ApplyFeedbackButtonVisual(
                    pair.Value,
                    selected,
                    feedbackTargetSelected,
                    label);
            }
        }

        static void ApplyFeedbackButtonVisual(
            Button button,
            bool selected,
            bool interactable,
            string label)
        {
            if (button == null)
                return;

            button.interactable = interactable;
            button.transition = Selectable.Transition.None;
            var image = button.targetGraphic as Image;
            if (image != null)
                image.color = !interactable ? Disabled : selected ? Teal : Navy;

            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = selected ? "✓  " + label : label;
                text.color = Color.white;
            }
        }

        void SubmitQuickFeedback()
        {
            if (!feedbackTargetSelected)
            {
                app.ErrorUI?.Show("피드백 대상을 먼저 선택하세요.");
                return;
            }
            if (selectedQuickFeedback.Count == 0)
            {
                app.ErrorUI?.Show("빠른 피드백 항목을 하나 이상 선택하세요.");
                return;
            }

            var feedback = app.Session.SaveQuickFeedback(feedbackTarget, selectedQuickFeedback);
            if (feedback == null)
            {
                app.ErrorUI?.Show("피드백을 저장하지 못했습니다.");
                return;
            }
            app.Telemetry?.QuickFeedbackSubmitted(feedbackTarget, feedback);
            feedbackOverlay.SetActive(false);
            if (completionPanel != null)
                completionPanel.SetActive(true);
            feedbackStatus.text = "피드백을 전송하고 있습니다.";
            feedbackStatus.color = MutedText;
            app.FeedbackDashboard?.SubmitQuickFeedback(
                feedbackTarget,
                feedback,
                (success, message) =>
                {
                    if (feedbackStatus == null)
                        return;
                    feedbackStatus.text = message;
                    feedbackStatus.color = success ? Teal : Orange;
                });
        }

        void CloseQuickFeedback()
        {
            if (feedbackOverlay != null)
                feedbackOverlay.SetActive(false);
            if (completionPanel != null)
                completionPanel.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void SkipFeedback()
        {
            var target = app.Session.SelectedMode == TrainingMode.Full
                ? TrainingStage.None
                : TrainingSessionState.FirstStageFor(app.Session.SelectedMode);
            var feedback = app.Session.SkipFeedback(target);
            app.Telemetry?.FeedbackSkipped(target, feedback);
            feedbackStatus.text = "피드백을 건너뛰었습니다.";
            feedbackStatus.color = MutedText;
        }

        void ShowRestartConfirmation()
        {
            if (!actionPending && confirmationRoot != null)
                confirmationRoot.SetActive(true);
        }

        void HideRestartConfirmation()
        {
            if (!actionPending && confirmationRoot != null)
                confirmationRoot.SetActive(false);
        }

        void ConfirmRestart()
        {
            if (actionPending)
                return;

            actionPending = true;
            if (confirmationRoot != null)
                confirmationRoot.SetActive(false);
            completionVoiceSource?.Stop();

            if (!app.ScenarioFlow.RestartSelectedTrainingFromResult())
            {
                actionPending = false;
                app.ErrorUI?.Show("교육 재시작 요청을 처리하지 못했습니다.");
            }
        }

        void ExitTraining()
        {
            if (actionPending)
                return;

            actionPending = true;
            completionVoiceSource?.Stop();
            if (!app.ScenarioFlow.ExitCompletedTraining())
            {
                actionPending = false;
                app.ErrorUI?.Show("교육 종료 요청을 처리하지 못했습니다.");
            }
        }

        IEnumerator PlayCompletionVoiceOnce()
        {
            if (completionVoicePlayed)
                yield break;

            completionVoicePlayed = true;
            PPETTSManager.Instance?.StopAll();
            yield return new WaitForSecondsRealtime(0.75f);

            var clip = Resources.Load<AudioClip>(CompletionVoiceResourcePath);
            if (clip == null)
            {
                Debug.LogWarning("[Complete Menu] 완료 안내 음성 리소스를 찾지 못했습니다.", this);
                yield break;
            }

            completionVoiceSource = gameObject.AddComponent<AudioSource>();
            completionVoiceSource.playOnAwake = false;
            completionVoiceSource.loop = false;
            completionVoiceSource.spatialBlend = 0f;
            completionVoiceSource.volume = 1f;
            completionVoiceSource.clip = clip;
            completionVoiceSource.Play();
        }

        void OnDestroy()
        {
            completionVoiceSource?.Stop();
        }
    }
}
