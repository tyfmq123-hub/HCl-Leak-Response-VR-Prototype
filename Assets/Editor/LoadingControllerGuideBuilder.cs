#if UNITY_EDITOR
using System;
using HClTraining.App;
using HClTraining.VRInput;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

public static class LoadingControllerGuideBuilder
{
    const string ScenePath = "Assets/Scenes/App/Loading.unity";
    const string InputPath = "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/XRI Default Input Actions.inputactions";
    const string LeftPrefab = "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/Controllers/XR Controller Left.prefab";
    const string RightPrefab = "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/Controllers/XR Controller Right.prefab";
    const string FontPath = "Assets/Resources/PPE/PPE_HangulFont.asset";
    const string InputThemePath = "Assets/Resources/App/VRInputVisualTheme.asset";
    static TMP_FontAsset font;

    struct PoseSnapshot
    {
        public bool valid;
        public Vector3 localPosition;
        public Quaternion localRotation;
    }

    struct ControllerPoseSnapshot
    {
        public PoseSnapshot visual;
    }

    [MenuItem("Tools/HCl Training/Build Loading Controller Guide")]
    public static void Build()
    {
        // The scene-authored VisualPivot is the only controller pose source.
        var poseScene = GetLoadedPoseSourceScene();
        var savedLeftPoses = CaptureControllerPoses(poseScene, "Enlarged Left Controller");
        var savedRightPoses = CaptureControllerPoses(poseScene, "Enlarged Right Controller");
        var savedLeftDisplay = CaptureTransform(poseScene, "Left Display Point");
        var savedRightDisplay = CaptureTransform(poseScene, "Right Display Point");

        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        var leftSource = AssetDatabase.LoadAssetAtPath<GameObject>(LeftPrefab);
        var rightSource = AssetDatabase.LoadAssetAtPath<GameObject>(RightPrefab);
        var inputTheme = AssetDatabase.LoadAssetAtPath<VRInputVisualTheme>(InputThemePath);
        if (font == null || leftSource == null || rightSource == null || inputTheme == null)
            throw new InvalidOperationException("Controller Guide: Required controller visual, Korean font, or global VR input theme is missing.");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("VR Controller Guide");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.localRotation = Quaternion.identity;
        var previewCamera = CreateStandalonePreviewCamera(scene);

        var displays = new GameObject("Enlarged Controller Root").transform;
        displays.SetParent(root.transform, false);
        // Keep both guide controllers in the unused upper panel area so they remain
        // inside a comfortable central VR field of view without covering instructions.
        displays.localPosition = new Vector3(0f, 0.17f, 0f);
        var leftPoint = Point(displays, "Left Display Point", new Vector3(-0.28f, 0f, 0f));
        var rightPoint = Point(displays, "Right Display Point", new Vector3(0.28f, 0f, 0f));
        RestorePose(leftPoint, savedLeftDisplay);
        RestorePose(rightPoint, savedRightDisplay);
        var left = Visual(leftSource, leftPoint, "Enlarged Left Controller", savedLeftPoses);
        var right = Visual(rightSource, rightPoint, "Enlarged Right Controller", savedRightPoses);

        var guide = root.AddComponent<XRControllerGuide>();
        var guideSo = new SerializedObject(guide);
        Assign(guideSo, "inputVisualTheme", inputTheme);
        Assign(guideSo, "leftControllerGuide", left);
        Assign(guideSo, "rightControllerGuide", right);
        Assign(guideSo, "leftPresentationRoot", left.transform.Find("PresentationRoot"));
        Assign(guideSo, "rightPresentationRoot", right.transform.Find("PresentationRoot"));
        Assign(guideSo, "leftControllerVisualRoot", left.transform.Find("PresentationRoot/VisualPivot"));
        Assign(guideSo, "rightControllerVisualRoot", right.transform.Find("PresentationRoot/VisualPivot"));
        Assign(guideSo, "leftModelOffset", left.transform.Find("PresentationRoot/VisualPivot/ModelOffset"));
        Assign(guideSo, "rightModelOffset", right.transform.Find("PresentationRoot/VisualPivot/ModelOffset"));
        Assign(guideSo, "leftDisplayPoint", leftPoint);
        Assign(guideSo, "rightDisplayPoint", rightPoint);
        var items = new[]
        {
            Item(left, "Trigger", "Guide_LeftTrigger", XRControllerGuide.ControllerButton.LeftTrigger),
            Item(left, "Bumper", "Guide_LeftGrip", XRControllerGuide.ControllerButton.LeftGrip),
            Item(left, "ThumbStick", "Guide_LeftStick", XRControllerGuide.ControllerButton.LeftStick),
            Item(left, "Button_A", "Guide_X", XRControllerGuide.ControllerButton.X),
            Item(left, "Button_B", "Guide_Y", XRControllerGuide.ControllerButton.Y),
            Item(right, "Trigger", "Guide_RightTrigger", XRControllerGuide.ControllerButton.RightTrigger),
            Item(right, "Bumper", "Guide_RightGrip", XRControllerGuide.ControllerButton.RightGrip),
            Item(right, "ThumbStick", "Guide_RightStick", XRControllerGuide.ControllerButton.RightStick),
            Item(right, "Button_A", "Guide_A", XRControllerGuide.ControllerButton.A),
            Item(right, "Button_B", "Guide_B", XRControllerGuide.ControllerButton.B),
        };
        Assign(guideSo, "leftTriggerPoint", items[0].point);
        Assign(guideSo, "leftGripPoint", items[1].point);
        Assign(guideSo, "leftStickPoint", items[2].point);
        Assign(guideSo, "leftPrimaryButtonPoint", items[3].point);
        Assign(guideSo, "leftSecondaryButtonPoint", items[4].point);
        Assign(guideSo, "rightTriggerPoint", items[5].point);
        Assign(guideSo, "rightGripPoint", items[6].point);
        Assign(guideSo, "rightStickPoint", items[7].point);
        Assign(guideSo, "rightPrimaryButtonPoint", items[8].point);
        Assign(guideSo, "rightSecondaryButtonPoint", items[9].point);
        Assign(guideSo, "xHighlight", items[3].marker);
        Assign(guideSo, "yHighlight", items[4].marker);
        Assign(guideSo, "aHighlight", items[8].marker);
        Assign(guideSo, "bHighlight", items[9].marker);
        var guideItems = guideSo.FindProperty("guideItems");
        guideItems.arraySize = items.Length;
        for (var i = 0; i < items.Length; i++)
        {
            var p = guideItems.GetArrayElementAtIndex(i);
            p.FindPropertyRelative("button").enumValueIndex = (int)items[i].button;
            p.FindPropertyRelative("label").objectReferenceValue = items[i].label;
            p.FindPropertyRelative("marker").objectReferenceValue = items[i].marker;
        }
        guideSo.FindProperty("triggerRotationOffset").vector3Value = new Vector3(0f, 180f, 0f);
        guideSo.FindProperty("gripRotationOffset").vector3Value = new Vector3(0f, -90f, 0f);
        guideSo.FindProperty("playAutomatically").boolValue = false;
        guideSo.ApplyModifiedPropertiesWithoutUndo();

        var ui = BuildUi(root.transform);
        var manager = root.AddComponent<ControllerTutorialManager>();
        var managerSo = new SerializedObject(manager);
        Assign(managerSo, "visualizer", guide);
        Assign(managerSo, "titleText", ui.title);
        Assign(managerSo, "progressText", ui.progress);
        Assign(managerSo, "instructionText", ui.instruction);
        Assign(managerSo, "descriptionText", ui.description);
        Assign(managerSo, "usageText", ui.usage);
        Assign(managerSo, "statusText", ui.status);
        Assign(managerSo, "continueButton", ui.button);
        Assign(managerSo, "standalonePreviewCamera", previewCamera);
        ConfigureSteps(managerSo.FindProperty("steps"));
        managerSo.ApplyModifiedPropertiesWithoutUndo();

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException("Controller Guide: Loading scene could not be saved.");
        AssetDatabase.SaveAssets();
        Debug.Log("[Controller Guide] Global Loading guide built successfully.");
    }

    static Camera CreateStandalonePreviewCamera(Scene scene)
    {
        var go = new GameObject("Standalone Preview Camera", typeof(Camera), typeof(AudioListener));
        go.tag = "MainCamera";
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.identity);
        var camera = go.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.012f, 0.022f, 0.035f);
        camera.fieldOfView = 60f;
        return camera;
    }

    static (TMP_Text title, TMP_Text progress, TMP_Text instruction, TMP_Text description, TMP_Text usage, TMP_Text status, Button button) BuildUi(Transform root)
    {
        var canvasGo = new GameObject("Controller Guide Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
        canvasGo.transform.SetParent(root, false);
        canvasGo.transform.localPosition = new Vector3(0f, 0f, 0.2f);
        canvasGo.transform.localScale = Vector3.one * 0.001f;
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(1200f, 760f);
        var panel = new GameObject("Orientation Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGo.transform, false);
        panel.GetComponent<RectTransform>().sizeDelta = new Vector2(1120f, 700f);
        panel.GetComponent<Image>().color = new Color(0.025f, 0.045f, 0.07f, 0.88f);

        var title = Text(canvasGo.transform, "Title", "VR 조작 가이드", new Vector2(-260, 310), new Vector2(560, 60), 40, TextAlignmentOptions.Left, Color.white);
        var progress = Text(canvasGo.transform, "Progress", "", new Vector2(430, 310), new Vector2(180, 50), 26, TextAlignmentOptions.Center, new Color(0.4f, 0.82f, 0.95f));
        // The controller visuals occupy the upper panel area; retain serialized
        // references for manager compatibility but do not render header captions.
        title.gameObject.SetActive(false);
        progress.gameObject.SetActive(false);
        var instruction = Text(canvasGo.transform, "Instruction", "", new Vector2(0, -90), new Vector2(980, 70), 40, TextAlignmentOptions.Center, Color.white);
        var description = Text(canvasGo.transform, "Description", "", new Vector2(0, -150), new Vector2(930, 55), 27, TextAlignmentOptions.Center, new Color(0.84f, 0.89f, 0.94f));
        var usage = Text(canvasGo.transform, "Usage", "", new Vector2(0, -205), new Vector2(930, 50), 22, TextAlignmentOptions.Center, new Color(0.63f, 0.72f, 0.8f));
        var status = Text(canvasGo.transform, "Status", "입력 대기 중", new Vector2(0, -300), new Vector2(600, 50), 25, TextAlignmentOptions.Center, new Color(0.72f, 0.78f, 0.84f));
        var buttonGo = new GameObject("Continue Training Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(canvasGo.transform, false);
        buttonGo.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -245);
        buttonGo.GetComponent<RectTransform>().sizeDelta = new Vector2(360, 76);
        buttonGo.GetComponent<Image>().color = new Color(0.12f, 0.42f, 0.55f, 0.96f);
        Text(buttonGo.transform, "Label", "훈련 시작", Vector2.zero, new Vector2(330, 60), 30, TextAlignmentOptions.Center, Color.white);
        buttonGo.SetActive(false);
        return (title, progress, instruction, description, usage, status, buttonGo.GetComponent<Button>());
    }

    static void ConfigureSteps(SerializedProperty steps)
    {
        steps.arraySize = 6;
        Step(steps.GetArrayElementAtIndex(0), "trigger", "{TRIGGER} / 선택·조작", "검지로 누르는 앞쪽 버튼입니다.", "UI를 선택하거나 상호작용 가능한 장치를 작동할 때 사용합니다.", "강조된 {TRIGGER} 버튼을 눌러 보십시오.", ControllerSide.Right, VRGuideInputType.Trigger, Ref("XRI Right Interaction", "Activate"), null);
        Step(steps.GetArrayElementAtIndex(1), "grab", "{GRIP} / 잡기", "컨트롤러 옆면에서 중지로 누르는 버튼입니다.", "VR 공간에서 장비, 기구, 물체를 잡거나 옮길 때 사용합니다.", "강조된 {GRIP} 버튼을 눌러 보십시오.", ControllerSide.Right, VRGuideInputType.Grip, Ref("XRI Right Interaction", "Select"), null);
        Step(steps.GetArrayElementAtIndex(2), "x", "{X} 버튼", "왼쪽 컨트롤러 위쪽의 {X} 버튼입니다.", "훈련 안내와 보조 기능을 선택할 때 사용합니다.", "강조된 {X} 버튼을 눌러 보십시오.", ControllerSide.Left, VRGuideInputType.XButton, null, null);
        Step(steps.GetArrayElementAtIndex(3), "y", "{Y} 버튼", "왼쪽 컨트롤러 위쪽의 {Y} 버튼입니다.", "훈련 안내와 보조 기능을 선택할 때 사용합니다.", "강조된 {Y} 버튼을 눌러 보십시오.", ControllerSide.Left, VRGuideInputType.YButton, null, null);
        Step(steps.GetArrayElementAtIndex(4), "a", "{A} 버튼", "오른쪽 컨트롤러 위쪽의 {A} 버튼입니다.", "확인 또는 장비 기능을 실행할 때 사용합니다.", "강조된 {A} 버튼을 눌러 보십시오.", ControllerSide.Right, VRGuideInputType.AButton, null, null);
        Step(steps.GetArrayElementAtIndex(5), "b", "{B} 버튼", "오른쪽 컨트롤러 위쪽의 {B} 버튼입니다.", "취소 또는 보조 기능을 실행할 때 사용합니다.", "강조된 {B} 버튼을 눌러 보십시오.", ControllerSide.Right, VRGuideInputType.BButton, null, null);
    }

    static void Step(SerializedProperty p, string id, string title, string desc, string usage, string instruction, ControllerSide side, VRGuideInputType type, InputActionReference primary, InputActionReference alternate)
    {
        p.FindPropertyRelative("stepId").stringValue = id; p.FindPropertyRelative("title").stringValue = title;
        p.FindPropertyRelative("description").stringValue = desc; p.FindPropertyRelative("usageDescription").stringValue = usage;
        p.FindPropertyRelative("instructionText").stringValue = instruction; p.FindPropertyRelative("controllerSide").enumValueIndex = (int)side;
        p.FindPropertyRelative("inputType").enumValueIndex = (int)type; p.FindPropertyRelative("primaryAction").objectReferenceValue = primary;
        p.FindPropertyRelative("alternateAction").objectReferenceValue = alternate; p.FindPropertyRelative("completionDelay").floatValue = 0.6f;
    }

    static InputActionReference Ref(string map, string action)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(InputPath))
            if (asset is InputActionReference r && r.action != null && r.action.actionMap.name == map && r.action.name == action) return r;
        throw new InvalidOperationException($"Controller Guide input reference missing: {map}/{action}");
    }

    static Transform Point(Transform parent, string name, Vector3 position) { var p = new GameObject(name).transform; p.SetParent(parent, false); p.localPosition = position; return p; }
    static Scene GetLoadedPoseSourceScene()
    {
        for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            var loadedScene = SceneManager.GetSceneAt(sceneIndex);
            if (loadedScene.isLoaded && loadedScene.path == ScenePath)
                return loadedScene;
        }

        if (System.IO.File.Exists(ScenePath))
            throw new InvalidOperationException(
                "Controller Guide: Open and save Assets/Scenes/App/Loading.unity before rebuilding so the manual VisualPivot rotations can be preserved.");
        return default;
    }

    static ControllerPoseSnapshot CaptureControllerPoses(Scene scene, string controllerName)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return default;

        foreach (var sceneRoot in scene.GetRootGameObjects())
        {
            var transforms = sceneRoot.GetComponentsInChildren<Transform>(true);
            foreach (var candidate in transforms)
            {
                if (candidate.name != controllerName) continue;
                var presentationRoot = candidate.Find("PresentationRoot");
                if (presentationRoot == null) continue;
                return new ControllerPoseSnapshot
                {
                    visual = Snapshot(presentationRoot.Find("VisualPivot")),
                };
            }
        }
        return default;
    }

    static PoseSnapshot CaptureTransform(Scene scene, string transformName)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return default;
        foreach (var sceneRoot in scene.GetRootGameObjects())
        {
            var transforms = sceneRoot.GetComponentsInChildren<Transform>(true);
            foreach (var candidate in transforms)
                if (candidate.name == transformName)
                    return Snapshot(candidate);
        }
        return default;
    }

    static PoseSnapshot Snapshot(Transform pose) => pose == null
        ? default
        : new PoseSnapshot
        {
            valid = true,
            localPosition = pose.localPosition,
            localRotation = pose.localRotation,
        };

    static GameObject Visual(
        GameObject source, Transform parent, string name, ControllerPoseSnapshot savedPoses)
    {
        var wrapper = new GameObject(name); wrapper.transform.SetParent(parent, false);
        var presentationRoot = new GameObject("PresentationRoot").transform;
        presentationRoot.SetParent(wrapper.transform, false);
        presentationRoot.localScale = Vector3.one * 3.2f;
        var visualRoot = new GameObject("VisualPivot").transform;
        visualRoot.SetParent(presentationRoot, false);
        RestorePose(visualRoot, savedPoses.visual);
        var modelOffset = new GameObject("ModelOffset").transform;
        modelOffset.SetParent(visualRoot, false);
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(source, wrapper.scene); visual.transform.SetParent(modelOffset, false);
        PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (var b in visual.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(b);
        foreach (var c in visual.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
        foreach (var r in visual.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(r);
        CenterModelOffset(modelOffset, visualRoot);
        return wrapper;
    }

    static void RestorePose(Transform target, PoseSnapshot snapshot)
    {
        if (!snapshot.valid) return;
        target.localPosition = snapshot.localPosition;
        target.localRotation = snapshot.localRotation;
    }

    static void CenterModelOffset(Transform modelOffset, Transform visualPivot)
    {
        var renderers = modelOffset.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        modelOffset.localPosition -= visualPivot.InverseTransformPoint(bounds.center);
    }

    static (Transform point, GameObject label, Renderer marker, XRControllerGuide.ControllerButton button) Item(GameObject root, string part, string name, XRControllerGuide.ControllerButton button)
    {
        Transform target = null; foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == part) { target = t; break; }
        if (target == null) throw new InvalidOperationException($"Controller model part missing: {part}");
        var pointOffset = part == "Bumper" ? new Vector3(0f, 0f, -0.006f) : Vector3.zero;
        var point = Point(target, name, pointOffset); var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "Current Control Marker"; marker.transform.SetParent(point, false); marker.transform.localScale = Vector3.one * 0.018f;
        UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>()); marker.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        // The central panel already names the requested input. Keep only the marker
        // on the physical control so a world-space caption cannot cover the model.
        return (point, null, marker.GetComponent<Renderer>(), button);
    }

    static TMP_Text Text(Transform parent, string name, string value, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions alignment, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>(); rect.anchoredPosition = pos; rect.sizeDelta = size;
        var text = go.GetComponent<TextMeshProUGUI>(); text.font = font; text.text = value; text.fontSize = fontSize; text.alignment = alignment; text.color = color; text.raycastTarget = false; return text;
    }

    static void Assign(SerializedObject so, string name, UnityEngine.Object value) { var p = so.FindProperty(name); if (p == null) throw new InvalidOperationException($"Missing serialized field: {name}"); p.objectReferenceValue = value; }
}
#endif
