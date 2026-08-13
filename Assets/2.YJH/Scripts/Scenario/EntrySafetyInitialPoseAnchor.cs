using System.Collections;
using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Places the equipment zone once from the user's initial HMD pose without following later head motion.</summary>
    public sealed class EntrySafetyInitialPoseAnchor : MonoBehaviour
    {
        [SerializeField] private Transform _head;
        [SerializeField] private Transform _equipmentZone;
        [SerializeField] private Vector3 _equipmentZoneOffset = new Vector3(-0.254f, -0.907f, -0.110f);
        [SerializeField, Min(0f)] private float _headPoseTimeoutSeconds = 10f;
        [SerializeField, Min(0f)] private float _minimumTrackedHeadHeight = 1.2f;
        [SerializeField, Min(0f)] private float _lateTrackingCorrectionWindowSeconds = 10f;
        [SerializeField, Min(0.01f)] private float _lateTrackingCorrectionDistanceMeters = 0.25f;
        [Header("Scene editing")]
        [Tooltip("Use Context Menu 'Capture Equipment Zone Offset From Current Placement' after moving the zone in Scene view.")]
        [SerializeField] private bool _showOffsetPreviewGizmo = true;

        private bool _hasAnchored;
        private bool _hasAppliedLateTrackingCorrection;
        private Vector3 _anchoredHeadPosition;
        private float _anchoredAtTime;

        private IEnumerator Start()
        {
            var timeoutAt = Time.unscaledTime + _headPoseTimeoutSeconds;
            while (!HasUsableHeadPose() && Time.unscaledTime < timeoutAt)
                yield return null;

            if (HasUsableHeadPose())
                AnchorToInitialPose();
        }

        private void Update()
        {
            if (!_hasAnchored)
            {
                if (HasUsableHeadPose())
                    AnchorToInitialPose();
                return;
            }

            if (!_hasAppliedLateTrackingCorrection &&
                Time.unscaledTime - _anchoredAtTime <= _lateTrackingCorrectionWindowSeconds &&
                Vector3.Distance(_head.position, _anchoredHeadPosition) >= _lateTrackingCorrectionDistanceMeters)
            {
                _hasAppliedLateTrackingCorrection = true;
                AnchorToInitialPose();
            }
        }

        private bool HasUsableHeadPose()
        {
            return _head != null && _head.position.y >= _minimumTrackedHeadHeight;
        }

        public bool AnchorToInitialPose()
        {
            if (_head == null || _equipmentZone == null)
                return false;

            var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            var yawRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            _equipmentZone.SetPositionAndRotation(
                _head.position + yawRotation * _equipmentZoneOffset,
                yawRotation);
            _hasAnchored = true;
            _anchoredHeadPosition = _head.position;
            _anchoredAtTime = Time.unscaledTime;
            return true;
        }

        /// <summary>Stores the current zone placement as a head-relative offset for the next Play Mode anchor.</summary>
        public bool CaptureOffsetFromCurrentZonePlacement()
        {
            if (_head == null || _equipmentZone == null)
                return false;

            var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            var yawRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            _equipmentZoneOffset = Quaternion.Inverse(yawRotation) * (_equipmentZone.position - _head.position);
            return true;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!_showOffsetPreviewGizmo || _head == null)
                return;

            var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            var yawRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            var previewPosition = _head.position + yawRotation * _equipmentZoneOffset;

            Gizmos.color = new Color(0.25f, 1f, 0.45f, 0.9f);
            Gizmos.DrawWireSphere(previewPosition, 0.03f);
            Gizmos.DrawLine(_head.position, previewPosition);
        }

        [ContextMenu("Capture Equipment Zone Offset From Current Placement")]
        private void CaptureOffsetFromCurrentZonePlacementMenu()
        {
            CaptureOffsetFromCurrentZonePlacement();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
#endif
    }
}
