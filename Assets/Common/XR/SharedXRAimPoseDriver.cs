using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace HClTraining.SharedXR
{
    /// <summary>
    /// Drives the existing interactor transform from the controller pointer pose.
    /// The visible controller remains on grip/device tracking, and no extra ray or
    /// line visual is created.
    /// </summary>
    [DefaultExecutionOrder(-110)]
    [DisallowMultipleComponent]
    public sealed class SharedXRAimPoseDriver : MonoBehaviour
    {
        [SerializeField] SharedXRHand hand;
        [SerializeField] Transform trackingSpace;

        InputAction positionAction;
        InputAction rotationAction;
        bool initialized;

        public void Initialize(SharedXRHand value, Transform valueTrackingSpace)
        {
            hand = value;
            trackingSpace = valueTrackingSpace;
            ResolveActions();
        }

        void OnEnable()
        {
            ResolveActions();
            Application.onBeforeRender += OnBeforeRender;
        }

        void OnDisable() => Application.onBeforeRender -= OnBeforeRender;

        void ResolveActions()
        {
            var input = SharedXRInputContract.Instance;
            if (input == null)
                return;
            input.TryGetAction(hand, SharedXRControl.Position, out positionAction);
            input.TryGetAction(hand, SharedXRControl.Rotation, out rotationAction);
            initialized = positionAction != null && rotationAction != null;
        }

        void Update() => ApplyPose();

        [BeforeRenderOrder(99)]
        void OnBeforeRender() => ApplyPose();

        void ApplyPose()
        {
            if (!initialized)
                ResolveActions();
            if (!initialized || trackingSpace == null || transform.parent == null ||
                !positionAction.enabled || !rotationAction.enabled ||
                positionAction.activeControl == null || rotationAction.activeControl == null)
            {
                return;
            }

            var trackingPosition = positionAction.ReadValue<Vector3>();
            var trackingRotation = rotationAction.ReadValue<Quaternion>();
            var worldPosition = trackingSpace.TransformPoint(trackingPosition);
            var worldRotation = trackingSpace.rotation * trackingRotation;
            transform.SetPositionAndRotation(worldPosition, worldRotation);
        }
    }
}
