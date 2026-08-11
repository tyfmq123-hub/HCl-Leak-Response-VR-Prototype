using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HClTraining.App
{
    [DisallowMultipleComponent]
    public sealed class PersistentEquipmentInventory : MonoBehaviour
    {
        public const string GasDetector = "GasDetector";
        public const string AbsorbentSock = "AbsorbentSock";
        public const string AbsorbentPad = "AbsorbentPad";
        public const string WasteContainer = "WasteContainer";
        public const string RecoveryTongs = "RecoveryTongs";

        sealed class RuntimeBinding
        {
            public GameObject instance;
            public Transform originalParent;
            public Vector3 originalLocalPosition;
            public Quaternion originalLocalRotation;
            public Vector3 originalLocalScale;
            public bool deployed;
        }

        readonly Dictionary<string, RuntimeBinding> bindings =
            new Dictionary<string, RuntimeBinding>(StringComparer.Ordinal);

        TrainingSessionState session;
        Transform storageRoot;
        Transform deploymentAnchor;

        public event Action Changed;
        public IReadOnlyCollection<string> AcquiredIds => session != null
            ? session.AcquiredEquipmentIds
            : Array.Empty<string>();

        public void Initialize(TrainingSessionState state, Transform anchor)
        {
            session = state;
            deploymentAnchor = anchor;
            var existing = transform.Find("PersistentEquipmentInventoryRoot");
            if (existing == null)
            {
                var root = new GameObject("PersistentEquipmentInventoryRoot");
                root.transform.SetParent(transform, false);
                existing = root.transform;
            }
            storageRoot = existing;
        }

        public void SyncFromLoadout()
        {
            if (session == null)
                return;

            var ids = new List<string>();
            var data = PPELoadoutData.Instance;
            var hasChemicalGloves = data != null && data.IsEquipped(PPEEquipmentManager.IdChemicalGloves);
            var hasResponseBag = data != null && data.IsEquipped(PPEEquipmentManager.IdResponseBag);
            var bagReady = data != null && data.BagContentsChecked && hasResponseBag;
            if (bagReady)
            {
                ids.Add(GasDetector);
                ids.Add(AbsorbentSock);
                ids.Add(AbsorbentPad);
            }
            if (data != null && data.IsPrepared(PPEEquipmentManager.IdWasteContainer))
            {
                ids.Add(WasteContainer);
                ids.Add(RecoveryTongs);
            }

            session.SetPpeResult(hasChemicalGloves, hasChemicalGloves, ids);
            EnsureStorageRecords(ids);
            Changed?.Invoke();
        }

        public void BeginStage(Scene scene, TrainingStage stage)
        {
            ReturnAllToStorage();
            bindings.Clear();
            session?.ClearScenarioAvailability();

            if (stage == TrainingStage.LeakResponse)
            {
                Bind(scene, AbsorbentSock, "INT_AbsorbentSock_Straight_01");
                Bind(scene, AbsorbentPad, "INT_AbsorbentPad_01");
                Bind(scene, WasteContainer, "INT_WasteContainer_01");
                Bind(scene, RecoveryTongs, "INT_RecoveryTongs_01");
            }
            Changed?.Invoke();
        }

        public bool SetAvailable(string equipmentId, bool available)
        {
            if (session == null || !session.IsEquipmentAcquired(equipmentId))
                return false;
            session.SetEquipmentAvailable(equipmentId, available);
            Changed?.Invoke();
            return true;
        }

        public bool IsDeployed(string equipmentId) =>
            bindings.TryGetValue(equipmentId, out var binding) && binding.deployed && binding.instance != null;

        public bool DeployOrRecall(string equipmentId)
        {
            if (session == null || !session.IsEquipmentAvailable(equipmentId) ||
                !session.IsEquipmentAcquired(equipmentId))
                return false;

            if (equipmentId == GasDetector)
            {
                var detector = FindSceneComponent<JH.EntrySafety.EntrySafetyHclDetectorController>();
                var equipped = detector != null && detector.TryEquip();
                Changed?.Invoke();
                return equipped;
            }

            if (!bindings.TryGetValue(equipmentId, out var binding) || binding.instance == null)
            {
                Debug.LogError($"[Equipment Inventory] '{equipmentId}' 장비 참조가 없어 해당 장비만 비활성화합니다.", this);
                session.SetEquipmentAvailable(equipmentId, false);
                Changed?.Invoke();
                return false;
            }

            if (binding.deployed)
            {
                Recall(equipmentId, binding);
                return true;
            }

            binding.instance.transform.SetParent(binding.originalParent, false);
            binding.instance.transform.localPosition = binding.originalLocalPosition;
            binding.instance.transform.localRotation = binding.originalLocalRotation;
            binding.instance.transform.localScale = binding.originalLocalScale;
            binding.instance.SetActive(true);
            ResetMotion(binding.instance);
            binding.deployed = true;
            Changed?.Invoke();
            return true;
        }

        public bool Recover(string equipmentId)
        {
            if (!bindings.TryGetValue(equipmentId, out var binding) || binding.instance == null)
                return false;

            binding.instance.transform.SetParent(binding.originalParent, false);
            binding.instance.transform.localPosition = binding.originalLocalPosition;
            binding.instance.transform.localRotation = binding.originalLocalRotation;
            binding.instance.transform.localScale = binding.originalLocalScale;
            binding.instance.SetActive(true);
            ResetMotion(binding.instance);
            binding.deployed = true;
            Changed?.Invoke();
            return true;
        }

        public void ReturnAllToStorage()
        {
            foreach (var pair in bindings)
            {
                if (pair.Value?.instance != null)
                    Recall(pair.Key, pair.Value);
            }
            session?.ClearScenarioAvailability();
            Changed?.Invoke();
        }

        void Recall(string equipmentId, RuntimeBinding binding)
        {
            ResetMotion(binding.instance);
            binding.instance.SetActive(false);
            binding.deployed = false;
            Changed?.Invoke();
        }

        void Bind(Scene scene, string equipmentId, string exactName)
        {
            if (session == null || !session.IsEquipmentAcquired(equipmentId))
                return;

            var instance = FindSceneObject(scene, exactName);
            if (instance == null)
            {
                Debug.LogError($"[Equipment Inventory] '{exactName}' 참조를 찾지 못했습니다. '{equipmentId}'만 비활성화합니다.", this);
                return;
            }

            bindings[equipmentId] = new RuntimeBinding
            {
                instance = instance,
                originalParent = instance.transform.parent,
                originalLocalPosition = instance.transform.localPosition,
                originalLocalRotation = instance.transform.localRotation,
                originalLocalScale = instance.transform.localScale,
                deployed = false,
            };
            instance.SetActive(false);
        }

        void EnsureStorageRecords(IEnumerable<string> ids)
        {
            if (storageRoot == null)
                return;
            var desired = new HashSet<string>(ids ?? Array.Empty<string>(), StringComparer.Ordinal);
            for (var i = storageRoot.childCount - 1; i >= 0; i--)
            {
                if (!desired.Contains(storageRoot.GetChild(i).name))
                    Destroy(storageRoot.GetChild(i).gameObject);
            }
            foreach (var id in desired)
            {
                if (storageRoot.Find(id) != null)
                    continue;
                var record = new GameObject(id);
                record.transform.SetParent(storageRoot, false);
                record.SetActive(false);
            }
        }

        static GameObject FindSceneObject(Scene scene, string exactName)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < transforms.Length; i++)
                {
                    if (transforms[i].name == exactName)
                        return transforms[i].gameObject;
                }
            }
            return null;
        }

        static T FindSceneComponent<T>() where T : Component
        {
            var all = Resources.FindObjectsOfTypeAll<T>();
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].gameObject.scene.IsValid() && all[i].gameObject.activeInHierarchy)
                    return all[i];
            }
            return null;
        }

        static void ResetMotion(GameObject instance)
        {
            var bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (var i = 0; i < bodies.Length; i++)
            {
                bodies[i].linearVelocity = Vector3.zero;
                bodies[i].angularVelocity = Vector3.zero;
            }
        }
    }
}
