using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Keeps the detector display renderer enabled without changing its user-authored transform.</summary>
    public sealed class EntrySafetyHclDetectorDisplayBillboard : MonoBehaviour
    {
        private Renderer _displayRenderer;

        private void Awake()
        {
            _displayRenderer = GetComponent<Renderer>();
        }

        private void LateUpdate()
        {
            if (_displayRenderer == null)
                _displayRenderer = GetComponent<Renderer>();

            if (_displayRenderer != null)
                _displayRenderer.enabled = true;
        }
    }
}
