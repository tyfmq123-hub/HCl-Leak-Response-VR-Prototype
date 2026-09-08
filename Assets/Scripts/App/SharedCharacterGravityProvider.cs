using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace HClTraining.App
{
    /// <summary>
    /// Shared walking gravity, using the SAME capsule/ignored collision pairs as
    /// horizontal movement. A head-to-floor sphere cast can mistake a hand-held
    /// detector or ignored tabletop for ground and leave the player suspended.
    /// Does not change the HMD pose, standing height, or authored Stage spawn.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(XRInteractionUpdateOrder.k_GravityProvider)]
    public sealed class SharedCharacterGravityProvider : LocomotionProvider
    {
        [SerializeField, Min(0.1f)] float terminalVelocity = 20f;
        [SerializeField, Min(0.1f)] float groundedDownSpeed = 1f;
        CharacterController capsule;
        float fallSpeed;
        readonly XROriginMovement movement = new XROriginMovement();

        public bool IsGrounded => capsule != null && capsule.isGrounded;
        public void ResetFallForce() => fallSpeed = 0f;

        protected override void OnEnable()
        {
            base.OnEnable();
            ResetFallForce();
            var origin = mediator != null ? mediator.xrOrigin : null;
            capsule = origin != null && origin.Origin != null
                ? origin.Origin.GetComponent<CharacterController>() : null;
        }

        void Update()
        {
            var origin = mediator != null ? mediator.xrOrigin : null;
            if (origin == null || capsule == null || !capsule.enabled || Time.deltaTime <= 0f ||
                origin.CameraInOriginSpaceHeight <= 0.001f)
            {
                ResetFallForce();
                TryEndLocomotion();
                return;
            }

            // Keep downward contact even when the stick is idle. MoveBody sizes
            // the capsule from the current tracked head pose, including crouching.
            // Physics.IgnoreCollision on PPE/held tools is respected by this Move.
            fallSpeed = IsGrounded ? groundedDownSpeed :
                Mathf.Min(terminalVelocity, fallSpeed + Physics.gravity.magnitude * Time.deltaTime);
            TryStartLocomotionImmediately();
            if (locomotionState != LocomotionState.Moving) return;
            movement.motion = -origin.Origin.transform.up * (fallSpeed * Time.deltaTime);
            TryQueueTransformation(movement);
        }
    }
}
