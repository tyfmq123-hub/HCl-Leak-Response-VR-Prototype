using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRProject.LeakResponse.Narration
{
    public static class P102RuntimeUtility
    {
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
                if (renderer == null)
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

            Material ghostMaterial = CreateCyanMaterial(
                $"{ghostName} Material", new Color(0.1f, 0.9f, 1f, 0.38f));
            if (ghostMaterial != null)
            {
                if (ghostMaterial.HasProperty("_Surface"))
                    ghostMaterial.SetFloat("_Surface", 1f);
                if (ghostMaterial.HasProperty("_ZWrite"))
                    ghostMaterial.SetFloat("_ZWrite", 0f);
                ghostMaterial.SetOverrideTag("RenderType", "Transparent");
                ghostMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                ghostMaterial.DisableKeyword("_SURFACE_TYPE_OPAQUE");
                ghostMaterial.renderQueue = 3000;
            }
            Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
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
                    "[P102 Flow] Required TMP font asset 'Assets/Resources/P102/Pretendard-SemiBold SDF.asset' " +
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
}
