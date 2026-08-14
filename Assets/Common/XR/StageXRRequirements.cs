using UnityEngine;

namespace HClTraining.SharedXR
{
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
            bool useComfortVignette = false)
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
        }
#endif
    }
}
