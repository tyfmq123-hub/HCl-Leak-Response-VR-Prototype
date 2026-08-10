using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cross-scene PPE loadout. Survives scene loads via DontDestroyOnLoad.
/// PPE room writes into this; the next scene reads with <see cref="IsEquipped"/> /
/// <see cref="IsPrepared"/> / <see cref="IsSequenceComplete"/>.
/// </summary>
public class PPELoadoutData : MonoBehaviour
{
    public static PPELoadoutData Instance { get; private set; }

    readonly HashSet<string> equippedIds = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<string> preparedIds = new HashSet<string>(StringComparer.Ordinal);
    readonly List<string> equippedList = new List<string>();
    readonly List<string> preparedList = new List<string>();

    bool sequenceComplete;
    bool bagContentsChecked;

    /// <summary>Raised after any capture / mark / clear.</summary>
    public event Action Changed;

    public bool IsSequenceComplete => sequenceComplete;
    public bool BagContentsChecked => bagContentsChecked;
    public IReadOnlyList<string> EquippedItemIds => equippedList;
    public IReadOnlyList<string> PreparedItemIds => preparedList;

    /// <summary>Creates or returns the persistent loadout object.</summary>
    public static PPELoadoutData Ensure()
    {
        if (Instance != null)
            return Instance;

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
        DontDestroyOnLoad(gameObject);
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
    /// Copies current PPE room manager state into this loadout.
    /// Safe to call repeatedly. If no managers exist (e.g. already in the next scene),
    /// existing loadout values are left unchanged.
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

        if (equipment != null)
        {
            equippedIds.Clear();
            preparedIds.Clear();
            equippedList.Clear();
            preparedList.Clear();

            equipment.CopyEquippedIdsTo(equippedList);
            equipment.CopyPreparedIdsTo(preparedList);

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

    /// <summary>Convenience used by PPE room managers after state changes.</summary>
    public static void SyncFromScene()
    {
        if (!Application.isPlaying)
            return;

        Ensure().CaptureFromManagers();
    }

    public void MarkEquipped(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
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

    /// <summary>Clears all loadout state (e.g. new playthrough).</summary>
    public void Clear()
    {
        equippedIds.Clear();
        preparedIds.Clear();
        equippedList.Clear();
        preparedList.Clear();
        sequenceComplete = false;
        bagContentsChecked = false;
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

        if (snapshot == null)
        {
            Changed?.Invoke();
            return;
        }

        if (snapshot.equippedItemIds != null)
        {
            for (var i = 0; i < snapshot.equippedItemIds.Count; i++)
            {
                var id = snapshot.equippedItemIds[i];
                if (string.IsNullOrWhiteSpace(id) || !equippedIds.Add(id))
                    continue;
                equippedList.Add(id);
            }
        }

        if (snapshot.preparedItemIds != null)
        {
            for (var i = 0; i < snapshot.preparedItemIds.Count; i++)
            {
                var id = snapshot.preparedItemIds[i];
                if (string.IsNullOrWhiteSpace(id) || !preparedIds.Add(id))
                    continue;
                preparedList.Add(id);
            }
        }

        sequenceComplete = snapshot.sequenceComplete;
        bagContentsChecked = snapshot.bagContentsChecked;
        Changed?.Invoke();
    }

#if UNITY_EDITOR
    [ContextMenu("Debug / Log Loadout")]
    void EditorLogLoadout()
    {
        Debug.Log(
            $"[PPE Loadout] equipped=[{string.Join(", ", equippedList)}] " +
            $"prepared=[{string.Join(", ", preparedList)}] " +
            $"sequenceComplete={sequenceComplete} bagChecked={bagContentsChecked}",
            this);
    }
#endif
}

/// <summary>Plain serializable copy of <see cref="PPELoadoutData"/>.</summary>
[Serializable]
public class PPELoadoutSnapshot
{
    public List<string> equippedItemIds = new List<string>();
    public List<string> preparedItemIds = new List<string>();
    public bool sequenceComplete;
    public bool bagContentsChecked;
}
