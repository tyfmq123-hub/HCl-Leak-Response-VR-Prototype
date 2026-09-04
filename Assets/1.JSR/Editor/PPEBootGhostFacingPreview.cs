using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PPEBootGhostFacingPreview
{
    const string PrefKey = "PPE.PreviewBootGhostFacing";
    static readonly Vector3 LeftToeLocal = new Vector3(0.9860346f, 0f, 0.1665409f);
    static readonly Vector3 RightToeLocal = new Vector3(0.9860046f, 0f, -0.1667183f);
    static Material previewMaterial;

    static void Toggle()
    {
        var enabled = !EditorPrefs.GetBool(PrefKey, false);
        EditorPrefs.SetBool(PrefKey, enabled);
        SceneView.RepaintAll();
        Debug.Log("[PPE Boot Ghost] Facing preview " + (enabled ? "enabled." : "disabled."));
    }

    static bool ValidateToggle()
    {
        Menu.SetChecked("Tools/HCl Training/PPE/Preview Boot Ghost Facing", EditorPrefs.GetBool(PrefKey, false));
        return true;
    }

    [InitializeOnLoadMethod]
    static void Register()
    {
        SceneView.duringSceneGui -= Draw;
        SceneView.duringSceneGui += Draw;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
    }

    static void Draw(SceneView view)
    {
        if (view == null || !EditorPrefs.GetBool(PrefKey, false))
            return;

        var members = Object.FindObjectsByType<PPEGlovePairMember>(
            FindObjectsInactive.Include);
        PPEGlovePairMember left = null;
        PPEGlovePairMember right = null;
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (member == null || member.TargetKind != PPEPairTargetKind.BodyLowerLeg)
                continue;
            if (member.GloveSide == PPEGrabHand.Right)
                right = member;
            else
                left = member;
        }

        if (left == null || right == null)
        {
            Handles.Label(view.pivot, "Boot pair not found in 04_PPE.");
            return;
        }

        var center = (left.transform.position + right.transform.position) * 0.5f +
                     Vector3.up * 0.35f +
                     Vector3.forward * 0.45f;
        var leftPosition = center + Vector3.left * 0.16f;
        var rightPosition = center + Vector3.right * 0.16f;

        Handles.color = new Color(0.2f, 1f, 0.35f, 1f);
        Handles.ArrowHandleCap(0, center + Vector3.back * 0.18f, Quaternion.identity, 0.32f, EventType.Repaint);
        Handles.Label(center + Vector3.forward * 0.18f, "Body Forward (+Z)");

        DrawBoot(view.camera, left, leftPosition, LeftToeLocal, "Left Boot");
        DrawBoot(view.camera, right, rightPosition, RightToeLocal, "Right Boot");
    }

    static void DrawBoot(
        Camera camera,
        PPEGlovePairMember member,
        Vector3 position,
        Vector3 toeLocal,
        string label)
    {
        var visual = member.VisualRoot;
        var calibration = member.GhostPoseCalibrationOrNull;
        if (visual == null || calibration == null)
            return;

        var visualRotation = member.GhostVisualLocalRotation;
        var filter = visual.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null && camera != null)
        {
            Graphics.DrawMesh(
                filter.sharedMesh,
                Matrix4x4.TRS(position, visualRotation, visual.lossyScale),
                PreviewMaterial,
                0,
                camera);
        }

        if (Selection.activeTransform == calibration)
        {
            EditorGUI.BeginChangeCheck();
            var newRotation = Handles.RotationHandle(calibration.localRotation, position);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(calibration, "Calibrate Boot Model Axis");
                calibration.localRotation = newRotation;
                EditorUtility.SetDirty(calibration);
                EditorSceneManager.MarkSceneDirty(member.gameObject.scene);
            }
        }

        var correctedToe = (visualRotation * toeLocal).normalized;
        Handles.color = Color.cyan;
        Handles.ArrowHandleCap(
            0,
            position,
            Quaternion.LookRotation(correctedToe, Vector3.up),
            0.22f,
            EventType.Repaint);
        Handles.Label(position + Vector3.up * 0.18f, label + " / select GhostPoseCalibration to rotate");
    }

    static Material PreviewMaterial
    {
        get
        {
            if (previewMaterial != null)
                return previewMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            previewMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
                color = new Color(0.25f, 0.75f, 1f, 0.35f),
            };
            return previewMaterial;
        }
    }

    static void Cleanup()
    {
        if (previewMaterial != null)
            Object.DestroyImmediate(previewMaterial);
        previewMaterial = null;
    }
}
