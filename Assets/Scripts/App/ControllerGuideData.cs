using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HClTraining.App
{
    public enum ControllerSide { Left, Right, Both }
    public enum VRGuideInputType { Grip, Trigger, XButton, YButton, AButton, BButton }

    [Serializable]
    public sealed class VRGuideStep
    {
        public string stepId;
        public string title;
        [TextArea] public string description;
        [TextArea] public string usageDescription;
        public string instructionText;
        public ControllerSide controllerSide;
        public VRGuideInputType inputType;
        public InputActionReference primaryAction;
        public InputActionReference alternateAction;
        [Min(0.2f)] public float completionDelay = 0.6f;
    }
}
