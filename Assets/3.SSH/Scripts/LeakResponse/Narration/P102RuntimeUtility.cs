using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace VRProject.LeakResponse.Narration
{
    public static class P102RuntimeUtility
    {
        public const string RightHandPoseAttachName = "VRHandPoseTool_Attach_Right";
        public const string HandPoseGhostsRootName = "VRHandPoseTool_Ghosts";
        const string RightWristName = "R_Wrist";

        static TMP_FontAsset runtimeKoreanFont;
        static Sprite roundedPanelSprite;
        static bool missingRuntimeFontReported;

        public static GameObject FindSceneObject(string exactName)
        {
            if (string.IsNullOrEmpty(exactName))
                return null;

            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < transforms.Length; ++i)
            {
                Transform candidate = transforms[i];
                if (candidate != null && candidate.gameObject.scene.IsValid() &&
                    string.Equals(candidate.name, exactName, StringComparison.Ordinal))
                    return candidate.gameObject;
            }

            return null;
        }

        public static T FindSceneComponent<T>(string exactObjectName = null) where T : Component
        {
            T[] components = Resources.FindObjectsOfTypeAll<T>();
            for (int i = 0; i < components.Length; ++i)
            {
                T candidate = components[i];
                if (candidate == null || !candidate.gameObject.scene.IsValid())
                    continue;
                if (exactObjectName == null || string.Equals(candidate.name, exactObjectName, StringComparison.Ordinal))
                    return candidate;
            }

            return null;
        }

        public static List<GameObject> FindSceneObjects(Func<string, bool> namePredicate)
        {
            var results = new List<GameObject>();
            if (namePredicate == null)
                return results;

            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < transforms.Length; ++i)
            {
                Transform candidate = transforms[i];
                if (candidate != null && candidate.gameObject.scene.IsValid() && namePredicate(candidate.name))
                    results.Add(candidate.gameObject);
            }

            return results;
        }

        public static Transform FindDescendantTransform(GameObject root, string exactName)
        {
            if (root == null || string.IsNullOrEmpty(exactName))
                return null;

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; ++i)
            {
                Transform candidate = transforms[i];
                if (candidate != null &&
                    string.Equals(candidate.name, exactName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        public static int HideHandPoseGhostRenderers(GameObject root)
        {
            return SetHandPoseGhostRenderersVisible(root, false);
        }

        public static int SetHandPoseGhostRenderersVisible(GameObject root, bool visible)
        {
            Transform ghostsRoot = FindDescendantTransform(root, HandPoseGhostsRootName);
            if (ghostsRoot == null)
                return 0;

            ghostsRoot.gameObject.SetActive(visible);
            Renderer[] renderers = ghostsRoot.GetComponentsInChildren<Renderer>(true);
            int changedCount = 0;
            for (int i = 0; i < renderers.Length; ++i)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                renderer.enabled = visible;
                ++changedCount;
            }

            return changedCount;
        }

        public static bool IsHandPoseGhostTransform(Transform candidate, Transform rootLimit)
        {
            Transform current = candidate;
            while (current != null)
            {
                if (string.Equals(current.name, HandPoseGhostsRootName,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                if (current == rootLimit)
                    break;
                current = current.parent;
            }

            return false;
        }

        public static XRSingleGrabFreeTransformer ConfigureExactPoseGrabTracking(
            XRGrabInteractable grab)
        {
            if (grab == null)
                return null;

            XRSingleGrabFreeTransformer transformer =
                GetOrAdd<XRSingleGrabFreeTransformer>(grab.gameObject);
            grab.addDefaultGrabTransformers = false;
            grab.ClearSingleGrabTransformers();
            grab.ClearMultipleGrabTransformers();
            grab.AddSingleGrabTransformer(transformer);
            grab.trackScale = false;
            grab.attachEaseInTime = 0f;
            return transformer;
        }

        public static P102LocomotionStableGrabRuntime ConfigureLocomotionStableGrab(
            XRGrabInteractable grab)
        {
            if (grab == null)
                return null;

            P102LocomotionStableGrabRuntime stableGrab =
                GetOrAdd<P102LocomotionStableGrabRuntime>(grab.gameObject);
            stableGrab.Configure(grab);
            return stableGrab;
        }

        /// <summary>
        /// HandSideAwareGrabInteractable creates a dynamic attach during Select. Re-seed that
        /// generated transform from the authored hand-pose attach so the held object converges
        /// to the exact pose previewed by the VR Hand Pose Tool ghost.
        /// </summary>
        public static bool ApplyAuthoredHandPoseAttach(
            XRGrabInteractable grab, Transform authoredAttach, SelectEnterEventArgs args)
        {
            if (grab == null || authoredAttach == null || args?.interactorObject == null)
                return false;

            return ApplyAuthoredHandPoseAttach(
                grab, authoredAttach, args.interactorObject);
        }

        public static bool TryPrepareRightWristCorrectedAttach(
            XRGrabInteractable grab, Transform authoredAttach)
        {
            if (grab == null || authoredAttach == null)
                return false;

            IXRSelectInteractor interactor = FindRightHandSelectInteractor();
            return interactor != null && TryApplyRightWristCorrection(
                grab.transform, authoredAttach, grab, interactor);
        }

        public static bool ApplyAuthoredHandPoseAttach(
            XRGrabInteractable grab, Transform authoredAttach, IXRSelectInteractor interactor)
        {
            if (grab == null || authoredAttach == null || interactor == null)
                return false;

            Transform activeAttach = grab.GetAttachTransform(interactor);
            if (activeAttach == null)
                return false;

            if (activeAttach != authoredAttach)
            {
                activeAttach.SetPositionAndRotation(
                    authoredAttach.position, authoredAttach.rotation);
            }

            return true;
        }

        public static bool IsAuthoredAttachAligned(
            XRGrabInteractable grab, Transform authoredAttach, IXRSelectInteractor interactor,
            float positionTolerance = 0.03f, float rotationTolerance = 8f)
        {
            if (grab == null || authoredAttach == null || interactor == null)
                return false;

            Transform interactorAttach = interactor.GetAttachTransform(grab);
            return interactorAttach != null &&
                   Vector3.Distance(authoredAttach.position, interactorAttach.position) <=
                   Mathf.Max(0f, positionTolerance) &&
                   Quaternion.Angle(authoredAttach.rotation, interactorAttach.rotation) <=
                   Mathf.Max(0f, rotationTolerance);
        }

        static bool TryApplyRightWristCorrection(
            Transform target, Transform authoredAttach, XRGrabInteractable grab,
            IXRSelectInteractor interactor)
        {
            if (target == null || authoredAttach == null || grab == null || interactor == null)
                return false;

            Transform ghostsRoot = FindDescendantTransform(
                target.gameObject, HandPoseGhostsRootName);
            Transform ghostWrist = ghostsRoot != null
                ? FindDescendantTransform(ghostsRoot.gameObject, RightWristName)
                : null;
            Transform controllerWrist = FindControllerWrist(interactor.transform);
            Transform interactorAttach = interactor.GetAttachTransform(grab);
            if (ghostWrist == null || controllerWrist == null || interactorAttach == null)
                return false;

            Vector3 ghostWristLocalPosition = target.InverseTransformPoint(ghostWrist.position);
            Quaternion ghostWristLocalRotation =
                Quaternion.Inverse(target.rotation) * ghostWrist.rotation;
            Vector3 controllerWristLocalPosition =
                interactorAttach.InverseTransformPoint(controllerWrist.position);
            Quaternion controllerWristLocalRotation =
                Quaternion.Inverse(interactorAttach.rotation) * controllerWrist.rotation;

            Quaternion inverseWristRotation =
                Quaternion.Inverse(controllerWristLocalRotation);
            Vector3 inverseWristPosition =
                inverseWristRotation * -controllerWristLocalPosition;
            Quaternion correctedLocalRotation =
                ghostWristLocalRotation * inverseWristRotation;
            Vector3 correctedLocalPosition =
                ghostWristLocalRotation * inverseWristPosition + ghostWristLocalPosition;

            authoredAttach.SetPositionAndRotation(
                target.TransformPoint(correctedLocalPosition),
                target.rotation * correctedLocalRotation);
            return true;
        }

        static Transform FindControllerWrist(Transform interactorTransform)
        {
            Transform current = interactorTransform;
            for (int depth = 0; current != null && depth < 8; ++depth)
            {
                Transform wrist = FindDescendantTransform(current.gameObject, RightWristName);
                if (wrist != null && !IsHandPoseGhostTransform(wrist, current))
                    return wrist;
                current = current.parent;
            }

            return null;
        }

        static IXRSelectInteractor FindRightHandSelectInteractor()
        {
            XRBaseInteractor[] interactors =
                Resources.FindObjectsOfTypeAll<XRBaseInteractor>();
            IXRSelectInteractor best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < interactors.Length; ++i)
            {
                XRBaseInteractor candidate = interactors[i];
                if (candidate == null || !candidate.gameObject.scene.IsValid() ||
                    candidate is not IXRSelectInteractor selectable ||
                    !IsRightControllerHierarchy(candidate.transform))
                {
                    continue;
                }

                int score = candidate.gameObject.activeInHierarchy ? 10 : 0;
                if (candidate.name.IndexOf("NearFar", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 100;
                if (candidate.name.StartsWith("Right_", StringComparison.Ordinal))
                    score += 20;
                if (score <= bestScore)
                    continue;

                best = selectable;
                bestScore = score;
            }

            return best;
        }

        static bool IsRightControllerHierarchy(Transform transform)
        {
            Transform current = transform;
            for (int depth = 0; current != null && depth < 8; ++depth)
            {
                if (string.Equals(current.name, "Right Controller", StringComparison.Ordinal) ||
                    current.name.StartsWith("Right_", StringComparison.Ordinal))
                {
                    return true;
                }
                current = current.parent;
            }

            return false;
        }

        public static Transform ResolveStableControllerAnchor(IXRSelectInteractor interactor)
        {
            if (interactor is not Component component)
                return null;

            Transform interactorTransform = component.transform;
            Transform current = interactorTransform;
            for (int depth = 0; current != null && depth < 8; ++depth)
            {
                if (string.Equals(current.name, "Left Controller", StringComparison.Ordinal) ||
                    string.Equals(current.name, "Right Controller", StringComparison.Ordinal))
                {
                    return current;
                }

                current = current.parent;
            }

            // A custom rig may not use the shared controller names. Its interactor root is
            // still safer than the selectable attach transform, which Near/Far manipulation
            // moves and rotates in response to the thumbstick.
            return interactorTransform;
        }

        public static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
                return false;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool initialized = false;
            for (int i = 0; i < renderers.Length; ++i)
            {
                Renderer renderer = renderers[i];
                if (renderer == null ||
                    IsHandPoseGhostTransform(renderer.transform, root.transform))
                    continue;

                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return initialized;
        }

        public static BoxCollider EnsureBoundsCollider(GameObject root, bool isTrigger)
        {
            if (root == null)
                return null;

            BoxCollider collider = root.GetComponent<BoxCollider>();
            if (collider == null)
                collider = root.AddComponent<BoxCollider>();

            if (TryGetRendererBounds(root, out Bounds worldBounds))
            {
                collider.center = root.transform.InverseTransformPoint(worldBounds.center);
                Vector3 scale = root.transform.lossyScale;
                collider.size = new Vector3(
                    SafeDivide(worldBounds.size.x, Mathf.Abs(scale.x)),
                    SafeDivide(worldBounds.size.y, Mathf.Abs(scale.y)),
                    SafeDivide(worldBounds.size.z, Mathf.Abs(scale.z)));
            }

            collider.isTrigger = isTrigger;
            collider.enabled = true;
            return collider;
        }

        public static Material CreateCyanMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("Unlit/Color") ??
                            Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            var material = new Material(shader)
            {
                name = name,
                color = color,
                hideFlags = HideFlags.DontSave
            };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 2f);
            }
            if (color.a < 0.999f)
            {
                if (material.HasProperty("_Surface"))
                    material.SetFloat("_Surface", 1f);
                if (material.HasProperty("_Blend"))
                    material.SetFloat("_Blend", 0f);
                if (material.HasProperty("_SrcBlend"))
                    material.SetFloat("_SrcBlend",
                        (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend"))
                    material.SetFloat("_DstBlend",
                        (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (material.HasProperty("_ZWrite"))
                    material.SetFloat("_ZWrite", 0f);
                if (material.HasProperty("_Cull"))
                    material.SetFloat("_Cull", 0f);
                if (material.HasProperty("_CullMode"))
                    material.SetFloat("_CullMode", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_SURFACE_TYPE_OPAQUE");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            material.enableInstancing = true;
            return material;
        }

        public static Transform CreateRuntimeGhost(GameObject source, string ghostName,
            Vector3 worldPosition, Quaternion worldRotation)
        {
            if (source == null)
                return null;

            GameObject ghost = UnityEngine.Object.Instantiate(source);
            ghost.name = ghostName;
            ghost.transform.SetParent(null, true);
            // Instantiate without a parent copies localScale, not the source's
            // accumulated world scale. Preserve the visible size of the authored lid.
            ghost.transform.localScale = source.transform.lossyScale;
            ghost.transform.SetPositionAndRotation(worldPosition, worldRotation);

            Collider[] colliders = ghost.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; ++i)
                colliders[i].enabled = false;

            Rigidbody[] bodies = ghost.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; ++i)
            {
                bodies[i].linearVelocity = Vector3.zero;
                bodies[i].angularVelocity = Vector3.zero;
                bodies[i].useGravity = false;
                bodies[i].isKinematic = true;
            }

            Behaviour[] behaviours = ghost.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; ++i)
            {
                if (behaviours[i] != null)
                    behaviours[i].enabled = false;
            }

            Material ghostMaterial = CreateCyanMaterial(
                $"{ghostName} Material", new Color(0.1f, 0.9f, 1f, 0.38f));
            if (ghostMaterial != null)
            {
                if (ghostMaterial.FindPass("DepthPrepass") >= 0)
                    ghostMaterial.SetShaderPassEnabled("DepthPrepass", false);
            }
            Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                if (IsHandPoseGhostTransform(renderers[i].transform, ghost.transform))
                    continue;

                int materialCount = renderers[i].sharedMaterials.Length;
                if (materialCount == 0 || ghostMaterial == null)
                    continue;

                var materials = new Material[materialCount];
                for (int materialIndex = 0; materialIndex < materialCount; ++materialIndex)
                    materials[materialIndex] = ghostMaterial;
                renderers[i].sharedMaterials = materials;
            }

            ghost.SetActive(true);
            return ghost.transform;
        }

        public static TMP_FontAsset LoadRuntimeFont()
        {
            if (runtimeKoreanFont == null)
                runtimeKoreanFont = Resources.Load<TMP_FontAsset>("P102/Pretendard-SemiBold SDF");

            if (runtimeKoreanFont == null && !missingRuntimeFontReported)
            {
                missingRuntimeFontReported = true;
                Debug.LogError(
                    "[P102 Flow] Required TMP font asset 'Assets/3.SSH/Resources/P102/Pretendard-SemiBold SDF.asset' " +
                    "could not be loaded from Resources path 'P102/Pretendard-SemiBold SDF'. " +
                    "System-font fallback is intentionally disabled for Quest/Android consistency.");
            }

            return runtimeKoreanFont;
        }

        public static Sprite GetRoundedPanelSprite()
        {
            if (roundedPanelSprite != null)
                return roundedPanelSprite;

            const int size = 32;
            const float radius = 8f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "P102 Rounded Subtitle Panel Runtime",
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            Vector2 innerHalf = new Vector2(center.x - radius, center.y - radius);
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    Vector2 offset = new Vector2(
                        Mathf.Max(Mathf.Abs(x - center.x) - innerHalf.x, 0f),
                        Mathf.Max(Mathf.Abs(y - center.y) - innerHalf.y, 0f));
                    float alpha = Mathf.Clamp01(radius + 0.5f - offset.magnitude);
                    pixels[y * size + x] = new Color32(255, 255, 255,
                        (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            roundedPanelSprite = Sprite.Create(
                texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            roundedPanelSprite.name = "P102 Rounded Subtitle Panel Runtime";
            roundedPanelSprite.hideFlags = HideFlags.DontSave;
            return roundedPanelSprite;
        }

        /// <summary>
        /// Resolves a world-up billboard rotation from positions only. Unity world-space Canvas
        /// content is authored on the transform's -Z face, hence the deliberate 180 degree
        /// front-axis correction. Camera yaw, pitch and roll never enter this calculation.
        /// </summary>
        public static Quaternion ResolveYawOnlyCanvasRotation(Vector3 worldPosition,
            Vector3 viewerWorldPosition, Quaternion fallbackRotation)
        {
            Vector3 towardViewer = Vector3.ProjectOnPlane(
                viewerWorldPosition - worldPosition, Vector3.up);
            if (towardViewer.sqrMagnitude <= 0.000001f)
            {
                towardViewer = Vector3.ProjectOnPlane(
                    -(fallbackRotation * Vector3.forward), Vector3.up);
            }

            if (towardViewer.sqrMagnitude <= 0.000001f)
                towardViewer = Vector3.forward;

            // Canvas front normal is -transform.forward.
            return Quaternion.LookRotation(-towardViewer.normalized, Vector3.up);
        }

        public static Quaternion ResolveYawOnlyCanvasRotation(Transform visual,
            Transform viewer)
        {
            Quaternion fallback = visual != null ? visual.rotation : Quaternion.identity;
            Vector3 position = visual != null ? visual.position : Vector3.zero;
            Vector3 viewerPosition = viewer != null
                ? viewer.position
                : position - fallback * Vector3.forward;
            return ResolveYawOnlyCanvasRotation(position, viewerPosition, fallback);
        }

        /// <summary>
        /// Updates only world yaw, with a small deadband and exponential smoothing. The visual's
        /// position is intentionally untouched so target UI never follows the HMD.
        /// </summary>
        public static bool ApplyYawOnlyBillboard(Transform visual, Transform viewer,
            float deltaTime, float deadbandDegrees = 0.75f, float smoothing = 18f)
        {
            if (visual == null || viewer == null)
                return false;

            Quaternion target = ResolveYawOnlyCanvasRotation(visual, viewer);
            float angle = Quaternion.Angle(visual.rotation, target);
            if (angle <= Mathf.Max(0f, deadbandDegrees))
                return false;

            float t = deltaTime <= 0f
                ? 1f
                : 1f - Mathf.Exp(-Mathf.Max(0.01f, smoothing) * deltaTime);
            visual.rotation = Quaternion.Slerp(visual.rotation, target, t);
            return true;
        }

        // Kept as a compatibility wrapper for runtime code authored before the common billboard.
        public static Quaternion ResolveStableCanvasRotation(Transform anchor, Vector3 worldPosition,
            Transform cameraTransform)
        {
            Quaternion fallback = anchor != null ? anchor.rotation : Quaternion.identity;
            Vector3 viewerPosition = cameraTransform != null
                ? cameraTransform.position
                : worldPosition - fallback * Vector3.forward;
            return ResolveYawOnlyCanvasRotation(worldPosition, viewerPosition, fallback);
        }

        public static Quaternion ResolveStableCanvasRotation(Vector3 worldPosition,
            Transform cameraTransform, Vector3 stableUp, Vector3 fallbackForward)
        {
            Vector3 horizontalFallback = Vector3.ProjectOnPlane(fallbackForward, Vector3.up);
            Quaternion fallback = horizontalFallback.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(horizontalFallback.normalized, Vector3.up)
                : Quaternion.identity;
            Vector3 viewerPosition = cameraTransform != null
                ? cameraTransform.position
                : worldPosition - fallback * Vector3.forward;
            return ResolveYawOnlyCanvasRotation(worldPosition, viewerPosition, fallback);
        }

        public static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "<missing>";

            string path = transform.name;
            Transform parent = transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        public static void SetIgnoreRaycastRecursively(GameObject root)
        {
            if (root == null)
                return;

            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycastLayer < 0)
                ignoreRaycastLayer = 2;

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; ++i)
                transforms[i].gameObject.layer = ignoreRaycastLayer;
        }

        public static void DisableGraphicRaycasts(GameObject root)
        {
            if (root == null)
                return;

            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; ++i)
                graphics[i].raycastTarget = false;
        }

        public static bool TryGetLocalColliderBounds(Transform root, Collider[] colliders,
            out Bounds localBounds)
        {
            localBounds = default;
            if (root == null || colliders == null)
                return false;

            bool initialized = false;
            for (int i = 0; i < colliders.Length; ++i)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                    continue;

                Bounds worldBounds = collider.bounds;
                Vector3 min = worldBounds.min;
                Vector3 max = worldBounds.max;
                for (int corner = 0; corner < 8; ++corner)
                {
                    Vector3 worldCorner = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    Vector3 localCorner = root.InverseTransformPoint(worldCorner);
                    if (!initialized)
                    {
                        localBounds = new Bounds(localCorner, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localCorner);
                    }
                }
            }

            return initialized;
        }

        public static T GetOrAdd<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        static float SafeDivide(float value, float divisor)
        {
            return divisor > 0.000001f ? value / divisor : value;
        }
    }

    /// <summary>
    /// Keeps a carried response item in the controller hierarchy so locomotion and
    /// snap-turn cannot apply a delayed physics correction that flings or rotates it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class P102LocomotionStableGrabRuntime : MonoBehaviour
    {
        XRGrabInteractable grab;
        Rigidbody body;
        Transform heldAttach;
        Transform restoreParent;
        Vector3 heldLocalPosition;
        Quaternion heldLocalRotation;
        bool subscribed;

        public void Configure(XRGrabInteractable targetGrab)
        {
            if (grab == targetGrab && subscribed)
                return;

            Unsubscribe();
            grab = targetGrab;
            body = grab != null ? grab.GetComponent<Rigidbody>() : null;
            Subscribe();
        }

        void OnEnable()
        {
            Subscribe();
        }

        void OnDisable()
        {
            RestoreParenting();
            Unsubscribe();
        }

        void OnDestroy()
        {
            RestoreParenting();
            Unsubscribe();
        }

        void LateUpdate()
        {
            if (heldAttach == null || grab == null || !grab.isSelected)
                return;

            transform.SetPositionAndRotation(
                heldAttach.TransformPoint(heldLocalPosition),
                heldAttach.rotation * heldLocalRotation);
            ClearVelocity();
        }

        void OnSelected(SelectEnterEventArgs args)
        {
            Transform selectionAttach = args?.interactorObject?.GetAttachTransform(grab);
            heldAttach = P102RuntimeUtility.ResolveStableControllerAnchor(
                             args?.interactorObject) ??
                         selectionAttach;
            if (heldAttach == null)
                return;

            restoreParent = transform.parent;
            heldLocalPosition = heldAttach.InverseTransformPoint(transform.position);
            heldLocalRotation = Quaternion.Inverse(heldAttach.rotation) * transform.rotation;
            transform.SetParent(heldAttach, true);
            grab.trackPosition = false;
            grab.trackRotation = false;
            ClearVelocity();
        }

        void OnReleased(SelectExitEventArgs args)
        {
            RestoreParenting();
            ClearVelocity();
        }

        void RestoreParenting()
        {
            if (heldAttach != null && transform.parent == heldAttach)
                transform.SetParent(restoreParent, true);

            heldAttach = null;
            restoreParent = null;
            if (grab != null)
            {
                grab.trackPosition = true;
                grab.trackRotation = true;
            }
        }

        void ClearVelocity()
        {
            if (body == null)
                return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        void Subscribe()
        {
            if (subscribed || grab == null)
                return;
            grab.selectEntered.AddListener(OnSelected);
            grab.selectExited.AddListener(OnReleased);
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed || grab == null)
                return;
            grab.selectEntered.RemoveListener(OnSelected);
            grab.selectExited.RemoveListener(OnReleased);
            subscribed = false;
        }
    }
}
