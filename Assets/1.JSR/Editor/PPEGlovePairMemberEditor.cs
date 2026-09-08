using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PPEGlovePairMember))]
public sealed class PPEGlovePairMemberEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var member = (PPEGlovePairMember)target;
        if (member == null || !member.IsTrackedHandGlove)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Glove Ghost Calibration", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Runtime position starts at the live XR Palm plus the small GhostPoseCalibration local position. " +
            "Mesh-origin compensation is separate. Rotation remains TargetPalm.rotation * calibration rotation. " +
            "Left and Right are independent.",
            MessageType.Info);

        var calibration = member.GhostPoseCalibrationOrNull;
        if (calibration != null)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector3Field("Local Position Offset", calibration.localPosition);
                EditorGUILayout.Vector3Field("Local Rotation Offset", calibration.localEulerAngles);
            }
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                EditorGUILayout.HelpBox(
                    "Quest runtime calibration: enable Development Runtime Calibration Mode, enter Play Mode, " +
                    "hold this glove, select GhostPoseCalibration, and tune the local position while the real hand stays visible.",
                    MessageType.None);
            }
        }

        if (GUILayout.Button("Select GhostPoseCalibration"))
        {
            var calib = member.EnsureGhostPoseCalibration();
            if (calib != null)
                Selection.activeTransform = calib;
        }
    }
}
