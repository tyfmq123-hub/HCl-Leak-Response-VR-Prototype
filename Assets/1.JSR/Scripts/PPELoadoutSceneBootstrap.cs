using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Use once on the pre-entry validation scene (not the PPE room).
/// Full training: reads committed DontDestroyOnLoad <see cref="PPELoadoutData"/>
/// (<see cref="PPELoadoutSource.PpeSceneResult"/>), including partial / empty gear.
/// Field-only start: applies <see cref="PPELoadoutPresets.FieldReadyComplete"/> only when
/// <see cref="PPELoadoutSource"/> is <see cref="PPELoadoutSource.None"/>.
/// </summary>
[DisallowMultipleComponent]
public class PPELoadoutSceneBootstrap : MonoBehaviour
{
    public enum MissingLoadoutMode
    {
        /// <summary>Do nothing when Source is None.</summary>
        LeaveEmpty = 0,
        /// <summary>Create empty PPELoadoutData with Source None (debug only).</summary>
        CreateEmpty = 1,
        /// <summary>Apply official FieldReadyComplete when Source is None.</summary>
        ApplyFieldReadyPreset = 2,
    }

    [Serializable]
    public class ItemVisualBinding
    {
        [Tooltip("Must match PPE ItemId from the PPE room (e.g. ChemicalSuit).")]
        public string itemId;

        [Tooltip("Optional wearable/visual root to SetActive(true) when this item is equipped/prepared.")]
        public GameObject visualRoot;

        [Tooltip("If true, also treat prepared ids as equipped for this binding.")]
        public bool acceptPrepared = true;
    }

    [Header("Auto Load")]
    [SerializeField]
    [Tooltip("Read loadout in Awake (recommended so visuals exist before other Start logic).")]
    bool loadOnAwake = true;

    [SerializeField]
    [Tooltip("Also read again in Start only if Awake did not already apply.")]
    bool loadOnStart = true;

    [SerializeField]
    [Tooltip("Only used when Source == None. Field-only module select should call BeginFieldStandaloneWithOfficialPreset before load; prefer LeaveEmpty for full-training validation.")]
    MissingLoadoutMode missingLoadoutMode = MissingLoadoutMode.LeaveEmpty;

    [Header("Optional Visual Apply")]
    [SerializeField]
    List<ItemVisualBinding> visualBindings = new List<ItemVisualBinding>();

    [SerializeField]
    [Tooltip("When true, deactivate binding visuals first, then enable only equipped ones.")]
    bool resetBindingsBeforeApply = true;

    [Header("Events")]
    [SerializeField]
    UnityEvent onLoadoutApplied;

    [SerializeField]
    UnityEvent onLoadoutUnresolved;

    [SerializeField]
    UnityEvent onPreparationComplete;

    PPELoadoutData cached;
    bool applied;

    public PPELoadoutData CurrentLoadout => cached;
    public bool HasApplied => applied;

    void Awake()
    {
        if (loadOnAwake)
            ApplyLoadout();
    }

    void Start()
    {
        if (loadOnStart && !applied)
            ApplyLoadout();
    }

    [ContextMenu("Apply Loadout Now")]
    public void ApplyLoadoutFromMenu()
    {
        applied = false;
        ApplyLoadout();
    }

    public void ApplyLoadout()
    {
        // Prevent Awake+Start double-fire of UnityEvents (scene loads, gate unlocks, etc.).
        if (applied)
            return;

        cached = ResolveOrSeedLoadout();

        if (cached == null || !cached.HasResolvedLoadout)
        {
            Debug.LogWarning(
                "[PPE Loadout Bootstrap] No resolved loadout (Source=None). Continuing without forcing gear.",
                this);
            ApplyBindingsEmpty();
            onLoadoutUnresolved?.Invoke();
            applied = true;
            return;
        }

        var equippedCount = cached.EquippedItemIds != null ? cached.EquippedItemIds.Count : 0;
        var preparedCount = cached.PreparedItemIds != null ? cached.PreparedItemIds.Count : 0;

        ApplyBindings(cached);
        onLoadoutApplied?.Invoke();

        if (cached.IsSequenceComplete || cached.HasCompletedPreparation)
            onPreparationComplete?.Invoke();

        applied = true;
        Debug.Log(
            $"[PPE Loadout Bootstrap] Applied. source={cached.Source}, equipped={equippedCount}, prepared={preparedCount}, " +
            $"sequenceComplete={cached.IsSequenceComplete}, bagChecked={cached.BagContentsChecked}",
            this);
    }

    PPELoadoutData ResolveOrSeedLoadout()
    {
        var existing = PPELoadoutData.Instance;

        // Source is the only gate. Partial/empty PpeSceneResult must never be overwritten.
        if (existing != null && existing.HasResolvedLoadout)
        {
            if (missingLoadoutMode == MissingLoadoutMode.ApplyFieldReadyPreset
                && existing.HasRecordedPpeResult
                && (existing.EquippedItemIds == null || existing.EquippedItemIds.Count == 0))
            {
                Debug.Log(
                    "[PPE Loadout Bootstrap] Keeping empty PpeSceneResult — will not apply FieldReady preset.",
                    this);
            }

            return existing;
        }

        switch (missingLoadoutMode)
        {
            case MissingLoadoutMode.ApplyFieldReadyPreset:
                // Only when Source == None.
                return PPELoadoutPresets.BeginFieldStandaloneWithOfficialPreset();

            case MissingLoadoutMode.CreateEmpty:
                return PPELoadoutData.Ensure();

            case MissingLoadoutMode.LeaveEmpty:
            default:
                return existing;
        }
    }

    void ApplyBindingsEmpty()
    {
        if (!resetBindingsBeforeApply || visualBindings == null)
            return;

        for (var i = 0; i < visualBindings.Count; i++)
        {
            var binding = visualBindings[i];
            if (binding?.visualRoot != null)
                binding.visualRoot.SetActive(false);
        }
    }

    void ApplyBindings(PPELoadoutData data)
    {
        if (visualBindings == null)
            return;

        if (resetBindingsBeforeApply)
            ApplyBindingsEmpty();

        for (var i = 0; i < visualBindings.Count; i++)
        {
            var binding = visualBindings[i];
            if (binding == null || binding.visualRoot == null || string.IsNullOrWhiteSpace(binding.itemId))
                continue;

            var on = data.IsEquipped(binding.itemId)
                     || (binding.acceptPrepared && data.IsPrepared(binding.itemId));

            binding.visualRoot.SetActive(on);
        }
    }
}
