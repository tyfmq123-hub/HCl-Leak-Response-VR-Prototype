using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Spawns real grab-ready bag items into the selecting XR hand from gear-panel card clicks.
/// Card previews stay display-only — only the assigned runtime prefabs become gameplay objects.
/// After a successful take the card restocks after <see cref="bagRestockDelaySeconds"/> (default 3s);
/// the held instance stays in the hand.
/// </summary>
[DisallowMultipleComponent]
public class ResponseBagItemRetriever : MonoBehaviour
{
    [Header("Runtime Prefabs (assign in Inspector)")]
    [SerializeField] GameObject gasDetectorRuntimePrefab;
    [SerializeField] GameObject absorbentSockRuntimePrefab;
    [SerializeField] GameObject absorbentPadRuntimePrefab;

    [Header("Restock")]
    [Tooltip("Seconds after a successful take before the card restocks (held item stays).")]
    [SerializeField] [Min(0.1f)] float bagRestockDelaySeconds = 3f;

    [Header("Refs")]
    [SerializeField] ResponseEquipmentManager equipmentManager;
    [SerializeField] XRInteractionManager interactionManager;

    readonly Dictionary<string, ResponseBagItemState> itemStates =
        new Dictionary<string, ResponseBagItemState>();
    readonly Dictionary<string, Coroutine> restockRoutines =
        new Dictionary<string, Coroutine>();
    readonly List<GameObject> spawnedInstances = new List<GameObject>();

    public float BagRestockDelaySeconds => bagRestockDelaySeconds;

    void Awake()
    {
        if (equipmentManager == null)
            equipmentManager = GetComponent<ResponseEquipmentManager>();
        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);

        EnsureStatesInitialized();
    }

    public void EditorSetPrefabs(GameObject gas, GameObject sock, GameObject pad)
    {
        gasDetectorRuntimePrefab = gas;
        absorbentSockRuntimePrefab = sock;
        absorbentPadRuntimePrefab = pad;
    }

    void EnsureStatesInitialized()
    {
        for (var i = 0; i < ResponseEquipmentManager.RequiredBagContentIds.Length; i++)
        {
            var id = ResponseEquipmentManager.RequiredBagContentIds[i];
            if (!itemStates.ContainsKey(id))
                itemStates[id] = ResponseBagItemState.InBag;
        }
    }

    public ResponseBagItemState GetItemState(string itemId)
    {
        EnsureStatesInitialized();
        var id = Normalize(itemId);
        return itemStates.TryGetValue(id, out var state) ? state : ResponseBagItemState.InBag;
    }

    public bool IsItemAvailableInBag(string itemId) =>
        GetItemState(itemId) == ResponseBagItemState.InBag;

    /// <summary>
    /// Attempts to spawn the runtime prefab into the selecting XR hand.
    /// Returns false without consuming card state when spawn/grab fails.
    /// </summary>
    public bool TryTakeItem(string itemId)
    {
        // Post-equip take-from-bag removed.
        return false;
    }

    public void ResetAllItems()
    {
        foreach (var pair in restockRoutines)
        {
            if (pair.Value != null)
                StopCoroutine(pair.Value);
        }
        restockRoutines.Clear();

        for (var i = spawnedInstances.Count - 1; i >= 0; i--)
        {
            if (spawnedInstances[i] != null)
                Destroy(spawnedInstances[i]);
        }
        spawnedInstances.Clear();

        itemStates.Clear();
        EnsureStatesInitialized();
        equipmentManager?.NotifyBagItemAvailabilityReset();
    }

    void ScheduleRestock(string itemId)
    {
        if (restockRoutines.TryGetValue(itemId, out var existing) && existing != null)
            StopCoroutine(existing);

        restockRoutines[itemId] = StartCoroutine(RestockAfterDelay(itemId));
    }

    IEnumerator RestockAfterDelay(string itemId)
    {
        yield return new WaitForSeconds(bagRestockDelaySeconds);

        itemStates[itemId] = ResponseBagItemState.InBag;
        restockRoutines.Remove(itemId);
        equipmentManager?.NotifyBagItemRestocked(itemId);
    }

    GameObject ResolvePrefab(string itemId)
    {
        if (itemId == ResponseEquipmentManager.IdGasDetector)
            return gasDetectorRuntimePrefab;
        if (itemId == ResponseEquipmentManager.IdAbsorbentSock)
            return absorbentSockRuntimePrefab;
        if (itemId == ResponseEquipmentManager.IdAbsorbentPad)
            return absorbentPadRuntimePrefab;
        return null;
    }

    static string Normalize(string itemId)
    {
        if (itemId == "AbsorbPad")
            return ResponseEquipmentManager.IdAbsorbentPad;
        if (itemId == "AbsorbSock")
            return ResponseEquipmentManager.IdAbsorbentSock;
        if (itemId == "ConcentrationMeter")
            return ResponseEquipmentManager.IdGasDetector;
        return itemId;
    }

    static void EnsureRuntimeMarker(GameObject instance, string itemId)
    {
        var marker = instance.GetComponent<ResponseBagRuntimeItem>();
        if (marker == null)
            marker = instance.AddComponent<ResponseBagRuntimeItem>();
        marker.ItemId = itemId;
    }

    static IXRSelectInteractor ResolveSelectingInteractor()
    {
        if (XRUISelectHandCapture.LastSelectInteractor != null &&
            Time.unscaledTime - XRUISelectHandCapture.LastSelectTime < 0.5f)
            return XRUISelectHandCapture.LastSelectInteractor;

        var rays = FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Exclude);
        IXRSelectInteractor fallback = null;
        for (var i = 0; i < rays.Length; i++)
        {
            var ray = rays[i];
            if (ray == null || !(ray is IXRSelectInteractor selectable))
                continue;
            if (!selectable.hasSelection)
                return selectable;
            fallback ??= selectable;
        }

        return fallback;
    }

    XRInteractionManager ResolveInteractionManager(IXRSelectInteractor interactor)
    {
        if (interactionManager != null)
            return interactionManager;

        if (interactor is XRBaseInteractor baseInteractor && baseInteractor.interactionManager != null)
            return baseInteractor.interactionManager;

        interactionManager = FindAnyObjectByType<XRInteractionManager>(FindObjectsInactive.Include);
        return interactionManager;
    }

    static void ShowFeedback(string message)
    {
        var feedback = FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);
        feedback?.ShowMessage(message);
    }
}
