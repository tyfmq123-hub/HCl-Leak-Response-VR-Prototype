using System;
using System.Collections.Generic;
using UnityEngine;

namespace HClTraining.SharedXR
{
    public enum SessionInventorySource
    {
        None = 0,
        PpeResult = 1,
        FieldReadyPreset = 2,
        StageStandalonePreset = 3,
    }

    [Serializable]
    public sealed class SessionInventoryEntry
    {
        public string itemId;
        public int storedCount;
        public int checkedOutCount;
    }

    [Serializable]
    public sealed class SessionInventorySnapshot
    {
        public SessionInventorySource source;
        public bool hasExplicitPpeStorage;
        public List<SessionInventoryEntry> entries = new List<SessionInventoryEntry>();
    }

    /// <summary>
    /// Cross-scene equipment data only. It never owns a bag Transform, Collider,
    /// prefab instance, or Stage interaction. PPE stores IDs; each Stage presents its
    /// own authored object when it checks an item out.
    /// </summary>
    [DefaultExecutionOrder(-9950)]
    [DisallowMultipleComponent]
    public sealed class SessionEquipmentInventory : MonoBehaviour
    {
        sealed class Counts
        {
            public int stored;
            public int checkedOut;
        }

        static bool allowEnsure = true;
        readonly Dictionary<string, Counts> items =
            new Dictionary<string, Counts>(StringComparer.Ordinal);

        public static SessionEquipmentInventory Instance { get; private set; }
        public SessionInventorySource Source { get; private set; }
        public bool HasResolvedInventory => Source != SessionInventorySource.None;
        public bool HasExplicitPpeStorage { get; private set; }
        public event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            allowEnsure = true;
        }

        public static SessionEquipmentInventory Ensure()
        {
            if (Instance != null)
                return Instance;
            if (!allowEnsure || !Application.isPlaying)
                return null;

            var existing = FindAnyObjectByType<SessionEquipmentInventory>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Instance = existing;
                DontDestroyOnLoad(existing.gameObject);
                return existing;
            }

            var root = new GameObject(nameof(SessionEquipmentInventory));
            DontDestroyOnLoad(root);
            return root.AddComponent<SessionEquipmentInventory>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);
        }

        public bool Contains(string itemId) => GetStoredCount(itemId) > 0;

        public int GetStoredCount(string itemId) =>
            TryGetCounts(itemId, out var counts) ? counts.stored : 0;

        public int GetCheckedOutCount(string itemId) =>
            TryGetCounts(itemId, out var counts) ? counts.checkedOut : 0;

        public bool Store(string itemId, int quantity = 1)
        {
            if (!IsValid(itemId, quantity))
                return false;

            var counts = GetOrCreate(itemId);
            counts.stored += quantity;
            Source = SessionInventorySource.PpeResult;
            HasExplicitPpeStorage = true;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// PPE calls this once when its real Grip-to-bag packing step begins. It makes
        /// an intentionally empty bag a valid result and prevents the legacy completion
        /// bridge from replacing explicit Store calls.
        /// </summary>
        public void BeginPpeBagCapture()
        {
            items.Clear();
            Source = SessionInventorySource.PpeResult;
            HasExplicitPpeStorage = true;
            Changed?.Invoke();
        }

        public bool TryCheckOut(string itemId, int quantity = 1)
        {
            if (!IsValid(itemId, quantity) || !TryGetCounts(itemId, out var counts) ||
                counts.stored < quantity)
            {
                return false;
            }

            counts.stored -= quantity;
            counts.checkedOut += quantity;
            Changed?.Invoke();
            return true;
        }

        public bool ReturnCheckedOut(string itemId, int quantity = 1)
        {
            if (!IsValid(itemId, quantity) || !TryGetCounts(itemId, out var counts) ||
                counts.checkedOut < quantity)
            {
                return false;
            }

            counts.checkedOut -= quantity;
            counts.stored += quantity;
            Changed?.Invoke();
            return true;
        }

        public bool ConsumeCheckedOut(string itemId, int quantity = 1)
        {
            if (!IsValid(itemId, quantity) || !TryGetCounts(itemId, out var counts) ||
                counts.checkedOut < quantity)
            {
                return false;
            }

            counts.checkedOut -= quantity;
            RemoveIfEmpty(itemId, counts);
            Changed?.Invoke();
            return true;
        }

        public void BeginNewSession()
        {
            items.Clear();
            Source = SessionInventorySource.None;
            HasExplicitPpeStorage = false;
            Changed?.Invoke();
        }

        public void ReplaceFromPpe(IEnumerable<string> itemIds)
        {
            if (HasExplicitPpeStorage)
            {
                Source = SessionInventorySource.PpeResult;
                Changed?.Invoke();
                return;
            }
            ReplaceContents(itemIds, SessionInventorySource.PpeResult);
        }

        public bool SeedStandalone(IEnumerable<string> itemIds)
        {
            if (HasResolvedInventory)
                return false;
            ReplaceContents(itemIds, SessionInventorySource.StageStandalonePreset);
            return true;
        }

        public void ApplyFieldReadyPreset()
        {
            ReplaceContents(
                SessionEquipmentPresets.OfficialFieldEquipment,
                SessionInventorySource.FieldReadyPreset);
        }

        public SessionInventorySnapshot CreateSnapshot()
        {
            var snapshot = new SessionInventorySnapshot
            {
                source = Source,
                hasExplicitPpeStorage = HasExplicitPpeStorage,
            };
            foreach (var pair in items)
            {
                snapshot.entries.Add(new SessionInventoryEntry
                {
                    itemId = pair.Key,
                    storedCount = pair.Value.stored,
                    checkedOutCount = pair.Value.checkedOut,
                });
            }
            return snapshot;
        }

        public void ApplySnapshot(SessionInventorySnapshot snapshot)
        {
            items.Clear();
            Source = snapshot != null ? snapshot.source : SessionInventorySource.None;
            HasExplicitPpeStorage = snapshot != null && snapshot.hasExplicitPpeStorage;
            if (snapshot?.entries != null)
            {
                for (var i = 0; i < snapshot.entries.Count; i++)
                {
                    var entry = snapshot.entries[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.itemId))
                        continue;
                    var counts = GetOrCreate(entry.itemId);
                    counts.stored = Mathf.Max(0, entry.storedCount);
                    counts.checkedOut = Mathf.Max(0, entry.checkedOutCount);
                    RemoveIfEmpty(entry.itemId, counts);
                }
            }
            Changed?.Invoke();
        }

        void ReplaceContents(IEnumerable<string> itemIds, SessionInventorySource source)
        {
            items.Clear();
            if (itemIds != null)
            {
                foreach (var itemId in itemIds)
                {
                    if (string.IsNullOrWhiteSpace(itemId))
                        continue;
                    GetOrCreate(itemId).stored++;
                }
            }
            Source = source;
            HasExplicitPpeStorage = false;
            Changed?.Invoke();
        }

        Counts GetOrCreate(string itemId)
        {
            if (!items.TryGetValue(itemId, out var counts))
            {
                counts = new Counts();
                items.Add(itemId, counts);
            }
            return counts;
        }

        bool TryGetCounts(string itemId, out Counts counts)
        {
            counts = null;
            return !string.IsNullOrWhiteSpace(itemId) && items.TryGetValue(itemId, out counts);
        }

        void RemoveIfEmpty(string itemId, Counts counts)
        {
            if (counts.stored == 0 && counts.checkedOut == 0)
                items.Remove(itemId);
        }

        static bool IsValid(string itemId, int quantity) =>
            !string.IsNullOrWhiteSpace(itemId) && quantity > 0;

        void OnApplicationQuit()
        {
            allowEnsure = false;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }

    public static class SessionEquipmentPresets
    {
        public static readonly string[] OfficialFieldEquipment =
        {
            SharedEquipmentIds.GasDetector,
            SharedEquipmentIds.AbsorbentSock,
            SharedEquipmentIds.AbsorbentPad,
            SharedEquipmentIds.RecoveryTongs,
        };

        public static readonly string[] PreEntryStandaloneEquipment =
        {
            SharedEquipmentIds.GasDetector,
        };

        public static readonly string[] LeakResponseStandaloneEquipment =
        {
            SharedEquipmentIds.AbsorbentSock,
            SharedEquipmentIds.AbsorbentPad,
            SharedEquipmentIds.RecoveryTongs,
        };
    }
}
