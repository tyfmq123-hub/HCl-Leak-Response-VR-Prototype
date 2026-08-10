using UnityEngine;

/// <summary>
/// Marker on spawned gameplay items taken from the response bag (not UI previews).
/// </summary>
[DisallowMultipleComponent]
public class ResponseBagRuntimeItem : MonoBehaviour
{
    [SerializeField] string itemId;

    public string ItemId
    {
        get => itemId;
        set => itemId = value;
    }
}
