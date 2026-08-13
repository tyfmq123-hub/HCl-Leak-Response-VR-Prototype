using System.Collections;
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

        private Material _materialInstance;
        private Color _opaqueColor;
        private bool _hasOpaqueColor;
        private bool _isCleared;
        private bool _isFading;

        public bool IsCleared => _isCleared;
        public bool IsFading => _isFading;

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
            EnsureReferences();
            CaptureOpaqueColor();
            _isCleared = false;
            _isFading = false;
            gameObject.SetActive(true);
            if (_renderer != null)
                _renderer.enabled = true;
            if (_collider != null)
                _collider.enabled = true;
            if (_hasOpaqueColor)
                ApplyColor(_opaqueColor);
        }

        public IEnumerator FadeOutAndClear()
        {
            if (_isCleared)
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
                var delta = Application.isPlaying ? Time.unscaledDeltaTime : duration;
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
        }

        private void EnsureReferences()
        {
            if (_renderer == null)
                _renderer = GetComponent<Renderer>();
            if (_collider == null)
                _collider = GetComponent<Collider>();

            if (_renderer == null || _materialInstance != null)
                return;

            var source = _renderer.sharedMaterial;
            _materialInstance = source != null
                ? new Material(source)
                : new Material(Shader.Find("Sprites/Default"));
            _renderer.sharedMaterial = _materialInstance;
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
