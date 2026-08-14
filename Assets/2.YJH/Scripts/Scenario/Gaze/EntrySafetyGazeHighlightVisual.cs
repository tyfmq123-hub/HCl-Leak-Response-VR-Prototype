using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>
    /// Thin rectangular world-space frame used to show the active P/T gaze readout.
    /// Does not modify panel materials; follows an anchor and visualizes the
    /// two-second panel gaze hold by blending its frame from yellow to green.
    /// </summary>
    [ExecuteAlways]
    public sealed class EntrySafetyGazeHighlightVisual : MonoBehaviour
    {
        private static readonly Color StartColor = new Color(1f, 0.86f, 0.08f, 0.95f);
        private static readonly Color CompleteColor = new Color(0.15f, 1f, 0.35f, 0.95f);

        [SerializeField, Min(0.01f)] private float _defaultWidth = 0.24f;
        [SerializeField, Min(0.01f)] private float _defaultHeight = 0.10f;
        [SerializeField, Min(0f)] private float _outwardOffsetMeters = 0.02f;
        [SerializeField, Min(0.001f)] private float _lineWidth = 0.008f;
        [SerializeField] private bool _showPreviewInEditMode = true;
        [SerializeField] private Transform _visualRoot;
        [SerializeField] private LineRenderer _frameLine;
        [SerializeField] private Material _lineMaterial;

        private Transform _anchor;
        private Vector3 _faceOutward = Vector3.back;
        private Vector2 _size = new Vector2(0.24f, 0.10f);
        private float _progress01;
        private bool _isVisible;

        public Vector2 CurrentSize => _size;

        private void Awake()
        {
            EnsureVisuals();
            if (Application.isPlaying)
                SetVisible(false);
        }

        private void OnEnable()
        {
            EnsureVisuals();
            if (!Application.isPlaying && _showPreviewInEditMode)
            {
                _size = new Vector2(_defaultWidth, _defaultHeight);
                _faceOutward = Vector3.back;
                _anchor = null;
                SetVisible(true);
                RebuildFrame(transform.position);
            }
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
                return;

            EnsureVisuals();
            if (!Application.isPlaying && _showPreviewInEditMode)
            {
                _size = new Vector2(_defaultWidth, _defaultHeight);
                RebuildFrame(transform.position);
            }
        }

        private void LateUpdate()
        {
            if (!_isVisible)
                return;

            var center = ResolveCenter();
            RebuildFrame(center);
            ApplyProgressColor();
        }

        public void SetVisible(bool visible)
        {
            EnsureVisuals();
            _isVisible = visible;
            if (!visible)
            {
                _anchor = null;
                _progress01 = 0f;
            }

            if (_visualRoot != null)
                _visualRoot.gameObject.SetActive(visible);

            if (_frameLine != null)
                _frameLine.enabled = visible;

            if (visible)
            {
                RebuildFrame(ResolveCenter());
                ApplyProgressColor();
            }
        }

        public void Follow(Transform anchor, Vector3 faceOutward, Vector2 sizeMeters)
        {
            EnsureVisuals();
            _anchor = anchor;
            _faceOutward = faceOutward.sqrMagnitude > 0.0001f ? faceOutward.normalized : Vector3.back;
            _size = new Vector2(
                Mathf.Max(0.01f, sizeMeters.x),
                Mathf.Max(0.01f, sizeMeters.y));
            SetVisible(anchor != null);
        }

        public void ClearFollow()
        {
            SetVisible(false);
        }

        public void SetProgress(float progress01)
        {
            _progress01 = Mathf.Clamp01(progress01);
            if (_isVisible)
                ApplyProgressColor();
        }

        public Vector3 GetFrameCornerLocal(int index)
        {
            var half = _size * 0.5f;
            switch (index)
            {
                case 0: return new Vector3(-half.x, -half.y, 0f);
                case 1: return new Vector3(-half.x, half.y, 0f);
                case 2: return new Vector3(half.x, half.y, 0f);
                case 3: return new Vector3(half.x, -half.y, 0f);
                default: return Vector3.zero;
            }
        }

        private Vector3 ResolveCenter()
        {
            var origin = _anchor != null ? _anchor.position : transform.position;
            return origin + _faceOutward * _outwardOffsetMeters;
        }

        private void RebuildFrame(Vector3 center)
        {
            EnsureVisuals();
            if (_frameLine == null)
                return;

            var rotation = Quaternion.LookRotation(-_faceOutward, Vector3.up);
            _visualRoot.SetPositionAndRotation(center, rotation);

            _frameLine.positionCount = 5;
            _frameLine.SetPosition(0, GetFrameCornerLocal(0));
            _frameLine.SetPosition(1, GetFrameCornerLocal(1));
            _frameLine.SetPosition(2, GetFrameCornerLocal(2));
            _frameLine.SetPosition(3, GetFrameCornerLocal(3));
            _frameLine.SetPosition(4, GetFrameCornerLocal(0));
            _frameLine.startWidth = _lineWidth;
            _frameLine.endWidth = _lineWidth;
        }

        private void ApplyProgressColor()
        {
            if (_frameLine == null)
                return;

            var color = Color.Lerp(StartColor, CompleteColor, _progress01);
            _frameLine.startColor = color;
            _frameLine.endColor = color;
        }

        private void EnsureVisuals()
        {
            if (_visualRoot == null)
            {
                var existing = transform.Find("VisualRoot");
                if (existing != null)
                    _visualRoot = existing;
                else
                {
                    var rootObject = new GameObject("VisualRoot");
                    rootObject.transform.SetParent(transform, false);
                    _visualRoot = rootObject.transform;
                }
            }

            if (_frameLine == null)
                _frameLine = _visualRoot.GetComponent<LineRenderer>();

            if (_frameLine == null)
                _frameLine = _visualRoot.gameObject.AddComponent<LineRenderer>();

            _frameLine.useWorldSpace = false;
            _frameLine.loop = false;
            _frameLine.positionCount = 5;
            _frameLine.alignment = LineAlignment.View;
            _frameLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _frameLine.receiveShadows = false;
            _frameLine.allowOcclusionWhenDynamic = false;
            _frameLine.numCapVertices = 2;
            _frameLine.numCornerVertices = 2;

            if (_lineMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    _lineMaterial = new Material(shader);
            }

            if (_lineMaterial != null && _frameLine.sharedMaterial != _lineMaterial)
                _frameLine.sharedMaterial = _lineMaterial;
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(_lineMaterial);
                else
                    DestroyImmediate(_lineMaterial);
            }
        }
    }
}
