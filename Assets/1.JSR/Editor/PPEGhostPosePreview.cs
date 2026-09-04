using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor-only glove Ghost calibration. Draws reference palms and the glove mesh
/// at TargetPalm * GhostPoseCalibration without entering Play Mode.
/// This class lives under Assets/1.JSR/Editor and does not ship in player builds.
/// </summary>
public static class PPEGhostPosePreview
{
    public const string PrefKey = "PPE.PreviewGloveGhostCalibration";
    const string ScenePath = "Assets/Scenes/04_PPE.unity";
    const string LeftHandModelPath =
        "Assets/Samples/XR Hands/1.7.3/HandVisualizer/Models/LeftHand.fbx";
    const string RightHandModelPath =
        "Assets/Samples/XR Hands/1.7.3/HandVisualizer/Models/RightHand.fbx";
    static readonly Vector3 ControllerPalmLocalOffset = new Vector3(0f, 0f, 0.07f);

    static bool hidTools;
    static Material previewMaterial;
    static Material referenceHandMaterial;
    static ReferenceHandPreview leftReference;
    static ReferenceHandPreview rightReference;

    sealed class ReferenceHandPreview
    {
        readonly GameObject root;
        readonly Transform palm;
        readonly Transform middleTip;
        readonly SkinnedMeshRenderer[] skins;
        readonly Mesh[] bakedMeshes;
        Matrix4x4 targetWorldToLocal = Matrix4x4.identity;

        ReferenceHandPreview(GameObject instance, string prefix)
        {
            root = instance;
            SetHideFlagsRecursively(root.transform);
            var behaviours = root.GetComponentsInChildren<Behaviour>(true);
            for (var i = 0; i < behaviours.Length; i++)
                if (behaviours[i] != null)
                    behaviours[i].enabled = false;

            palm = FindNamed(root.transform, prefix + "_Palm");
            middleTip = FindNamed(root.transform, prefix + "_MiddleTip");
            skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            bakedMeshes = new Mesh[skins.Length];
            for (var i = 0; i < skins.Length; i++)
            {
                bakedMeshes[i] = new Mesh
                {
                    name = "PPE Reference Hand Preview",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                skins[i].enabled = false;
            }
        }

        public static ReferenceHandPreview Create(string assetPath, string prefix)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
                return null;
            var instance = Object.Instantiate(asset);
            instance.name = "PPE_GhostCalibration_" + prefix + "_Reference";
            return new ReferenceHandPreview(instance, prefix);
        }

        public void SetPalmPose(Vector3 position, Quaternion rotation)
        {
            if (root == null)
                return;

            // The hand the trainee sees is the FBX root under Left/Right
            // Controller. Controller fallback TargetPalm is the same rotation
            // plus a +Z 7 cm palm offset, so reconstruct that visible root here.
            root.transform.rotation = rotation;
            root.transform.position = position - rotation * ControllerPalmLocalOffset;
            targetWorldToLocal = Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
        }

        public void Draw(Camera camera)
        {
            if (root == null || camera == null)
                return;
            for (var i = 0; i < skins.Length; i++)
            {
                var skin = skins[i];
                var mesh = bakedMeshes[i];
                if (skin == null || mesh == null)
                    continue;
                skin.BakeMesh(mesh, true);
                Graphics.DrawMesh(
                    mesh,
                    skin.transform.localToWorldMatrix,
                    ReferenceHandMaterial,
                    0,
                    camera);
            }
        }

        public bool TryGetPalmLocalGeometry(
            out Vector3 fingerDirection,
            out Vector3 palmNormal,
            out Vector3 boundsCenter)
        {
            fingerDirection = Vector3.forward;
            palmNormal = Vector3.up;
            boundsCenter = Vector3.zero;
            if (root == null || palm == null)
                return false;

            if (middleTip != null)
                fingerDirection = targetWorldToLocal.MultiplyVector(
                    (middleTip.position - palm.position).normalized);
            palmNormal = targetWorldToLocal.MultiplyVector(palm.up).normalized;

            var hasBounds = false;
            var bounds = new Bounds();
            for (var i = 0; i < skins.Length; i++)
            {
                var skin = skins[i];
                var mesh = bakedMeshes[i];
                if (skin == null || mesh == null)
                    continue;
                skin.BakeMesh(mesh, true);
                EncapsulateBounds(
                    mesh.bounds,
                    targetWorldToLocal * skin.transform.localToWorldMatrix,
                    ref bounds,
                    ref hasBounds);
            }

            if (hasBounds)
                boundsCenter = bounds.center;
            return true;
        }

        public void Destroy()
        {
            if (root != null)
                Object.DestroyImmediate(root);
            for (var i = 0; i < bakedMeshes.Length; i++)
                if (bakedMeshes[i] != null)
                    Object.DestroyImmediate(bakedMeshes[i]);
        }
    }

    static void TogglePreview()
    {
        var enabled = !EditorPrefs.GetBool(PrefKey, false);
        EditorPrefs.SetBool(PrefKey, enabled);
        if (!enabled)
        {
            RestoreTools();
            DestroyReferenceHands();
        }
        SceneView.RepaintAll();
        Debug.Log(
            "[PPE Glove Ghost] Preview " + (enabled ? "ON" : "OFF") +
            ". Select a GhostPoseCalibration child and use Move/Rotate against the reference palms. " +
            "Not shown in player builds. Default is OFF.");
    }

    static bool TogglePreviewValidate()
    {
        var on = EditorPrefs.GetBool(PrefKey, false);
        Menu.SetChecked("Tools/HCl Training/PPE/Preview Glove Ghost Calibration", on);
        Menu.SetChecked("Tools/HCl Training/PPE/Glove Ghost Calibration Preview", on);
        Menu.SetChecked("Tools/PPE/Preview Glove Ghost Calibration", on);
        return true;
    }

    static void EnsureGloveCalibrations()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            Debug.LogWarning("[PPE Glove Ghost] Open 04_PPE.unity for authored children.");

        var created = 0;
        var members = Object.FindObjectsByType<PPEGlovePairMember>(FindObjectsInactive.Include);
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (!IsAuthorableGlove(member))
                continue;
            Undo.RecordObject(member, "Ensure Ghost Pose Calibration");
            var before = member.GhostPoseCalibrationOrNull;
            var calib = member.EnsureGhostPoseCalibration();
            if (before == null && calib != null)
            {
                created++;
                Undo.RegisterCreatedObjectUndo(calib.gameObject, "Create GhostPoseCalibration");
            }

            EditorUtility.SetDirty(member);
        }

        if (scene.IsValid())
            EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("[PPE Glove Ghost] Ensured GhostPoseCalibration on the four physical gloves. New children: " + created);
    }

    static void ResetSelected()
    {
        var member = ResolveSelectedGloveMember();
        if (member == null)
        {
            Debug.LogWarning("[PPE Glove Ghost] Select a glove physical member or its GhostPoseCalibration.");
            return;
        }

        Undo.RecordObject(member, "Reset Ghost Calibration");
        var calib = member.EnsureGhostPoseCalibration();
        if (calib != null)
            Undo.RecordObject(calib, "Reset Ghost Calibration");
        member.EditorResetGhostCalibration();
        EditorUtility.SetDirty(member);
        if (calib != null)
            EditorUtility.SetDirty(calib);
        EditorSceneManager.MarkSceneDirty(member.gameObject.scene);
        SceneView.RepaintAll();
    }

    static void CopyMirrored()
    {
        var member = ResolveSelectedGloveMember();
        if (member == null || member.Coordinator == null)
        {
            Debug.LogWarning("[PPE Glove Ghost] Select a glove physical member in a pair.");
            return;
        }

        var source = member.GloveSide == PPEGrabHand.Left
            ? member.Coordinator.RightMember
            : member.Coordinator.LeftMember;
        if (source == null)
        {
            Debug.LogWarning("[PPE Glove Ghost] Opposite pair member is missing.");
            return;
        }

        Undo.RecordObject(member, "Copy Mirrored Calibration");
        var calib = member.EnsureGhostPoseCalibration();
        if (calib != null)
            Undo.RecordObject(calib, "Copy Mirrored Calibration");
        member.EditorCopyMirroredCalibrationFrom(source);
        EditorUtility.SetDirty(member);
        if (calib != null)
            EditorUtility.SetDirty(calib);
        EditorSceneManager.MarkSceneDirty(member.gameObject.scene);
        SceneView.RepaintAll();
        Debug.Log(
            "[PPE Glove Ghost] Copied mirrored calibration from " + source.name + " onto " + member.name +
            ". Left/Right were not auto-mirrored; this command was explicit.");
    }

    static void AlignSelectedToReferenceHand()
    {
        var member = ResolveSelectedGloveMember();
        if (member == null)
        {
            Debug.LogWarning("[PPE Glove Ghost] Select a glove physical member or its GhostPoseCalibration.");
            return;
        }

        EnsureReferenceHands();
        var reference = member.GloveSide == PPEGrabHand.Right ? rightReference : leftReference;
        if (reference == null ||
            !reference.TryGetPalmLocalGeometry(
                out var targetFinger,
                out var targetNormal,
                out var targetCenter) ||
            !TryGetSourceGeometry(
                member,
                out var sourcePalm,
                out var sourceFinger,
                out var sourceNormal,
                out var sourceCenter,
                out var hasSourcePalm))
        {
            Debug.LogWarning("[PPE Glove Ghost] Could not derive a safe geometry-based starting pose.", member);
            return;
        }

        var sourceBasis = SafeLookRotation(sourceFinger, sourceNormal);
        var targetBasis = SafeLookRotation(targetFinger, targetNormal);
        var calibrationRotation = targetBasis * Quaternion.Inverse(sourceBasis);
        var sourceScale = member.VisualRoot != null ? member.VisualRoot.lossyScale : Vector3.one;
        var sourcePoint = hasSourcePalm ? sourcePalm : sourceCenter;
        var targetPoint = hasSourcePalm ? Vector3.zero : targetCenter;
        var calibrationPosition =
            targetPoint - calibrationRotation * Vector3.Scale(sourcePoint, sourceScale);

        var calib = member.EnsureGhostPoseCalibration();
        Undo.RecordObject(calib, "Align Ghost To Reference Hand");
        Undo.RecordObject(member, "Align Ghost To Reference Hand");
        member.EditorSetGhostVisualOriginOffset(calibrationPosition);
        calib.localPosition = Vector3.zero;
        calib.localRotation = calibrationRotation;
        EditorUtility.SetDirty(calib);
        EditorUtility.SetDirty(member);
        EditorSceneManager.MarkSceneDirty(member.gameObject.scene);
        Selection.activeTransform = calib;
        EditorPrefs.SetBool(PrefKey, true);
        SceneView.RepaintAll();
        Debug.Log(
            "[PPE Glove Ghost] Geometry-based starting pose applied to " + member.name +
            ". Fine-tune GhostPoseCalibration against the visible reference hand before Quest acceptance.",
            member);
    }

    static void AlignSelectedEquipReferenceToGhostPalm()
    {
        var member = ResolveSelectedGloveMember();
        if (member == null)
        {
            Debug.LogWarning("[PPE Glove Ghost] Select a glove physical member, GhostPoseCalibration, or EquipReference.");
            return;
        }

        AlignEquipReferenceToGhostPalm(member);
    }

    public static bool AlignEquipReferenceToGhostPalm(PPEGlovePairMember member)
    {
        if (!IsAuthorableGlove(member) ||
            member.VisualRoot == null ||
            member.GhostPoseCalibrationOrNull == null)
            return false;

        var equipReference = member.EnsureEquipReference();
        if (equipReference == null)
            return false;

        var calibration = member.GhostPoseCalibrationOrNull;
        var visualToMember =
            member.transform.worldToLocalMatrix * member.VisualRoot.localToWorldMatrix;
        var ghostVisualInTarget = Matrix4x4.TRS(
            calibration.localPosition + member.GhostVisualOriginOffset,
            calibration.localRotation,
            member.VisualRoot.lossyScale);
        var memberToTarget = ghostVisualInTarget * visualToMember.inverse;
        var semanticPalmInMember = memberToTarget.inverse.MultiplyPoint3x4(Vector3.zero);

        Undo.RecordObject(equipReference, "Align EquipReference To Ghost Palm");
        var semanticPalmWorld = member.transform.TransformPoint(semanticPalmInMember);
        equipReference.position = semanticPalmWorld;
        equipReference.localRotation = Quaternion.identity;
        equipReference.localScale = Vector3.one;
        EditorUtility.SetDirty(equipReference);
        EditorUtility.SetDirty(member);
        EditorSceneManager.MarkSceneDirty(member.gameObject.scene);
        Selection.activeTransform = equipReference;
        EditorPrefs.SetBool(PrefKey, true);
        SceneView.RepaintAll();
        Debug.Log(
            "[PPE Glove Ghost] EquipReference aligned independently to the target Palm for " +
            member.name + ". GhostPoseCalibration was not changed.",
            member);
        return true;
    }

    [InitializeOnLoadMethod]
    static void RegisterSceneGui()
    {
        SceneView.duringSceneGui -= OnSceneGui;
        SceneView.duringSceneGui += OnSceneGui;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnSceneGui(SceneView view)
    {
        if (view == null || !EditorPrefs.GetBool(PrefKey, false))
        {
            RestoreTools();
            DestroyReferenceHands();
            return;
        }

        GetReferencePalmPoses(out var leftPalmPos, out var leftPalmRot, out var rightPalmPos, out var rightPalmRot);
        EnsureReferenceHands();
        if (leftReference != null)
        {
            leftReference.SetPalmPose(leftPalmPos, leftPalmRot);
            leftReference.Draw(view.camera);
        }
        if (rightReference != null)
        {
            rightReference.SetPalmPose(rightPalmPos, rightPalmRot);
            rightReference.Draw(view.camera);
        }
        DrawPalmGizmo(leftPalmPos, leftPalmRot, "Reference Left Palm");
        DrawPalmGizmo(rightPalmPos, rightPalmRot, "Reference Right Palm");

        var members = Object.FindObjectsByType<PPEGlovePairMember>(FindObjectsInactive.Include);
        var handledSelection = false;
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (!IsAuthorableGlove(member))
                continue;
            var isRight = member.GloveSide == PPEGrabHand.Right;
            if (DrawGlovePreview(
                    view,
                    member,
                    isRight ? rightPalmPos : leftPalmPos,
                    isRight ? rightPalmRot : leftPalmRot))
                handledSelection = true;
        }

        if (!handledSelection)
            RestoreTools();
    }

    static bool IsAuthorableGlove(PPEGlovePairMember member)
    {
        if (member == null || !member.IsTrackedHandGlove)
            return false;
        var n = member.name;
        return n == "LeftInnerGlovePhysical" ||
               n == "RightInnerGlovePhysical" ||
               n == "LeftProtectiveGlovePhysical" ||
               n == "RightProtectiveGlovePhysical";
    }

    static PPEGlovePairMember ResolveSelectedGloveMember()
    {
        var selected = Selection.activeTransform;
        if (selected == null)
            return null;
        var member = selected.GetComponent<PPEGlovePairMember>();
        if (member == null)
            member = selected.GetComponentInParent<PPEGlovePairMember>();
        return IsAuthorableGlove(member) ? member : null;
    }

    static void GetReferencePalmPoses(
        out Vector3 leftPos,
        out Quaternion leftRot,
        out Vector3 rightPos,
        out Quaternion rightRot)
    {
        var origin = Vector3.zero;
        var yaw = Quaternion.identity;
        var members = Object.FindObjectsByType<PPEGlovePairMember>(FindObjectsInactive.Include);
        for (var i = 0; i < members.Length; i++)
        {
            if (!IsAuthorableGlove(members[i]))
                continue;
            origin = members[i].transform.position;
            var forward = Vector3.ProjectOnPlane(members[i].transform.forward, Vector3.up);
            if (forward.sqrMagnitude > 0.0001f)
                yaw = Quaternion.LookRotation(forward.normalized, Vector3.up);
            break;
        }

        var stage = origin + yaw * new Vector3(0f, 0.35f, 0.45f);
        leftPos = stage + yaw * new Vector3(-0.12f, 0f, 0f);
        rightPos = stage + yaw * new Vector3(0.12f, 0f, 0f);
        // Independent of HMD / holding hand / body yaw. +Z along fingers, +Y out of palm.
        leftRot = yaw * Quaternion.Euler(-90f, 0f, 90f);
        rightRot = yaw * Quaternion.Euler(-90f, 0f, -90f);
    }

    static void DrawPalmGizmo(Vector3 position, Quaternion rotation, string label)
    {
        Handles.color = new Color(1f, 0.85f, 0.2f, 0.95f);
        Handles.matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
        Handles.DrawWireDisc(Vector3.zero, Vector3.up, 0.035f);
        Handles.DrawLine(Vector3.zero, Vector3.forward * 0.09f);
        Handles.color = Color.red;
        Handles.DrawLine(Vector3.zero, Vector3.right * 0.04f);
        Handles.color = Color.green;
        Handles.DrawLine(Vector3.zero, Vector3.up * 0.04f);
        Handles.color = Color.blue;
        Handles.DrawLine(Vector3.zero, Vector3.forward * 0.08f);
        Handles.matrix = Matrix4x4.identity;
        Handles.Label(position + Vector3.up * 0.06f, label);
    }

    static bool DrawGlovePreview(SceneView view, PPEGlovePairMember member, Vector3 palmPos, Quaternion palmRot)
    {
        var calib = member.GhostPoseCalibrationOrNull;
        if (calib == null)
            return false;

        var semanticRootPos = palmPos + palmRot * calib.localPosition;
        var previewPos = semanticRootPos + palmRot * member.GhostVisualOriginOffset;
        var previewRot = palmRot * calib.localRotation;
        var equipReference = member.EquipReference;
        var selected = Selection.activeTransform == calib;
        var equipSelected = equipReference != null && Selection.activeTransform == equipReference;

        if (selected)
        {
            Tools.hidden = true;
            hidTools = true;
            EditorGUI.BeginChangeCheck();
            var newPos = semanticRootPos;
            var newRot = previewRot;
            var handleRot = Tools.pivotRotation == PivotRotation.Local ? previewRot : Quaternion.identity;
            if (Tools.current == Tool.Rotate)
            {
                newRot = Handles.RotationHandle(previewRot, semanticRootPos);
            }
            else if (Tools.current == Tool.Move)
            {
                newPos = Handles.PositionHandle(semanticRootPos, handleRot);
            }
            else
            {
                newPos = Handles.PositionHandle(semanticRootPos, handleRot);
                newRot = Handles.RotationHandle(previewRot, semanticRootPos);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(calib, "Calibrate Glove Ghost Pose");
                calib.localPosition = Quaternion.Inverse(palmRot) * (newPos - palmPos);
                calib.localRotation = Quaternion.Inverse(palmRot) * newRot;
                EditorUtility.SetDirty(calib);
                EditorUtility.SetDirty(member);
            }
        }

        DrawVisualClone(member.VisualRoot, previewPos, previewRot, view != null ? view.camera : null);
        DrawEquipReferenceMarker(member, equipReference, previewPos, previewRot, equipSelected);
        Handles.Label(semanticRootPos + Vector3.up * 0.08f, member.name + " Palm Root");
        return selected || equipSelected;
    }

    static void DrawEquipReferenceMarker(
        PPEGlovePairMember member,
        Transform equipReference,
        Vector3 previewPos,
        Quaternion previewRot,
        bool selected)
    {
        if (member == null || member.VisualRoot == null || equipReference == null)
            return;

        var memberToPreview = ComputeMemberToPreview(member, previewPos, previewRot);
        var memberLocal = member.transform.InverseTransformPoint(equipReference.position);
        var markerPosition = memberToPreview.MultiplyPoint3x4(memberLocal);

        if (selected)
        {
            Tools.hidden = true;
            hidTools = true;
            EditorGUI.BeginChangeCheck();
            var newPosition = Handles.PositionHandle(markerPosition, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(equipReference, "Calibrate Glove EquipReference");
                var newMemberLocal = memberToPreview.inverse.MultiplyPoint3x4(newPosition);
                equipReference.position = member.transform.TransformPoint(newMemberLocal);
                EditorUtility.SetDirty(equipReference);
                EditorUtility.SetDirty(member);
            }
        }

        Handles.color = new Color(1f, 0.78f, 0.12f, 1f);
        Handles.SphereHandleCap(
            0,
            markerPosition,
            Quaternion.identity,
            0.025f,
            EventType.Repaint);
        Handles.DrawDottedLine(markerPosition, previewPos, 4f);
        Handles.Label(markerPosition + Vector3.up * 0.025f, member.name + " EquipReference");
    }

    static Matrix4x4 ComputeMemberToPreview(
        PPEGlovePairMember member,
        Vector3 previewPos,
        Quaternion previewRot)
    {
        var visual = member.VisualRoot;
        var visualToMember = member.transform.worldToLocalMatrix * visual.localToWorldMatrix;
        var ghostVisual = Matrix4x4.TRS(previewPos, previewRot, visual.lossyScale);
        return ghostVisual * visualToMember.inverse;
    }

    static void DrawVisualClone(Transform source, Vector3 previewPos, Quaternion previewRot, Camera camera)
    {
        if (source == null)
            return;
        var root = Matrix4x4.TRS(previewPos, previewRot, source.lossyScale);
        DrawVisualCloneRecursive(source, root, camera);
    }

    static void DrawVisualCloneRecursive(Transform source, Matrix4x4 matrix, Camera camera)
    {
        var filter = source.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            var mesh = filter.sharedMesh;
            if (camera != null)
                Graphics.DrawMesh(mesh, matrix, PreviewMaterial, 0, camera);
        }

        var skin = source.GetComponent<SkinnedMeshRenderer>();
        if (skin != null && skin.sharedMesh != null && camera != null)
            Graphics.DrawMesh(skin.sharedMesh, matrix, PreviewMaterial, 0, camera);

        foreach (Transform child in source)
        {
            var childMatrix = matrix * Matrix4x4.TRS(child.localPosition, child.localRotation, child.localScale);
            DrawVisualCloneRecursive(child, childMatrix, camera);
        }
    }

    static bool TryGetSourceGeometry(
        PPEGlovePairMember member,
        out Vector3 palmPoint,
        out Vector3 fingerDirection,
        out Vector3 palmNormal,
        out Vector3 boundsCenter,
        out bool hasPalm)
    {
        palmPoint = Vector3.zero;
        fingerDirection = Vector3.forward;
        palmNormal = Vector3.up;
        boundsCenter = Vector3.zero;
        hasPalm = false;
        var visual = member != null ? member.VisualRoot : null;
        if (visual == null)
            return false;

        var prefix = member.GloveSide == PPEGrabHand.Right ? "R" : "L";
        var palm = FindNamed(member.transform, prefix + "_Palm");
        var tip = FindNamed(member.transform, prefix + "_MiddleTip");
        if (palm != null && tip != null)
        {
            palmPoint = visual.InverseTransformPoint(palm.position);
            fingerDirection = visual.InverseTransformDirection(
                (tip.position - palm.position).normalized);
            palmNormal = visual.InverseTransformDirection(palm.up);
            hasPalm = true;
        }

        var hasBounds = false;
        var bounds = new Bounds();
        CollectCloneLocalBounds(visual, visual, Matrix4x4.identity, ref bounds, ref hasBounds);
        if (!hasBounds)
            return hasPalm;

        boundsCenter = bounds.center;
        if (!hasPalm)
        {
            var size = bounds.size;
            if (size.x >= size.y && size.x >= size.z)
                fingerDirection = Vector3.right;
            else if (size.y >= size.z)
                fingerDirection = Vector3.up;
            else
                fingerDirection = Vector3.forward;

            palmNormal = Mathf.Abs(Vector3.Dot(fingerDirection, Vector3.up)) < 0.8f
                ? Vector3.up
                : Vector3.forward;
        }

        return true;
    }

    static void CollectCloneLocalBounds(
        Transform root,
        Transform current,
        Matrix4x4 matrix,
        ref Bounds bounds,
        ref bool hasBounds)
    {
        var mesh = GetSharedMesh(current);
        if (mesh != null)
            EncapsulateBounds(mesh.bounds, matrix, ref bounds, ref hasBounds);

        foreach (Transform child in current)
        {
            var childMatrix = matrix * Matrix4x4.TRS(
                child.localPosition,
                child.localRotation,
                child.localScale);
            CollectCloneLocalBounds(root, child, childMatrix, ref bounds, ref hasBounds);
        }
    }

    static Mesh GetSharedMesh(Transform transform)
    {
        var filter = transform.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
            return filter.sharedMesh;
        var skin = transform.GetComponent<SkinnedMeshRenderer>();
        return skin != null ? skin.sharedMesh : null;
    }

    static void EncapsulateBounds(
        Bounds source,
        Matrix4x4 matrix,
        ref Bounds result,
        ref bool hasResult)
    {
        var center = source.center;
        var extent = source.extents;
        for (var x = -1; x <= 1; x += 2)
        for (var y = -1; y <= 1; y += 2)
        for (var z = -1; z <= 1; z += 2)
        {
            var point = matrix.MultiplyPoint3x4(
                center + Vector3.Scale(extent, new Vector3(x, y, z)));
            if (!hasResult)
            {
                result = new Bounds(point, Vector3.zero);
                hasResult = true;
            }
            else
            {
                result.Encapsulate(point);
            }
        }
    }

    static Quaternion SafeLookRotation(Vector3 forward, Vector3 up)
    {
        if (forward.sqrMagnitude < 0.000001f)
            forward = Vector3.forward;
        forward.Normalize();
        up -= Vector3.Project(up, forward);
        if (up.sqrMagnitude < 0.000001f)
            up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < 0.9f
                ? Vector3.up
                : Vector3.right;
        return Quaternion.LookRotation(forward, up.normalized);
    }

    static Transform FindNamed(Transform root, string objectName)
    {
        if (root == null)
            return null;
        if (root.name == objectName)
            return root;
        foreach (Transform child in root)
        {
            var found = FindNamed(child, objectName);
            if (found != null)
                return found;
        }
        return null;
    }

    static void SetHideFlagsRecursively(Transform root)
    {
        if (root == null)
            return;
        root.gameObject.hideFlags = HideFlags.HideAndDontSave;
        foreach (Transform child in root)
            SetHideFlagsRecursively(child);
    }

    static void EnsureReferenceHands()
    {
        if (leftReference == null)
            leftReference = ReferenceHandPreview.Create(LeftHandModelPath, "L");
        if (rightReference == null)
            rightReference = ReferenceHandPreview.Create(RightHandModelPath, "R");
    }

    static void DestroyReferenceHands()
    {
        if (leftReference != null)
            leftReference.Destroy();
        if (rightReference != null)
            rightReference.Destroy();
        leftReference = null;
        rightReference = null;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode ||
            state == PlayModeStateChange.EnteredPlayMode)
            DestroyReferenceHands();
    }

    static void Cleanup()
    {
        RestoreTools();
        DestroyReferenceHands();
        if (previewMaterial != null)
            Object.DestroyImmediate(previewMaterial);
        if (referenceHandMaterial != null)
            Object.DestroyImmediate(referenceHandMaterial);
        previewMaterial = null;
        referenceHandMaterial = null;
    }

    static Material PreviewMaterial
    {
        get
        {
            if (previewMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                previewMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    color = new Color(0.2f, 0.85f, 1f, 0.28f)
                };
            }

            return previewMaterial;
        }
    }

    static Material ReferenceHandMaterial
    {
        get
        {
            if (referenceHandMaterial == null)
            {
                var template = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/1.JSR/Materials/PPE_GuideNotReady.mat");
                referenceHandMaterial = template != null
                    ? new Material(template)
                    : new Material(Shader.Find("Sprites/Default"));
                referenceHandMaterial.hideFlags = HideFlags.HideAndDontSave;
                var color = new Color(0.72f, 0.74f, 0.78f, 0.30f);
                if (referenceHandMaterial.HasProperty("_BaseColor"))
                    referenceHandMaterial.SetColor("_BaseColor", color);
                if (referenceHandMaterial.HasProperty("_Color"))
                    referenceHandMaterial.SetColor("_Color", color);
                if (referenceHandMaterial.HasProperty("_EmissionColor"))
                    referenceHandMaterial.SetColor("_EmissionColor", Color.black);
                referenceHandMaterial.DisableKeyword("_EMISSION");
            }
            return referenceHandMaterial;
        }
    }

    static void RestoreTools()
    {
        if (!hidTools)
            return;
        Tools.hidden = false;
        hidTools = false;
    }
}
