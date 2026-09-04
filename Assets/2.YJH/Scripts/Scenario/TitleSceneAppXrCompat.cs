using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>
    /// TitleScene XR 진입점.
    /// - 단독 Play: 스테이지 XR Origin (VR) 사용, 데스크톱 Main Camera 비활성
    /// - App 연동: Common XR Origin 우선, 스테이지 XR Origin 비활성
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TitleSceneAppXrCompat : MonoBehaviour
    {
        const string CommonXrOriginName = "Common XR Origin";
        const string StageXrOriginName = "XR Origin (VR)";
        const string StageInteractionManagerName = "XR Interaction Manager";

        [SerializeField] Camera titleDesktopCamera;
        [SerializeField] Transform spawnAnchor;
        [SerializeField] GameObject stageXrOrigin;

        void Awake()
        {
            if (titleDesktopCamera == null)
            {
                var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
                for (var i = 0; i < cameras.Length; i++)
                {
                    var camera = cameras[i];
                    if (camera != null && camera.CompareTag("MainCamera") &&
                        (stageXrOrigin == null || !camera.transform.IsChildOf(stageXrOrigin.transform)))
                    {
                        titleDesktopCamera = camera;
                        break;
                    }
                }
            }

            if (stageXrOrigin == null)
            {
                var stage = EntrySafetyAppXrResolve.FindActiveByName(StageXrOriginName);
                if (stage != null)
                    stageXrOrigin = stage.gameObject;
            }

            var commonXr = EntrySafetyAppXrResolve.FindActiveByName(CommonXrOriginName);
            if (commonXr != null)
            {
                if (spawnAnchor != null)
                    commonXr.SetPositionAndRotation(spawnAnchor.position, spawnAnchor.rotation);

                if (stageXrOrigin != null)
                    stageXrOrigin.SetActive(false);

                if (titleDesktopCamera != null)
                    titleDesktopCamera.gameObject.SetActive(false);

                // The App already owns the one live XRInteractionManager. Keep the
                // Title's standalone manager only when this Scene runs by itself.
                var stageManager = FindSceneObject(StageInteractionManagerName);
                if (stageManager != null)
                    stageManager.SetActive(false);
                return;
            }

            // Standalone Title: use stage XR Origin.
            if (stageXrOrigin != null)
            {
                stageXrOrigin.SetActive(true);
                if (spawnAnchor != null)
                    stageXrOrigin.transform.SetPositionAndRotation(spawnAnchor.position, spawnAnchor.rotation);
            }

            if (titleDesktopCamera != null)
                titleDesktopCamera.gameObject.SetActive(false);
        }

        GameObject FindSceneObject(string objectName)
        {
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < transforms.Length; i++)
                    if (transforms[i] != null && transforms[i].name == objectName)
                        return transforms[i].gameObject;
            }
            return null;
        }
    }
}
