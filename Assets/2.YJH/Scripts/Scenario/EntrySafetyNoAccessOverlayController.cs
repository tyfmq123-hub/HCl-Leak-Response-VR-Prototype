using System.Collections;
using HClTraining.SharedXR;
using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>
    /// Fades out the restricted-area mesh after Goal 2 safety confirmation,
    /// then disables rendering and collision.
    /// </summary>
    public sealed class EntrySafetyNoAccessOverlayController : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField, Min(0.05f)] private float _fadeOutSeconds = 0.4f;
        [SerializeField] private Renderer _renderer;
        [SerializeField] private Collider _collider;
        [Header("LeakResponse Return Boundary")]
        [SerializeField, Min(1f)] private float _returnBoundaryWorldWidth = 6f;
        [SerializeField, Min(1f)] private float _returnBoundaryWorldHeight = 5f;
        [SerializeField, Min(0.05f)] private float _returnBoundaryWorldDepth = 0.2f;

        private Material _materialInstance;
        private BoxCollider _returnBoundaryCollider;
        private Color _opaqueColor;
        private bool _hasOpaqueColor;
        private bool _isCleared;
        private bool _isFading;

        public bool IsCleared => _isCleared;
        public bool IsFading => _isFading;
        public Collider PhysicalBoundaryCollider => _returnBoundaryCollider != null
            ? _returnBoundaryCollider
            : _collider;

        private void Awake()
        {
            EnsureReferences();
            CaptureOpaqueColor();
            RestoreImmediate();
        }

        private void OnDestroy()
        {
            if (_materialInstance == null)
                return;

            if (Application.isPlaying)
                Destroy(_materialInstance);
            else
                DestroyImmediate(_materialInstance);
        }

        public void RestoreImmediate()
        {
            if (this == null)
                return;

            EnsureReferences();
            CaptureOpaqueColor();
            _isCleared = false;
            _isFading = false;
            gameObject.SetActive(true);
            if (_renderer != null)
                _renderer.enabled = true;
            if (_collider != null)
                _collider.enabled = true;
            if (_returnBoundaryCollider != null)
                _returnBoundaryCollider.enabled = true;
            if (_hasOpaqueColor)
                ApplyColor(_opaqueColor);
        }

        public IEnumerator FadeOutAndClear()
        {
            if (this == null || _isCleared)
                yield break;

            EnsureReferences();
            CaptureOpaqueColor();
            if (_renderer == null)
            {
                ClearImmediate();
                yield break;
            }

            _isFading = true;
            var start = GetCurrentColor();
            var elapsed = 0f;
            var duration = Mathf.Max(0.05f, _fadeOutSeconds);
            while (elapsed < duration)
            {
                var delta = Application.isPlaying ? SharedTrainingPause.UnscaledDeltaTime : duration;
                elapsed += Mathf.Max(delta, 0.0001f);
                var t = Mathf.Clamp01(elapsed / duration);
                var color = start;
                color.a = Mathf.Lerp(start.a, 0f, t);
                ApplyColor(color);
                yield return null;
            }

            ClearImmediate();
        }

        public void ClearImmediate()
        {
            if (this == null)
                return;

            EnsureReferences();
            _isFading = false;
            _isCleared = true;
            if (_hasOpaqueColor)
            {
                var clear = _opaqueColor;
                clear.a = 0f;
                ApplyColor(clear);
            }

            if (_renderer != null)
                _renderer.enabled = false;
            if (_collider != null)
                _collider.enabled = false;
            if (_returnBoundaryCollider != null)
                _returnBoundaryCollider.enabled = false;
        }

        private void EnsureReferences()
        {
            if (this == null)
                return;

            if (_renderer == null)
                _renderer = GetComponent<Renderer>();
            if (_collider == null)
                _collider = GetComponent<Collider>();

            EnsureReturnBoundaryCollider();

            if (_renderer == null || _materialInstance != null)
                return;

            var source = _renderer.sharedMaterial;
            _materialInstance = source != null
                ? new Material(source)
                : new Material(Shader.Find("Sprites/Default"));
            _renderer.sharedMaterial = _materialInstance;
        }

        private void EnsureReturnBoundaryCollider()
        {
            if (_returnBoundaryCollider == null)
                _returnBoundaryCollider = GetComponent<BoxCollider>();
            if (_returnBoundaryCollider == null)
                _returnBoundaryCollider = gameObject.AddComponent<BoxCollider>();

            var scale = transform.lossyScale;
            _returnBoundaryCollider.center = Vector3.zero;
            _returnBoundaryCollider.size = new Vector3(
                _returnBoundaryWorldWidth / Mathf.Max(Mathf.Abs(scale.x), 0.001f),
                _returnBoundaryWorldHeight / Mathf.Max(Mathf.Abs(scale.y), 0.001f),
                _returnBoundaryWorldDepth / Mathf.Max(Mathf.Abs(scale.z), 0.001f));
            _returnBoundaryCollider.isTrigger = false;
        }

        private void CaptureOpaqueColor()
        {
            if (_hasOpaqueColor || _materialInstance == null)
                return;

            _opaqueColor = ReadColor(_materialInstance);
            if (_opaqueColor.a <= 0.001f)
                _opaqueColor.a = 1f;
            _hasOpaqueColor = true;
        }

        private Color GetCurrentColor()
        {
            return _materialInstance != null ? ReadColor(_materialInstance) : _opaqueColor;
        }

        private void ApplyColor(Color color)
        {
            if (_materialInstance == null)
                return;

            if (_materialInstance.HasProperty(BaseColorId))
                _materialInstance.SetColor(BaseColorId, color);
            if (_materialInstance.HasProperty(ColorId))
                _materialInstance.SetColor(ColorId, color);
        }

        private static Color ReadColor(Material material)
        {
            if (material.HasProperty(BaseColorId))
                return material.GetColor(BaseColorId);
            if (material.HasProperty(ColorId))
                return material.GetColor(ColorId);
            return Color.white;
        }
    }
}
