using System;
using UnityEngine;

namespace HClTraining.VRInput
{
    [CreateAssetMenu(fileName = "VRInputVisualTheme", menuName = "HCl Training/VR Input Visual Theme")]
    public sealed class VRInputVisualTheme : ScriptableObject
    {
        [Serializable]
        public struct InputStyle
        {
            [SerializeField] string displayName;
            [SerializeField] Color color;

            public string DisplayName => displayName;
            public Color Color => color;

            public InputStyle(string displayName, Color color)
            {
                this.displayName = displayName;
                this.color = color;
            }
        }

        [Header("Global VR Input Styles")]
        [SerializeField] InputStyle a = new InputStyle("A", new Color(0.18f, 0.9f, 0.32f, 1f));
        [SerializeField] InputStyle b = new InputStyle("B", new Color(1f, 0.2f, 0.2f, 1f));
        [SerializeField] InputStyle x = new InputStyle("X", new Color(0.18f, 0.48f, 1f, 1f));
        [SerializeField] InputStyle y = new InputStyle("Y", new Color(1f, 0.85f, 0.12f, 1f));
        [SerializeField] InputStyle trigger = new InputStyle("Trigger", new Color(0.08f, 0.9f, 1f, 1f));
        [SerializeField] InputStyle grip = new InputStyle("Grip", new Color(1f, 0.48f, 0.08f, 1f));

        public InputStyle GetStyle(VRInputType input)
        {
            switch (input)
            {
                case VRInputType.A: return a;
                case VRInputType.B: return b;
                case VRInputType.X: return x;
                case VRInputType.Y: return y;
                case VRInputType.Trigger: return trigger;
                case VRInputType.Grip: return grip;
                default: return new InputStyle(input.ToString(), Color.white);
            }
        }

        public Color GetColor(VRInputType input) => GetStyle(input).Color;

        public string GetDisplayName(VRInputType input)
        {
            var displayName = GetStyle(input).DisplayName;
            return string.IsNullOrWhiteSpace(displayName) ? input.ToString() : displayName;
        }
    }
}
