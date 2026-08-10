using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Supplies a registered gaze target hit by the configured camera ray, ignoring ordinary colliders.</summary>
    public sealed class EntrySafetyCameraGazeAdapter : MonoBehaviour
    {
        private const int MaxRaycastHits = 32;

        [SerializeField] private Camera _mainCamera;
        [SerializeField] private LayerMask _layerMask = ~0;
        [SerializeField, Min(0.01f)] private float _maxDistance = 30f;
        [SerializeField] private QueryTriggerInteraction _queryTriggerInteraction = QueryTriggerInteraction.Collide;
        [SerializeField] private bool _showGuideRay;
        [SerializeField, Min(0.1f)] private float _guideRayIdleLength = 3f;
        // Quest/HMD comfort center is often slightly below geometric viewport center.
        [SerializeField] private Vector2 _gazeViewportPoint = new Vector2(0.5f, 0.47f);
        [SerializeField, Range(0f, 1f)] private float _guideRayOpacity = 0f;

        private readonly RaycastHit[] _hits = new RaycastHit[MaxRaycastHits];
        private LineRenderer _guideLine;
        private Transform _guideMarker;
        private Renderer _guideMarkerRenderer;
        private Material _guideMaterial;

        public Camera GazeCamera => _mainCamera;

        public bool TryGetTarget(out EntrySafetyGazeTargetId targetId)
        {
            targetId = EntrySafetyGazeTargetId.None;
            if (_mainCamera == null)
                return false;

            var ray = CreateGazeRay();
            var hitCount = Physics.RaycastNonAlloc(ray, _hits, _maxDistance, _layerMask, _queryTriggerInteraction);
            var closestTargetDistance = float.PositiveInfinity;
            for (var i = 0; i < hitCount; i++)
            {
                var target = _hits[i].collider.GetComponentInParent<EntrySafetyGazeTargetBehaviour>();
                if (target == null || target.TargetId == EntrySafetyGazeTargetId.None ||
                    _hits[i].distance >= closestTargetDistance)
                    continue;

                closestTargetDistance = _hits[i].distance;
                targetId = target.TargetId;
            }

            UpdateGuideRay(ray, targetId != EntrySafetyGazeTargetId.None
                ? closestTargetDistance
                : Mathf.Min(_guideRayIdleLength, _maxDistance), targetId != EntrySafetyGazeTargetId.None);
            return targetId != EntrySafetyGazeTargetId.None;
        }

        public void SetGuideRayVisible(bool visible)
        {
            if (_mainCamera == null)
                return;

            _showGuideRay = visible;
            if (!visible)
            {
                if (_guideLine != null)
                    _guideLine.enabled = false;
                if (_guideMarker != null)
                    _guideMarker.gameObject.SetActive(false);
                return;
            }

            EnsureGuideVisual();
            ApplyGuideVisualState();
        }

        private Ray CreateGazeRay()
        {
            var viewport = new Vector3(
                Mathf.Clamp01(_gazeViewportPoint.x),
                Mathf.Clamp01(_gazeViewportPoint.y),
                0f);
            return _mainCamera.ViewportPointToRay(viewport, Camera.MonoOrStereoscopicEye.Mono);
        }

        private void UpdateGuideRay(Ray ray, float distance, bool hasTarget)
        {
            if (!_showGuideRay)
                return;

            EnsureGuideVisual();
            var endPosition = ray.GetPoint(distance);
            _guideLine.SetPosition(0, ray.origin);
            _guideLine.SetPosition(1, endPosition);
            var color = hasTarget
                ? new Color(0.2f, 1f, 0.8f, _guideRayOpacity)
                : new Color(1f, 0.75f, 0.2f, _guideRayOpacity);
            _guideLine.startColor = color;
            _guideLine.endColor = color;
            _guideMarker.position = endPosition;
            if (_guideMarkerRenderer != null && _guideMarkerRenderer.sharedMaterial != null)
                _guideMarkerRenderer.sharedMaterial.color = color;
            ApplyGuideVisualState();
        }

        private void ApplyGuideVisualState()
        {
            var showVisual = _showGuideRay && _guideRayOpacity > 0.001f;
            if (_guideLine != null)
                _guideLine.enabled = showVisual;
            if (_guideMarker != null)
                _guideMarker.gameObject.SetActive(showVisual);
        }

        private void EnsureGuideVisual()
        {
            if (_guideLine != null)
                return;

            var guideObject = new GameObject("EntrySafetyGazeGuideRay");
            guideObject.transform.SetParent(_mainCamera.transform, false);
            _guideLine = guideObject.AddComponent<LineRenderer>();
            _guideLine.useWorldSpace = true;
            _guideLine.positionCount = 2;
            _guideLine.startWidth = 0.006f;
            _guideLine.endWidth = 0.006f;
            _guideLine.alignment = LineAlignment.View;

            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _guideMaterial = new Material(shader);
                _guideLine.material = _guideMaterial;
            }

            var markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            markerObject.name = "EntrySafetyGazeGuidePoint";
            DestroyObject(markerObject.GetComponent<Collider>());
            markerObject.transform.localScale = Vector3.one * 0.035f;
            markerObject.transform.SetParent(guideObject.transform, true);
            _guideMarkerRenderer = markerObject.GetComponent<Renderer>();
            if (_guideMaterial != null && _guideMarkerRenderer != null)
                _guideMarkerRenderer.material = _guideMaterial;
            _guideMarker = markerObject.transform;
        }

        private void OnDestroy()
        {
            if (_guideMaterial != null)
                DestroyObject(_guideMaterial);
        }

        private static void DestroyObject(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}
