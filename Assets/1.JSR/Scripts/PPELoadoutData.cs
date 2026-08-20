using System;
using System.Collections.Generic;
using HClTraining.SharedXR;
using UnityEngine;

/// <summary>
/// Where the current DontDestroyOnLoad loadout came from.
/// Do not infer this from singleton presence, list counts, or sequenceComplete.
/// </summary>
public enum PPELoadoutSource
{
    /// <summary>No training result and no standalone preset applied.</summary>
    None = 0,

    /// <summary>Explicit PPE-room finalize (may be partial or fully missing gear).</summary>
    PpeSceneResult = 1,

    /// <summary>Module-select / field-only official complete preset.</summary>
    FieldReadyPreset = 2,
}

/// <summary>
/// Cross-scene PPE loadout. Survives scene loads via DontDestroyOnLoad.
/// PPE room commits with <see cref="CommitPpeSceneResultFromManagers"/>;
/// field-only start uses <see cref="PPELoadoutPresets.BeginFieldStandaloneWithOfficialPreset"/>.
/// </summary>
public class PPELoadoutData : MonoBehaviour
{
    public static PPELoadoutData Instance { get; private set; }

    static bool allowEnsure = true;

    /// <summary>Canonical wearable ItemIds stored as Equipped.</summary>
    public static readonly string[] CanonicalEquippedItemIds =
    {
        PPEEquipmentManager.IdInnerGloves,
        PPEEquipmentManager.IdChemicalSuit,
        PPEEquipmentManager.IdChemicalBoots,
        PPEEquipmentManager.IdFaceRespirator,
        PPEEquipmentManager.IdResponseBag,
        PPEEquipmentManager.IdFaceShield,
        PPEEquipmentManager.IdChemicalGloves,
    };

    /// <summary>Canonical prep ItemIds stored as Prepared.</summary>
    public static readonly string[] CanonicalPreparedItemIds =
    {
        PPEEquipmentManager.IdWasteContainer,
    };

    readonly HashSet<string> equippedIds = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<string> preparedIds = new HashSet<string>(StringComparer.Ordinal);
    readonly List<string> equippedList = new List<string>();
    readonly List<string> preparedList = new List<string>();

    PPELoadoutSource source = PPELoadoutSource.None;
    bool sequenceComplete;
    bool bagContentsChecked;

    /// <summary>Raised after any capture / mark / clear / commit.</summary>
    public event Action Changed;

    public PPELoadoutSource Source => source;
    public bool HasRecordedPpeResult => source == PPELoadoutSource.PpeSceneResult;
    public bool HasResolvedLoadout => source != PPELoadoutSource.None;

    public bool IsSequenceComplete => sequenceComplete;
    public bool BagContentsChecked => bagContentsChecked;
    public IReadOnlyList<string> EquippedItemIds => equippedList;
    public IReadOnlyList<string> PreparedItemIds => preparedList;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        allowEnsure = true;
    }

#if UNITY_EDITOR
    static PPELoadoutData()
    {
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
        {
            allowEnsure = false;
            DestroyPersistentInstanceImmediate();
        }
        else if (state == UnityEditor.PlayModeStateChange.EnteredPlayMode)
        {
            allowEnsure = true;
        }
    }
#endif

    static void DestroyPersistentInstanceImmediate()
    {
        if (Instance == null)
            return;

        var go = Instance.gameObject;
        Instance = null;
        if (go != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEngine.Object.DestroyImmediate(go);
            else
#endif
                UnityEngine.Object.Destroy(go);
        }
    }

    /// <summary>Creates or returns the persistent loadout object.</summary>
    public static PPELoadoutData Ensure()
    {
        if (Instance != null)
            return Instance;

        // Never spawn DontDestroyOnLoad objects while the editor/app is tearing down.
        if (!allowEnsure || !Application.isPlaying)
            return null;

        var existing = FindAnyObjectByType<PPELoadoutData>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Instance = existing;
            DontDestroyOnLoad(existing.gameObject);
            return Instance;
        }

        var go = new GameObject(nameof(PPELoadoutData));
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<PPELoadoutData>();
        return Instance;
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

    void OnApplicationQuit()
    {
        allowEnsure = false;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool IsEquipped(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return false;
        return equippedIds.Contains(itemId);
    }

    public bool IsPrepared(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return false;
        return preparedIds.Contains(itemId);
    }

    /// <summary>True if the player finished the full PPE preparation sequence.</summary>
    public bool HasCompletedPreparation => sequenceComplete;

    /// <summary>
    /// Live draft mirror from PPE room managers. Does <b>not</b> set
    /// <see cref="Source"/> to <see cref="PPELoadoutSource.PpeSceneResult"/>.
    /// Prefer <see cref="CommitPpeSceneResultFromManagers"/> for scene handoff.
    /// </summary>
    public void CaptureFromManagers()
    {
        var equipment = PPEEquipmentManager.Instance != null
            ? PPEEquipmentManager.Instance
            : FindAnyObjectByType<PPEEquipmentManager>(FindObjectsInactive.Include);

        var sequence = PPESequenceManager.Resolve();

        // Next scene has no PPE room managers — do not wipe persisted loadout.
        if (equipment == null && sequence == null)
            return;

        // Committed PPE handoff must not be rewritten by later draft syncs in the PPE room.
        if (source == PPELoadoutSource.PpeSceneResult)
            return;

        // Entering PPE room after a field-ready preset must demote to draft (Source=None).
        if (source == PPELoadoutSource.FieldReadyPreset)
            source = PPELoadoutSource.None;

        if (equipment != null)
        {
            equippedIds.Clear();
            preparedIds.Clear();
            equippedList.Clear();
            preparedList.Clear();

            equipment.CopyEquippedIdsTo(equippedList);
            equipment.CopyPreparedIdsTo(preparedList);

            if (ContainsId(preparedList, PPEEquipmentManager.IdResponseBag)
                && !ContainsId(equippedList, PPEEquipmentManager.IdResponseBag))
            {
                equippedList.Add(PPEEquipmentManager.IdResponseBag);
            }

            FilterToCanonicalListsInPlace();

            for (var i = 0; i < equippedList.Count; i++)
                equippedIds.Add(equippedList[i]);
            for (var i = 0; i < preparedList.Count; i++)
                preparedIds.Add(preparedList[i]);
        }

        if (sequence != null)
        {
            sequenceComplete = sequence.IsSequenceComplete;
            bagContentsChecked = sequence.BagContentsChecked;
        }

        Changed?.Invoke();
    }

    /// <summary>Draft sync only. Does not mark <see cref="PPELoadoutSource.PpeSceneResult"/>.</summary>
    public static void SyncFromScene()
    {
        if (!Application.isPlaying || !allowEnsure)
            return;

        var data = Ensure();
        data?.CaptureFromManagers();
    }

    /// <summary>
    /// Explicit PPE-room finalize: one snapshot replace with
    /// <see cref="PPELoadoutSource.PpeSceneResult"/>. Safe for partial / empty gear.
    /// </summary>
    public static PPELoadoutData CommitPpeSceneResultFromManagers()
    {
        var snapshot = BuildPpeSceneSnapshotFromManagers();
        snapshot.source = PPELoadoutSource.PpeSceneResult;

        // Prefer existing DDOL instance. Avoid creating one during scene/play-mode teardown.
        var data = Instance != null ? Instance : Ensure();
        if (data == null)
            return null;

        data.ReplaceWithSnapshot(snapshot);
        SyncSessionInventoryFromPpe(data);
        return data;
    }

    /// <summary>
    /// Compatibility bridge for the current PPE-room completion rules. New Grip-to-bag
    /// interactions should call SessionEquipmentInventory.Store directly per item.
    /// </summary>
    public static void SyncSessionInventoryFromPpe(PPELoadoutData data)
    {
        var inventory = SessionEquipmentInventory.Ensure();
        if (inventory == null)
            return;

        var equipmentIds = new List<string>();
        var responseBagReady = data != null && data.BagContentsChecked &&
                               data.IsEquipped(PPEEquipmentManager.IdResponseBag);
        if (responseBagReady)
        {
            equipmentIds.Add(SharedEquipmentIds.GasDetector);
            equipmentIds.Add(SharedEquipmentIds.AbsorbentSock);
            equipmentIds.Add(SharedEquipmentIds.AbsorbentPad);
        }

        if (data != null && data.IsPrepared(PPEEquipmentManager.IdWasteContainer))
        {
            equipmentIds.Add(SharedEquipmentIds.WasteContainer);
            equipmentIds.Add(SharedEquipmentIds.RecoveryTongs);
        }

        inventory.ReplaceFromPpe(equipmentIds);
    }

    /// <summary>Builds canonical snapshot from current PPE room managers (Source left as None).</summary>
    public static PPELoadoutSnapshot BuildPpeSceneSnapshotFromManagers()
    {
        var snapshot = new PPELoadoutSnapshot
        {
            source = PPELoadoutSource.None,
            equippedItemIds = new List<string>(),
            preparedItemIds = new List<string>(),
        };

        var equipment = PPEEquipmentManager.Instance != null
            ? PPEEquipmentManager.Instance
            : FindAnyObjectByType<PPEEquipmentManager>(FindObjectsInactive.Include);

        var sequence = PPESequenceManager.Resolve();

        if (equipment != null)
        {
            var equipped = new List<string>();
            var prepared = new List<string>();
            equipment.CopyEquippedIdsTo(equipped);
            equipment.CopyPreparedIdsTo(prepared);

            // Spec: ResponseBag is Equipped only. Promote legacy prepared-only entries.
            if (ContainsId(prepared, PPEEquipmentManager.IdResponseBag)
                && !ContainsId(equipped, PPEEquipmentManager.IdResponseBag))
            {
                equipped.Add(PPEEquipmentManager.IdResponseBag);
            }

            AppendCanonicalEquipped(equipped, snapshot.equippedItemIds);
            AppendCanonicalPrepared(prepared, snapshot.preparedItemIds);
        }
        else if (Instance != null)
        {
            AppendCanonicalEquipped(Instance.equippedList, snapshot.equippedItemIds);
            AppendCanonicalPrepared(Instance.preparedList, snapshot.preparedItemIds);
        }

        if (sequence != null)
        {
            snapshot.sequenceComplete = sequence.IsSequenceComplete;
            snapshot.bagContentsChecked = sequence.BagContentsChecked;
        }
        else if (Instance != null)
        {
            snapshot.sequenceComplete = Instance.sequenceComplete;
            snapshot.bagContentsChecked = Instance.bagContentsChecked;
        }

        return snapshot;
    }

    public void MarkEquipped(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return;
        if (!IsCanonicalEquipped(itemId))
            return;

        if (!equippedIds.Add(itemId))
            return;

        equippedList.Add(itemId);
        Changed?.Invoke();
    }

    public void MarkPrepared(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return;
        if (!IsCanonicalPrepared(itemId))
            return;

        if (!preparedIds.Add(itemId))
            return;

        preparedList.Add(itemId);
        Changed?.Invoke();
    }

    public void SetSequenceComplete(bool complete)
    {
        if (sequenceComplete == complete)
            return;

        sequenceComplete = complete;
        Changed?.Invoke();
    }

    public void SetBagContentsChecked(bool checkedState)
    {
        if (bagContentsChecked == checkedState)
            return;

        bagContentsChecked = checkedState;
        Changed?.Invoke();
    }

    /// <summary>Clears all loadout state and sets <see cref="Source"/> to None.</summary>
    public void Clear()
    {
        equippedIds.Clear();
        preparedIds.Clear();
        equippedList.Clear();
        preparedList.Clear();
        sequenceComplete = false;
        bagContentsChecked = false;
        source = PPELoadoutSource.None;
        Changed?.Invoke();
    }

    /// <summary>
    /// Alias of <see cref="ApplySnapshot"/> — clears first, then writes the snapshot.
    /// Use when starting a new module so stale DontDestroyOnLoad data cannot linger.
    /// </summary>
    public void ReplaceWithSnapshot(PPELoadoutSnapshot snapshot) => ApplySnapshot(snapshot);

    /// <summary>Serializable snapshot for saving / debugging / future JSON use.</summary>
    public PPELoadoutSnapshot CreateSnapshot()
    {
        return new PPELoadoutSnapshot
        {
            source = source,
            equippedItemIds = new List<string>(equippedList),
            preparedItemIds = new List<string>(preparedList),
            sequenceComplete = sequenceComplete,
            bagContentsChecked = bagContentsChecked,
        };
    }

    public void ApplySnapshot(PPELoadoutSnapshot snapshot)
    {
        // Always clear first so previous DDOL state cannot leak into a new run.
        equippedIds.Clear();
        preparedIds.Clear();
        equippedList.Clear();
        preparedList.Clear();
        sequenceComplete = false;
        bagContentsChecked = false;
        source = PPELoadoutSource.None;

        if (snapshot == null)
        {
            Changed?.Invoke();
            return;
        }

        if (snapshot.equippedItemIds != null)
            AppendCanonicalEquipped(snapshot.equippedItemIds, equippedList);

        if (snapshot.preparedItemIds != null)
            AppendCanonicalPrepared(snapshot.preparedItemIds, preparedList);

        for (var i = 0; i < equippedList.Count; i++)
            equippedIds.Add(equippedList[i]);
        for (var i = 0; i < preparedList.Count; i++)
            preparedIds.Add(preparedList[i]);

        sequenceComplete = snapshot.sequenceComplete;
        bagContentsChecked = snapshot.bagContentsChecked;
        source = snapshot.source;
        Changed?.Invoke();
    }

    void FilterToCanonicalListsInPlace()
    {
        var equippedFiltered = new List<string>();
        AppendCanonicalEquipped(equippedList, equippedFiltered);
        equippedList.Clear();
        equippedList.AddRange(equippedFiltered);

        var preparedFiltered = new List<string>();
        AppendCanonicalPrepared(preparedList, preparedFiltered);
        preparedList.Clear();
        preparedList.AddRange(preparedFiltered);
    }

    static bool IsCanonicalEquipped(string itemId)
    {
        for (var i = 0; i < CanonicalEquippedItemIds.Length; i++)
        {
            if (CanonicalEquippedItemIds[i] == itemId)
                return true;
        }

        return false;
    }

    static bool IsCanonicalPrepared(string itemId)
    {
        for (var i = 0; i < CanonicalPreparedItemIds.Length; i++)
        {
            if (CanonicalPreparedItemIds[i] == itemId)
                return true;
        }

        return false;
    }

    static void AppendCanonicalEquipped(IList<string> sourceIds, List<string> destination)
    {
        if (sourceIds == null || destination == null)
            return;

        var seen = new HashSet<string>(destination, StringComparer.Ordinal);
        for (var i = 0; i < CanonicalEquippedItemIds.Length; i++)
        {
            var id = CanonicalEquippedItemIds[i];
            if (!ContainsId(sourceIds, id) || !seen.Add(id))
                continue;
            destination.Add(id);
        }
    }

    static void AppendCanonicalPrepared(IList<string> sourceIds, List<string> destination)
    {
        if (sourceIds == null || destination == null)
            return;

        var seen = new HashSet<string>(destination, StringComparer.Ordinal);
        for (var i = 0; i < CanonicalPreparedItemIds.Length; i++)
        {
            var id = CanonicalPreparedItemIds[i];
            if (!ContainsId(sourceIds, id) || !seen.Add(id))
                continue;
            destination.Add(id);
        }
    }

    static bool ContainsId(IList<string> ids, string itemId)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == itemId)
                return true;
        }

        return false;
    }

#if UNITY_EDITOR
    [ContextMenu("Debug / Log Loadout")]
    void EditorLogLoadout()
    {
        Debug.Log(
            $"[PPE Loadout] source={source} equipped=[{string.Join(", ", equippedList)}] " +
            $"prepared=[{string.Join(", ", preparedList)}] " +
            $"sequenceComplete={sequenceComplete} bagChecked={bagContentsChecked}",
            this);
    }

    [ContextMenu("Debug / Commit PPE Scene Result Now")]
    void EditorCommitPpeSceneResult()
    {
        CommitPpeSceneResultFromManagers();
        EditorLogLoadout();
    }
#endif
}

/// <summary>Plain serializable copy of <see cref="PPELoadoutData"/>.</summary>
[Serializable]
public class PPELoadoutSnapshot
{
    public PPELoadoutSource source = PPELoadoutSource.None;
    public List<string> equippedItemIds = new List<string>();
    public List<string> preparedItemIds = new List<string>();
    public bool sequenceComplete;
    public bool bagContentsChecked;
}
