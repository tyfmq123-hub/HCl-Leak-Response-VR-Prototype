using System;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRProject.LeakResponse.Narration
{
    /// <summary>Locks only the carrying interactor's select signal; activation stays physical.
    /// Unlike nested manual selection, cancellation/unloading needs no second SelectExit.</summary>
    public sealed class P102CarryInputLock : IXRInputButtonReader, IDisposable
    {
        readonly XRBaseInputInteractor interactor;
        readonly XRInputButtonReader input;
        readonly IXRInputButtonReader previousBypass;
        readonly XRBaseInputInteractor.InputTriggerType previousTrigger;
        bool disposed;

        public P102CarryInputLock(XRBaseInputInteractor owner)
        {
            interactor = owner;
            input = owner.selectInput;
            previousBypass = input.bypass;
            previousTrigger = owner.selectActionTrigger;
            input.bypass = this;
            owner.selectActionTrigger = XRBaseInputInteractor.InputTriggerType.State;
        }

        public void ReadPhysicalGrip(out bool held, out bool pressed)
        {
            // Read the unchanged physical binding, including a previous upstream bypass.
            input.bypass = previousBypass;
            try
            {
                held = input.ReadIsPerformed();
                pressed = input.ReadWasPerformedThisFrame();
            }
            finally { if (!disposed) input.bypass = this; }
        }

        public bool ReadIsPerformed() => !disposed;
        public bool ReadWasPerformedThisFrame() => false;
        public bool ReadWasCompletedThisFrame() => false;
        public float ReadValue() => disposed ? 0f : 1f;
        public bool TryReadValue(out float value) { value = ReadValue(); return true; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (ReferenceEquals(input.bypass, this)) input.bypass = previousBypass;
            if (interactor != null) interactor.selectActionTrigger = previousTrigger;
        }
    }
}
