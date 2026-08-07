using System;
using UnityEngine;
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
            if (isInstalled)
                ApplyInstalledState();
            else
                ApplyInitialGhostState();
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

            targetRigidbody.linearVelocity = Vector3.zero;
            targetRigidbody.angularVelocity = Vector3.zero;
            targetRigidbody.useGravity = false;
            targetRigidbody.isKinematic = true;
            isInstalled = false;
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
            targetRigidbody.isKinematic = installedIsKinematic;
            targetRigidbody.useGravity = installedUseGravity;

            for (int i = 0; i < colliders.Length; ++i)
                colliders[i].TargetCollider.enabled = colliders[i].InstalledEnabled;

            grabInteractable.enabled = true;
            isInstalled = true;
            return true;
        }

        void ApplyMaterials(bool useInstalledMaterials)
        {
            for (int i = 0; i < rendererMaterials.Length; ++i)
            {
                RendererMaterialBinding binding = rendererMaterials[i];
                Material[] materials = useInstalledMaterials
                    ? binding.InstalledMaterials
                    : binding.GhostMaterials;
                binding.TargetRenderer.sharedMaterials = CloneMaterials(materials);
            }
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
