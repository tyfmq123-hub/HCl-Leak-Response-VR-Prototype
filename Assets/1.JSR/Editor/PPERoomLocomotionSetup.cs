using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

/// <summary>
/// Grafts XRI Starter Assets locomotion onto the existing PPE XR Origin (VR):
/// continuous move + snap turn + gravity, CharacterController wall collision,
/// left-door exit blocker, and Tunneling Vignette comfort.
/// </summary>
public static class PPERoomLocomotionSetup
{
    const string PreferredScenePath = "Assets/Scenes/04_PPE.unity";
    const string SampleRigPath =
        "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
    const string TunnelingVignettePath =
        "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/TunnelingVignette/TunnelingVignette.prefab";

    const string OriginName = "XR Origin (VR)";
    const string LocomotionName = "Locomotion";
    const string VignetteName = "TunnelingVignette";
    const string ExitBlockerName = "PPE_RoomExitBlocker";

    // Left wall door gap sits between Wall_Left_Front (z≈-0.6) and Wall_Left_Back (z≈0.6).
    static readonly Vector3 ExitBlockerPosition = new Vector3(-3.5f, 1.1f, 0f);
    static readonly Vector3 ExitBlockerScale = new Vector3(0.2f, 2.2f, 1.25f);

    const float ComfortMoveSpeed = 1.75f;

    static readonly string[] DisableLocomotionChildren =
    {
        "Teleportation",
        "Climb",
        "Climb Teleport",
        "Grab Move",
        "Jump",
    };

    public static void SetupRoomLocomotion()
    {
        EnsurePreferredSceneLoaded();

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[PPE Locomotion] No active scene loaded.");
            return;
        }

        var originGo = FindOrigin();
        if (originGo == null)
        {
            Debug.LogError($"[PPE Locomotion] '{OriginName}' not found.");
            return;
        }

        var origin = originGo.GetComponent<XROrigin>();
        if (origin == null)
        {
            Debug.LogError("[PPE Locomotion] XROrigin missing on XR Origin (VR).");
            return;
        }

        var camera = origin.Camera != null ? origin.Camera : Camera.main;
        if (camera == null)
        {
            Debug.LogError("[PPE Locomotion] Main Camera not found under XR Origin.");
            return;
        }

        var leftController = FindChildRecursive(originGo.transform, "Left Controller");
        var rightController = FindChildRecursive(originGo.transform, "Right Controller");
        if (leftController == null || rightController == null)
        {
            Debug.LogError("[PPE Locomotion] Left/Right Controller not found under XR Origin.");
            return;
        }

        EnsureCharacterController(originGo);
        var locomotion = EnsureLocomotionSubtree(originGo);
        if (locomotion == null)
            return;

        WireLocomotion(origin, locomotion, camera.transform, leftController, rightController);
        DisableExtraLocomotionModes(locomotion);
        PreferSnapTurnForComfort(locomotion);
        ApplyLeftMoveRightTurnOnly(locomotion);
        EnsureTunnelingVignette(camera.transform, locomotion);
        EnsureExitBlocker();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(
            "[PPE Locomotion] Ready: left-stick move only, right-stick snap turn only, wall collision, " +
            "door blocker, Tunneling Vignette comfort.",
            originGo);
    }

    static void EnsurePreferredSceneLoaded()
    {
        var active = SceneManager.GetActiveScene();
        if (active.path == PreferredScenePath)
            return;

        if (File.Exists(PreferredScenePath)
            || File.Exists(PreferredScenePath.Replace('/', Path.DirectorySeparatorChar)))
        {
            EditorSceneManager.OpenScene(PreferredScenePath, OpenSceneMode.Single);
        }
    }

    static GameObject FindOrigin()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == OriginName)
                return root;

            var child = root.transform.Find(OriginName);
            if (child != null)
                return child.gameObject;
        }

        var origins = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include);
        return origins.Length > 0 ? origins[0].gameObject : null;
    }

    static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root;

        for (var i = 0; i < root.childCount; i++)
        {
            var found = FindChildRecursive(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    static void EnsureCharacterController(GameObject originGo)
    {
        var cc = originGo.GetComponent<CharacterController>();
        if (cc == null)
            cc = originGo.AddComponent<CharacterController>();

        cc.height = 1.6f;
        cc.radius = 0.15f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.skinWidth = 0.08f;
        cc.stepOffset = 0.3f;
        cc.slopeLimit = 45f;
        cc.minMoveDistance = 0.001f;
    }

    static GameObject EnsureLocomotionSubtree(GameObject originGo)
    {
        var existing = originGo.transform.Find(LocomotionName);
        if (existing != null)
            return existing.gameObject;

        var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SampleRigPath);
        if (rigPrefab == null)
        {
            Debug.LogError($"[PPE Locomotion] Sample XR Rig missing at {SampleRigPath}");
            return null;
        }

        var temp = PrefabUtility.InstantiatePrefab(rigPrefab) as GameObject;
        if (temp == null)
        {
            Debug.LogError("[PPE Locomotion] Failed to instantiate sample XR Rig.");
            return null;
        }

        try
        {
            var sampleLocomotion = temp.transform.Find(LocomotionName);
            if (sampleLocomotion == null)
            {
                Debug.LogError("[PPE Locomotion] Sample rig has no Locomotion child.");
                return null;
            }

            var copy = Object.Instantiate(sampleLocomotion.gameObject, originGo.transform, false);
            copy.name = LocomotionName;
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            Undo.RegisterCreatedObjectUndo(copy, "PPE Locomotion");
            return copy;
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    static void WireLocomotion(
        XROrigin origin,
        GameObject locomotion,
        Transform head,
        Transform leftController,
        Transform rightController)
    {
        if (locomotion == null)
            return;

        foreach (var mb in locomotion.GetComponents<MonoBehaviour>())
        {
            if (mb == null || mb.GetType().Name != "XRBodyTransformer")
                continue;

            var so = new SerializedObject(mb);
            var prop = so.FindProperty("m_XROrigin");
            if (prop != null)
            {
                prop.objectReferenceValue = origin;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        var move = locomotion.GetComponentInChildren<DynamicMoveProvider>(true);
        if (move != null)
        {
            move.headTransform = head;
            move.leftControllerTransform = leftController;
            move.rightControllerTransform = rightController;

            var moveSo = new SerializedObject(move);
            var speed = moveSo.FindProperty("m_MoveSpeed");
            if (speed != null)
                speed.floatValue = ComfortMoveSpeed;
            moveSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(move);
        }
    }

    static void DisableExtraLocomotionModes(GameObject locomotion)
    {
        if (locomotion == null)
            return;

        foreach (var childName in DisableLocomotionChildren)
        {
            var child = locomotion.transform.Find(childName);
            if (child != null)
                child.gameObject.SetActive(false);
        }
    }

    static void PreferSnapTurnForComfort(GameObject locomotion)
    {
        if (locomotion == null)
            return;

        var turnRoot = locomotion.transform.Find("Turn");
        if (turnRoot == null)
            return;

        // Snap turn is usually more comfortable than continuous turn.
        foreach (var mb in turnRoot.GetComponents<MonoBehaviour>())
        {
            if (mb == null)
                continue;

            var typeName = mb.GetType().Name;
            if (typeName == "ContinuousTurnProvider")
            {
                mb.enabled = false;
                continue;
            }

            if (typeName != "SnapTurnProvider")
                continue;

            mb.enabled = true;
            var snapSo = new SerializedObject(mb);
            var amount = snapSo.FindProperty("m_TurnAmount");
            if (amount != null)
                amount.floatValue = 45f;
            snapSo.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// Left stick = move only, right stick = snap turn only.
    /// XRInputReader InputSourceMode 0 = Unused.
    /// </summary>
    static void ApplyLeftMoveRightTurnOnly(GameObject locomotion)
    {
        if (locomotion == null)
            return;

        var move = locomotion.GetComponentInChildren<DynamicMoveProvider>(true);
        if (move != null)
        {
            var moveSo = new SerializedObject(move);
            var rightMove = moveSo.FindProperty("m_RightHandMoveInput.m_InputSourceMode");
            if (rightMove != null)
                rightMove.intValue = 0;
            var leftMove = moveSo.FindProperty("m_LeftHandMoveInput.m_InputSourceMode");
            if (leftMove != null)
                leftMove.intValue = 2; // Input Action Reference
            moveSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(move);
        }

        var turnRoot = locomotion.transform.Find("Turn");
        if (turnRoot == null)
            return;

        foreach (var mb in turnRoot.GetComponents<MonoBehaviour>())
        {
            if (mb == null)
                continue;

            var typeName = mb.GetType().Name;
            if (typeName != "SnapTurnProvider" && typeName != "ContinuousTurnProvider")
                continue;

            var turnSo = new SerializedObject(mb);
            var leftTurn = turnSo.FindProperty("m_LeftHandTurnInput.m_InputSourceMode");
            if (leftTurn != null)
                leftTurn.intValue = 0;

            var rightTurn = turnSo.FindProperty("m_RightHandTurnInput.m_InputSourceMode");
            if (rightTurn != null)
                rightTurn.intValue = typeName == "SnapTurnProvider" ? 2 : 0;

            turnSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mb);
        }
    }

    static void EnsureTunnelingVignette(Transform cameraTransform, GameObject locomotion)
    {
        if (cameraTransform == null || locomotion == null)
            return;

        var existing = cameraTransform.Find(VignetteName);
        GameObject vignetteGo;
        if (existing != null)
        {
            vignetteGo = existing.gameObject;
        }
        else
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TunnelingVignettePath);
            if (prefab == null)
            {
                Debug.LogWarning($"[PPE Locomotion] TunnelingVignette prefab missing at {TunnelingVignettePath}");
                return;
            }

            vignetteGo = PrefabUtility.InstantiatePrefab(prefab, cameraTransform) as GameObject;
            if (vignetteGo == null)
                return;

            vignetteGo.name = VignetteName;
            vignetteGo.transform.localPosition = Vector3.zero;
            vignetteGo.transform.localRotation = Quaternion.identity;
            vignetteGo.transform.localScale = Vector3.one;
            Undo.RegisterCreatedObjectUndo(vignetteGo, "PPE Tunneling Vignette");
        }

        var providers = new List<LocomotionProvider>();
        var move = locomotion.GetComponentInChildren<DynamicMoveProvider>(true);
        if (move != null && move.enabled && move.gameObject.activeInHierarchy)
            providers.Add(move);

        var turnRoot = locomotion.transform.Find("Turn");
        if (turnRoot != null)
        {
            foreach (var mb in turnRoot.GetComponents<MonoBehaviour>())
            {
                if (mb == null || !mb.enabled)
                    continue;
                if (mb.GetType().Name != "SnapTurnProvider")
                    continue;
                if (mb is LocomotionProvider provider)
                    providers.Add(provider);
            }
        }

        MonoBehaviour vignette = null;
        foreach (var mb in vignetteGo.GetComponents<MonoBehaviour>())
        {
            if (mb != null && mb.GetType().Name == "TunnelingVignetteController")
            {
                vignette = mb;
                break;
            }
        }

        if (vignette == null)
        {
            Debug.LogWarning("[PPE Locomotion] TunnelingVignetteController not found on vignette prefab.");
            return;
        }

        var so = new SerializedObject(vignette);
        var list = so.FindProperty("m_LocomotionVignetteProviders");
        if (list == null)
        {
            Debug.LogWarning("[PPE Locomotion] m_LocomotionVignetteProviders missing.");
            return;
        }

        list.ClearArray();
        for (var i = 0; i < providers.Count; i++)
        {
            list.InsertArrayElementAtIndex(i);
            var elem = list.GetArrayElementAtIndex(i);
            var enabledProp = elem.FindPropertyRelative("m_Enabled");
            if (enabledProp != null)
                enabledProp.boolValue = true;

            var providerProp = elem.FindPropertyRelative("m_LocomotionProvider");
            if (providerProp != null)
                providerProp.objectReferenceValue = providers[i];

            var aperture = elem.FindPropertyRelative("m_OverrideParameters.m_ApertureSize");
            if (aperture != null)
                aperture.floatValue = 0.55f;
            var easeIn = elem.FindPropertyRelative("m_OverrideParameters.m_EaseInTime");
            if (easeIn != null)
                easeIn.floatValue = 0.2f;
            var easeOut = elem.FindPropertyRelative("m_OverrideParameters.m_EaseOutTime");
            if (easeOut != null)
                easeOut.floatValue = 0.25f;
            var feather = elem.FindPropertyRelative("m_OverrideParameters.m_FeatheringEffect");
            if (feather != null)
                feather.floatValue = 0.25f;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(vignette);
    }

    static void EnsureExitBlocker()
    {
        var existing = GameObject.Find(ExitBlockerName);
        if (existing != null)
        {
            existing.transform.position = ExitBlockerPosition;
            existing.transform.localScale = ExitBlockerScale;
            return;
        }

        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.name = ExitBlockerName;
        blocker.transform.position = ExitBlockerPosition;
        blocker.transform.rotation = Quaternion.identity;
        blocker.transform.localScale = ExitBlockerScale;

        var renderer = blocker.GetComponent<MeshRenderer>();
        if (renderer != null)
            Object.DestroyImmediate(renderer);

        var filter = blocker.GetComponent<MeshFilter>();
        if (filter != null)
            Object.DestroyImmediate(filter);

        var collider = blocker.GetComponent<BoxCollider>();
        if (collider != null)
        {
            collider.isTrigger = false;
            collider.enabled = true;
        }

        Undo.RegisterCreatedObjectUndo(blocker, "PPE Room Exit Blocker");
    }
}
