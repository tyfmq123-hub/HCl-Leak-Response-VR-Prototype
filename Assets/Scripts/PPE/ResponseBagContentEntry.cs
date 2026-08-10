using System;
using UnityEngine;

/// <summary>Serializable bag-contents definition used by the inspection UI.</summary>
[Serializable]
public class ResponseBagContentEntry
{
    public string ItemId;
    public string KoreanDisplayName;
    [TextArea(2, 5)]
    public string KoreanDescription;
    public GameObject PreviewPrefab;
    public Sprite OptionalIcon;

    [NonSerialized]
    public bool IsInspected;
}
