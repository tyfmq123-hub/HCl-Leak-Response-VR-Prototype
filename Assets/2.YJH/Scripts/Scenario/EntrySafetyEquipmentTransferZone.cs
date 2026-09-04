using HClTraining.SharedXR;
using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Requires the left hand to remain inside the configured zone before equip input is accepted.</summary>
    public sealed class EntrySafetyEquipmentTransferZone : MonoBehaviour
    {
        [SerializeField] private Transform _leftHand;
        [SerializeField] private Collider _zoneCollider;
        [SerializeField, Min(0.01f)] private float _requiredDwellSeconds = 0.2f;
        [Header("Scene editing")]
        [SerializeField] private bool _drawSceneGizmo = true;
        [Tooltip("Optional. If enabled, keep BoxCollider size at (1,1,1) and resize the zone with the Scale tool. Leave off when this object has visual children.")]
        [SerializeField] private bool _useTransformScaleAsBoxSize = false;

        private EntrySafetyZoneDwellTimer _timer;

        public bool IsLeftHandInside { get; private set; }
        public bool IsReadyForEquip => _timer != null && _timer.IsCompleted;
        public float Progress01 => _timer?.Progress01 ?? 0f;
        public bool TryGetWorldBounds(out Bounds bounds)
        {
            ResolveRuntimeBindings();
            if (_zoneCollider == null)
            {
                bounds = default;
                return false;
            }

            bounds = _zoneCollider.bounds;
            return _zoneCollider.enabled && _zoneCollider.gameObject.activeInHierarchy;
        }
        public Transform TrackedLeftHand
        {
            get
            {
                ResolveRuntimeBindings();
                return _leftHand;
            }
        }

        private void Awake()
        {
            _timer = new EntrySafetyZoneDwellTimer(_requiredDwellSeconds);
            ApplyScaleToBoxColliderIfNeeded();
            ResolveRuntimeBindings();
        }

        private void OnValidate()
        {
            ApplyScaleToBoxColliderIfNeeded();
        }

        public void UpdateZone(bool isActive, float deltaTime)
        {
            ResolveRuntimeBindings();
            if (_leftHand == null || _zoneCollider == null)
            {
                IsLeftHandInside = false;
                _timer.Reset();
                return;
            }

            ApplyScaleToBoxColliderIfNeeded();
            IsLeftHandInside = isActive && _zoneCollider.bounds.Contains(_leftHand.position);
            _timer.Update(IsLeftHandInside, deltaTime);
            if (!isActive)
                _timer.Reset();
        }

        public void ResetZone()
        {
            IsLeftHandInside = false;
            _timer?.Reset();
        }

        /// <summary>
        /// Re-evaluates the live shared hand/bag pose on the exact Y-button frame.
        /// The normal Update path still provides dwell feedback, but a valid hand
        /// already inside the bag must not miss retrieval because of frame order.
        /// </summary>
        public bool EvaluateEquipAttempt()
        {
            ResolveRuntimeBindings();
            if (_leftHand == null || _zoneCollider == null)
            {
                IsLeftHandInside = false;
                _timer.Reset();
                return false;
            }

            IsLeftHandInside = _zoneCollider.bounds.Contains(_leftHand.position);
            _timer.Update(IsLeftHandInside, IsLeftHandInside ? _requiredDwellSeconds : 0f);
            return IsReadyForEquip;
        }

        private void ResolveRuntimeBindings()
        {
            _leftHand = EntrySafetyAppXrResolve.PreferCommonController(_leftHand, "Left Controller");
            _zoneCollider = EntrySafetyAppXrResolve.PreferLiveCollider(
                _zoneCollider,
                "PreEntry_BagRetrievalZone");

            // Last resort: local box on this object (solo-play / missing bag collider).
            if (_zoneCollider == null || !_zoneCollider.gameObject.activeInHierarchy)
            {
                var localBox = GetComponent<BoxCollider>();
                if (localBox != null)
                {
                    if (!localBox.enabled)
                        localBox.enabled = true;
                    _zoneCollider = localBox;
                }
            }
        }

        private void ApplyScaleToBoxColliderIfNeeded()
        {
            if (!_useTransformScaleAsBoxSize || _zoneCollider == null)
                return;

            // Only reshape a BoxCollider that lives on this object (never an external mesh collider).
            if (_zoneCollider.transform != transform)
                return;

            var box = _zoneCollider as BoxCollider;
            if (box == null)
                box = GetComponent<BoxCollider>();
            if (box == null)
                return;

            // World extents follow transform scale; keep local BoxCollider size at 1.
            if ((box.size - Vector3.one).sqrMagnitude > 0.000001f)
                box.size = Vector3.one;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_drawSceneGizmo)
                DrawZoneGizmo(false);
        }

        private void OnDrawGizmosSelected()
        {
            if (_drawSceneGizmo)
                DrawZoneGizmo(true);
        }

        private void DrawZoneGizmo(bool selected)
        {
            ApplyScaleToBoxColliderIfNeeded();
            var collider = _zoneCollider != null ? _zoneCollider : GetComponent<Collider>();
            if (collider == null)
                return;

            var color = new Color(0.25f, 1f, 0.45f, selected ? 0.95f : 0.55f);
            Gizmos.color = color;
            var previous = Gizmos.matrix;
            Gizmos.matrix = collider.transform.localToWorldMatrix;

            if (collider is BoxCollider box)
            {
                Gizmos.DrawWireCube(box.center, box.size);
                var fill = color;
                fill.a = selected ? 0.12f : 0.05f;
                Gizmos.color = fill;
                Gizmos.DrawCube(box.center, box.size);
            }
            else if (collider is SphereCollider sphere)
            {
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            }
            else
            {
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
            }

            Gizmos.matrix = previous;
        }
#endif
    }
}
