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

        [SerializeField, Range(1f, 15f)] private float _windsockAcquireAngle = 7f;
        [SerializeField, Range(1f, 20f)] private float _windsockRetainAngle = 10f;
        private bool _trackingWindsock;
        private EntrySafetyGazeTargetId _lastWindsockId;

        public Camera GazeCamera => PreferCamera();

        // A distant windsock must not require hitting a few pixels with a mono viewport ray.
        // Use the head's centre direction and the authored target bounds; keep panel gaze precise.
        public bool TryGetWindsockTarget(EntrySafetyGazeTargetBehaviour target,
            out EntrySafetyGazeTargetId targetId)
        {
            targetId = EntrySafetyGazeTargetId.None;
            var camera = PreferCamera();
            if (camera == null || target == null || !target.isActiveAndEnabled)
            {
                _trackingWindsock = false;
                return false;
            }
            if (_lastWindsockId != target.TargetId)
                _trackingWindsock = false;
            _lastWindsockId = target.TargetId;

            var collider = target.GetComponent<Collider>();
            if (collider == null || !collider.enabled)
                return _trackingWindsock = false;
            var ray = new Ray(camera.transform.position, camera.transform.forward);
            var bounds = collider.bounds;
            float depth = Vector3.Dot(bounds.center - ray.origin, ray.direction);
            var nearest = bounds.ClosestPoint(ray.GetPoint(Mathf.Max(0f, depth)));
            float angle = _trackingWindsock ? _windsockRetainAngle : _windsockAcquireAngle;
            _trackingWindsock = IsInsideGazeCone(ray, nearest, angle, Mathf.Max(60f, _maxDistance));
            UpdateGuideRay(camera, ray, _trackingWindsock ? Mathf.Max(0.1f, depth) : _guideRayIdleLength,
                _trackingWindsock);
            if (_trackingWindsock)
                targetId = target.TargetId;
            return _trackingWindsock;
        }

        public static bool IsInsideGazeCone(Ray headRay, Vector3 target, float angle, float maxDistance)
        {
            Vector3 offset = target - headRay.origin;
            return offset.sqrMagnitude <= maxDistance * maxDistance &&
                   Vector3.Dot(headRay.direction, offset) > 0f &&
                   Vector3.Angle(headRay.direction, offset) <= angle;
        }

        public bool TryGetTarget(out EntrySafetyGazeTargetId targetId)
        {
            targetId = EntrySafetyGazeTargetId.None;
            var camera = PreferCamera();
            if (camera == null)
                return false;

            var ray = CreateGazeRay(camera);
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

            UpdateGuideRay(camera, ray, targetId != EntrySafetyGazeTargetId.None
                ? closestTargetDistance
                : Mathf.Min(_guideRayIdleLength, _maxDistance), targetId != EntrySafetyGazeTargetId.None);
            return targetId != EntrySafetyGazeTargetId.None;
        }

        public void SetGuideRayVisible(bool visible)
        {
            var camera = PreferCamera();
            if (camera == null)
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

            EnsureGuideVisual(camera);
            ApplyGuideVisualState();
        }

        private Camera PreferCamera()
        {
            _mainCamera = EntrySafetyAppXrResolve.PreferLiveMainCamera(_mainCamera);
            return _mainCamera;
        }

        private Ray CreateGazeRay(Camera camera)
        {
            var viewport = new Vector3(
                Mathf.Clamp01(_gazeViewportPoint.x),
                Mathf.Clamp01(_gazeViewportPoint.y),
                0f);
            return camera.ViewportPointToRay(viewport, Camera.MonoOrStereoscopicEye.Mono);
        }

        private void UpdateGuideRay(Camera camera, Ray ray, float distance, bool hasTarget)
        {
            if (!_showGuideRay)
                return;

            EnsureGuideVisual(camera);
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

        private void EnsureGuideVisual(Camera camera)
        {
            if (_guideLine != null)
            {
                if (_guideLine.transform.parent != camera.transform)
                    _guideLine.transform.SetParent(camera.transform, false);
                return;
            }

            var guideObject = new GameObject("EntrySafetyGazeGuideRay");
            guideObject.transform.SetParent(camera.transform, false);
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
            DestroyRuntimeObject(markerObject.GetComponent<Collider>());
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
                DestroyRuntimeObject(_guideMaterial);
        }

        private static void DestroyRuntimeObject(Object target)
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
