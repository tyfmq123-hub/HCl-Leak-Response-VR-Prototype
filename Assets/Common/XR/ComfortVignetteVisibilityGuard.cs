using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;

namespace HClTraining.SharedXR
{
    /// <summary>
    /// Stops the no-effect comfort mesh from being submitted on XR runtimes.
    /// The XRI controller still owns all easing and locomotion behavior; this
    /// component only hides the renderer after aperture has fully returned to 1.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TunnelingVignetteController), typeof(MeshRenderer))]
    public sealed class ComfortVignetteVisibilityGuard : MonoBehaviour
    {
        const float NoEffectAperture = 0.9999f;

        TunnelingVignetteController controller;
        MeshRenderer vignetteRenderer;

        public bool IsVisuallyActive => vignetteRenderer != null && vignetteRenderer.enabled;

        void Awake()
        {
            controller = GetComponent<TunnelingVignetteController>();
            vignetteRenderer = GetComponent<MeshRenderer>();
            SetRendererVisible(false);
        }

        void LateUpdate()
        {
            var shouldRender = controller != null &&
                               controller.isActiveAndEnabled &&
                               controller.currentParameters != null &&
                               controller.currentParameters.apertureSize < NoEffectAperture;
            SetRendererVisible(shouldRender);
        }

        void OnDisable()
        {
            SetRendererVisible(false);
        }

        void SetRendererVisible(bool visible)
        {
            if (vignetteRenderer != null && vignetteRenderer.enabled != visible)
                vignetteRenderer.enabled = visible;
        }
    }
}
