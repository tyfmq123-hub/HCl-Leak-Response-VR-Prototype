using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRProject.ObjectInstallation
{
    /// <summary>
    /// Converts one Scene-preplaced Ghost object into its usable installed state.
    /// It never creates a replacement, snaps the object back, or restores the Ghost after installation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PreplacedGhostInstallItem : MonoBehaviour
    {
        [Serializable]
        public sealed class RendererMaterialBinding
        {
            [SerializeField] Renderer targetRenderer;
            [SerializeField] Material[] ghostMaterials = Array.Empty<Material>();
            [SerializeField] Material[] installedMaterials = Array.Empty<Material>();

            public Renderer TargetRenderer => targetRenderer;
            public Material[] GhostMaterials => ghostMaterials;
            public Material[] InstalledMaterials => installedMaterials;

            public RendererMaterialBinding(
                Renderer renderer,
                Material[] newGhostMaterials,
                Material[] newInstalledMaterials)
            {
                targetRenderer = renderer;
                ghostMaterials = CloneMaterials(newGhostMaterials);
                installedMaterials = CloneMaterials(newInstalledMaterials);
            }
        }

        [Serializable]
        public sealed class ColliderBinding
        {
            [SerializeField] Collider targetCollider;
            [SerializeField] bool installedEnabled;

            public Collider TargetCollider => targetCollider;
            public bool InstalledEnabled => installedEnabled;

            public ColliderBinding(Collider collider, bool newInstalledEnabled)
            {
                targetCollider = collider;
                installedEnabled = newInstalledEnabled;
            }
        }

        [Header("Installation Type")]
        [Tooltip("Type installed by this preplaced object, for example Pad or Sock.")]
        [SerializeField] string installationTypeId;

        [Header("Visual State")]
        [Tooltip("Per-Renderer Ghost materials and the exact opaque materials restored on installation.")]
        [SerializeField] RendererMaterialBinding[] rendererMaterials =
            Array.Empty<RendererMaterialBinding>();

        [Header("Interaction State")]
        [Tooltip("Colliders disabled while this object is a Ghost and restored individually on installation.")]
        [SerializeField] ColliderBinding[] colliders = Array.Empty<ColliderBinding>();

        [SerializeField] Rigidbody targetRigidbody;
        [SerializeField] XRGrabInteractable grabInteractable;

        [Header("Installed Rigidbody State")]
        [SerializeField] bool installedIsKinematic;
        [SerializeField] bool installedUseGravity;

        [Header("Runtime State (Read Only)")]
        [SerializeField] bool isInstalled;
        [SerializeField] bool hasBeenReleased;

        bool selectEventsSubscribed;
        readonly List<Material> runtimeGhostMaterials = new List<Material>();

        [Header("Initial Pose (Recorded Only)")]
        [SerializeField] Vector3 initialLocalPosition;
        [SerializeField] Quaternion initialLocalRotation = Quaternion.identity;
        [SerializeField] Vector3 initialLocalScale = Vector3.one;

        public string InstallationTypeId => installationTypeId;
        public bool IsInstalled => isInstalled;
        public Rigidbody TargetRigidbody => targetRigidbody;
        public XRGrabInteractable GrabInteractable => grabInteractable;
        public RendererMaterialBinding[] RendererMaterials => rendererMaterials;
        public ColliderBinding[] Colliders => colliders;

        void Awake()
        {
            // Runtime bootstrap can add this component before supplying its existing Scene references.
            if (rendererMaterials == null || rendererMaterials.Length == 0)
                return;

            if (isInstalled)
                ApplyInstalledState();
            else
                ApplyInitialGhostState();
        }

        void OnEnable()
        {
            SubscribeSelectEvents();
        }

        void OnDisable()
        {
            UnsubscribeSelectEvents();
        }

        void OnDestroy()
        {
            UnsubscribeSelectEvents();
            ReleaseRuntimeGhostMaterials();
        }

        public void Configure(
            string newInstallationTypeId,
            RendererMaterialBinding[] newRendererMaterials,
            ColliderBinding[] newColliders,
            Rigidbody existingRigidbody,
            XRGrabInteractable existingGrabInteractable,
            bool newInstalledIsKinematic,
            bool newInstalledUseGravity)
        {
            installationTypeId = newInstallationTypeId;
            rendererMaterials = newRendererMaterials ?? Array.Empty<RendererMaterialBinding>();
            colliders = newColliders ?? Array.Empty<ColliderBinding>();
            targetRigidbody = existingRigidbody;
            grabInteractable = existingGrabInteractable;
            installedIsKinematic = newInstalledIsKinematic;
            installedUseGravity = newInstalledUseGravity;
            initialLocalPosition = transform.localPosition;
            initialLocalRotation = transform.localRotation;
            initialLocalScale = transform.localScale;
            isInstalled = false;
            hasBeenReleased = false;
        }

        public bool TryValidateConfiguration(out string issue)
        {
            if (string.IsNullOrWhiteSpace(installationTypeId))
                return Fail("Installation Type Id is empty.", out issue);

            if (rendererMaterials == null || rendererMaterials.Length == 0)
                return Fail("At least one Renderer material binding is required.", out issue);

            for (int i = 0; i < rendererMaterials.Length; ++i)
            {
                RendererMaterialBinding binding = rendererMaterials[i];
                if (binding == null || binding.TargetRenderer == null)
                    return Fail($"Renderer binding {i} has no target Renderer.", out issue);

                int rendererSlotCount = binding.TargetRenderer.sharedMaterials.Length;
                if (!HasCompleteMaterials(binding.GhostMaterials, rendererSlotCount))
                    return Fail($"Renderer '{binding.TargetRenderer.name}' has an incomplete Ghost material array.", out issue);

                if (!HasCompleteMaterials(binding.InstalledMaterials, rendererSlotCount))
                    return Fail($"Renderer '{binding.TargetRenderer.name}' has an incomplete installed material array.", out issue);
            }

            if (colliders == null || colliders.Length == 0)
                return Fail("At least one Collider binding is required.", out issue);

            for (int i = 0; i < colliders.Length; ++i)
            {
                if (colliders[i] == null || colliders[i].TargetCollider == null)
                    return Fail($"Collider binding {i} has no target Collider.", out issue);
            }

            if (targetRigidbody == null)
                return Fail("The existing Rigidbody is not assigned.", out issue);

            if (grabInteractable == null)
                return Fail("The existing XR Grab Interactable is not assigned.", out issue);

            issue = null;
            return true;
        }

        public bool ApplyInitialGhostState()
        {
            if (!TryValidateConfiguration(out string issue))
            {
                Debug.LogError($"[Preplaced Ghost Install] {name}: {issue}", this);
                return false;
            }

            grabInteractable.enabled = false;

            for (int i = 0; i < colliders.Length; ++i)
                colliders[i].TargetCollider.enabled = false;

            ApplyMaterials(useInstalledMaterials: false);

            if (!targetRigidbody.isKinematic)
            {
                targetRigidbody.linearVelocity = Vector3.zero;
                targetRigidbody.angularVelocity = Vector3.zero;
            }
            targetRigidbody.useGravity = false;
            targetRigidbody.isKinematic = true;
            isInstalled = false;
            hasBeenReleased = false;
            return true;
        }

        public bool TryInstall()
        {
            if (isInstalled)
                return false;

            if (!TryValidateConfiguration(out string issue))
            {
                Debug.LogError($"[Preplaced Ghost Install] {name}: {issue}", this);
                return false;
            }

            return ApplyInstalledState();
        }

        public bool TryGetGhostMaterials(Renderer renderer, out Material[] materials)
        {
            if (rendererMaterials != null)
            {
                for (int i = 0; i < rendererMaterials.Length; ++i)
                {
                    RendererMaterialBinding binding = rendererMaterials[i];
                    if (binding != null && binding.TargetRenderer == renderer)
                    {
                        materials = CloneMaterials(binding.GhostMaterials);
                        return true;
                    }
                }
            }

            materials = null;
            return false;
        }

        public bool TryGetInstalledColliderState(Collider collider, out bool enabled)
        {
            if (colliders != null)
            {
                for (int i = 0; i < colliders.Length; ++i)
                {
                    ColliderBinding binding = colliders[i];
                    if (binding != null && binding.TargetCollider == collider)
                    {
                        enabled = binding.InstalledEnabled;
                        return true;
                    }
                }
            }

            enabled = false;
            return false;
        }

        public bool TryGetInstalledRigidbodyState(out bool isKinematic, out bool useGravity)
        {
            if (targetRigidbody != null)
            {
                isKinematic = installedIsKinematic;
                useGravity = installedUseGravity;
                return true;
            }

            isKinematic = false;
            useGravity = false;
            return false;
        }

        bool ApplyInstalledState()
        {
            ApplyMaterials(useInstalledMaterials: true);

            targetRigidbody.linearVelocity = Vector3.zero;
            targetRigidbody.angularVelocity = Vector3.zero;

            if (hasBeenReleased)
                RestoreInstalledRigidbodyState();
            else
                ApplyInstalledAnchoredRigidbodyState();

            for (int i = 0; i < colliders.Length; ++i)
                colliders[i].TargetCollider.enabled = colliders[i].InstalledEnabled;

            isInstalled = true;
            grabInteractable.enabled = true;
            return true;
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (!isInstalled || hasBeenReleased || grabInteractable.isSelected)
                return;

            // XRGrabInteractable.Drop has already restored its captured Rigidbody state
            // before this second-pass selectExited event is raised.
            if (!grabInteractable.throwOnDetach)
            {
                targetRigidbody.linearVelocity = Vector3.zero;
                targetRigidbody.angularVelocity = Vector3.zero;
            }

            hasBeenReleased = true;
            RestoreInstalledRigidbodyState();
        }

        void ApplyInstalledAnchoredRigidbodyState()
        {
            targetRigidbody.isKinematic = true;
            targetRigidbody.useGravity = false;
        }

        void RestoreInstalledRigidbodyState()
        {
            targetRigidbody.isKinematic = installedIsKinematic;
            targetRigidbody.useGravity = installedUseGravity;
        }

        void SubscribeSelectEvents()
        {
            if (selectEventsSubscribed || grabInteractable == null)
                return;

            grabInteractable.selectExited.AddListener(OnSelectExited);
            selectEventsSubscribed = true;
        }

        void UnsubscribeSelectEvents()
        {
            if (!selectEventsSubscribed || grabInteractable == null)
                return;

            grabInteractable.selectExited.RemoveListener(OnSelectExited);
            selectEventsSubscribed = false;
        }

        void ApplyMaterials(bool useInstalledMaterials)
        {
            if (!useInstalledMaterials)
                ReleaseRuntimeGhostMaterials();

            for (int i = 0; i < rendererMaterials.Length; ++i)
            {
                RendererMaterialBinding binding = rendererMaterials[i];
                Material[] materials = useInstalledMaterials
                    ? binding.InstalledMaterials
                    : Application.isPlaying
                        ? CreateRuntimeGhostMaterials(binding.GhostMaterials)
                        : binding.GhostMaterials;
                binding.TargetRenderer.sharedMaterials = CloneMaterials(materials);
            }

            if (useInstalledMaterials)
                ReleaseRuntimeGhostMaterials();
        }

        Material[] CreateRuntimeGhostMaterials(Material[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<Material>();

            var materials = new Material[source.Length];
            for (int i = 0; i < source.Length; ++i)
            {
                Material sourceMaterial = source[i];
                Material runtimeMaterial = CreateStereoSafeGhostMaterial(
                    $"{sourceMaterial.name} (Ghost Runtime, Stereo Safe)",
                    new Color(0.1f, 0.9f, 1f, 0.38f));
                if (runtimeMaterial == null)
                {
                    runtimeMaterial = new Material(sourceMaterial)
                    {
                        name = $"{sourceMaterial.name} (Ghost Runtime, No Depth Write)",
                        hideFlags = HideFlags.DontSave
                    };
                }

                // The current HandGhostFresnel shader is transparent in its color pass,
                // but its separate DepthPrepass writes an opaque silhouette into depth.
                // Disabling only that pass keeps the visible Fresnel guide while allowing
                // the transparent puddle behind it to render normally.
                if (runtimeMaterial.FindPass("DepthPrepass") >= 0)
                    runtimeMaterial.SetShaderPassEnabled("DepthPrepass", false);

                materials[i] = runtimeMaterial;
                runtimeGhostMaterials.Add(runtimeMaterial);
            }

            return materials;
        }

        static Material CreateStereoSafeGhostMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("Unlit/Color") ??
                            Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            var material = new Material(shader)
            {
                name = materialName,
                color = color,
                hideFlags = HideFlags.DontSave,
                enableInstancing = true,
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent
            };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
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
            return material;
        }

        void ReleaseRuntimeGhostMaterials()
        {
            for (int i = 0; i < runtimeGhostMaterials.Count; ++i)
            {
                Material material = runtimeGhostMaterials[i];
                if (material == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(material);
                else
                    DestroyImmediate(material);
            }

            runtimeGhostMaterials.Clear();
        }

        static bool HasCompleteMaterials(Material[] materials, int expectedCount)
        {
            if (materials == null || materials.Length != expectedCount)
                return false;

            for (int i = 0; i < materials.Length; ++i)
            {
                if (materials[i] == null)
                    return false;
            }

            return true;
        }

        static Material[] CloneMaterials(Material[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<Material>();

            var clone = new Material[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        static bool Fail(string message, out string issue)
        {
            issue = message;
            return false;
        }
    }
}
