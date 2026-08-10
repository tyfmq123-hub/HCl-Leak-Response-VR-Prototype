using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Drop on the field / leak-response scene.
/// Full training: reads live DontDestroyOnLoad <see cref="PPELoadoutData"/> from PPE room.
/// Field-only start: applies <see cref="PPELoadoutPresets.FieldReadyComplete"/> (never an empty loadout).
/// </summary>
[DisallowMultipleComponent]
public class PPELoadoutSceneBootstrap : MonoBehaviour
{
    public enum MissingLoadoutMode
    {
        /// <summary>Do nothing / report empty (legacy). Prefer not for field scene.</summary>
        LeaveEmpty = 0,
        /// <summary>Create empty PPELoadoutData (debug only).</summary>
        CreateEmpty = 1,
        /// <summary>Replace with official FieldReadyComplete preset (field standalone / module select).</summary>
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
    [Tooltip("Also read again in Start (safe if other scripts create state later).")]
    bool loadOnStart = true;

    [SerializeField]
    [Tooltip("When PPELoadoutData is missing or empty (field-only start).")]
    MissingLoadoutMode missingLoadoutMode = MissingLoadoutMode.ApplyFieldReadyPreset;

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
    UnityEvent onLoadoutEmpty;

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
        if (loadOnStart)
            ApplyLoadout();
    }

    [ContextMenu("Apply Loadout Now")]
    public void ApplyLoadout()
    {
        cached = ResolveOrSeedLoadout();

        if (cached == null)
        {
            Debug.LogWarning("[PPE Loadout Bootstrap] No PPELoadoutData. Continuing without visuals.", this);
            ApplyBindingsEmpty();
            onLoadoutEmpty?.Invoke();
            applied = true;
            return;
        }

        var equippedCount = cached.EquippedItemIds != null ? cached.EquippedItemIds.Count : 0;
        var preparedCount = cached.PreparedItemIds != null ? cached.PreparedItemIds.Count : 0;
        var empty = equippedCount == 0 && preparedCount == 0 && !cached.IsSequenceComplete;

        ApplyBindings(cached);

        if (empty)
            onLoadoutEmpty?.Invoke();
        else
            onLoadoutApplied?.Invoke();

        if (cached.IsSequenceComplete || cached.HasCompletedPreparation)
            onPreparationComplete?.Invoke();

        applied = true;
        Debug.Log(
            $"[PPE Loadout Bootstrap] Applied. equipped={equippedCount}, prepared={preparedCount}, " +
            $"sequenceComplete={cached.IsSequenceComplete}, bagChecked={cached.BagContentsChecked}",
            this);
    }

    PPELoadoutData ResolveOrSeedLoadout()
    {
        var existing = PPELoadoutData.Instance;
        if (existing != null && !IsEffectivelyEmpty(existing))
            return existing;

        switch (missingLoadoutMode)
        {
            case MissingLoadoutMode.ApplyFieldReadyPreset:
                // Replaces any stale empty / missing DDOL with the official complete preset.
                return PPELoadoutPresets.BeginFieldStandaloneWithOfficialPreset();

            case MissingLoadoutMode.CreateEmpty:
                return PPELoadoutData.Ensure();

            case MissingLoadoutMode.LeaveEmpty:
            default:
                return existing;
        }
    }

    static bool IsEffectivelyEmpty(PPELoadoutData data)
    {
        if (data == null)
            return true;
        var eq = data.EquippedItemIds != null ? data.EquippedItemIds.Count : 0;
        var pr = data.PreparedItemIds != null ? data.PreparedItemIds.Count : 0;
        return eq == 0 && pr == 0 && !data.IsSequenceComplete;
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
