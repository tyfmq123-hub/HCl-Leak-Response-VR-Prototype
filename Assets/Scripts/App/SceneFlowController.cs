using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HClTraining.App
{
    [DisallowMultipleComponent]
    public sealed class SceneFlowController : MonoBehaviour
    {
        public const string AppScenePath = "Assets/Scenes/App/App.unity";
        public const string StartScenePath = "Assets/Scenes/App/Start.unity";
        public const string PpeScenePath = "Assets/Scenes/PPE_PreparationRoom.unity";
        public const string PreEntryScenePath = "Assets/3.JH/Scenes/Prototype/JH_Test_Scene.unity";
        public const string LeakResponseScenePath = "Assets/Scenes/SSH_VR_Project.unity";
        public const string CompleteScenePath = "Assets/Scenes/App/Complete.unity";

        TransitionInputLock inputLock;
        ScreenFadeController fade;
        StageXRProfileController xrProfiles;
        StageLifecycleAdapter lifecycle;
        TrainingSessionState session;
        PPEStateController ppeState;
        PersistentEquipmentInventory inventory;
        CommonErrorUI errorUI;
        Scene contentScene;
        Coroutine transitionRoutine;

        public bool IsTransitioning => transitionRoutine != null;
        public Scene CurrentContentScene => contentScene;

        public void Initialize(
            TransitionInputLock transitionLock,
            ScreenFadeController fadeController,
            StageXRProfileController profileController,
            StageLifecycleAdapter lifecycleAdapter,
            TrainingSessionState state,
            PPEStateController ppe,
            PersistentEquipmentInventory equipment,
            CommonErrorUI errors)
        {
            inputLock = transitionLock;
            fade = fadeController;
            xrProfiles = profileController;
            lifecycle = lifecycleAdapter;
            session = state;
            ppeState = ppe;
            inventory = equipment;
            errorUI = errors;
        }

        public bool ShowStart() => RequestTransition(StartScenePath, TrainingStage.None);
        public bool ShowStage(TrainingStage stage) => RequestTransition(PathFor(stage), stage);
        public bool ShowComplete() => RequestTransition(CompleteScenePath, TrainingStage.Complete);

        bool RequestTransition(string scenePath, TrainingStage stage)
        {
            if (transitionRoutine != null || inputLock == null || !inputLock.TryLock())
                return false;
            transitionRoutine = StartCoroutine(TransitionRoutine(scenePath, stage));
            return true;
        }

        IEnumerator TransitionRoutine(string scenePath, TrainingStage stage)
        {
            errorUI?.Hide();
            yield return fade.FadeOut();

            lifecycle.Unbind();
            inventory.ReturnAllToStorage();
            yield return null;

            if (contentScene.IsValid() && contentScene.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(contentScene);
                if (unload != null)
                {
                    while (!unload.isDone)
                        yield return null;
                }
            }
            contentScene = default;

            if (!Application.CanStreamedLevelBeLoaded(scenePath))
            {
                yield return RecoverToStart($"필요한 Scene을 불러올 수 없습니다: {scenePath}");
                yield break;
            }

            AsyncOperation load = null;
            string loadExceptionMessage = null;
            try
            {
                load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            }
            catch (Exception exception)
            {
                loadExceptionMessage = exception.Message;
            }

            if (loadExceptionMessage != null)
            {
                yield return RecoverToStart($"Scene 로드 요청이 실패했습니다: {loadExceptionMessage}");
                yield break;
            }

            if (load == null)
            {
                yield return RecoverToStart($"Scene 로드 작업을 만들지 못했습니다: {scenePath}");
                yield break;
            }

            while (!load.isDone)
                yield return null;

            contentScene = SceneManager.GetSceneByPath(scenePath);
            if (!contentScene.IsValid() || !contentScene.isLoaded)
            {
                yield return RecoverToStart($"Scene 로드 완료 후 Scene을 찾지 못했습니다: {scenePath}");
                yield break;
            }

            SceneManager.SetActiveScene(contentScene);
            session.EnterStage(stage);

            if (stage == TrainingStage.None)
            {
                if (!xrProfiles.ApplyStartProfile(out var startXrError))
                    errorUI?.Show(startXrError);
            }
            else if (stage != TrainingStage.Complete)
            {
                if (!xrProfiles.ApplyStageProfile(contentScene, stage, out var xrError))
                    errorUI?.Show(xrError);
                yield return lifecycle.BindStage(contentScene, stage);
                ppeState.ApplyPersistentState();
            }

            yield return fade.FadeIn();
            inputLock.Unlock(false);
            transitionRoutine = null;
        }

        IEnumerator RecoverToStart(string message)
        {
            Debug.LogError("[Scene Flow] " + message, this);
            errorUI?.Show(message + "\n시작 화면으로 안전하게 복귀합니다.");

            if (contentScene.IsValid() && contentScene.isLoaded)
            {
                var cleanup = SceneManager.UnloadSceneAsync(contentScene);
                if (cleanup != null)
                    while (!cleanup.isDone) yield return null;
            }
            contentScene = default;

            if (Application.CanStreamedLevelBeLoaded(StartScenePath))
            {
                var load = SceneManager.LoadSceneAsync(StartScenePath, LoadSceneMode.Additive);
                if (load != null)
                    while (!load.isDone) yield return null;
                contentScene = SceneManager.GetSceneByPath(StartScenePath);
                if (contentScene.IsValid())
                    SceneManager.SetActiveScene(contentScene);
            }
            session.ReturnToStart();
            if (!xrProfiles.ApplyStartProfile(out var startXrError))
                errorUI?.Show(startXrError);
            yield return fade.FadeIn();
            inputLock.Unlock(false);
            transitionRoutine = null;
        }

        static string PathFor(TrainingStage stage)
        {
            switch (stage)
            {
                case TrainingStage.PPE: return PpeScenePath;
                case TrainingStage.PreEntry: return PreEntryScenePath;
                case TrainingStage.LeakResponse: return LeakResponseScenePath;
                default: throw new ArgumentOutOfRangeException(nameof(stage));
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class ScenarioFlowManager : MonoBehaviour
    {
        TrainingSessionState session;
        SceneFlowController scenes;
        PersistentEquipmentInventory inventory;
        bool completionQueued;

        public void Initialize(
            TrainingSessionState state,
            SceneFlowController sceneController,
            PersistentEquipmentInventory equipment)
        {
            session = state;
            scenes = sceneController;
            inventory = equipment;
        }

        public bool StartTraining(TrainingMode mode)
        {
            if (scenes.IsTransitioning || mode == TrainingMode.None)
                return false;

            session.Begin(mode);
            completionQueued = false;
            if (mode == TrainingMode.Full || mode == TrainingMode.PPEOnly)
                PPELoadoutPresets.BeginNewFullTrainingClear();
            else
                PPELoadoutPresets.BeginFieldStandaloneWithOfficialPreset();
            inventory.SyncFromLoadout();
            return scenes.ShowStage(TrainingSessionState.FirstStageFor(mode));
        }

        public void CompleteCurrentStage()
        {
            if (completionQueued || scenes.IsTransitioning || !session.MarkCurrentStageComplete())
                return;
            completionQueued = true;
            StartCoroutine(ContinueAfterCompletion());
        }

        IEnumerator ContinueAfterCompletion()
        {
            yield return new WaitForSecondsRealtime(0.65f);
            var next = session.GetNextStage();
            completionQueued = false;
            if (next == TrainingStage.Complete)
                scenes.ShowComplete();
            else
                scenes.ShowStage(next);
        }

        public bool RestartCurrentStage()
        {
            if (scenes.IsTransitioning || session.RestartTarget == TrainingStage.None)
                return false;
            completionQueued = false;
            return scenes.ShowStage(session.RestartTarget);
        }

        public bool RepeatSelectedTraining() => StartTraining(session.SelectedMode);

        public bool ReturnToStart()
        {
            if (scenes.IsTransitioning)
                return false;
            completionQueued = false;
            session.ReturnToStart();
            return scenes.ShowStart();
        }
    }
}
