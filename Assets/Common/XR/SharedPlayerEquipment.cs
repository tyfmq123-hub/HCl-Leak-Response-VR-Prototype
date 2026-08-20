using UnityEngine;
using UnityEngine.SceneManagement;

namespace HClTraining.SharedXR
{
    public static class SharedEquipmentIds
    {
        public const string GasDetector = "GasDetector";
        public const string AbsorbentSock = "AbsorbentSock";
        public const string AbsorbentPad = "AbsorbentPad";
        public const string WasteContainer = "WasteContainer";
        public const string RecoveryTongs = "RecoveryTongs";
    }

    /// <summary>
    /// Generic inventory bridge. Stage code chooses the equipment id and timing;
    /// the App-owned provider only stores and deploys an existing scene instance.
    /// </summary>
    public interface ISharedEquipmentInventory
    {
        bool IsAcquired(string equipmentId);
        bool IsAvailable(string equipmentId);
        bool IsDeployed(string equipmentId);
        bool SetAvailable(string equipmentId, bool available);
        bool TryDeployAt(string equipmentId, Transform worldPose);
        bool TryRecall(string equipmentId);
    }

    public static class SharedEquipmentInventory
    {
        public static ISharedEquipmentInventory Provider { get; private set; }

        public static void Register(ISharedEquipmentInventory provider)
        {
            if (provider != null)
                Provider = provider;
        }

        public static void Unregister(ISharedEquipmentInventory provider)
        {
            if (ReferenceEquals(Provider, provider))
                Provider = null;
        }
    }

    /// <summary>
    /// App-owned equipment contract shared by every Stage. The same bag can use its
    /// legacy body-relative pose or a Stage-provided fixed anchor. It never chooses
    /// which item a Stage retrieves.
    /// </summary>
    [DefaultExecutionOrder(-9800)]
    [DisallowMultipleComponent]
    public sealed class SharedPlayerEquipment : MonoBehaviour
    {
        const string BodyAnchorName = "PlayerBodyAnchor";
        const string BagName = "ResponseBag_Wearable";
        const string AccessRootName = "Shared Bag Access";
        const string RetrievalAnchorName = "Item Retrieval Anchor";
        public const string StageBagAnchorName = "StageBagAnchor";

        [SerializeField] Transform bodyAnchor;
        [SerializeField] Transform bagRoot;
        [SerializeField] Transform leftController;
        [SerializeField] SphereCollider retrievalZone;
        [SerializeField] Transform retrievalAnchor;
        [SerializeField, Min(0.1f)] float accessRadius = 0.38f;
        [SerializeField] Vector3 retrievalLocalPosition = new Vector3(0f, 0.08f, 0.22f);

        public static SharedPlayerEquipment Instance { get; private set; }
        public Transform BodyAnchor => bodyAnchor;
        public Transform BagRoot => bagRoot;
        public Transform LeftController => leftController;
        public Collider RetrievalZone => retrievalZone;
        public Transform RetrievalAnchor => retrievalAnchor;
        public bool IsBagVisible => bagRoot != null && bagRoot.gameObject.activeInHierarchy;
        public bool IsUsingStageAnchor => stageBagAnchor != null;

        Transform accessRoot;
        Transform originalBagParent;
        Vector3 originalBagLocalPosition;
        Quaternion originalBagLocalRotation;
        Vector3 originalBagLocalScale;
        Transform stageBagAnchor;

        public void Initialize(Transform commonXrRoot)
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[Shared Equipment] Duplicate SharedPlayerEquipment was disabled.", this);
                enabled = false;
                return;
            }

            Instance = this;
            if (commonXrRoot == null)
            {
                Debug.LogError("[Shared Equipment] Common XR root is missing.", this);
                return;
            }

            bodyAnchor = FindDescendant(commonXrRoot, BodyAnchorName);
            bagRoot = bodyAnchor != null ? FindDescendant(bodyAnchor, BagName) : null;
            leftController = commonXrRoot.Find("Camera Offset/Left Controller");

            if (bodyAnchor == null || bagRoot == null || leftController == null)
            {
                Debug.LogError(
                    $"[Shared Equipment] Required binding missing: body={bodyAnchor != null}, " +
                    $"bag={bagRoot != null}, leftController={leftController != null}.", this);
                return;
            }

            originalBagParent = bagRoot.parent;
            originalBagLocalPosition = bagRoot.localPosition;
            originalBagLocalRotation = bagRoot.localRotation;
            originalBagLocalScale = bagRoot.localScale;
            EnsureAccessObjects();
            StabilizeBagPhysics();
        }

        void LateUpdate()
        {
            if (stageBagAnchor != null)
                ApplyStageAnchorPose();
            else
                AlignAccessRootToBag();
        }

        /// <summary>
        /// Uses a fixed StageBagAnchor when one is authored in the active Stage.
        /// Missing anchors deliberately fall back to the current body-relative pose,
        /// allowing existing Stage scenes to migrate without runtime failures.
        /// </summary>
        public bool TryPlaceAtStageAnchor(Scene scene)
        {
            if (bagRoot == null || accessRoot == null || !scene.IsValid() || !scene.isLoaded)
                return false;

            var anchor = FindSceneTransform(scene, StageBagAnchorName);
            if (anchor == null)
            {
                RestoreBodyRelativePlacement();
                return false;
            }

            stageBagAnchor = anchor;
            bagRoot.SetParent(transform, true);
            accessRoot.SetParent(transform, true);
            ApplyStageAnchorPose();
            StabilizeBagPhysics();
            return true;
        }

        public void RestoreBodyRelativePlacement()
        {
            stageBagAnchor = null;
            if (bagRoot == null || originalBagParent == null)
                return;

            bagRoot.SetParent(originalBagParent, false);
            bagRoot.localPosition = originalBagLocalPosition;
            bagRoot.localRotation = originalBagLocalRotation;
            bagRoot.localScale = originalBagLocalScale;

            if (accessRoot != null && bodyAnchor != null)
                accessRoot.SetParent(bodyAnchor, false);
            AlignAccessRootToBag();
            StabilizeBagPhysics();
        }

        public bool IsLeftHandInRetrievalZone()
        {
            if (!IsBagVisible || leftController == null || retrievalZone == null ||
                !retrievalZone.enabled || !retrievalZone.gameObject.activeInHierarchy)
            {
                return false;
            }

            var scale = retrievalZone.transform.lossyScale;
            var worldRadius = retrievalZone.radius *
                              Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            var center = retrievalZone.transform.TransformPoint(retrievalZone.center);
            return (leftController.position - center).sqrMagnitude <= worldRadius * worldRadius;
        }

        void EnsureAccessObjects()
        {
            accessRoot = bodyAnchor.Find(AccessRootName);
            if (accessRoot == null)
            {
                var accessObject = new GameObject(AccessRootName);
                accessRoot = accessObject.transform;
                accessRoot.SetParent(bodyAnchor, false);
            }

            AlignAccessRootToBag();
            accessRoot.gameObject.layer = 2;

            retrievalZone = accessRoot.GetComponent<SphereCollider>();
            if (retrievalZone == null)
                retrievalZone = accessRoot.gameObject.AddComponent<SphereCollider>();
            retrievalZone.isTrigger = true;
            retrievalZone.center = Vector3.zero;
            retrievalZone.radius = Mathf.Max(0.1f, accessRadius);
            retrievalZone.enabled = true;

            retrievalAnchor = accessRoot.Find(RetrievalAnchorName);
            if (retrievalAnchor == null)
            {
                var anchorObject = new GameObject(RetrievalAnchorName);
                retrievalAnchor = anchorObject.transform;
                retrievalAnchor.SetParent(accessRoot, false);
            }
            retrievalAnchor.localPosition = retrievalLocalPosition;
            retrievalAnchor.localRotation = Quaternion.identity;
            retrievalAnchor.localScale = Vector3.one;
        }

        void AlignAccessRootToBag()
        {
            if (accessRoot == null || bagRoot == null || bodyAnchor == null)
                return;

            if (accessRoot.parent != bodyAnchor)
                accessRoot.SetParent(bodyAnchor, false);
            accessRoot.localPosition = bagRoot.localPosition;
            accessRoot.localRotation = bagRoot.localRotation;
            accessRoot.localScale = Vector3.one;
        }

        void ApplyStageAnchorPose()
        {
            if (stageBagAnchor == null || bagRoot == null || accessRoot == null)
                return;

            bagRoot.SetPositionAndRotation(stageBagAnchor.position, stageBagAnchor.rotation);
            accessRoot.SetPositionAndRotation(bagRoot.position, bagRoot.rotation);
            accessRoot.localScale = InverseScale(accessRoot.parent != null
                ? accessRoot.parent.lossyScale
                : Vector3.one);
        }

        void StabilizeBagPhysics()
        {
            if (bagRoot == null)
                return;

            var bodies = bagRoot.GetComponentsInChildren<Rigidbody>(true);
            for (var i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                if (body == null)
                    continue;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = false;
                body.isKinematic = true;
            }
        }

        static Transform FindDescendant(Transform root, string exactName)
        {
            if (root == null)
                return null;
            var descendants = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < descendants.Length; i++)
                if (descendants[i] != null && descendants[i].name == exactName) return descendants[i];
            return null;
        }

        static Transform FindSceneTransform(Scene scene, string exactName)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var descendants = root.GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < descendants.Length; i++)
                {
                    if (descendants[i] != null && descendants[i].name == exactName)
                        return descendants[i];
                }
            }
            return null;
        }

        static Vector3 InverseScale(Vector3 scale) => new Vector3(
            Mathf.Abs(scale.x) > 0.0001f ? 1f / scale.x : 1f,
            Mathf.Abs(scale.y) > 0.0001f ? 1f / scale.y : 1f,
            Mathf.Abs(scale.z) > 0.0001f ? 1f / scale.z : 1f);

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
