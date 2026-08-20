using UnityEngine;
using UnityEngine.SceneManagement;

namespace HClTraining.App
{
    /// <summary>
    /// Keeps direct stage-scene Play runs on the same App/Common XR path as the full flow.
    /// </summary>
    public static class StandaloneStageRedirect
    {
        const string EditorPendingModeKey = "HClTraining.PendingStandaloneMode";
        const string AppScenePath = "Assets/Scenes/01_App.unity";
        const string PpeScenePath = "Assets/Scenes/04_PPE.unity";
        const string PreEntryScenePath = "Assets/Scenes/05_PreEntry.unity";
        const string LeakResponseScenePath = "Assets/Scenes/06_LeakResponse.unity";

        static TrainingMode pendingMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterDirectStageLaunchRedirect()
        {
            pendingMode = TrainingMode.None;
            SceneManager.sceneLoaded -= OnInitialSceneLoaded;
            SceneManager.sceneLoaded += OnInitialSceneLoaded;
        }

        static void OnInitialSceneLoaded(Scene scene, LoadSceneMode loadMode)
        {
            SceneManager.sceneLoaded -= OnInitialSceneLoaded;
            pendingMode = ResolveMode(scene.path);
            if (pendingMode == TrainingMode.None)
                return;

            SceneManager.LoadScene(AppScenePath, LoadSceneMode.Single);
        }

        public static bool TryConsumePendingMode(out TrainingMode mode)
        {
            mode = pendingMode;
            pendingMode = TrainingMode.None;
#if UNITY_EDITOR
            if (mode == TrainingMode.None)
                mode = (TrainingMode)UnityEditor.SessionState.GetInt(EditorPendingModeKey, (int)TrainingMode.None);
            UnityEditor.SessionState.SetInt(EditorPendingModeKey, (int)TrainingMode.None);
#endif
            return mode != TrainingMode.None;
        }

        static TrainingMode ResolveMode(string scenePath)
        {
            var normalized = scenePath?.Replace('\\', '/');
            if (string.Equals(normalized, PpeScenePath, System.StringComparison.OrdinalIgnoreCase))
                return TrainingMode.PPEOnly;
            if (string.Equals(normalized, PreEntryScenePath, System.StringComparison.OrdinalIgnoreCase))
                return TrainingMode.PreEntryOnly;
            if (string.Equals(normalized, LeakResponseScenePath, System.StringComparison.OrdinalIgnoreCase))
                return TrainingMode.LeakResponseOnly;
            return TrainingMode.None;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterEditorPlayRedirect()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayModeChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeChanged;
        }

        static void OnEditorPlayModeChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingEditMode)
            {
                var mode = ResolveMode(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path);
                UnityEditor.SessionState.SetInt(EditorPendingModeKey, (int)mode);
                UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = mode == TrainingMode.None
                    ? null
                    : UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(AppScenePath);
            }
            else if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = null;
                UnityEditor.SessionState.SetInt(EditorPendingModeKey, (int)TrainingMode.None);
            }
        }
#endif
    }
}
