using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Identifies a Collider as an Entry Safety gaze target.</summary>
    public sealed class EntrySafetyGazeTargetBehaviour : MonoBehaviour
    {
        [SerializeField] private EntrySafetyGazeTargetId _targetId;
        [SerializeField] private bool _drawSceneGizmo = true;

        public EntrySafetyGazeTargetId TargetId => _targetId;

        public void SetTargetId(EntrySafetyGazeTargetId targetId)
        {
            _targetId = targetId;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!_drawSceneGizmo)
                return;

            DrawGazeGizmo(false);
        }

        private void OnDrawGizmosSelected()
        {
            if (!_drawSceneGizmo)
                return;

            DrawGazeGizmo(true);
        }

        private void DrawGazeGizmo(bool selected)
        {
            var collider = GetComponent<Collider>();
            if (collider == null)
                return;

            var color = GetGizmoColor();
            color.a = selected ? 0.95f : 0.55f;
            Gizmos.color = color;

            var previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

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
                var fill = color;
                fill.a = selected ? 0.12f : 0.05f;
                Gizmos.color = fill;
                Gizmos.DrawSphere(sphere.center, sphere.radius);
            }
            else
            {
                var bounds = collider.bounds;
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }

            Gizmos.matrix = previous;
        }

        private Color GetGizmoColor()
        {
            switch (_targetId)
            {
                case EntrySafetyGazeTargetId.InitialWindsock:
                case EntrySafetyGazeTargetId.ChangedWindsock:
                    return new Color(1.00f, 0.85f, 0.15f, 1f);
                case EntrySafetyGazeTargetId.PressureDisplay:
                case EntrySafetyGazeTargetId.TemperatureDisplay:
                case EntrySafetyGazeTargetId.ReducedPressureDisplay:
                case EntrySafetyGazeTargetId.RunIndicator:
                case EntrySafetyGazeTargetId.StopIndicator:
                    return new Color(0.20f, 0.85f, 0.95f, 1f);
                default:
                    return new Color(0.80f, 0.80f, 0.80f, 1f);
            }
        }
#endif
    }
}
