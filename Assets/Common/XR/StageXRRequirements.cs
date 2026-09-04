using UnityEngine;

namespace HClTraining.SharedXR
{
    public enum StageXRHandVisualMode
    {
        Controllers = 0,
        LeftHand = 1,
        RightHand = 2,
        BothHands = 3,
    }

    /// <summary>
    /// Stage-owned declaration of the shared XR capabilities it needs.
    /// Store the asset below that Stage's own Resources folder. The App consumes
    /// this contract without knowing the Stage name.
    /// </summary>
    [CreateAssetMenu(menuName = "HCl Training/Shared XR/Stage Requirements")]
    public sealed class StageXRRequirements : ScriptableObject
    {
        [SerializeField] string scenePath;
        [SerializeField] Vector3 spawnPosition;
        [SerializeField] Vector3 spawnEulerAngles;
        [SerializeField] bool farInteraction;
        [SerializeField] bool nearInteraction;
        [SerializeField] bool uiInteraction;
        [SerializeField] bool grabInteraction;
        [SerializeField] bool haptics;
        [SerializeField] bool movement;
        [SerializeField] bool turning;
        [SerializeField] bool comfortVignette;
        [SerializeField] StageXRHandVisualMode handVisualMode;

        public string ScenePath => scenePath;
        public Vector3 SpawnPosition => spawnPosition;
        public Quaternion SpawnRotation => Quaternion.Euler(spawnEulerAngles);
        public bool FarInteraction => farInteraction;
        public bool NearInteraction => nearInteraction;
        public bool UIInteraction => uiInteraction;
        public bool GrabInteraction => grabInteraction;
        public bool Haptics => haptics;
        public bool Movement => movement;
        public bool Turning => turning;
        public bool ComfortVignette => comfortVignette;
        public StageXRHandVisualMode HandVisualMode => handVisualMode;

        /// <summary>
        /// Near/Far already includes far casting. Activating both visual systems
        /// would produce two lines, so Near/Far is the single pointer when requested.
        /// </summary>
        public bool UseNearFarPointer => nearInteraction;
        public bool UseStandardRayPointer => farInteraction && !nearInteraction;

#if UNITY_EDITOR
        public void EditorConfigure(
            string valueScenePath,
            Vector3 position,
            Quaternion rotation,
            bool far,
            bool near,
            bool ui,
            bool grab,
            bool useHaptics,
            bool allowMovement = true,
            bool allowTurning = true,
            bool useComfortVignette = false,
            StageXRHandVisualMode visualMode = StageXRHandVisualMode.Controllers)
        {
            scenePath = valueScenePath;
            spawnPosition = position;
            spawnEulerAngles = rotation.eulerAngles;
            farInteraction = far;
            nearInteraction = near;
            uiInteraction = ui;
            grabInteraction = grab;
            haptics = useHaptics;
            movement = allowMovement;
            turning = allowTurning;
            comfortVignette = useComfortVignette;
            handVisualMode = visualMode;
        }
#endif
    }

    public static class WorldBoardUiPlacement
    {
        public static bool TryPlace(
            Transform uiRoot, Camera camera, string boardName, Vector2 canvasSize,
            float surfaceOffset = 0.12f, float inset = 0.9f)
        {
            if (uiRoot == null || camera == null || string.IsNullOrWhiteSpace(boardName))
                return false;

            Transform board = null;
            var transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && transforms[i].name == boardName)
                {
                    board = transforms[i];
                    break;
                }
            }
            if (board == null)
                return false;

            var renderers = board.GetComponentsInChildren<Renderer>(true);
            Renderer surface = null;
            var largestArea = 0f;
            for (var i = 0; i < renderers.Length; i++)
            {
                var candidate = renderers[i];
                if (candidate == null)
                    continue;
                var size = candidate.bounds.size;
                var area = Mathf.Max(size.x * size.y, size.x * size.z, size.y * size.z);
                if (area > largestArea)
                {
                    largestArea = area;
                    surface = candidate;
                }
            }
            if (surface == null)
                return false;

            var localSize = surface.localBounds.size;
            var scale = surface.transform.lossyScale;
            var lengths = new[]
            {
                Mathf.Abs(localSize.x * scale.x),
                Mathf.Abs(localSize.y * scale.y),
                Mathf.Abs(localSize.z * scale.z),
            };
            var axes = new[]
            {
                surface.transform.right.normalized,
                surface.transform.up.normalized,
                surface.transform.forward.normalized,
            };

            var normalIndex = lengths[0] <= lengths[1] && lengths[0] <= lengths[2]
                ? 0 : lengths[1] <= lengths[2] ? 1 : 2;
            var firstPlaneIndex = (normalIndex + 1) % 3;
            var secondPlaneIndex = (normalIndex + 2) % 3;
            var upIndex = Mathf.Abs(Vector3.Dot(axes[firstPlaneIndex], Vector3.up)) >=
                          Mathf.Abs(Vector3.Dot(axes[secondPlaneIndex], Vector3.up))
                ? firstPlaneIndex : secondPlaneIndex;
            var widthIndex = upIndex == firstPlaneIndex ? secondPlaneIndex : firstPlaneIndex;

            var normal = axes[normalIndex];
            var towardCamera = camera.transform.position - surface.bounds.center;
            if (Vector3.Dot(normal, towardCamera) < 0f)
                normal = -normal;
            var up = axes[upIndex];
            if (Vector3.Dot(up, Vector3.up) < 0f)
                up = -up;

            uiRoot.position = surface.bounds.center + normal * surfaceOffset;
            uiRoot.rotation = Quaternion.LookRotation(-normal, up);
            var widthScale = lengths[widthIndex] / Mathf.Max(1f, canvasSize.x);
            var heightScale = lengths[upIndex] / Mathf.Max(1f, canvasSize.y);
            uiRoot.localScale = Vector3.one * Mathf.Min(widthScale, heightScale) * inset;
            return true;
        }
    }
}
