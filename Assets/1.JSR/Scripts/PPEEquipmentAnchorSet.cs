using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Inspector-authored lookup for PPE equipment destinations. This component only
/// describes anchors; it does not move, equip, or deactivate PPE objects.
/// </summary>
[DisallowMultipleComponent]
public sealed class PPEEquipmentAnchorSet : MonoBehaviour
{
    [Header("Head")]
    [SerializeField] Transform head;
    [SerializeField] Transform face;

    [Header("Hands")]
    [SerializeField] Transform leftHand;
    [SerializeField] Transform rightHand;

    [Header("Body")]
    [SerializeField] Transform body;
    [SerializeField] Transform leftFoot;
    [SerializeField] Transform rightFoot;

    [Header("Carried Equipment")]
    [SerializeField] Transform radio;
    [SerializeField] Transform gasDetector;

    public Transform Head => head;
    public Transform Face => face;
    public Transform LeftHand => leftHand;
    public Transform RightHand => rightHand;
    public Transform Body => body;
    public Transform LeftFoot => leftFoot;
    public Transform RightFoot => rightFoot;
    public Transform Radio => radio;
    public Transform GasDetector => gasDetector;

    /// <summary>Returns the Transform assigned to one concrete slot.</summary>
    public bool TryGetAnchor(PPEEquipSlot slot, out Transform anchor)
    {
        anchor = GetAnchor(slot);
        return anchor != null;
    }

    /// <summary>Returns the Transform assigned to one concrete slot.</summary>
    public Transform GetAnchor(PPEEquipSlot slot)
    {
        switch (slot)
        {
            case PPEEquipSlot.Head:
                return head;
            case PPEEquipSlot.Face:
                return face;
            case PPEEquipSlot.LeftHand:
                return leftHand;
            case PPEEquipSlot.RightHand:
                return rightHand;
            case PPEEquipSlot.Body:
                return body;
            case PPEEquipSlot.LeftFoot:
                return leftFoot;
            case PPEEquipSlot.RightFoot:
                return rightFoot;
            case PPEEquipSlot.Radio:
                return radio;
            case PPEEquipSlot.GasDetector:
                return gasDetector;
            default:
                return null;
        }
    }

    /// <summary>Adds every assigned anchor represented by a slot mask.</summary>
    public int GetAnchors(PPEEquipSlot slots, List<Transform> results)
    {
        if (results == null)
            return 0;

        var before = results.Count;
        AddIfRequested(slots, PPEEquipSlot.Head, head, results);
        AddIfRequested(slots, PPEEquipSlot.Face, face, results);
        AddIfRequested(slots, PPEEquipSlot.LeftHand, leftHand, results);
        AddIfRequested(slots, PPEEquipSlot.RightHand, rightHand, results);
        AddIfRequested(slots, PPEEquipSlot.Body, body, results);
        AddIfRequested(slots, PPEEquipSlot.LeftFoot, leftFoot, results);
        AddIfRequested(slots, PPEEquipSlot.RightFoot, rightFoot, results);
        AddIfRequested(slots, PPEEquipSlot.Radio, radio, results);
        AddIfRequested(slots, PPEEquipSlot.GasDetector, gasDetector, results);
        return results.Count - before;
    }

    static void AddIfRequested(
        PPEEquipSlot requested,
        PPEEquipSlot slot,
        Transform anchor,
        List<Transform> results)
    {
        if ((requested & slot) != 0 && anchor != null)
            results.Add(anchor);
    }

#if UNITY_EDITOR
    public void EditorWire(
        Transform headAnchor,
        Transform faceAnchor,
        Transform leftHandAnchor,
        Transform rightHandAnchor,
        Transform bodyAnchor,
        Transform leftFootAnchor,
        Transform rightFootAnchor,
        Transform radioAnchor,
        Transform gasDetectorAnchor)
    {
        head = headAnchor;
        face = faceAnchor;
        leftHand = leftHandAnchor;
        rightHand = rightHandAnchor;
        body = bodyAnchor;
        leftFoot = leftFootAnchor;
        rightFoot = rightFootAnchor;
        radio = radioAnchor;
        gasDetector = gasDetectorAnchor;
    }
#endif
}
