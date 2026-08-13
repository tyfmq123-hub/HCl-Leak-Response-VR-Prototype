using UnityEditor;
using UnityEngine;

namespace VRProject.Valves.Editor
{
    [CustomEditor(typeof(ValveWheelClosureController))]
    public sealed class ValveWheelClosureControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ValveWheelClosureController controller = (ValveWheelClosureController)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime Debug", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField("Current Hinge Angle", controller.CurrentHingeAngle);
                EditorGUILayout.FloatField("Current Closure Percent", controller.ClosurePercent);
                EditorGUILayout.FloatField("Current Leak Intensity", controller.LeakIntensity01);
                EditorGUILayout.EnumPopup("Current State", controller.CurrentState);
                EditorGUILayout.EnumPopup("Rotation Direction", controller.CurrentRotationDirection);
                EditorGUILayout.Toggle("Is Grabbed", controller.IsGrabbed);
            }
        }
    }
}
