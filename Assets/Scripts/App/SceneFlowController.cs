using System;
using System.Collections;
using System.Collections.Generic;
using JH.EntrySafety;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRProject.LeakResponse.Narration;

namespace HClTraining.App
{
    [DisallowMultipleComponent]
    public sealed class SceneFlowController : MonoBehaviour
    {
        public const string AppScenePath = "Assets/Scenes/App/App.unity";
        public const string StartScenePath = "Assets/Scenes/App/Start.unity";
        public const string PpeScenePath = "Assets/Scenes/PPE_PreparationRoom.unity";
        public const string PreEntryScenePath = "Assets/Scenes/JH_Test_Scene.unity";
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
        readonly List<Scene> contentScenes = new List<Scene>();
        Coroutine transitionRoutine;

        public bool IsTransitioning => transitionRoutine != null;
        public Scene CurrentContentScene => contentScene;
        public IReadOnlyList<Scene> LoadedContentScenes => contentScenes;

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
            var isCompletionTransition = stage == TrainingStage.Complete;
            yield return fade.FadeOut(isCompletionTransition ? 0.8f : 0.25f);

            lifecycle.Unbind();
            inventory.ReturnAllToStorage();
            yield return null;
            yield return UnloadContentScenes();

            var plan = BuildLoadPlan(scenePath, stage);
            var loadOrder = plan.BuildLoadOrder();
            for (var pathIndex = 0; pathIndex < loadOrder.Count; pathIndex++)
            {
                var path = loadOrder[pathIndex];
                if (!Application.CanStreamedLevelBeLoaded(path))
                {
                    yield return RecoverToStart($"필요한 Scene을 불러올 수 없습니다: {path}");
                    yield break;
                }

                var isLeakScene = PathsMatch(path, LeakResponseScenePath);
                var isPreEntryScene = PathsMatch(path, PreEntryScenePath);
                if (isLeakScene)
                    P102LeakStageActivation.PrepareSceneLoad(path, stage == TrainingStage.LeakResponse);
                if (isPreEntryScene)
                    EntrySafetyStageActivation.PrepareSceneLoad(path, stage == TrainingStage.PreEntry);

                AsyncOperation load = null;
                string loadExceptionMessage = null;
                try
                {
                    load = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
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
                    yield return RecoverToStart($"Scene 로드 작업을 만들지 못했습니다: {path}");
                    yield break;
                }

                while (!load.isDone)
                    yield return null;

                var loadedScene = SceneManager.GetSceneByPath(path);
                if (!loadedScene.IsValid() || !loadedScene.isLoaded)
                {
                    yield return RecoverToStart($"Scene 로드 완료 후 Scene을 찾지 못했습니다: {path}");
                    yield break;
                }

                contentScenes.Add(loadedScene);
                if (isLeakScene)
                    P102LeakStageActivation.Apply(loadedScene, stage == TrainingStage.LeakResponse);
                if (isPreEntryScene)
                    EntrySafetyStageActivation.Apply(loadedScene, stage == TrainingStage.PreEntry);

                if (!PathsMatch(path, plan.PrimaryScenePath) &&
                    !xrProfiles.PrepareDependencyScene(loadedScene, out var dependencyError))
                {
                    yield return RecoverToStart(dependencyError);
                    yield break;
                }
            }

            contentScene = SceneManager.GetSceneByPath(plan.PrimaryScenePath);
            if (!contentScene.IsValid() || !contentScene.isLoaded)
            {
                yield return RecoverToStart($"Primary Scene을 찾지 못했습니다: {plan.PrimaryScenePath}");
                yield break;
            }

            SceneManager.SetActiveScene(contentScene);
            session.EnterStage(stage);

            if (stage == TrainingStage.None)
            {
                if (!xrProfiles.ApplyStartProfile(out var startXrError))
                    errorUI?.Show(startXrError);
            }
            else if (stage == TrainingStage.Complete)
            {
                if (!xrProfiles.ApplyCompleteProfile(out var completeXrError))
                    errorUI?.Show(completeXrError);
            }
            else if (stage != TrainingStage.Complete)
            {
                if (!xrProfiles.ApplyStageProfile(contentScene, stage, out var xrError))
                    errorUI?.Show(xrError);
                if (stage == TrainingStage.LeakResponse)
                {
                    var entrySpawn = FindSceneTransform(contentScene, "LeakResponseEntrySpawn");
                    if (entrySpawn == null)
                    {
                        yield return RecoverToStart("LeakResponseEntrySpawn reference is missing.");
                        yield break;
                    }
                    if (!MoveSharedPlayerToEntrySpawn(entrySpawn, out var moveError))
                    {
                        yield return RecoverToStart(moveError);
                        yield break;
                    }
                }
                if (stage == TrainingStage.LeakResponse)
                    P102LeakStageActivation.Apply(contentScene, true);
                yield return lifecycle.BindStage(contentScene, stage);
                ppeState.ApplyPersistentState();
            }

            // Unloading a legacy Stage rig can disable the shared XRI asset after
            // its profile audit has run. Start/Complete have no active Stage audit,
            // so restore the App-owned semantic actions at the transition boundary.
            AppBootstrap.Instance?.SharedInput?.EnsureActionsEnabled();

            yield return fade.FadeIn(isCompletionTransition ? 0.65f : 0.3f);
            inputLock.Unlock(false);
            transitionRoutine = null;
        }

        public bool HandoffPreEntryToLoadedLeakResponse()
        {
            if (transitionRoutine != null || session.SelectedMode != TrainingMode.Full ||
                session.CurrentStage != TrainingStage.PreEntry || inputLock == null || !inputLock.TryLock())
            {
                return false;
            }

            transitionRoutine = StartCoroutine(HandoffPreEntryToLeakRoutine());
            return true;
        }

        IEnumerator HandoffPreEntryToLeakRoutine()
        {
            errorUI?.Hide();
            yield return fade.FadeOut();

            var leakScene = SceneManager.GetSceneByPath(LeakResponseScenePath);
            if (!leakScene.IsValid() || !leakScene.isLoaded)
            {
                lifecycle.Unbind();
                yield return RecoverToStart("LeakResponse Additive Scene이 준비되지 않아 handoff를 중단했습니다.");
                yield break;
            }

            var entrySpawn = FindSceneTransform(leakScene, "LeakResponseEntrySpawn");
            if (entrySpawn == null)
            {
                lifecycle.Unbind();
                yield return RecoverToStart("LeakResponseEntrySpawn 참조가 없습니다.");
                yield break;
            }

            lifecycle.Unbind();
            inventory.ReturnAllToStorage();
            var preEntryScene = SceneManager.GetSceneByPath(PreEntryScenePath);
            if (preEntryScene.IsValid() && preEntryScene.isLoaded)
                EntrySafetyStageActivation.Apply(preEntryScene, false);
            if (!session.MarkCurrentStageComplete())
            {
                yield return RecoverToStart("PreEntry 완료 상태를 기록하지 못했습니다.");
                yield break;
            }

            session.EnterStage(TrainingStage.LeakResponse);
            if (!xrProfiles.ApplyStageProfile(leakScene, TrainingStage.LeakResponse, out var xrError))
            {
                yield return RecoverToStart(xrError);
                yield break;
            }

            if (!MoveSharedPlayerToEntrySpawn(entrySpawn, out var moveError))
            {
                yield return RecoverToStart(moveError);
                yield break;
            }

            contentScene = leakScene;
            SceneManager.SetActiveScene(leakScene);
            // Leak-owned runtime objects created during activation must be authored
            // into the Leak Scene, never into the still-loaded PreEntry primary Scene.
            P102LeakStageActivation.Apply(leakScene, true);
            yield return lifecycle.BindStage(leakScene, TrainingStage.LeakResponse);
            ppeState.ApplyPersistentState();

            yield return fade.FadeIn();
            inputLock.Unlock(false);
            transitionRoutine = null;
        }

        bool MoveSharedPlayerToEntrySpawn(Transform entrySpawn, out string error)
        {
            error = null;
            var commonRoot = xrProfiles != null ? xrProfiles.CommonXrOrigin : null;
            var origin = commonRoot != null ? commonRoot.GetComponent<XROrigin>() : null;
            if (origin == null || origin.Camera == null)
            {
                error = "Shared XR Origin 또는 XR Camera가 없어 handoff 이동을 수행할 수 없습니다.";
                return false;
            }

            var up = entrySpawn.up.sqrMagnitude > 0.001f ? entrySpawn.up.normalized : Vector3.up;
            var forward = Vector3.ProjectOnPlane(entrySpawn.forward, up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(origin.Camera.transform.forward, up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            origin.MatchOriginUpCameraForward(up, forward.normalized);
            var trackedHeadHeight = Mathf.Max(0f, origin.CameraInOriginSpaceHeight);
            origin.MoveCameraToWorldLocation(entrySpawn.position + up * trackedHeadHeight);
            return true;
        }

        IEnumerator UnloadContentScenes()
        {
            for (var i = contentScenes.Count - 1; i >= 0; i--)
            {
                var scene = contentScenes[i];
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                var unload = SceneManager.UnloadSceneAsync(scene);
                if (unload != null)
                    while (!unload.isDone) yield return null;
            }

            if (contentScenes.Count == 0 && contentScene.IsValid() && contentScene.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(contentScene);
                if (unload != null)
                    while (!unload.isDone) yield return null;
            }

            contentScenes.Clear();
            contentScene = default;
        }

        IEnumerator RecoverToStart(string message)
        {
            Debug.LogError("[Scene Flow] " + message, this);
            errorUI?.Show(message + "\n시작 화면으로 안전하게 복귀합니다.");

            yield return UnloadContentScenes();

            if (Application.CanStreamedLevelBeLoaded(StartScenePath))
            {
                var load = SceneManager.LoadSceneAsync(StartScenePath, LoadSceneMode.Additive);
                if (load != null)
                    while (!load.isDone) yield return null;
                contentScene = SceneManager.GetSceneByPath(StartScenePath);
                if (contentScene.IsValid())
                {
                    contentScenes.Add(contentScene);
                    SceneManager.SetActiveScene(contentScene);
                }
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

        static StageLoadPlan BuildLoadPlan(string primaryScenePath, TrainingStage stage)
        {
            if (stage == TrainingStage.PreEntry)
                return new StageLoadPlan(primaryScenePath, new[] { LeakResponseScenePath });
            if (stage == TrainingStage.LeakResponse)
                return new StageLoadPlan(primaryScenePath, new[] { PreEntryScenePath });
            return new StageLoadPlan(primaryScenePath, Array.Empty<string>());
        }

        static Transform FindSceneTransform(Scene scene, string exactName)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < transforms.Length; i++)
                    if (transforms[i] != null && transforms[i].name == exactName) return transforms[i];
            }
            return null;
        }

        static bool PathsMatch(string left, string right) =>
            string.Equals(
                left?.Replace('\\', '/'),
                right?.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase);

        readonly struct StageLoadPlan
        {
            public StageLoadPlan(string primaryScenePath, string[] dependencies)
            {
                PrimaryScenePath = primaryScenePath;
                Dependencies = dependencies ?? Array.Empty<string>();
            }

            public string PrimaryScenePath { get; }
            public string[] Dependencies { get; }

            public List<string> BuildLoadOrder()
            {
                var result = new List<string>(Dependencies.Length + 1);
                for (var i = 0; i < Dependencies.Length; i++)
                    result.Add(Dependencies[i]);
                result.Add(PrimaryScenePath);
                return result;
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

        public bool CompletePreEntryAndHandoff()
        {
            if (completionQueued || scenes.IsTransitioning || session.SelectedMode != TrainingMode.Full ||
                session.CurrentStage != TrainingStage.PreEntry)
            {
                return false;
            }

            return scenes.HandoffPreEntryToLoadedLeakResponse();
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
