using UnityEngine;

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
    /// App-owned body-relative equipment contract shared by every Stage.
    /// It does not know which item a Stage wants to retrieve.
    /// </summary>
    [DefaultExecutionOrder(-9800)]
    [DisallowMultipleComponent]
    public sealed class SharedPlayerEquipment : MonoBehaviour
    {
        const string BodyAnchorName = "PlayerBodyAnchor";
        const string BagName = "ResponseBag_Wearable";
        const string AccessRootName = "Shared Bag Access";
        const string RetrievalAnchorName = "Item Retrieval Anchor";

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

        Transform accessRoot;

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

            EnsureAccessObjects();
            StabilizeBagPhysics();
        }

        void LateUpdate()
        {
            // The visual may reapply its authored local pose when it is first equipped.
            // Keep the common access volume on the same body-relative pose. Scale stays
            // independent so deployed equipment keeps its authored size.
            AlignAccessRootToBag();
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

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
