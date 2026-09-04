using System.Collections;
using HClTraining.SharedXR;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HClTraining.App
{
    /// <summary>
    /// Stage-authored pause menu view. Add one instance to a Stage Scene so its
    /// Transform and camera-relative placement can be adjusted without changing
    /// the shared XR rig.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrainingPauseMenuPresenter : MonoBehaviour
    {
        enum PendingAction
        {
            None,
            RestartStage,
            CourseSelection,
            Title,
            ExitApplication,
        }

        static readonly Vector2 MenuSize = new Vector2(1000f, 840f);
        static readonly Color Panel = new Color32(22, 31, 42, 247);
        static readonly Color Primary = new Color32(25, 143, 137, 255);
        static readonly Color Secondary = new Color32(35, 66, 96, 255);
        static readonly Color Warning = new Color32(184, 91, 35, 255);
        static readonly Color Text = new Color32(241, 246, 249, 255);
        static readonly Color MutedText = new Color32(184, 199, 211, 255);

        [Header("Stage")]
        [SerializeField] TrainingStage targetStage = TrainingStage.PPE;

        [Header("Placement - editable by the Stage owner")]
        [Tooltip("When enabled, the menu moves to the camera-relative offset whenever it opens.")]
        [SerializeField] bool followCameraOnOpen = true;
        [SerializeField] Vector3 cameraLocalPosition = new Vector3(0f, -0.05f, 1.35f);
        [SerializeField] Vector3 cameraLocalEulerAngles = Vector3.zero;
        [SerializeField, Min(0.0001f)] float canvasWorldScale = 0.001f;

        AppBootstrap app;
        GameObject menuRoot;
        GameObject confirmationRoot;
        TMP_Text confirmationMessage;
        Coroutine bindRoutine;
        PendingAction pendingAction;
        bool inputSubscribed;
        bool pausedByThisMenu;

        void OnEnable()
        {
            bindRoutine = StartCoroutine(BindWhenAppReady());
        }

        IEnumerator BindWhenAppReady()
        {
            while (isActiveAndEnabled)
            {
                app = AppBootstrap.Instance;
                if (app != null && app.SharedInput != null && app.SharedInput.IsReady &&
                    app.PauseController != null)
                {
                    break;
                }
                yield return null;
            }

            if (!isActiveAndEnabled || app == null)
                yield break;

            BuildMenu();
            app.SharedInput.Performed += OnSharedInputPerformed;
            inputSubscribed = true;
            bindRoutine = null;
        }

        void Update()
        {
#if UNITY_EDITOR
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame &&
                IsTargetStageActive())
            {
                ToggleMenu();
            }
#endif
        }

        void BuildMenu()
        {
            if (menuRoot != null)
                return;

            var stageLabel = GetPauseStageLabel();
            var canvas = TrainingUIFactory.CreateWorldCanvas(
                transform, $"{targetStage}_PauseMenuCanvas", MenuSize, false);
            canvas.transform.localPosition = Vector3.zero;
            canvas.transform.localRotation = Quaternion.identity;
            canvas.transform.localScale = Vector3.one * canvasWorldScale;
            menuRoot = canvas.gameObject;

            var panel = TrainingUIFactory.CreatePanel(canvas.transform, "PauseMenuPanel", MenuSize, Vector2.zero, Panel);
            TrainingUIFactory.CreateText(panel.transform, "교육 메뉴", new Vector2(860f, 72f), new Vector2(0f, 350f),
                48f, TextAlignmentOptions.Center, Text);
            TrainingUIFactory.CreateText(panel.transform, $"{stageLabel} 교육이 일시정지되었습니다.",
                new Vector2(860f, 46f), new Vector2(0f, 295f), 26f,
                TextAlignmentOptions.Center, MutedText);

            TrainingUIFactory.CreateButton(panel.transform, "계속하기", new Vector2(820f, 88f), new Vector2(0f, 200f),
                CloseMenu, false, 31f, Primary);
            TrainingUIFactory.CreateButton(panel.transform, $"{stageLabel} 다시 시작", new Vector2(820f, 88f),
                new Vector2(0f, 95f),
                () => Ask(PendingAction.RestartStage,
                    $"현재 {stageLabel} 진행 내용을 초기화하고 다시 시작할까요?"),
                false, 31f, Secondary);
            TrainingUIFactory.CreateButton(panel.transform, "교육 과정 선택", new Vector2(820f, 88f), new Vector2(0f, -10f),
                () => Ask(PendingAction.CourseSelection, "현재 교육을 종료하고 과정 선택 화면으로 이동할까요?"),
                false, 31f, Secondary);
            TrainingUIFactory.CreateButton(panel.transform, "처음 화면", new Vector2(820f, 88f), new Vector2(0f, -115f),
                () => Ask(PendingAction.Title, "현재 교육을 종료하고 처음 화면으로 이동할까요?"),
                false, 31f, Secondary);
            TrainingUIFactory.CreateButton(panel.transform, "앱 종료", new Vector2(820f, 88f), new Vector2(0f, -220f),
                () => Ask(PendingAction.ExitApplication, "앱을 종료할까요?"),
                false, 31f, Warning);
            TrainingUIFactory.CreateText(panel.transform, "왼쪽 컨트롤러의 메뉴 버튼으로 열거나 닫을 수 있습니다.",
                new Vector2(880f, 42f), new Vector2(0f, -340f), 22f, TextAlignmentOptions.Center, MutedText);

            BuildConfirmation(canvas.transform);
            menuRoot.SetActive(false);
        }

        string GetPauseStageLabel()
        {
            return targetStage switch
            {
                TrainingStage.PPE => "PPE",
                TrainingStage.PreEntry => "현장 진입",
                TrainingStage.LeakResponse => "누출 대응",
                _ => TrainingSessionState.GetStageLabel(targetStage),
            };
        }

        void BuildConfirmation(Transform parent)
        {
            var overlay = TrainingUIFactory.CreatePanel(parent, "ConfirmationPanel", MenuSize, Vector2.zero,
                new Color32(10, 18, 27, 252));
            confirmationRoot = overlay.gameObject;
            TrainingUIFactory.CreateText(overlay.transform, "확인", new Vector2(820f, 76f), new Vector2(0f, 180f),
                48f, TextAlignmentOptions.Center, Text);
            confirmationMessage = TrainingUIFactory.CreateText(overlay.transform, string.Empty,
                new Vector2(820f, 180f), new Vector2(0f, 45f), 31f, TextAlignmentOptions.Center, Text);
            TrainingUIFactory.CreateButton(overlay.transform, "취소", new Vector2(350f, 95f), new Vector2(-205f, -160f),
                CancelPendingAction, false, 31f, Secondary);
            TrainingUIFactory.CreateButton(overlay.transform, "확인", new Vector2(350f, 95f), new Vector2(205f, -160f),
                ConfirmPendingAction, false, 31f, Warning);
            confirmationRoot.SetActive(false);
        }

        void OnSharedInputPerformed(SharedXRHand hand, SharedXRControl control)
        {
            if (hand == SharedXRHand.Left && control == SharedXRControl.Menu && IsTargetStageActive())
                ToggleMenu();
        }

        bool IsTargetStageActive()
        {
            return app != null && app.Session != null && app.Session.CurrentStage == targetStage &&
                   app.SceneFlow != null && !app.SceneFlow.IsTransitioning;
        }

        void ToggleMenu()
        {
            if (menuRoot == null)
                return;
            if (menuRoot.activeSelf)
                CloseMenu();
            else
                OpenMenu();
        }

        void OpenMenu()
        {
            if (!IsTargetStageActive() || menuRoot == null)
                return;

            if (followCameraOnOpen)
                PlaceInFrontOfCamera();
            pendingAction = PendingAction.None;
            confirmationRoot?.SetActive(false);
            menuRoot.SetActive(true);
            if (!app.PauseController.IsPaused)
            {
                app.PauseController.PauseTraining();
                pausedByThisMenu = true;
            }
        }

        void CloseMenu()
        {
            pendingAction = PendingAction.None;
            confirmationRoot?.SetActive(false);
            if (pausedByThisMenu && app != null && app.PauseController != null)
                app.PauseController.ResumeTraining();
            pausedByThisMenu = false;
            menuRoot?.SetActive(false);
        }

        void PlaceInFrontOfCamera()
        {
            var camera = app != null ? app.MainCamera : null;
            if (camera == null)
                return;
            transform.SetPositionAndRotation(
                camera.transform.TransformPoint(cameraLocalPosition),
                camera.transform.rotation * Quaternion.Euler(cameraLocalEulerAngles));
        }

        void Ask(PendingAction action, string message)
        {
            pendingAction = action;
            if (confirmationMessage != null)
                confirmationMessage.text = message;
            confirmationRoot?.SetActive(true);
        }

        void CancelPendingAction()
        {
            pendingAction = PendingAction.None;
            confirmationRoot?.SetActive(false);
        }

        void ConfirmPendingAction()
        {
            var action = pendingAction;
            CloseMenu();
            if (app == null || app.ScenarioFlow == null)
                return;

            var accepted = action switch
            {
                PendingAction.RestartStage => app.ScenarioFlow.RestartCurrentStage(),
                PendingAction.CourseSelection => app.ScenarioFlow.ReturnToStart(),
                PendingAction.Title => app.ScenarioFlow.ReturnToTitle(),
                PendingAction.ExitApplication => app.ScenarioFlow.ExitApplication(),
                _ => true,
            };
            if (!accepted)
                app.ErrorUI?.Show("요청을 처리하지 못했습니다. 잠시 후 다시 시도해 주세요.");
        }

        void OnDisable()
        {
            if (bindRoutine != null)
            {
                StopCoroutine(bindRoutine);
                bindRoutine = null;
            }
            if (inputSubscribed && app != null && app.SharedInput != null)
                app.SharedInput.Performed -= OnSharedInputPerformed;
            inputSubscribed = false;
            if (pausedByThisMenu && app != null && app.PauseController != null)
                app.PauseController.ResumeTraining();
            pausedByThisMenu = false;
        }
    }
}
