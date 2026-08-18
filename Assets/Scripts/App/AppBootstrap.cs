using System.Collections;
using HClTraining.SharedXR;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace HClTraining.App
{
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class AppBootstrap : MonoBehaviour
    {
        [SerializeField] GameObject commonXrOrigin;

        public static AppBootstrap Instance { get; private set; }
        public TrainingSessionState Session { get; private set; }
        public ScenarioFlowManager ScenarioFlow { get; private set; }
        public SceneFlowController SceneFlow { get; private set; }
        public PersistentEquipmentInventory Inventory { get; private set; }
        public PPEStateController PPEState { get; private set; }
        public StageXRProfileController XRProfiles { get; private set; }
        public SharedXRInputContract SharedInput { get; private set; }
        public SharedPlayerEquipment SharedEquipment { get; private set; }
        public CommonErrorUI ErrorUI { get; private set; }
        public TrainingPauseController PauseController { get; private set; }
        public Camera MainCamera { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Quest screenshots include the eye texture outside the visible lens.
            // URP's hidden-area occlusion mesh leaves that crescent unrendered,
            // exposing the camera clear color on the right edge of captures.
            XRSRPSettings.occlusionMeshScale = 0f;

            ResolveCommonXr();
            if (commonXrOrigin == null)
            {
                Debug.LogError("[App Bootstrap] 공용 XR Origin이 App Scene에 없습니다.", this);
                enabled = false;
                return;
            }

            commonXrOrigin.SetActive(true);
            MainCamera = commonXrOrigin.GetComponentInChildren<Camera>(true);
            if (MainCamera == null)
            {
                Debug.LogError("[App Bootstrap] 공용 XR Main Camera 참조가 없습니다.", this);
                enabled = false;
                return;
            }
            EnsureSingleEventSystem();

            Session = GetOrAdd<TrainingSessionState>();
            Inventory = GetOrAdd<PersistentEquipmentInventory>();
            PPEState = GetOrAdd<PPEStateController>();
            SharedInput = GetOrAdd<SharedXRInputContract>();
            SharedEquipment = GetOrAdd<SharedPlayerEquipment>();
            XRProfiles = GetOrAdd<StageXRProfileController>();
            var inputLock = GetOrAdd<TransitionInputLock>();
            var fade = GetOrAdd<ScreenFadeController>();
            var lifecycle = GetOrAdd<StageLifecycleAdapter>();
            PauseController = GetOrAdd<TrainingPauseController>();
            SceneFlow = GetOrAdd<SceneFlowController>();
            ScenarioFlow = GetOrAdd<ScenarioFlowManager>();
            ErrorUI = GetOrAdd<CommonErrorUI>();

            SharedEquipment.Initialize(commonXrOrigin.transform);
            Inventory.Initialize(Session, SharedEquipment.RetrievalAnchor);
            PPEState.Initialize(Session, Inventory, commonXrOrigin.transform);
            SharedInput.Initialize(commonXrOrigin);
            XRProfiles.Initialize(commonXrOrigin);
            inputLock.Initialize(commonXrOrigin.transform);
            fade.Initialize(MainCamera);
            fade.SetImmediate(1f);
            ErrorUI.Initialize(MainCamera);
            ScenarioFlow.Initialize(Session, SceneFlow, Inventory);
            lifecycle.Initialize(ScenarioFlow, Session, Inventory, PPEState);
            SceneFlow.Initialize(inputLock, fade, XRProfiles, lifecycle, Session, PPEState, Inventory, ErrorUI);
            PauseController.Initialize(lifecycle, SharedInput, commonXrOrigin.transform);
        }

        IEnumerator Start()
        {
            yield return null;
            SceneFlow.ShowTitle();
        }

        void ResolveCommonXr()
        {
            if (commonXrOrigin != null)
                return;
            var origins = FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < origins.Length; i++)
            {
                if (origins[i] != null && origins[i].gameObject.scene == gameObject.scene)
                {
                    commonXrOrigin = origins[i].gameObject;
                    return;
                }
            }
        }

        void EnsureSingleEventSystem()
        {
            var systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            EventSystem keep = null;
            for (var i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null || systems[i].gameObject.scene != gameObject.scene)
                    continue;
                if (keep == null)
                {
                    keep = systems[i];
                    keep.gameObject.SetActive(true);
                }
                else
                {
                    systems[i].gameObject.SetActive(false);
                }
            }

            if (keep == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
                keep = go.GetComponent<EventSystem>();
            }
            else if (keep.GetComponent<XRUIInputModule>() == null)
            {
                keep.gameObject.AddComponent<XRUIInputModule>();
            }

            var modules = keep.GetComponents<BaseInputModule>();
            XRUIInputModule xrModule = null;
            for (var i = 0; i < modules.Length; i++)
            {
                if (modules[i] is XRUIInputModule candidate && xrModule == null)
                {
                    xrModule = candidate;
                    candidate.enabled = true;
                }
                else
                {
                    modules[i].enabled = false;
                }
            }

            // XRUIInputModule keeps its built-in mouse fallback when no explicit
            // 2D actions are assigned, while registered XR interactors continue to
            // drive tracked pointers. Copying actions from a disabled desktop module
            // prevents that fallback from owning the complete click lifecycle.
        }

        T GetOrAdd<T>() where T : Component
        {
            var component = GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
