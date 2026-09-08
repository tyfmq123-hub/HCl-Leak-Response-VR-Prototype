using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>
    /// Scene-placed gaze-hold progress bar.
    /// Uses one dark track line + one yellow progress line with higher sortingOrder,
    /// so the fill is visible from 0% (avoids transparent center-sort hiding the left half).
    /// </summary>
    [ExecuteAlways]
    public sealed class EntrySafetyGazeProgressVisual : MonoBehaviour
    {
        private static readonly Color BackgroundColor = new Color(0.05f, 0.05f, 0.07f, 1f);
        private static readonly Color FillColor = new Color(1f, 0.9f, 0.1f, 1f);
        private static readonly Color CompleteColor = new Color(0.15f, 1f, 0.35f, 1f);

        [SerializeField, Min(0.05f)] private float _barWidth = 4f;
        [SerializeField, Min(0.01f)] private float _barThickness = 0.22f;
        [SerializeField, Range(0f, 1f)] private float _editorPreviewProgress = 0.2f;
        [SerializeField] private bool _showPreviewInEditMode = true;
        [SerializeField] private bool _faceCameraWhilePlaying = true;
        [SerializeField] private Camera _billboardCamera;
        [SerializeField] private Transform _visualRoot;
        [SerializeField] private LineRenderer _backgroundLine;
        [SerializeField] private LineRenderer _fillLine;
        [SerializeField] private Material _lineMaterial;

        private float _progress01;
        private bool _isComplete;
        private Transform _followVisual;
        private Vector3 _followWorldOffsetFromBoundsCenter;
        private bool _hasFollowOffset;

        public void SetBillboardCamera(Camera camera)
        {
            _billboardCamera = camera;
        }

        /// <summary>
        /// Tracks the visible windsock mesh the same way the step arrow does:
        /// follow renderer bounds, not the gaze-collider origin. The authored
        /// world offset from that visual center is kept so the bar translates
        /// with the sock after WindPivot rotation.
        /// </summary>
        public void Follow(Transform visual)
        {
            _followVisual = visual;
        }

        private void Awake()
        {
            EnsureVisuals();
            if (Application.isPlaying)
            {
                SetProgress(0f, false);
                SetVisible(false);
            }
        }

        private void OnEnable()
        {
            EnsureVisuals();
            if (!Application.isPlaying)
            {
                SetProgress(_editorPreviewProgress, false);
                SetVisible(_showPreviewInEditMode);
            }
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
                return;

            EnsureVisuals();
            if (!Application.isPlaying)
                SetProgress(_editorPreviewProgress, false);
        }

        private void LateUpdate()
        {
            ApplyFollow();
            if (!_faceCameraWhilePlaying)
                return;
            if (_visualRoot == null || !_visualRoot.gameObject.activeSelf)
                return;
            if (!Application.isPlaying && !_showPreviewInEditMode)
                return;

            FaceCamera();
            RebuildLines();
        }

        public void SetVisible(bool visible)
        {
            EnsureVisuals();
            ApplyFollow();
            if (_visualRoot != null)
                _visualRoot.gameObject.SetActive(visible);

            if (visible)
            {
                FaceCamera();
                RebuildLines();
            }
        }

        public void SetProgress(float progress01, bool isComplete)
        {
            EnsureVisuals();
            _progress01 = Mathf.Clamp01(progress01);
            _isComplete = isComplete;
            RebuildLines();
        }

        public float GetFillRightEdgeLocalX()
        {
            return -_barWidth * 0.5f + _barWidth * _progress01;
        }

        private void ApplyFollow()
        {
            if (_followVisual == null || !TryGetRendererBounds(_followVisual, out var bounds))
                return;

            if (bounds.size.sqrMagnitude < 0.0001f)
                return;

            if (!_hasFollowOffset)
            {
                _followWorldOffsetFromBoundsCenter = transform.position - bounds.center;
                _hasFollowOffset = true;
            }

            transform.position = bounds.center + _followWorldOffsetFromBoundsCenter;
        }

        private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
                return false;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var found = false;
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;
                if (!found)
                {
                    bounds = renderers[i].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            return found;
        }

        private void FaceCamera()
        {
            var camera = ResolveCamera();
            if (camera == null)
                return;

            var awayFromCamera = transform.position - camera.transform.position;
            if (awayFromCamera.sqrMagnitude <= 0.0001f)
                return;

            transform.rotation = Quaternion.LookRotation(awayFromCamera, Vector3.up);
        }

        private Camera ResolveCamera()
        {
            _billboardCamera = EntrySafetyAppXrResolve.PreferLiveMainCamera(_billboardCamera);
            return _billboardCamera;
        }

        private void RebuildLines()
        {
            if (_backgroundLine == null || _fillLine == null)
                return;

            var half = _barWidth * 0.5f;
            var leftX = -half;
            var rightX = half;
            var splitX = leftX + _barWidth * _progress01;

            // Split the bar into non-overlapping segments so transparent sorting
            // cannot hide the early (left) fill behind the full-width track.
            // [yellow fill: left → split] [dark empty: split → right]
            var fillColor = _isComplete ? CompleteColor : FillColor;
            var hasFill = _progress01 > 0.001f;
            var hasEmpty = _progress01 < 0.999f;

            _fillLine.enabled = hasFill;
            _fillLine.sortingOrder = 20;
            if (hasFill)
            {
                _fillLine.SetPosition(0, new Vector3(leftX, 0f, 0f));
                _fillLine.SetPosition(1, new Vector3(Mathf.Max(leftX + 0.001f, splitX), 0f, 0f));
                _fillLine.startWidth = _barThickness;
                _fillLine.endWidth = _barThickness;
                _fillLine.startColor = fillColor;
                _fillLine.endColor = fillColor;
            }

            _backgroundLine.enabled = hasEmpty;
            _backgroundLine.sortingOrder = 10;
            if (hasEmpty)
            {
                _backgroundLine.SetPosition(0, new Vector3(Mathf.Min(splitX, rightX - 0.001f), 0f, 0f));
                _backgroundLine.SetPosition(1, new Vector3(rightX, 0f, 0f));
                _backgroundLine.startWidth = _barThickness;
                _backgroundLine.endWidth = _barThickness;
                _backgroundLine.startColor = BackgroundColor;
                _backgroundLine.endColor = BackgroundColor;
            }
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
                    var root = new GameObject("VisualRoot");
                    root.transform.SetParent(transform, false);
                    _visualRoot = root.transform;
                }
            }

            HideLegacyChild(_visualRoot, "GazeProgressBackground");
            HideLegacyChild(_visualRoot, "FillPivot");
            HideLegacyChild(_visualRoot, "GazeProgressLabel");
            HideLegacyChild(_visualRoot, "GazeProgressBackground_LegacyHidden");
            HideLegacyChild(_visualRoot, "FillPivot_LegacyHidden");

            EnsureLineMaterial();
            if (_backgroundLine == null)
                _backgroundLine = FindOrCreateLine("GazeProgressBackgroundLine", BackgroundColor, 0);
            if (_fillLine == null)
                _fillLine = FindOrCreateLine("GazeProgressFillLine", FillColor, 20);

            _backgroundLine.sortingOrder = 0;
            _fillLine.sortingOrder = 20;
        }

        private void EnsureLineMaterial()
        {
            if (_lineMaterial != null)
                return;

            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                return;

            _lineMaterial = new Material(shader) { name = "GazeHoldBar_Line" };
            _lineMaterial.color = Color.white;
            if (_lineMaterial.HasProperty("_MainTex"))
                _lineMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (_lineMaterial.HasProperty("_BaseMap"))
                _lineMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (_lineMaterial.HasProperty("_BaseColor"))
                _lineMaterial.SetColor("_BaseColor", Color.white);
        }

        private LineRenderer FindOrCreateLine(string name, Color color, int sortingOrder)
        {
            var existing = _visualRoot.Find(name);
            LineRenderer line;
            if (existing != null)
            {
                line = existing.GetComponent<LineRenderer>();
                if (line == null)
                    line = existing.gameObject.AddComponent<LineRenderer>();
            }
            else
            {
                var go = new GameObject(name);
                go.transform.SetParent(_visualRoot, false);
                line = go.AddComponent<LineRenderer>();
            }

            line.useWorldSpace = false;
            line.positionCount = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.sortingOrder = sortingOrder;
            if (_lineMaterial != null)
                line.sharedMaterial = _lineMaterial;

            line.startColor = color;
            line.endColor = color;
            return line;
        }

        private static void HideLegacyChild(Transform parent, string childName)
        {
            if (parent == null)
                return;

            var child = parent.Find(childName);
            if (child == null)
                return;

            if (child.gameObject.activeSelf)
                child.gameObject.SetActive(false);
        }
    }
}
