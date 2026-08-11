using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace HClTraining.App
{
    [DisallowMultipleComponent]
    public sealed class StageXRProfileController : MonoBehaviour
    {
        const string HandVisualizerPath = "Camera Offset/Hand Visualizer";
        const string HandTrackingPath = "Camera Offset/VR Hand Pose Tool Tracking";

        [SerializeField] GameObject commonXrOrigin;
        [SerializeField] StageXRProfile[] profiles;

        readonly Dictionary<Behaviour, bool> startBehaviourStates = new Dictionary<Behaviour, bool>();
        readonly Dictionary<Renderer, bool> startRendererStates = new Dictionary<Renderer, bool>();
        readonly Dictionary<GameObject, bool> startObjectStates = new Dictionary<GameObject, bool>();
        readonly Dictionary<Transform, Quaternion> startRotationStates = new Dictionary<Transform, Quaternion>();
        readonly Dictionary<XRRayInteractor, XRInputButtonReader> startUiPressStates =
            new Dictionary<XRRayInteractor, XRInputButtonReader>();
        readonly Dictionary<XRRayInteractor, bool> startUiInteractionStates =
            new Dictionary<XRRayInteractor, bool>();
        bool startProfileActive;

        public GameObject CommonXrOrigin => commonXrOrigin;
        public int LastReboundReferenceCount { get; private set; }
        public string LastAppliedProfile { get; private set; }

        public void Initialize(GameObject xrOrigin)
        {
            commonXrOrigin = xrOrigin;
            if (profiles == null || profiles.Length == 0)
                profiles = Resources.LoadAll<StageXRProfile>("App/StageXRProfiles");
        }

        public bool ApplyStartProfile(out string error)
        {
            error = null;
            if (commonXrOrigin == null)
            {
                error = "Common XR Origin is missing.";
                return false;
            }

            RestoreStartProfile();
            startProfileActive = true;

            var behaviours = commonXrOrigin.GetComponentsInChildren<Behaviour>(true);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour != null && IsStartLocomotionBehaviour(behaviour.GetType()))
                    SetStartBehaviour(behaviour, false);
            }

            SetStartObject("Locomotion/Grab Move", false);
            SetStartObject("Locomotion/Teleportation", false);
            SetStartObject("Locomotion/Climb", false);
            SetStartObject("Camera Offset/Hand Visualizer", false);
            SetStartObject("Camera Offset/VR Hand Pose Tool Tracking", false);
            SetStartObject("Camera Offset/Main Camera/TunnelingVignette", false);
            SetStartObject("Camera Offset/Left Controller/Left_NearFarInteractor", false);
            SetStartObject("Camera Offset/Right Controller/Right_NearFarInteractor", false);
            SetStartObject("Camera Offset/Left Controller/Visuals", false);
            SetStartObject("Camera Offset/Right Controller/Visuals", false);

            var managers = commonXrOrigin.GetComponentsInChildren<InputActionManager>(true);
            for (var i = 0; i < managers.Length; i++)
                SetStartBehaviour(managers[i], true);

            if (!ConfigureStartRay("Camera Offset/Left Controller/Ray Interactor", "XRI Left Interaction", out error) ||
                !ConfigureStartRay("Camera Offset/Right Controller/Ray Interactor", "XRI Right Interaction", out error))
            {
                RestoreStartProfile();
                return false;
            }

            LastAppliedProfile = "StartMenu";
            Debug.Log("[Stage XR Profile] Applied StartMenu; locomotion disabled, controller visuals hidden, UI rays enabled.", this);
            return true;
        }

        public bool ApplyStageProfile(Scene scene, TrainingStage stage, out string error)
        {
            error = null;
            RestoreStartProfile();
            if (commonXrOrigin == null)
            {
                error = "공용 XR Origin 참조가 없습니다.";
                Debug.LogError("[Stage XR Profile] " + error, this);
                return false;
            }

            var sourceOrigin = FindSceneOrigin(scene);
            var profile = FindProfile(stage);
            if (sourceOrigin == null)
            {
                error = $"{TrainingSessionState.GetStageLabel(stage)} Scene의 기존 XR Origin을 찾지 못했습니다.";
                Debug.LogError("[Stage XR Profile] " + error, this);
                if (profile != null)
                    commonXrOrigin.transform.SetPositionAndRotation(profile.SpawnPosition, profile.SpawnRotation);
                return false;
            }

            commonXrOrigin.SetActive(true);
            commonXrOrigin.transform.SetPositionAndRotation(
                sourceOrigin.transform.position,
                sourceOrigin.transform.rotation);

            DisableProfileManagedBehaviours(commonXrOrigin.transform);
            CopySourceHierarchyState(sourceOrigin.transform, commonXrOrigin.transform);
            LastReboundReferenceCount = RebindSceneReferences(scene, sourceOrigin.transform, commonXrOrigin.transform);
            DisableLegacyXrObjects(scene, sourceOrigin.gameObject);

            // LeakResponse is controller-only. Its legacy source rig contains XR Hands sample
            // objects that must not be copied into the active common rig.
            if (stage == TrainingStage.LeakResponse)
                ApplyLeakResponseControllerOnlyPolicy(commonXrOrigin.transform);

            LastAppliedProfile = profile != null ? profile.name : stage.ToString();
            Debug.Log(
                $"[Stage XR Profile] Applied {LastAppliedProfile}; spawn={commonXrOrigin.transform.position}; " +
                $"reboundReferences={LastReboundReferenceCount}.", this);
            return true;
        }

        static void ApplyLeakResponseControllerOnlyPolicy(Transform xrRoot)
        {
            SetObjectActive(xrRoot, HandVisualizerPath, false);
            SetObjectActive(xrRoot, HandTrackingPath, false);
        }

        static void SetObjectActive(Transform root, string relativePath, bool active)
        {
            var target = root != null ? root.Find(relativePath) : null;
            if (target != null)
                target.gameObject.SetActive(active);
        }

        bool ConfigureStartRay(string relativePath, string actionMapName, out string error)
        {
            error = null;
            var rayTransform = commonXrOrigin.transform.Find(relativePath);
            if (rayTransform == null)
            {
                error = $"Start UI Ray was not found: {relativePath}";
                return false;
            }

            SetStartObject(relativePath, true);
            if (!startRotationStates.ContainsKey(rayTransform))
                startRotationStates.Add(rayTransform, rayTransform.localRotation);
            rayTransform.localRotation = Quaternion.identity;

            var ray = rayTransform.GetComponent<XRRayInteractor>();
            var lineVisual = rayTransform.GetComponent<XRInteractorLineVisual>();
            var lineRenderer = rayTransform.GetComponent<LineRenderer>();
            if (ray == null || lineVisual == null || lineRenderer == null)
            {
                error = $"Start UI Ray components are incomplete: {relativePath}";
                return false;
            }

            SetStartBehaviour(ray, true);
            SetStartBehaviour(lineVisual, true);
            if (!startRendererStates.ContainsKey(lineRenderer))
                startRendererStates.Add(lineRenderer, lineRenderer.enabled);
            lineRenderer.enabled = true;

            if (!startUiInteractionStates.ContainsKey(ray))
                startUiInteractionStates.Add(ray, ray.enableUIInteraction);
            ray.enableUIInteraction = true;

            if (!startUiPressStates.ContainsKey(ray))
                startUiPressStates.Add(ray, ray.uiPressInput);
            var uiPress = FindInputAction(actionMapName, "UI Press");
            var uiPressValue = FindInputAction(actionMapName, "UI Press Value");
            if (uiPress == null || uiPressValue == null)
            {
                error = $"Start UI actions are missing: {actionMapName}/UI Press.";
                return false;
            }

            ray.uiPressInput = new XRInputButtonReader("UI Press", "UI Press Value")
            {
                inputSourceMode = XRInputButtonReader.InputSourceMode.InputActionReference,
                inputActionReferencePerformed = InputActionReference.Create(uiPress),
                inputActionReferenceValue = InputActionReference.Create(uiPressValue),
            };
            return true;
        }

        InputAction FindInputAction(string mapName, string actionName)
        {
            var managers = commonXrOrigin.GetComponentsInChildren<InputActionManager>(true);
            for (var managerIndex = 0; managerIndex < managers.Length; managerIndex++)
            {
                var assets = managers[managerIndex].actionAssets;
                if (assets == null)
                    continue;
                for (var assetIndex = 0; assetIndex < assets.Count; assetIndex++)
                {
                    var action = assets[assetIndex]?.FindAction($"{mapName}/{actionName}", false);
                    if (action != null)
                        return action;
                }
            }
            return null;
        }

        void SetStartObject(string relativePath, bool active)
        {
            var target = commonXrOrigin.transform.Find(relativePath);
            if (target == null)
                return;
            if (!startObjectStates.ContainsKey(target.gameObject))
                startObjectStates.Add(target.gameObject, target.gameObject.activeSelf);
            target.gameObject.SetActive(active);
        }

        void SetStartBehaviour(Behaviour behaviour, bool enabled)
        {
            if (behaviour == null)
                return;
            if (!startBehaviourStates.ContainsKey(behaviour))
                startBehaviourStates.Add(behaviour, behaviour.enabled);
            behaviour.enabled = enabled;
        }

        void RestoreStartProfile()
        {
            if (!startProfileActive)
                return;

            foreach (var pair in startUiPressStates)
                if (pair.Key != null) pair.Key.uiPressInput = pair.Value;
            foreach (var pair in startUiInteractionStates)
                if (pair.Key != null) pair.Key.enableUIInteraction = pair.Value;
            foreach (var pair in startRotationStates)
                if (pair.Key != null) pair.Key.localRotation = pair.Value;
            foreach (var pair in startBehaviourStates)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            foreach (var pair in startRendererStates)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            foreach (var pair in startObjectStates)
                if (pair.Key != null) pair.Key.SetActive(pair.Value);

            startUiPressStates.Clear();
            startUiInteractionStates.Clear();
            startRotationStates.Clear();
            startBehaviourStates.Clear();
            startRendererStates.Clear();
            startObjectStates.Clear();
            startProfileActive = false;
        }

        static bool IsStartLocomotionBehaviour(Type type)
        {
            var ns = type.Namespace ?? string.Empty;
            var name = type.Name;
            return ns.IndexOf(".Locomotion", StringComparison.Ordinal) >= 0 ||
                   name.IndexOf("MoveProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("TurnProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("TeleportationProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("ClimbProvider", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        StageXRProfile FindProfile(TrainingStage stage)
        {
            if (profiles == null)
                return null;
            for (var i = 0; i < profiles.Length; i++)
            {
                if (profiles[i] != null && profiles[i].Stage == stage)
                    return profiles[i];
            }
            return null;
        }

        static XROrigin FindSceneOrigin(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var origins = root.GetComponentsInChildren<XROrigin>(true);
                if (origins.Length > 0)
                    return origins[0];
            }
            return null;
        }

        static void DisableProfileManagedBehaviours(Transform targetRoot)
        {
            var behaviours = targetRoot.GetComponentsInChildren<Behaviour>(true);
            for (var i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null && IsProfileManagedType(behaviours[i].GetType()))
                    behaviours[i].enabled = false;
            }
        }

        static bool IsProfileManagedType(Type type)
        {
            var ns = type.Namespace ?? string.Empty;
            var name = type.Name;
            return ns.StartsWith("UnityEngine.XR", StringComparison.Ordinal) ||
                   ns.StartsWith("Unity.XR", StringComparison.Ordinal) ||
                   name.IndexOf("Locomotion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("MoveProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("TurnProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("TeleportationProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Interactor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("InputActionManager", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void CopySourceHierarchyState(Transform sourceRoot, Transform targetRoot)
        {
            var sourceTransforms = sourceRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < sourceTransforms.Length; i++)
            {
                var sourceTransform = sourceTransforms[i];
                var relativePath = GetRelativePath(sourceRoot, sourceTransform);
                var targetTransform = string.IsNullOrEmpty(relativePath)
                    ? targetRoot
                    : targetRoot.Find(relativePath);
                if (targetTransform == null)
                    continue;

                if (targetTransform != targetRoot)
                    targetTransform.gameObject.SetActive(sourceTransform.gameObject.activeSelf);

                var sourceComponents = sourceTransform.GetComponents<Component>();
                var typeCounts = new Dictionary<Type, int>();
                for (var componentIndex = 0; componentIndex < sourceComponents.Length; componentIndex++)
                {
                    var source = sourceComponents[componentIndex];
                    if (source == null || source is Transform || !IsProfileManagedType(source.GetType()))
                        continue;

                    var type = source.GetType();
                    typeCounts.TryGetValue(type, out var typeIndex);
                    typeCounts[type] = typeIndex + 1;
                    var targets = targetTransform.GetComponents(type);
                    if (typeIndex >= targets.Length)
                    {
                        try
                        {
                            targetTransform.gameObject.AddComponent(type);
                            targets = targetTransform.GetComponents(type);
                        }
                        catch (Exception exception)
                        {
                            Debug.LogError($"[Stage XR Profile] Could not add {type.FullName}: {exception.Message}");
                            continue;
                        }
                    }

                    var target = targets[typeIndex];
                    CopySerializedFields(source, target, sourceRoot, targetRoot);
                    if (source is Behaviour sourceBehaviour && target is Behaviour targetBehaviour)
                        targetBehaviour.enabled = sourceBehaviour.enabled;
                }
            }
        }

        static void CopySerializedFields(Component source, Component target, Transform sourceRoot, Transform targetRoot)
        {
            for (var type = source.GetType(); type != null && type != typeof(Component) && type != typeof(UnityEngine.Object); type = type.BaseType)
            {
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (var i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    if (field.IsStatic || field.IsInitOnly || field.IsLiteral ||
                        (field.IsPrivate && field.GetCustomAttribute<SerializeField>() == null))
                        continue;
                    try
                    {
                        var value = field.GetValue(source);
                        field.SetValue(target, RemapValue(value, sourceRoot, targetRoot));
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[Stage XR Profile] Skipped {source.GetType().Name}.{field.Name}: {exception.Message}");
                    }
                }
            }
        }

        static object RemapValue(object value, Transform sourceRoot, Transform targetRoot)
        {
            if (value is UnityEngine.Object unityObject)
                return RemapObject(unityObject, sourceRoot, targetRoot) ?? unityObject;

            if (value is Array array)
            {
                var clone = (Array)array.Clone();
                for (var i = 0; i < clone.Length; i++)
                    clone.SetValue(RemapValue(clone.GetValue(i), sourceRoot, targetRoot), i);
                return clone;
            }

            if (value is IList list && value.GetType().GetConstructor(Type.EmptyTypes) != null)
            {
                try
                {
                    var clone = (IList)Activator.CreateInstance(value.GetType());
                    foreach (var item in list)
                        clone.Add(RemapValue(item, sourceRoot, targetRoot));
                    return clone;
                }
                catch
                {
                    return value;
                }
            }
            return value;
        }

        static UnityEngine.Object RemapObject(UnityEngine.Object sourceObject, Transform sourceRoot, Transform targetRoot)
        {
            if (sourceObject == null)
                return null;

            Transform sourceTransform = null;
            if (sourceObject is GameObject gameObject)
                sourceTransform = gameObject.transform;
            else if (sourceObject is Component component)
                sourceTransform = component.transform;

            if (sourceTransform == null || (sourceTransform != sourceRoot && !sourceTransform.IsChildOf(sourceRoot)))
                return null;

            var relativePath = GetRelativePath(sourceRoot, sourceTransform);
            var targetTransform = string.IsNullOrEmpty(relativePath) ? targetRoot : targetRoot.Find(relativePath);
            if (targetTransform == null)
                return null;
            if (sourceObject is GameObject)
                return targetTransform.gameObject;
            if (sourceObject is Transform)
                return targetTransform;

            var sourceComponent = (Component)sourceObject;
            var sourceSiblings = sourceTransform.GetComponents(sourceComponent.GetType());
            var componentIndex = Array.IndexOf(sourceSiblings, sourceComponent);
            var targetSiblings = targetTransform.GetComponents(sourceComponent.GetType());
            return componentIndex >= 0 && componentIndex < targetSiblings.Length
                ? targetSiblings[componentIndex]
                : null;
        }

        static int RebindSceneReferences(Scene scene, Transform sourceRoot, Transform targetRoot)
        {
            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                var components = root.GetComponentsInChildren<MonoBehaviour>(true);
                for (var i = 0; i < components.Length; i++)
                {
                    var component = components[i];
                    if (component == null || component.transform == sourceRoot || component.transform.IsChildOf(sourceRoot))
                        continue;
                    count += RebindObjectFields(component, sourceRoot, targetRoot);
                }
            }
            return count;
        }

        static int RebindObjectFields(MonoBehaviour component, Transform sourceRoot, Transform targetRoot)
        {
            var count = 0;
            for (var type = component.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            {
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (var i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    if (field.IsStatic || field.IsInitOnly ||
                        (field.IsPrivate && field.GetCustomAttribute<SerializeField>() == null))
                        continue;
                    try
                    {
                        var value = field.GetValue(component);
                        if (value is UnityEngine.Object unityObject)
                        {
                            var replacement = RemapObject(unityObject, sourceRoot, targetRoot);
                            if (replacement != null && replacement != unityObject)
                            {
                                field.SetValue(component, replacement);
                                count++;
                            }
                        }
                        else if (value is Array array)
                        {
                            var changed = false;
                            for (var index = 0; index < array.Length; index++)
                            {
                                if (!(array.GetValue(index) is UnityEngine.Object item))
                                    continue;
                                var replacement = RemapObject(item, sourceRoot, targetRoot);
                                if (replacement == null || replacement == item)
                                    continue;
                                array.SetValue(replacement, index);
                                count++;
                                changed = true;
                            }
                            if (changed)
                                field.SetValue(component, array);
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[Stage XR Profile] Reference rebind skipped for {component.GetType().Name}.{field.Name}: {exception.Message}");
                    }
                }
            }
            return count;
        }

        static void DisableLegacyXrObjects(Scene scene, GameObject legacyOrigin)
        {
            legacyOrigin.SetActive(false);
            foreach (var root in scene.GetRootGameObjects())
            {
                var eventSystems = root.GetComponentsInChildren<EventSystem>(true);
                for (var i = 0; i < eventSystems.Length; i++)
                    eventSystems[i].gameObject.SetActive(false);

                var components = root.GetComponentsInChildren<Behaviour>(true);
                for (var i = 0; i < components.Length; i++)
                {
                    var behaviour = components[i];
                    if (behaviour == null)
                        continue;
                    var name = behaviour.GetType().Name;
                    if (name == "XRInteractionManager" || name == "InputActionManager")
                        behaviour.gameObject.SetActive(false);
                }

                var cameras = root.GetComponentsInChildren<Camera>(true);
                for (var i = 0; i < cameras.Length; i++)
                {
                    if (cameras[i].CompareTag("MainCamera") || cameras[i].name == "Main Camera")
                        cameras[i].enabled = false;
                }
                var listeners = root.GetComponentsInChildren<AudioListener>(true);
                for (var i = 0; i < listeners.Length; i++)
                    listeners[i].enabled = false;
            }
        }

        static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root)
                return string.Empty;
            var parts = new Stack<string>();
            for (var current = target; current != null && current != root; current = current.parent)
                parts.Push(current.name);
            return string.Join("/", parts);
        }
    }
}
