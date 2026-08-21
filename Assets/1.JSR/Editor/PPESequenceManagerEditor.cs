using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PPESequenceManager))]
public sealed class PPESequenceManagerEditor : Editor
{
    SerializedProperty donningOrder;
    SerializedProperty equipmentPreparationOrder;
    SerializedProperty sequenceOrder;
    SerializedProperty showBlockedFeedback;
    SerializedProperty blockedMessage;
    SerializedProperty bagNotCheckedMessage;

    void OnEnable()
    {
        donningOrder = serializedObject.FindProperty("donningOrder");
        equipmentPreparationOrder = serializedObject.FindProperty("equipmentPreparationOrder");
        sequenceOrder = serializedObject.FindProperty("sequenceOrder");
        showBlockedFeedback = serializedObject.FindProperty("showBlockedFeedback");
        blockedMessage = serializedObject.FindProperty("blockedMessage");
        bagNotCheckedMessage = serializedObject.FindProperty("bagNotCheckedMessage");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var manager = (PPESequenceManager)target;

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.EnumPopup("Current Phase", manager.CurrentPhase);
            EditorGUILayout.TextField("Required Step", manager.CurrentRequiredStep?.ToString() ?? "(none)");
        }

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(donningOrder, true);
        EditorGUILayout.PropertyField(equipmentPreparationOrder, true);

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(showBlockedFeedback);
        EditorGUILayout.PropertyField(blockedMessage);
        EditorGUILayout.PropertyField(bagNotCheckedMessage);

        if (sequenceOrder != null && sequenceOrder.arraySize > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Legacy mixed Sequence Order is kept for migration only. Runtime uses Donning Order and Equipment Preparation Order.",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(sequenceOrder, new GUIContent("Legacy Sequence Order"), true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
